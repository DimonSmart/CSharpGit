using CSharpGit.Application.Abstractions;
using CSharpGit.Presentation.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CSharpGit.Presentation;

public sealed partial class MainPage
{
    private GitCommandFeedbackCoordinator? _commandFeedback;
    private DispatcherTimer? _commandFeedbackTimer;
    private GitCommandFeedbackView? _floatingFeedbackView;
    private GitCommandFeedbackView? _cloneFeedbackView;
    private volatile bool _cloneDialogFeedbackActive;

    private void InitializeGitCommandFeedback()
    {
        _commandFeedback = new GitCommandFeedbackCoordinator(_gitCommandActivitySource);
        _floatingFeedbackView = FloatingGitCommandFeedbackView;
        WireGitCommandFeedback(_floatingFeedbackView);
        _commandFeedbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(75)
        };
        _commandFeedbackTimer.Tick += (_, _) => RenderGitCommandFeedback();
        _gitConsoleSettings.Changed += GitCommandFeedbackSettingsChanged;
        ApplyGitCommandFeedbackSettings();
    }

    private void GitCommandFeedbackSettingsChanged(object? sender, EventArgs args)
    {
        if (IsShuttingDown) return;
        if (DispatcherQueue.HasThreadAccess)
            ApplyGitCommandFeedbackSettings();
        else
            DispatcherQueue.TryEnqueue(ApplyGitCommandFeedbackSettings);
    }

    private void ApplyGitCommandFeedbackSettings()
    {
        if (IsShuttingDown || _commandFeedback is null) return;
        var enabled = _gitConsoleSettings.ShowGitCommandProgressDialog;
        _commandFeedback.Configure(
            enabled, _gitConsoleSettings.AutoCloseSuccessfulGitCommandFeedback,
            _gitConsoleSettings.SuccessfulGitCommandFeedbackDurationSeconds);

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

    private bool RouteGitCommandFeedback(GitCommandActivityChangedEventArgs args) =>
        _commandFeedback?.Handle(args, _cloneDialogFeedbackActive) == true;

    private void RenderGitCommandFeedback()
    {
        if (IsShuttingDown) return;
        var state = _commandFeedback?.Capture();
        if (state is not { IsVisible: true })
        {
            GitCommandFeedbackModalHost.Visibility = Visibility.Collapsed;
            if (_cloneFeedbackView is not null)
                _cloneFeedbackView.Visibility = Visibility.Collapsed;
            return;
        }

        if (state.Owner == GitCommandFeedbackOwner.EmbeddedClone)
        {
            GitCommandFeedbackModalHost.Visibility = Visibility.Collapsed;
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
        if (_gitConsoleOpen)
        {
            GitCommandFeedbackModalHost.Visibility = Visibility.Collapsed;
            return;
        }

        if (_floatingFeedbackView is null) return;
        _floatingFeedbackView.Width = Math.Min(480, Math.Max(180, RootLayout.ActualWidth - 32));
        _floatingFeedbackView.Apply(state);
        // Last RootLayout child supplies the modal dimmer and blocks workspace input.
        GitCommandFeedbackModalHost.Visibility = Visibility.Visible;
    }

    private void WireGitCommandFeedback(GitCommandFeedbackView view)
    {
        view.DismissRequested += id =>
        {
            _commandFeedback?.Dismiss(id);
            RenderGitCommandFeedback();
        };
        view.OpenConsoleRequested += id =>
        {
            // Get(id) can still return a detached Running activity. Only actual
            // Console history rows can be selected, never an approximate match.
            if (!_gitCommandActivitySource.GetSnapshot(GitCommandFilter.AllCommands)
                    .Any(item => item.Id == id))
            {
                view.ShowUnavailableMessage();
                return;
            }
            _commandFeedback?.Dismiss(id);
            RenderGitCommandFeedback();
            OpenGitConsole(id, manualOpen: true);
        };
        view.InteractionChanged += (hovered, focused) =>
            _commandFeedback?.Interacting(hovered, focused);
    }

    private GitCommandFeedbackView? CreateCloneGitCommandFeedback()
    {
        if (!_gitConsoleSettings.ShowGitCommandProgressDialog)
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

    private void RemoveFloatingGitCommandFeedback()
    {
        GitCommandFeedbackModalHost.Visibility = Visibility.Collapsed;
    }

    private void ShutdownGitCommandFeedback()
    {
        _gitConsoleSettings.Changed -= GitCommandFeedbackSettingsChanged;
        _commandFeedback?.Configure(false, false);
        _commandFeedbackTimer?.Stop();
        RemoveFloatingGitCommandFeedback();
        _cloneDialogFeedbackActive = false;
        _cloneFeedbackView = null;
        _commandFeedback = null;
    }
}
