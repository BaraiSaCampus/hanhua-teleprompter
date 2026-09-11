using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Teleprompter.Models;

public sealed class PromptItem : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private int _displayIndex;
    private string _state = string.Empty;

    public Guid Id { get; set; } = Guid.NewGuid();

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;
            _text = value;
            OnPropertyChanged();
        }
    }

    [JsonIgnore]
    public int DisplayIndex
    {
        get => _displayIndex;
        set
        {
            if (_displayIndex == value) return;
            _displayIndex = value;
            OnPropertyChanged();
        }
    }

    [JsonIgnore]
    public string State
    {
        get => _state;
        set
        {
            if (_state == value) return;
            _state = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

