using System.Collections.ObjectModel;
using Teleprompter.Models;

namespace Teleprompter.Tests;

public sealed class PromptSessionTests
{
    [Fact]
    public void AdvanceRollbackAndBoundsAreStable()
    {
        var session = Create("一", "二");
        Assert.True(session.Advance());
        Assert.True(session.Advance());
        Assert.False(session.Advance());
        Assert.True(session.Rollback());
        Assert.Equal("二", session.NextItem?.Text);
        Assert.True(session.Rollback());
        Assert.False(session.Rollback());
    }

    [Fact]
    public void DeletingConsumedItemPreservesNextItem()
    {
        var session = Create("一", "二", "三");
        session.Advance();
        var expected = session.NextItem;
        session.Delete(session.Items[0]);
        Assert.Same(expected, session.NextItem);
        Assert.Equal(0, session.Cursor);
    }

    [Fact]
    public void InsertingAtNextDoesNotSkipNewItem()
    {
        var session = Create("一", "二");
        session.Advance();
        var inserted = session.InsertAtNext("插入");
        Assert.Same(inserted, session.NextItem);
        Assert.Equal("二", session.Items[2].Text);
    }

    [Fact]
    public void SetNextMovesCursorToSelectedItem()
    {
        var session = Create("一", "二", "三");
        session.SetNext(session.Items[2]);
        Assert.Equal(2, session.Cursor);
        Assert.Equal("下一条", session.Items[2].State);
    }

    private static PromptSession Create(params string[] lines)
    {
        var session = new PromptSession
        {
            Items = new ObservableCollection<PromptItem>(lines.Select(text => new PromptItem { Text = text }))
        };
        session.RefreshPresentation();
        return session;
    }
}

