using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation;

/// <summary>Late-bound typed lookup of the application's canonical XAML resources.</summary>
internal static class UiStyles
{
    internal static T Resolve<T>(string key)
    {
        var resources = Application.Current?.Resources
            ?? throw new InvalidOperationException("Application resources have not been initialized.");
        if (TryFind(resources, key, out var resource) && resource is T typed)
            return typed;

        throw new InvalidOperationException(
            $"Required UI resource '{key}' of type {typeof(T).Name} was not found.");
    }

    private static bool TryFind(ResourceDictionary dictionary, string key, out object? value)
    {
        if (dictionary.TryGetValue(key, out value))
            return true;

        foreach (var merged in dictionary.MergedDictionaries)
        {
            if (TryFind(merged, key, out value))
                return true;
        }

        value = null;
        return false;
    }
}
