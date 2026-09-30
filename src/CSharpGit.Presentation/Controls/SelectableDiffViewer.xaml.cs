using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace CSharpGit.Presentation.Controls;

public sealed partial class SelectableDiffViewer : UserControl
{
    private static readonly CompactDiffLine[] EmptyLines = [];

    public SelectableDiffViewer()
    {
        InitializeComponent();
        AddPrimaryAccelerator(VirtualKey.A, selectAll: true);
        AddPrimaryAccelerator(VirtualKey.C, selectAll: false);
    }

    public string LogicalText => DiffText.Text;

    internal FrameworkElement? FirstRealizedRow =>
        RowsRepeater.TryGetElement(0) as FrameworkElement;

    public void SetLines(IReadOnlyList<CompactDiffLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var snapshot = lines.Count == 0 ? EmptyLines : lines.ToArray();
        var logicalText = new DiffLogicalText(snapshot);

        // Force native selection state to reset even when a different file has
        // exactly the same displayed diff text.
        DiffText.Text = string.Empty;
        RowsRepeater.ItemsSource = null;

        RowsRepeater.ItemsSource = snapshot;
        DiffText.Text = logicalText.Text;
    }

    public void Clear() => SetLines(EmptyLines);

    private void AddPrimaryAccelerator(VirtualKey key, bool selectAll)
    {
        var accelerator = new KeyboardAccelerator
        {
            Key = key,
            Modifiers = OperatingSystem.IsMacOS()
                ? VirtualKeyModifiers.Windows
                : VirtualKeyModifiers.Control
        };
        accelerator.Invoked += selectAll
            ? SelectAllAccelerator_Invoked
            : CopyAccelerator_Invoked;
        DiffText.KeyboardAccelerators.Add(accelerator);
    }

    private void SelectAllAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (string.IsNullOrEmpty(DiffText.Text)) return;

        DiffText.SelectAll();
        args.Handled = true;
    }

    private void CopyAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (string.IsNullOrEmpty(DiffText.SelectedText)) return;

        DiffText.CopySelectionToClipboard();
        args.Handled = true;
    }
}
