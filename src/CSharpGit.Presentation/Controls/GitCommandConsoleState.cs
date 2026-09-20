using System.Collections.ObjectModel;
using CSharpGit.Application.Abstractions;

namespace CSharpGit.Presentation.Controls;

internal sealed class GitCommandConsoleState
{
    public ObservableCollection<GitCommandConsoleItem> Items { get; } = [];
    public GitCommandFilter Filter { get; private set; } = GitCommandFilter.UserCommands;

    public void Reset(GitCommandFilter filter, IReadOnlyList<GitCommandActivity> activities)
    {
        ArgumentNullException.ThrowIfNull(activities);
        Filter = filter;
        Items.Clear();
        foreach (var activity in activities)
            Items.Add(new GitCommandConsoleItem(activity));
    }

    public void ApplyStarted(GitCommandActivity activity, Guid? evictedActivityId)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (evictedActivityId is { } evictedId)
            Remove(evictedId);
        if (!IsVisible(activity) || Find(activity.Id) is not null) return;
        Items.Insert(0, new GitCommandConsoleItem(activity));
    }

    public void ApplyLifecycle(GitCommandActivity activity, Guid? evictedActivityId = null)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (evictedActivityId is { } evictedId)
            Remove(evictedId);
        Find(activity.Id)?.Apply(activity);
    }

    public bool Remove(Guid id)
    {
        var item = Find(id);
        return item is not null && Items.Remove(item);
    }

    public GitCommandConsoleItem? Find(Guid id) =>
        Items.FirstOrDefault(item => item.Id == id);

    public bool IsVisible(GitCommandActivity activity) =>
        Filter == GitCommandFilter.AllCommands || activity.CommandKind == GitCommandKind.User;
}
