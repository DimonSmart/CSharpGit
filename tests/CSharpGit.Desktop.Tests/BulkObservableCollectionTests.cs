using System.Collections.Specialized;
using System.ComponentModel;
using CSharpGit.Presentation.ViewModels;

namespace CSharpGit.Desktop.Tests;

public sealed class BulkObservableCollectionTests
{
    [Fact]
    public void ReplaceAllWithTenThousandItemsPublishesSingleReset()
    {
        var collection = new BulkObservableCollection<int>();
        var changes = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, args) => changes.Add(args);
        var items = Enumerable.Range(0, 10_000).ToArray();

        collection.ReplaceAll(items);

        Assert.Equal(10_000, collection.Count);
        Assert.Equal(items, collection.ToArray());
        Assert.Equal(NotifyCollectionChangedAction.Reset, Assert.Single(changes).Action);
    }

    [Fact]
    public void ReplaceAllPublishesCountAndIndexerPropertyChanges()
    {
        var collection = new BulkObservableCollection<int>();
        var propertyNames = new List<string?>();
        ((INotifyPropertyChanged)collection).PropertyChanged += (_, args) => propertyNames.Add(args.PropertyName);

        collection.ReplaceAll([1, 2, 3]);

        Assert.Equal(["Count", "Item[]"], propertyNames);
    }

    [Fact]
    public void ReplaceAllFullyReplacesExistingContentsWithSingleReset()
    {
        var collection = new BulkObservableCollection<int>();
        collection.ReplaceAll(Enumerable.Range(0, 10_000).ToArray());
        var changes = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, args) => changes.Add(args);
        var replacement = Enumerable.Range(20_000, 5_000).ToArray();

        collection.ReplaceAll(replacement);

        Assert.Equal(replacement, collection.ToArray());
        Assert.Equal(NotifyCollectionChangedAction.Reset, Assert.Single(changes).Action);
    }

    [Fact]
    public void ReplaceAllWithEmptySnapshotClearsWithSingleReset()
    {
        var collection = new BulkObservableCollection<int>();
        collection.ReplaceAll([1, 2, 3]);
        var changes = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, args) => changes.Add(args);

        collection.ReplaceAll(Array.Empty<int>());

        Assert.Empty(collection);
        Assert.Equal(NotifyCollectionChangedAction.Reset, Assert.Single(changes).Action);
    }

    [Fact]
    public void ReplaceAllPreservesSourceOrder()
    {
        var collection = new BulkObservableCollection<int>();

        collection.ReplaceAll([7, 3, 9, 1]);

        Assert.Equal([7, 3, 9, 1], collection);
    }

    [Fact]
    public void ReplaceAllDoesNotRetainMutableSourceStorage()
    {
        var collection = new BulkObservableCollection<int>();
        var source = new List<int> { 1, 2, 3 };

        collection.ReplaceAll(source);
        source.Clear();

        Assert.Equal([1, 2, 3], collection);
    }

    [Fact]
    public void EnumerableSourceIsEnumeratedOnce()
    {
        var collection = new BulkObservableCollection<int>();
        var enumerationCount = 0;

        IEnumerable<int> Values()
        {
            enumerationCount++;
            yield return 1;
            yield return 2;
            yield return 3;
        }

        collection.ReplaceAll(Values());

        Assert.Equal(1, enumerationCount);
        Assert.Equal([1, 2, 3], collection);
    }

    [Fact]
    public void ReplacingFromSelfIsANoOp()
    {
        var collection = new BulkObservableCollection<int>();
        collection.ReplaceAll([1, 2, 3]);
        var changes = new List<NotifyCollectionChangedEventArgs>();
        collection.CollectionChanged += (_, args) => changes.Add(args);

        collection.ReplaceAll(collection);

        Assert.Equal([1, 2, 3], collection);
        Assert.Empty(changes);
    }
}
