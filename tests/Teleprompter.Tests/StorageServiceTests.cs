using System.Collections.ObjectModel;
using Teleprompter.Models;
using Teleprompter.Services;

namespace Teleprompter.Tests;

public sealed class StorageServiceTests
{
    [Fact]
    public void SavesAndRestoresEditedQueueAndCursor()
    {
        var root = CreateTempDirectory();
        try
        {
            var source = Path.Combine(root, "台词.txt");
            File.WriteAllText(source, "一\n二");
            var storage = new StorageService(root);
            var session = new PromptSession
            {
                SourcePath = source,
                Fingerprint = DocumentImportService.CreateFingerprint(source),
                Items = new ObservableCollection<PromptItem>(
                    [new PromptItem { Text = "编辑后的一" }, new PromptItem { Text = "二" }]),
                Cursor = 1
            };

            Assert.True(storage.SaveSession(session));
            var restored = storage.LoadSession(source);

            Assert.NotNull(restored);
            Assert.Equal(1, restored.Cursor);
            Assert.Equal("编辑后的一", restored.Items[0].Text);
            Assert.Equal("下一条", restored.Items[1].State);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void CorruptSettingsFallsBackToDefaults()
    {
        var root = CreateTempDirectory();
        try
        {
            var data = Path.Combine(root, "data");
            Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data, "settings.json"), "{broken");
            var settings = new StorageService(root).LoadSettings();
            Assert.True(double.IsNaN(settings.Left));
            Assert.Null(settings.LastSourcePath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"teleprompter-storage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
