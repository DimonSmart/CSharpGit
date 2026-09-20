using System.ComponentModel;
using System.Globalization;
using CSharpGit.Application.Abstractions;
using CSharpGit.Domain;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private const double RepositoryMaintenanceContentWidth = 460;
    private IRepositoryMaintenanceService? _repositoryMaintenanceService;
    private bool _repositoryMaintenanceInProgress;

    internal bool IsRepositoryMaintenanceInProgress => _repositoryMaintenanceInProgress;

    public MainPage(
        OpenRepositoryViewModel viewModel,
        IReferenceHistoryService referenceHistoryService,
        IReferenceService referenceService,
        IWorkingTreeDiffService workingTreeDiffService,
        IRepositoryFileVersionService fileVersionService,
        IDesktopShellService desktopShellService,
        IRepositoryPathService repositoryPathService,
        IGitToolsService gitToolsService,
        IRepositorySnapshotService repositorySnapshotService,
        IRepositoryHistoryRewriteService repositoryHistoryRewriteService,
        IRepositoryMaintenanceService repositoryMaintenanceService)
        : this(
            viewModel,
            referenceHistoryService,
            referenceService,
            workingTreeDiffService,
            fileVersionService,
            desktopShellService,
            repositoryPathService,
            gitToolsService,
            repositorySnapshotService,
            repositoryHistoryRewriteService)
    {
        _repositoryMaintenanceService = repositoryMaintenanceService
            ?? throw new ArgumentNullException(nameof(repositoryMaintenanceService));
        _viewModel.PropertyChanged += RepositoryMaintenanceViewModel_PropertyChanged;
        UpdateOptimizeRepositoryAvailability();
    }

    private void RepositoryMaintenanceViewModel_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(OpenRepositoryViewModel.Repository)
            or nameof(OpenRepositoryViewModel.IsBusy)
            or nameof(OpenRepositoryViewModel.CurrentOperation))
        {
            UpdateOptimizeRepositoryAvailability();
        }
    }

    private bool CanStartRepositoryMaintenance() =>
        _repositoryMaintenanceService is not null
        && _viewModel.Repository is not null
        && !_viewModel.IsBusy
        && _viewModel.CurrentOperation == RepositoryOperation.None
        && !_repositoryMaintenanceInProgress;

    private void UpdateOptimizeRepositoryAvailability()
    {
        OptimizeRepositoryMenuItem.IsEnabled = CanStartRepositoryMaintenance();
    }

    private async void OptimizeRepository_Click(object sender, RoutedEventArgs e)
    {
        if (!CanStartRepositoryMaintenance()
            || _repositoryMaintenanceService is not { } service
            || _viewModel.Repository is not { } repository)
        {
            return;
        }

        RepositoryStorageStatistics before;
        try
        {
            before = await service.GetStorageStatisticsAsync(repository);
        }
        catch (Exception exception)
        {
            await ShowRepositoryMaintenanceMessageAsync(
                "Could not read repository statistics",
                exception.Message);
            return;
        }

        if (!CanStartRepositoryMaintenance()
            || !Equals(repository, _viewModel.Repository))
        {
            await ShowRepositoryMaintenanceMessageAsync(
                "Optimization not started",
                "Repository state changed before optimization could start.");
            return;
        }

        await ShowRepositoryMaintenanceDialogAsync(repository, before, service);
    }

    private async Task ShowRepositoryMaintenanceDialogAsync(
        Repository repository,
        RepositoryStorageStatistics before,
        IRepositoryMaintenanceService service)
    {
        var initial = CreateRepositoryMaintenanceInitialContent(before);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Optimize repository",
            Content = initial.Content,
            PrimaryButtonText = "Optimize",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        dialog.Closing += (_, args) =>
        {
            if (_repositoryMaintenanceInProgress)
                args.Cancel = true;
        };

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            args.Cancel = true;
            var deferral = args.GetDeferral();
            try
            {
                if (_repositoryMaintenanceInProgress)
                    return;

                if (!CanStartRepositoryMaintenance()
                    || !Equals(repository, _viewModel.Repository))
                {
                    ShowRepositoryMaintenanceNotStartedState(
                        dialog,
                        "Repository state changed before optimization could start.");
                    return;
                }

                var options = new RepositoryGcOptions(
                    Aggressive: initial.Aggressive.IsChecked == true,
                    PruneNow: initial.PruneNow.IsChecked == true,
                    KeepLargestPack: initial.KeepLargestPack.IsChecked == true);

                _repositoryMaintenanceInProgress = true;
                UpdateOptimizeRepositoryAvailability();
                ShowRepositoryMaintenanceRunningState(dialog);

                RepositoryStorageStatistics? after = null;
                Exception? afterStatisticsFailure = null;

                var outcome = await _viewModel.RunRepositoryMaintenanceMutationAsync(
                    async () =>
                    {
                        await service.GarbageCollectAsync(repository, options);
                        try
                        {
                            after = await service.GetStorageStatisticsAsync(repository);
                        }
                        catch (Exception exception)
                        {
                            afterStatisticsFailure = exception;
                        }
                    },
                    includeHistory: _viewModel.ShowReflog,
                    additionalRefresh: () => RefreshWorktreePresentationAsync(throwOnError: true));

                _repositoryMaintenanceInProgress = false;
                UpdateOptimizeRepositoryAvailability();

                if (!outcome.Started)
                {
                    ShowRepositoryMaintenanceNotStartedState(
                        dialog,
                        "Another repository mutation started first. Optimization was not run.");
                    return;
                }

                if (!outcome.MutationSucceeded)
                {
                    ShowRepositoryMaintenanceFailureState(dialog);
                    return;
                }

                ShowRepositoryMaintenanceSuccessState(
                    dialog,
                    before,
                    after,
                    afterStatisticsFailure,
                    outcome.RefreshFailure);
            }
            finally
            {
                if (_repositoryMaintenanceInProgress)
                {
                    _repositoryMaintenanceInProgress = false;
                    UpdateOptimizeRepositoryAvailability();
                }

                deferral.Complete();
            }
        };

        await dialog.ShowAsync();
    }

    private static RepositoryMaintenanceInitialContent CreateRepositoryMaintenanceInitialContent(
        RepositoryStorageStatistics before)
    {
        var content = new StackPanel
        {
            Spacing = 12,
            Width = RepositoryMaintenanceContentWidth
        };

        content.Children.Add(new TextBlock
        {
            Text = "Current object storage",
            FontWeight = FontWeights.SemiBold
        });
        content.Children.Add(CreateCurrentStatisticsGrid(before));
        content.Children.Add(new TextBlock
        {
            Text = "Options",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 0, 0)
        });

        var aggressive = CreateRepositoryGcOption(
            "Aggressive optimization",
            "More thorough recompression. Can take much longer.");
        var pruneNow = CreateRepositoryGcOption(
            "Prune unreachable objects now",
            "Immediately remove unreachable objects instead of keeping them temporarily.");
        var keepLargestPack = CreateRepositoryGcOption(
            "Keep largest pack",
            "Leave the largest existing pack untouched. Useful for large repositories.");

        var pruneWarning = new TextBlock
        {
            Text = "Unreachable objects will be removed immediately.\n" +
                   "Recently lost objects may become unrecoverable.\n\n" +
                   "Do not use this option while another Git process may be writing to this repository.",
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(28, 0, 0, 0)
        };
        pruneNow.Checked += (_, _) => pruneWarning.Visibility = Visibility.Visible;
        pruneNow.Unchecked += (_, _) => pruneWarning.Visibility = Visibility.Collapsed;

        content.Children.Add(aggressive);
        content.Children.Add(pruneNow);
        content.Children.Add(pruneWarning);
        content.Children.Add(keepLargestPack);

        return new RepositoryMaintenanceInitialContent(
            content,
            aggressive,
            pruneNow,
            keepLargestPack);
    }

    private static CheckBox CreateRepositoryGcOption(
        string title,
        string description)
    {
        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock { Text = title });
        text.Children.Add(new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75
        });

        return new CheckBox
        {
            IsChecked = false,
            Content = text,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private static Grid CreateCurrentStatisticsGrid(
        RepositoryStorageStatistics statistics)
    {
        var grid = CreateStatisticsGrid(columnCount: 2, rowCount: 4);
        AddStatisticsCell(grid, 0, 0, "Loose objects");
        AddStatisticsCell(grid, 0, 1, FormatCount(statistics.LooseObjectCount), true);
        AddStatisticsCell(grid, 1, 0, "Packed objects");
        AddStatisticsCell(grid, 1, 1, FormatCount(statistics.PackedObjectCount), true);
        AddStatisticsCell(grid, 2, 0, "Pack files");
        AddStatisticsCell(grid, 2, 1, FormatCount(statistics.PackCount), true);
        AddStatisticsCell(grid, 3, 0, "Object storage");
        AddStatisticsCell(grid, 3, 1, FormatStorageSize(statistics.ObjectStorageSizeBytes), true);
        return grid;
    }

    private static Grid CreateBeforeAfterStatisticsGrid(
        RepositoryStorageStatistics before,
        RepositoryStorageStatistics after)
    {
        var grid = CreateStatisticsGrid(columnCount: 3, rowCount: 5);
        AddStatisticsCell(grid, 0, 1, "Before", true);
        AddStatisticsCell(grid, 0, 2, "After", true);

        AddStatisticsCell(grid, 1, 0, "Loose objects");
        AddStatisticsCell(grid, 1, 1, FormatCount(before.LooseObjectCount), true);
        AddStatisticsCell(grid, 1, 2, FormatCount(after.LooseObjectCount), true);

        AddStatisticsCell(grid, 2, 0, "Packed objects");
        AddStatisticsCell(grid, 2, 1, FormatCount(before.PackedObjectCount), true);
        AddStatisticsCell(grid, 2, 2, FormatCount(after.PackedObjectCount), true);

        AddStatisticsCell(grid, 3, 0, "Pack files");
        AddStatisticsCell(grid, 3, 1, FormatCount(before.PackCount), true);
        AddStatisticsCell(grid, 3, 2, FormatCount(after.PackCount), true);

        AddStatisticsCell(grid, 4, 0, "Object storage");
        AddStatisticsCell(grid, 4, 1, FormatStorageSize(before.ObjectStorageSizeBytes), true);
        AddStatisticsCell(grid, 4, 2, FormatStorageSize(after.ObjectStorageSizeBytes), true);
        return grid;
    }

    private static Grid CreateStatisticsGrid(int columnCount, int rowCount)
    {
        var grid = new Grid
        {
            ColumnSpacing = 16,
            RowSpacing = 4
        };

        for (var column = 0; column < columnCount; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = column == 0
                    ? new GridLength(1, GridUnitType.Star)
                    : GridLength.Auto
            });
        }

        for (var row = 0; row < rowCount; row++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        return grid;
    }

    private static void AddStatisticsCell(
        Grid grid,
        int row,
        int column,
        string text,
        bool alignRight = false)
    {
        var block = new TextBlock
        {
            Text = text,
            HorizontalAlignment = alignRight
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Left
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    private static void ShowRepositoryMaintenanceRunningState(ContentDialog dialog)
    {
        dialog.Title = "Optimize repository";
        dialog.PrimaryButtonText = string.Empty;
        dialog.CloseButtonText = string.Empty;
        dialog.DefaultButton = ContentDialogButton.None;
        dialog.Content = new StackPanel
        {
            Spacing = 12,
            MinWidth = 420,
            Children =
            {
                new TextBlock { Text = "Optimizing repository…" },
                new ProgressRing
                {
                    IsActive = true,
                    Width = 32,
                    Height = 32,
                    HorizontalAlignment = HorizontalAlignment.Center
                }
            }
        };
    }

    private void ShowRepositoryMaintenanceFailureState(ContentDialog dialog)
    {
        var content = new StackPanel { Spacing = 12, MinWidth = 460 };
        content.Children.Add(new TextBlock
        {
            Text = "Git GC did not complete successfully.",
            TextWrapping = TextWrapping.Wrap
        });

        var viewConsole = new Button { Content = "View Git console" };
        viewConsole.Click += (_, _) =>
        {
            dialog.Hide();
            OpenGitConsole(null, manualOpen: true);
        };
        content.Children.Add(viewConsole);

        dialog.Title = "Optimization failed";
        dialog.Content = content;
        dialog.PrimaryButtonText = string.Empty;
        dialog.CloseButtonText = "Close";
        dialog.DefaultButton = ContentDialogButton.Close;
    }

    private static void ShowRepositoryMaintenanceNotStartedState(
        ContentDialog dialog,
        string message)
    {
        dialog.Title = "Optimization not started";
        dialog.Content = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520
        };
        dialog.PrimaryButtonText = string.Empty;
        dialog.CloseButtonText = "Close";
        dialog.DefaultButton = ContentDialogButton.Close;
    }

    private static void ShowRepositoryMaintenanceSuccessState(
        ContentDialog dialog,
        RepositoryStorageStatistics before,
        RepositoryStorageStatistics? after,
        Exception? afterStatisticsFailure,
        Exception? refreshFailure)
    {
        var content = new StackPanel
        {
            Spacing = 12,
            Width = RepositoryMaintenanceContentWidth
        };

        if (after is not null)
        {
            content.Children.Add(CreateBeforeAfterStatisticsGrid(before, after));

            var delta = after.ObjectStorageSizeBytes - before.ObjectStorageSizeBytes;
            var deltaText = delta switch
            {
                < 0 => $"Object storage saved    {FormatStorageSize(-delta)}",
                0 => "Object storage change   0 B",
                _ => $"Object storage change   +{FormatStorageSize(delta)}"
            };
            content.Children.Add(new TextBlock
            {
                Text = deltaText,
                FontWeight = FontWeights.SemiBold
            });
        }

        if (afterStatisticsFailure is not null)
        {
            content.Children.Add(new TextBlock
            {
                Text = "Git GC completed successfully, but updated object storage statistics could not be read.",
                TextWrapping = TextWrapping.Wrap
            });
        }

        if (refreshFailure is not null)
        {
            content.Children.Add(new TextBlock
            {
                Text = "Git GC completed successfully, but some repository information could not be refreshed.\n\n" +
                       "Use Refresh to reread repository state if necessary.",
                TextWrapping = TextWrapping.Wrap
            });
        }

        dialog.Title = "Repository optimized";
        dialog.Content = content;
        dialog.PrimaryButtonText = string.Empty;
        dialog.CloseButtonText = "Close";
        dialog.DefaultButton = ContentDialogButton.Close;
    }

    private async Task ShowRepositoryMaintenanceMessageAsync(
        string title,
        string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
                MaxWidth = 620
            },
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close
        };

        await dialog.ShowAsync();
    }

    private static string FormatCount(long value) =>
        value.ToString("N0", CultureInfo.CurrentCulture);

    private static string FormatStorageSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes.ToString("N0", CultureInfo.CurrentCulture)} B";

        string[] units = ["KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = -1;
        do
        {
            value /= 1024d;
            unit++;
        }
        while (value >= 1024d && unit < units.Length - 1);

        var format = value >= 100d
            ? "N0"
            : value >= 10d
                ? "N1"
                : "N2";
        return $"{value.ToString(format, CultureInfo.CurrentCulture)} {units[unit]}";
    }

    private sealed record RepositoryMaintenanceInitialContent(
        StackPanel Content,
        CheckBox Aggressive,
        CheckBox PruneNow,
        CheckBox KeepLargestPack);
}
