using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.Controls;
using CSharpGit.Presentation.Views;
using CSharpGit.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation.Dialogs;

// Window-scoped owner of modal Git command feedback, independent of the console view.
internal sealed class GitCommandFeedbackController(
    IGitCommandActivitySource activitySource,
    IAppSettingsService settings,
    GitCommandFeedbackView floatingView,
    FrameworkElement modalHost,
    FrameworkElement rootLayout,
    GitConsoleHostView console,
    Microsoft.UI.Dispatching.DispatcherQueue dispatcher,
    Func<bool> isShuttingDown)
{
    private readonly IGitCommandActivitySource _activitySource = activitySource;
    private readonly IAppSettingsService _settings = settings;
    private readonly GitCommandFeedbackView _floatingHost = floatingView;
    private readonly FrameworkElement _modalHost = modalHost;
    private readonly FrameworkElement _rootLayout = rootLayout;
    private readonly GitConsoleHostView _console = console;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher = dispatcher;
    private readonly Func<bool> _isShuttingDown = isShuttingDown;

    private GitCommandFeedbackCoordinator? _commandFeedback;
    private DispatcherTimer? _commandFeedbackTimer;
    private GitCommandFeedbackView? _floatingFeedbackView;
    private GitCommandFeedbackView? _cloneFeedbackView;
    private volatile bool _cloneDialogFeedbackActive;

    internal bool CloneDialogFeedbackActive
    {
        get => _cloneDialogFeedbackActive;
        set => _cloneDialogFeedbackActive = value;
    }

    internal void InitializeGitCommandFeedback()
    {
        _commandFeedback = new GitCommandFeedbackCoordinator(_activitySource);
        _floatingFeedbackView = _floatingHost;
        WireGitCommandFeedback(_floatingFeedbackView);
        _commandFeedbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(75)
        };
        _commandFeedbackTimer.Tick += (_, _) => RenderGitCommandFeedback();
        _settings.Changed += GitCommandFeedbackSettingsChanged;
        ApplyGitCommandFeedbackSettings();
    }

    private void GitCommandFeedbackSettingsChanged(object? sender, EventArgs args)
    {
        if (_isShuttingDown()) return;
        if (_dispatcher.HasThreadAccess)
            ApplyGitCommandFeedbackSettings();
        else
            _dispatcher.TryEnqueue(ApplyGitCommandFeedbackSettings);
    }

    private void ApplyGitCommandFeedbackSettings()
    {
        if (_isShuttingDown() || _commandFeedback is null) return;
        var enabled = _settings.ShowGitCommandProgressDialog;
        _commandFeedback.Configure(
            enabled, _settings.AutoCloseSuccessfulGitCommandFeedback,
            _settings.SuccessfulGitCommandFeedbackDurationSeconds);

        if (enabled)
            _commandFeedbackTimer?.Start();
        else
        {
            _commandFeedbackTimer?.Stop();
            RemoveFloatingGitCommandFeedback();
            if (_cloneFeedbackView is not null)
                _cloneFeedbackView.Visibility = Visibility.Collapsed;
        }
        RenderGitCommandFeedback();
    }

    internal bool RouteGitCommandFeedback(GitCommandActivityChangedEventArgs args) =>
        _commandFeedback?.Handle(args, _cloneDialogFeedbackActive) == true;

    internal void RenderGitCommandFeedback()
    {
        if (_isShuttingDown()) return;
        var state = _commandFeedback?.Capture(
            presentationAvailable: !_console.IsOpen || _cloneDialogFeedbackActive);
        if (state is not { IsVisible: true })
        {
            _modalHost.Visibility = Visibility.Collapsed;
            if (_cloneFeedbackView is not null)
                _cloneFeedbackView.Visibility = Visibility.Collapsed;
            return;
        }

        if (state.Owner == GitCommandFeedbackOwner.EmbeddedClone)
        {
            _modalHost.Visibility = Visibility.Collapsed;
            if (_cloneFeedbackView is not null && _cloneDialogFeedbackActive)
            {
                _cloneFeedbackView.Apply(state);
                _cloneFeedbackView.Visibility = Visibility.Visible;
            }
            return;
        }

        if (_cloneFeedbackView is not null)
            _cloneFeedbackView.Visibility = Visibility.Collapsed;

        // Git Console remains the explicit destination for this activity.
        if (_console.IsOpen)
        {
            _modalHost.Visibility = Visibility.Collapsed;
            return;
        }

        if (_floatingFeedbackView is null) return;
        _floatingFeedbackView.Width = Math.Min(480, Math.Max(180, _rootLayout.ActualWidth - 32));
        _floatingFeedbackView.Apply(state);
        // Focus the modal host itself, not a text box: merely opening the dialog
        // must not pause the success countdown as though the user interacted.
        if (_modalHost.Visibility != Visibility.Visible)
        {
            _modalHost.Visibility = Visibility.Visible;
            _floatingFeedbackView.Focus(FocusState.Programmatic);
        }
    }

    private void WireGitCommandFeedback(GitCommandFeedbackView view)
    {
        view.AutoCloseCancellationRequested += id =>
        {
            if (_commandFeedback?.CancelAutoClose(id) != true)
                return false;

            RenderGitCommandFeedback();
            return true;
        };
        view.DismissRequested += id =>
        {
            _commandFeedback?.Dismiss(id);
            RenderGitCommandFeedback();
        };
        view.OpenConsoleRequested += id =>
        {
            // Get(id) can still return a detached Running activity. Only actual
            // Console history rows can be selected, never an approximate match.
            if (!_activitySource.GetSnapshot(GitCommandFilter.AllCommands)
                    .Any(item => item.Id == id))
            {
                view.ShowUnavailableMessage();
                return;
            }
            _commandFeedback?.Dismiss(id);
            RenderGitCommandFeedback();
            _console.Open(id, manualOpen: true);
        };
        view.InteractionChanged += (hovered, focused) =>
            _commandFeedback?.Interacting(hovered, focused);
    }

    internal GitCommandFeedbackView? CreateCloneGitCommandFeedback()
    {
        if (!_settings.ShowGitCommandProgressDialog)
            return null;
        var view = new GitCommandFeedbackView
        {
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxHeight = 270
        };
        WireGitCommandFeedback(view);
        _cloneFeedbackView = view;
        return view;
    }

    internal void ReleaseCloneGitCommandFeedback(GitCommandFeedbackView view)
    {
        if (ReferenceEquals(_cloneFeedbackView, view))
            _cloneFeedbackView = null;
        view.Visibility = Visibility.Collapsed;
    }

    private void RemoveFloatingGitCommandFeedback()
    {
        _modalHost.Visibility = Visibility.Collapsed;
    }

    internal void ShutdownGitCommandFeedback()
    {
        _settings.Changed -= GitCommandFeedbackSettingsChanged;
        _commandFeedback?.Configure(false, false);
        _commandFeedbackTimer?.Stop();
        RemoveFloatingGitCommandFeedback();
        _cloneDialogFeedbackActive = false;
        _cloneFeedbackView = null;
        _commandFeedback = null;
    }
}
