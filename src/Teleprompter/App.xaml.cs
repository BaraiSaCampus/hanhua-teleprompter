using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace Teleprompter;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        _singleInstanceMutex = new Mutex(true, "Local\\TeleprompterPortable-94F0B4D2-4E29-4A27-B38C-EA02AA8771C8", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("提词器已经在运行。", "提词器", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"程序遇到错误，但会尽量继续运行：\n\n{e.Exception.Message}", "提词器", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
