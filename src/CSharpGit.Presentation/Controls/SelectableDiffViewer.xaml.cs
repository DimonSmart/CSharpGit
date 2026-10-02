using CSharpGit.Domain;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace CSharpGit.Presentation.Controls;

public sealed partial class SelectableDiffViewer : UserControl
{
    private static readonly CompactDiffLine[] EmptyLines = [];
    private readonly DiffViewportResetGate _viewportResetGate = new();

    public SelectableDiffViewer()
    {
        InitializeComponent();
        AddPrimaryAccelerator(VirtualKey.A, selectAll: true);
        AddPrimaryAccelerator(VirtualKey.C, selectAll: false);
        DiffScroller.PointerPressed += DiffScroller_UserInteraction;
        DiffScroller.PointerWheelChanged += DiffScroller_UserInteraction;
        DiffScroller.KeyDown += DiffScroller_KeyDown;
    }

    public string LogicalText => DiffText.Text;

    internal FrameworkElement? FirstRealizedRow =>
        RowsRepeater.TryGetElement(0) as FrameworkElement;

    public void SetLines(
        IReadOnlyList<CompactDiffLine> lines,
        IReadOnlyList<DiffDiagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var ticket = _viewportResetGate.BeginReplacement();
        var snapshot = lines.Count == 0 ? EmptyLines : lines.ToArray();
        var logicalText = new DiffLogicalText(snapshot);

        DiffText.Text = string.Empty;
        RowsRepeater.ItemsSource = null;

        RowsRepeater.ItemsSource = snapshot;
        DiffText.Text = logicalText.Text;
        SetDiagnostics(diagnostics ?? []);
        ResetViewport(ticket);

        DispatcherQueue.TryEnqueue(() => ResetViewport(ticket));
    }

    public void Clear() => SetLines(EmptyLines, []);

    private void ResetViewport(DiffViewportResetTicket ticket)
    {
        if (!_viewportResetGate.IsCurrent(ticket)) return;
        DiffScroller.ChangeView(0d, 0d, null, true);
    }

    private void SetDiagnostics(IReadOnlyList<DiffDiagnostic> diagnostics)
    {
        var messages = diagnostics
            .Select(DescribeDiagnostic)
            .Where(message => message is not null)
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        DiffDiagnosticsInfo.Message = string.Join(" · ", messages);
        DiffDiagnosticsInfo.Visibility = messages.Length == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private static string? DescribeDiagnostic(DiffDiagnostic diagnostic) => diagnostic.Kind switch
    {
        DiffDiagnosticKind.Utf8BomAdded => "UTF-8 BOM added",
        DiffDiagnosticKind.Utf8BomRemoved => "UTF-8 BOM removed",
        DiffDiagnosticKind.LineEndingsChanged when
            diagnostic.OriginalLineEnding is { } original &&
            diagnostic.ChangedLineEnding is { } changed =>
            $"Line endings changed: {DescribeLineEnding(original)} → {DescribeLineEnding(changed)}",
        DiffDiagnosticKind.NoFinalNewline => null,
        _ => null
    };

    private static string DescribeLineEnding(DiffTextLineEnding ending) => ending switch
    {
        DiffTextLineEnding.Lf => "LF",
        DiffTextLineEnding.CrLf => "CRLF",
        _ => ending.ToString()
    };

    private void DiffScroller_UserInteraction(object sender, PointerRoutedEventArgs args) =>
        _viewportResetGate.RegisterInteraction();

    private void DiffScroller_KeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key is VirtualKey.Up or VirtualKey.Down or VirtualKey.Left or VirtualKey.Right
            or VirtualKey.PageUp or VirtualKey.PageDown or VirtualKey.Home or VirtualKey.End)
            _viewportResetGate.RegisterInteraction();
    }

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
