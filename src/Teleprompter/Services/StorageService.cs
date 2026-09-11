using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO;
using Teleprompter.Models;

namespace Teleprompter.Services;

public sealed class StorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _dataDirectory;
    private readonly string _sessionsDirectory;

    public StorageService(string baseDirectory)
    {
        _dataDirectory = Path.Combine(baseDirectory, "data");
        _sessionsDirectory = Path.Combine(_dataDirectory, "sessions");
        try
        {
            Directory.CreateDirectory(_sessionsDirectory);
            var probe = Path.Combine(_dataDirectory, $".write-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
        }
        catch
        {
            IsReadOnly = true;
        }
    }

    public bool IsReadOnly { get; }

    public AppSettings LoadSettings()
    {
        try
        {
            var path = Path.Combine(_dataDirectory, "settings.json");
            return File.Exists(path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings()
                : new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public PromptSession? LoadSession(string sourcePath)
    {
        try
        {
            var path = GetSessionPath(sourcePath);
            if (!File.Exists(path)) return null;
            var session = JsonSerializer.Deserialize<PromptSession>(File.ReadAllText(path), JsonOptions);
            session?.RefreshPresentation();
            return session;
        }
        catch
        {
            return null;
        }
    }

    public bool SaveSettings(AppSettings settings) =>
        TryAtomicWrite(Path.Combine(_dataDirectory, "settings.json"), settings);

    public bool SaveSession(PromptSession session)
    {
        session.UpdatedUtc = DateTime.UtcNow;
        return TryAtomicWrite(GetSessionPath(session.SourcePath), session);
    }

    private string GetSessionPath(string sourcePath)
    {
        var normalized = Path.GetFullPath(sourcePath).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        return Path.Combine(_sessionsDirectory, $"{name}.json");
    }

    private bool TryAtomicWrite<T>(string destination, T value)
    {
        if (IsReadOnly) return false;
        var temp = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(temp, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false));
            File.Move(temp, destination, true);
            return true;
        }
        catch
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            return false;
        }
    }
}
