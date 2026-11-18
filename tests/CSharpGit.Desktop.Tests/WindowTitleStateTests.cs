using System.ComponentModel;
using CSharpGit.Presentation;

namespace CSharpGit.Desktop.Tests;

public sealed class WindowTitleStateTests
{
    [Fact]
    public void SeparateWindowsDoNotPublishIntoEachOtherAndDisposalDetachesCallbacks()
    {
        var firstSource = new TitleSource("first");
        var secondSource = new TitleSource("second");
        using var first = new WindowTitleState(firstSource, () => firstSource.Title);
        using var second = new WindowTitleState(secondSource, () => secondSource.Title);

        var firstUpdates = new List<string>();
        var secondUpdates = new List<string>();
        first.TitleChanged += firstUpdates.Add;
        second.TitleChanged += secondUpdates.Add;

        Assert.Equal("first", first.CurrentTitle);
        Assert.Equal("second", second.CurrentTitle);

        firstSource.Change("first updated");
        Assert.Equal(new[] { "first updated" }, firstUpdates);
        Assert.Empty(secondUpdates);

        first.Dispose();
        firstSource.Change("closed");
        secondSource.Change("second updated");

        Assert.Equal(["first updated"], firstUpdates);
        Assert.Equal(new[] { "second updated" }, secondUpdates);
    }

    private sealed class TitleSource(string title) : INotifyPropertyChanged
    {
        internal string Title { get; private set; } = title;
        public event PropertyChangedEventHandler? PropertyChanged;

        internal void Change(string title)
        {
            Title = title;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("CurrentBranchName"));
        }
    }
}
