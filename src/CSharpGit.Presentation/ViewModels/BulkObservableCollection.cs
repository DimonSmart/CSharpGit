using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace CSharpGit.Presentation.ViewModels;

internal sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IReadOnlyList<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (ReferenceEquals(values, this)) return;

        Items.Clear();
        for (var index = 0; index < values.Count; index++)
            Items.Add(values[index]);

        PublishReset();
    }

    public void ReplaceAll(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (ReferenceEquals(values, this)) return;

        if (values is IReadOnlyList<T> snapshot)
        {
            ReplaceAll(snapshot);
            return;
        }

        ReplaceAll(values.ToArray());
    }

    private void PublishReset()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
