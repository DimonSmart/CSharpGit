using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Controls;

public sealed partial class HistoryReferenceBadge : UserControl
{
    public static readonly DependencyProperty ReferenceNameProperty = DependencyProperty.Register(
        nameof(ReferenceName),
        typeof(string),
        typeof(HistoryReferenceBadge),
        new PropertyMetadata(string.Empty, OnReferenceNameChanged));

    private bool _presentationContextSubscribed;

    public HistoryReferenceBadge()
    {
        InitializeComponent();
        Loaded += HistoryReferenceBadge_Loaded;
        Unloaded += HistoryReferenceBadge_Unloaded;
    }

    public string ReferenceName
    {
        get => (string?)GetValue(ReferenceNameProperty) ?? string.Empty;
        set => SetValue(ReferenceNameProperty, value);
    }

    private static void OnReferenceNameChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args) =>
        ((HistoryReferenceBadge)dependencyObject).UpdatePresentation();

    private void HistoryReferenceBadge_Loaded(object sender, RoutedEventArgs args)
    {
        if (!_presentationContextSubscribed)
        {
            HistoryReferencePresentationContext.Changed += PresentationContext_Changed;
            _presentationContextSubscribed = true;
        }

        UpdatePresentation();
    }

    private void HistoryReferenceBadge_Unloaded(object sender, RoutedEventArgs args)
    {
        if (!_presentationContextSubscribed)
            return;

        HistoryReferencePresentationContext.Changed -= PresentationContext_Changed;
        _presentationContextSubscribed = false;
    }

    private void PresentationContext_Changed(object? sender, EventArgs args) =>
        UpdatePresentation();

    private void UpdatePresentation()
    {
        ReferenceText.Text = ReferenceName;
        DefaultBranchIcon.Visibility =
            HistoryReferencePresentationContext.IsDefaultRemoteBranch(ReferenceName)
                ? Visibility.Visible
                : Visibility.Collapsed;
    }
}
