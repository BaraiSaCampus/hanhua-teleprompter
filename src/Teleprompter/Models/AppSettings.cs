namespace Teleprompter.Models;

public sealed class AppSettings
{
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 620;
    public double ExpandedHeight { get; set; } = 650;
    public bool IsExpanded { get; set; }
    public string? LastSourcePath { get; set; }
}

