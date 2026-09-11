namespace Teleprompter.Models;

public sealed class SourceFingerprint
{
    public long Length { get; set; }
    public long LastWriteUtcTicks { get; set; }
    public string Sha256 { get; set; } = string.Empty;

    public bool Matches(SourceFingerprint? other) => other is not null &&
        Length == other.Length &&
        LastWriteUtcTicks == other.LastWriteUtcTicks &&
        string.Equals(Sha256, other.Sha256, StringComparison.OrdinalIgnoreCase);
}

