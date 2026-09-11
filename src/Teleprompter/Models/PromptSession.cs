using System.Collections.ObjectModel;

namespace Teleprompter.Models;

public sealed class PromptSession
{
    public string SourcePath { get; set; } = string.Empty;
    public SourceFingerprint Fingerprint { get; set; } = new();
    public ObservableCollection<PromptItem> Items { get; set; } = [];
    public int Cursor { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public PromptItem? NextItem => Cursor >= 0 && Cursor < Items.Count ? Items[Cursor] : null;

    public bool Advance()
    {
        if (Cursor >= Items.Count) return false;
        Cursor++;
        RefreshPresentation();
        return true;
    }

    public bool Rollback()
    {
        if (Cursor <= 0) return false;
        Cursor--;
        RefreshPresentation();
        return true;
    }

    public void Reset()
    {
        Cursor = 0;
        RefreshPresentation();
    }

    public void SetNext(PromptItem item)
    {
        var index = Items.IndexOf(item);
        if (index < 0) return;
        Cursor = index;
        RefreshPresentation();
    }

    public PromptItem InsertAtNext(string text = "新提词")
    {
        var item = new PromptItem { Text = text };
        Items.Insert(Math.Clamp(Cursor, 0, Items.Count), item);
        RefreshPresentation();
        return item;
    }

    public void Delete(PromptItem item)
    {
        var index = Items.IndexOf(item);
        if (index < 0) return;
        Items.RemoveAt(index);
        if (index < Cursor) Cursor--;
        Cursor = Math.Clamp(Cursor, 0, Items.Count);
        RefreshPresentation();
    }

    public void Move(PromptItem item, int destinationIndex)
    {
        var oldIndex = Items.IndexOf(item);
        if (oldIndex < 0 || Items.Count < 2) return;

        var wasConsumed = oldIndex < Cursor;
        Items.RemoveAt(oldIndex);
        if (wasConsumed) Cursor--;

        destinationIndex = Math.Clamp(destinationIndex, 0, Items.Count);
        Items.Insert(destinationIndex, item);
        if (destinationIndex < Cursor) Cursor++;
        Cursor = Math.Clamp(Cursor, 0, Items.Count);
        RefreshPresentation();
    }

    public void RefreshPresentation()
    {
        Cursor = Math.Clamp(Cursor, 0, Items.Count);
        for (var i = 0; i < Items.Count; i++)
        {
            Items[i].DisplayIndex = i + 1;
            Items[i].State = i < Cursor ? "已输出" : i == Cursor ? "下一条" : "待输出";
        }
    }
}
