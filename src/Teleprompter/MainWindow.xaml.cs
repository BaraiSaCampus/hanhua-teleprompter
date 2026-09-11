using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Teleprompter.Models;
using Teleprompter.Services;

namespace Teleprompter;

public partial class MainWindow : Window
{
    private const double CompactHeight = 300;
    private readonly StorageService _storage = new(AppContext.BaseDirectory);
    private readonly DocumentImportService _importer = new();
    private readonly ClipboardService _clipboard = new();
    private readonly KeyboardHookService _keyboardHook = new();
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly AppSettings _settings;
    private PromptSession? _session;
    private bool _isArmed;
    private bool _isExpanded;
    private bool _isLoaded;
    private double _expandedHeight = 650;
    private Point _dragStart;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _storage.LoadSettings();
        _keyboardHook.HotkeyPressed = HandleHotkey;
        _saveTimer.Tick += (_, _) => SaveNow();
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            StatusText.Text = _storage.IsReadOnly ? "只读模式：无法保存进度" : "就绪";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(109, 114, 128));
        };

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        LocationChanged += (_, _) => ScheduleSave();
        SizeChanged += (_, _) =>
        {
            if (_isExpanded && WindowState == WindowState.Normal) _expandedHeight = ActualHeight;
            ScheduleSave();
        };
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyWindowSettings();
        _isLoaded = true;
        if (_storage.IsReadOnly) ShowStatus("EXE 所在目录不可写：当前为只读会话模式", true);

        if (!string.IsNullOrWhiteSpace(_settings.LastSourcePath) && File.Exists(_settings.LastSourcePath))
            await ImportFileAsync(_settings.LastSourcePath, true);
    }

    private async void ChooseFileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择提词文件",
            Filter = "支持的文件 (*.txt;*.docx;*.doc)|*.txt;*.docx;*.doc|文本文件 (*.txt)|*.txt|Word 文件 (*.docx;*.doc)|*.docx;*.doc",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true) await ImportFileAsync(dialog.FileName, false);
    }

    private async Task ImportFileAsync(string path, bool isStartupRestore)
    {
        ChooseFileButton.IsEnabled = false;
        ShowStatus("正在解析文件…");
        try
        {
            var result = await _importer.ImportAsync(path);
            var saved = _storage.LoadSession(result.SourcePath);
            PromptSession nextSession;

            if (saved is not null && saved.Items.Count > 0 && saved.Fingerprint.Matches(result.Fingerprint))
            {
                nextSession = saved;
                ShowStatus($"已恢复上次进度：{saved.Cursor} / {saved.Items.Count}");
            }
            else if (saved is not null && saved.Items.Count > 0 && !saved.Fingerprint.Matches(result.Fingerprint))
            {
                var choice = MessageBox.Show(this,
                    "源文件自上次使用后发生了变化。\n\n选择“是”重新导入新内容；选择“否”继续使用上次编辑过的队列；选择“取消”保持当前内容。",
                    "源文件已变化", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Yes);
                if (choice == MessageBoxResult.Cancel) return;
                if (choice == MessageBoxResult.No)
                {
                    saved.Fingerprint = result.Fingerprint;
                    nextSession = saved;
                }
                else
                {
                    nextSession = CreateSession(result);
                }
            }
            else
            {
                nextSession = CreateSession(result);
            }

            SetSession(nextSession);
            _settings.LastSourcePath = result.SourcePath;
            SetArmed(true);
            ScheduleSave();
            if (!isStartupRestore) ShowStatus($"已导入 {nextSession.Items.Count} 条并启用快捷键");
        }
        catch (OperationCanceledException)
        {
            ShowStatus("已取消导入");
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, true);
            if (!isStartupRestore)
                MessageBox.Show(this, ex.Message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ChooseFileButton.IsEnabled = true;
        }
    }

    private static PromptSession CreateSession(ImportResult result)
    {
        var session = new PromptSession
        {
            SourcePath = result.SourcePath,
            Fingerprint = result.Fingerprint,
            Items = new ObservableCollection<PromptItem>(result.Lines.Select(text => new PromptItem { Text = text })),
            Cursor = 0
        };
        session.RefreshPresentation();
        return session;
    }

    private void SetSession(PromptSession session)
    {
        if (_session is not null)
            foreach (var item in _session.Items) item.PropertyChanged -= PromptItem_PropertyChanged;

        _session = session;
        _session.RefreshPresentation();
        foreach (var item in _session.Items) item.PropertyChanged += PromptItem_PropertyChanged;
        ItemsGrid.ItemsSource = _session.Items;
        RefreshView();
    }

    private void PromptItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PromptItem.Text))
        {
            RefreshView();
            ScheduleSave();
        }
    }

    private void ArmButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null)
        {
            ShowStatus("请先选择提词文件", true);
            return;
        }
        SetArmed(!_isArmed);
    }

    private void SetArmed(bool armed)
    {
        try
        {
            if (armed) _keyboardHook.Install(); else _keyboardHook.Uninstall();
            _isArmed = armed;
            RefreshView();
            ShowStatus(armed ? "已启用：Ctrl+V 输出，Ctrl+C 回退" : "已暂停：系统复制粘贴已恢复");
        }
        catch (Exception ex)
        {
            _isArmed = false;
            _keyboardHook.Uninstall();
            RefreshView();
            ShowStatus(ex.Message, true);
        }
    }

    private HookDecision HandleHotkey(PromptHotkey hotkey)
    {
        if (!Dispatcher.CheckAccess()) return Dispatcher.Invoke(() => HandleHotkey(hotkey));
        if (!_isArmed || _session is null) return HookDecision.PassThrough;

        if (hotkey == PromptHotkey.Rollback)
        {
            if (_session.Rollback())
            {
                RefreshView();
                ScheduleSave();
                ShowStatus("已回退一条");
            }
            else ShowStatus("已经是第一条", true);
            return HookDecision.Swallow;
        }

        var next = _session.NextItem;
        if (next is null)
        {
            ShowStatus("队列已经全部输出，可按 Ctrl+C 回退", true);
            return HookDecision.Swallow;
        }

        if (!_clipboard.TrySetText(next.Text))
        {
            ShowStatus("剪贴板正被其他程序占用，本条未跳过", true);
            return HookDecision.Swallow;
        }

        _session.Advance();
        RefreshView();
        ScheduleSave();
        ShowStatus("已输出并准备下一条");
        return HookDecision.PassThrough;
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        _session.Reset();
        RefreshView();
        ScheduleSave();
        ShowStatus("已回到开头");
    }

    private void ExpandButton_Click(object sender, RoutedEventArgs e)
    {
        _isExpanded = !_isExpanded;
        ExpandedPanel.Visibility = _isExpanded ? Visibility.Visible : Visibility.Collapsed;
        ExpandButton.Content = _isExpanded ? "收起 ▴" : "展开 ▾";
        if (_isExpanded)
        {
            Height = Math.Max(_expandedHeight, 520);
        }
        else
        {
            _expandedHeight = Math.Max(ActualHeight, 520);
            Height = CompactHeight;
        }
        ScheduleSave();
    }

    private void InsertButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        var item = _session.InsertAtNext();
        item.PropertyChanged += PromptItem_PropertyChanged;
        ItemsGrid.SelectedItem = item;
        ItemsGrid.ScrollIntoView(item);
        RefreshView();
        ScheduleSave();
        Dispatcher.BeginInvoke(() =>
        {
            ItemsGrid.Focus();
            ItemsGrid.CurrentCell = new DataGridCellInfo(item, ItemsGrid.Columns[2]);
            ItemsGrid.BeginEdit();
        }, DispatcherPriority.Background);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || ItemsGrid.SelectedItem is not PromptItem item) return;
        item.PropertyChanged -= PromptItem_PropertyChanged;
        _session.Delete(item);
        RefreshView();
        ScheduleSave();
    }

    private void SetNextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || ItemsGrid.SelectedItem is not PromptItem item) return;
        _session.SetNext(item);
        RefreshView();
        ScheduleSave();
        ShowStatus($"第 {item.DisplayIndex} 条已设为下一条");
    }

    private void ItemsGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (_session is not null && e.Row.Item is PromptItem item)
            {
                item.Text = item.Text.Trim();
                if (item.Text.Length == 0)
                {
                    item.PropertyChanged -= PromptItem_PropertyChanged;
                    _session.Delete(item);
                }
            }
            _session?.RefreshPresentation();
            RefreshView();
            ScheduleSave();
        }, DispatcherPriority.Background);

    private void ItemsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _dragStart = e.GetPosition(null);

    private void ItemsGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || ItemsGrid.SelectedItem is not PromptItem item) return;
        var current = e.GetPosition(null);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        DragDrop.DoDragDrop(ItemsGrid, item, DragDropEffects.Move);
    }

    private void ItemsGrid_Drop(object sender, DragEventArgs e)
    {
        if (_session is null || !e.Data.GetDataPresent(typeof(PromptItem))) return;
        var item = (PromptItem)e.Data.GetData(typeof(PromptItem))!;
        var row = FindAncestor<DataGridRow>((DependencyObject)e.OriginalSource);
        var destination = row?.Item is PromptItem target ? _session.Items.IndexOf(target) : _session.Items.Count;
        _session.Move(item, destination);
        ItemsGrid.SelectedItem = item;
        RefreshView();
        ScheduleSave();
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        var file = files.FirstOrDefault(path => Path.GetExtension(path).ToLowerInvariant() is ".txt" or ".docx" or ".doc");
        if (file is not null) await ImportFileAsync(file, false);
        else ShowStatus("拖入的文件格式不受支持", true);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void RefreshView()
    {
        if (_session is null)
        {
            StateText.Text = "未载入";
            StateBadge.Background = new SolidColorBrush(Color.FromRgb(138, 144, 157));
            FileText.Text = "可拖入 TXT / DOCX / DOC";
            ProgressText.Text = "0 / 0";
            PreviewText.Text = "请先选择文件";
            ArmButton.Content = "启用";
            return;
        }

        _session.RefreshPresentation();
        FileText.Text = Path.GetFileName(_session.SourcePath);
        ProgressText.Text = $"{_session.Cursor} / {_session.Items.Count}";
        PreviewText.Text = _session.NextItem?.Text ?? "全部输出完成；Ctrl+C 可回退";
        StateText.Text = _isArmed ? (_session.NextItem is null ? "已完成" : "已启用") : "已暂停";
        StateBadge.Background = new SolidColorBrush(_isArmed ? Color.FromRgb(22, 134, 90) : Color.FromRgb(138, 144, 157));
        ArmButton.Content = _isArmed ? "暂停" : "启用";
    }

    private void ShowStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = new SolidColorBrush(isError ? Color.FromRgb(191, 59, 69) : Color.FromRgb(83, 109, 254));
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private void ScheduleSave()
    {
        if (!_isLoaded) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        _saveTimer.Stop();
        if (!_isLoaded) return;
        if (WindowState == WindowState.Normal)
        {
            _settings.Left = Left;
            _settings.Top = Top;
            _settings.Width = ActualWidth;
            _settings.ExpandedHeight = _isExpanded ? ActualHeight : _expandedHeight;
        }
        _settings.IsExpanded = _isExpanded;
        _storage.SaveSettings(_settings);
        if (_session is not null) _storage.SaveSession(_session);
    }

    private void ApplyWindowSettings()
    {
        _expandedHeight = Math.Max(_settings.ExpandedHeight, 520);
        Width = Math.Max(_settings.Width, MinWidth);
        _isExpanded = _settings.IsExpanded;
        Height = _isExpanded ? _expandedHeight : CompactHeight;
        ExpandedPanel.Visibility = _isExpanded ? Visibility.Visible : Visibility.Collapsed;
        ExpandButton.Content = _isExpanded ? "收起 ▴" : "展开 ▾";

        var work = SystemParameters.WorkArea;
        Left = double.IsFinite(_settings.Left) ? Math.Clamp(_settings.Left, work.Left, Math.Max(work.Left, work.Right - Width)) : work.Right - Width - 24;
        Top = double.IsFinite(_settings.Top) ? Math.Clamp(_settings.Top, work.Top, Math.Max(work.Top, work.Bottom - Height)) : work.Top + 24;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        _keyboardHook.Dispose();
        SaveNow();
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T found) return found;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
