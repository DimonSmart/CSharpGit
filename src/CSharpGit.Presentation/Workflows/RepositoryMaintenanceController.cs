using System.ComponentModel;
using CSharpGit.Presentation.ViewModels;
using Microsoft.UI.Xaml;

namespace CSharpGit.Presentation.Workflows;

// Owns the repository maintenance dialog and its window-scoped subscriptions.
internal sealed class RepositoryMaintenanceController
{
    private readonly OpenRepositoryViewModel _repository;
    private readonly RepositoryMaintenanceViewModel _maintenance;
    private readonly Func<XamlRoot?> _xamlRoot;
    private readonly Func<bool> _isShuttingDown;
    private readonly Action<bool> _setOptimizeAvailability;
    private readonly Action _openGitConsole;
    private readonly Action<Exception, string> _reportUiFailure;
    private bool _initialized;

    internal RepositoryMaintenanceController(
        OpenRepositoryViewModel repository,
        RepositoryMaintenanceViewModel maintenance,
        Func<XamlRoot?> xamlRoot,
        Func<bool> isShuttingDown,
        Action<bool> setOptimizeAvailability,
        Action openGitConsole,
        Action<Exception, string> reportUiFailure)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _maintenance = maintenance ?? throw new ArgumentNullException(nameof(maintenance));
        _xamlRoot = xamlRoot ?? throw new ArgumentNullException(nameof(xamlRoot));
        _isShuttingDown = isShuttingDown ?? throw new ArgumentNullException(nameof(isShuttingDown));
        _setOptimizeAvailability = setOptimizeAvailability ?? throw new ArgumentNullException(nameof(setOptimizeAvailability));
        _openGitConsole = openGitConsole ?? throw new ArgumentNullException(nameof(openGitConsole));
        _reportUiFailure = reportUiFailure ?? throw new ArgumentNullException(nameof(reportUiFailure));
    }

    internal void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        _maintenance.PropertyChanged += Maintenance_PropertyChanged;
        _repository.PropertyChanged += Repository_PropertyChanged;
        UpdateOptimizeAvailability();
    }

    internal void Shutdown()
    {
        if (!_initialized) return;
        _initialized = false;
        _repository.PropertyChanged -= Repository_PropertyChanged;
        _maintenance.PropertyChanged -= Maintenance_PropertyChanged;
    }

    private void Maintenance_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!_initialized) return;
        if (args.PropertyName is nameof(RepositoryMaintenanceViewModel.IsRunning)
            or nameof(RepositoryMaintenanceViewModel.CanStart))
            UpdateOptimizeAvailability();
    }

    private void Repository_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!_initialized) return;
        if (args.PropertyName == nameof(OpenRepositoryViewModel.Repository))
            _maintenance.Invalidate();

        if (args.PropertyName is nameof(OpenRepositoryViewModel.Repository)
            or nameof(OpenRepositoryViewModel.IsBusy)
            or nameof(RepositoryOperationsViewModel.CurrentOperation))
            UpdateOptimizeAvailability();
    }

    private void UpdateOptimizeAvailability() =>
        _setOptimizeAvailability(_maintenance.CanStart);

    internal async Task OptimizeAsync()
    {
        if (!_initialized || !_maintenance.CanStart
            || _repository.Repository is not { } repository)
            return;

        if (_xamlRoot() is not { } root) return;
        var dialog = new RepositoryMaintenanceDialog(root, _openGitConsole);

        try
        {
            if (!await _maintenance.PrepareAsync(repository))
            {
                if (!ReferenceEquals(repository, _repository.Repository) || _isShuttingDown())
                    return;

                if (_maintenance.PreparationFailure is { } failure)
                    await dialog.ShowMessageAsync("Could not read repository statistics", failure.Message);
                else
                    await dialog.ShowMessageAsync("Optimization not started",
                        "Repository state changed before optimization could start.");
                return;
            }

            if (!ReferenceEquals(repository, _repository.Repository) || _isShuttingDown())
                return;

            await dialog.ShowAsync(_maintenance, repository);
        }
        catch (OperationCanceledException) when (_isShuttingDown())
        {
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _reportUiFailure(exception, "Repository optimization");
            if (!_isShuttingDown() && ReferenceEquals(repository, _repository.Repository))
            {
                try { await dialog.ShowMessageAsync("Optimization not started", exception.Message); }
                catch (Exception notificationError)
                {
                    _reportUiFailure(notificationError, "Repository optimization error dialog");
                }
            }
        }
    }
}
