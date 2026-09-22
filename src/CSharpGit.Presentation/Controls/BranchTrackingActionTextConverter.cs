using CSharpGit.Domain;
using Microsoft.UI.Xaml.Data;

namespace CSharpGit.Presentation.Controls;

public sealed class BranchTrackingActionTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var action = parameter as string;
        if (value is not GitBranch { IsCurrent: true } branch)
            return action == "PushMenu" ? "Push" : action == "Push" ? "Push ▼" : "Pull";

        if (string.IsNullOrWhiteSpace(branch.Upstream))
        {
            return action switch
            {
                "Push" => "Publish branch… ▼",
                "PushMenu" => "Publish branch…",
                _ => "Pull"
            };
        }

        return action switch
        {
            "Pull" when branch.Behind > 0 => $"Pull ↓{branch.Behind}",
            "Push" when branch.Ahead > 0 => $"Push ↑{branch.Ahead} ▼",
            "Push" => "Push ▼",
            "PushMenu" => "Push",
            _ => "Pull"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
