using CSharpGit.Application.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace CSharpGit.Presentation.Controls;

public sealed partial class AuthorAvatar : UserControl
{
    private static readonly Color[] BackgroundPalette =
    [
        Color.FromArgb(255, 54, 92, 141),
        Color.FromArgb(255, 76, 105, 75),
        Color.FromArgb(255, 122, 76, 118),
        Color.FromArgb(255, 145, 87, 52),
        Color.FromArgb(255, 63, 112, 118),
        Color.FromArgb(255, 93, 84, 142),
        Color.FromArgb(255, 112, 96, 62),
        Color.FromArgb(255, 77, 104, 126)
    ];

    private IAuthorAvatarService? _avatarService;
    private IAppSettingsService? _settings;
    private readonly AuthorAvatarRequestGate _requestGate = new();
    private readonly AuthorAvatarLifecycleState _lifecycleState = new();
    private bool _settingsSubscribed;
    private int _refreshScheduled;
    private long _serviceGeneration;
    private AuthorAvatarLookupKey? _displayedRemoteKey;
    private string? _resolvedImagePath;
    private string? _fallbackIdentity;

    public AuthorAvatar()
    {
        HistoryRenderDiagnostics.AuthorAvatarCreated();
        InitializeComponent();
        Loaded += AuthorAvatar_Loaded;
        Unloaded += AuthorAvatar_Unloaded;
        ApplySize();
    }

    public static readonly DependencyProperty AuthorNameProperty = DependencyProperty.Register(
        nameof(AuthorName),
        typeof(string),
        typeof(AuthorAvatar),
        new PropertyMetadata(string.Empty, IdentityPropertyChanged));

    public static readonly DependencyProperty AuthorEmailProperty = DependencyProperty.Register(
        nameof(AuthorEmail),
        typeof(string),
        typeof(AuthorAvatar),
        new PropertyMetadata(string.Empty, IdentityPropertyChanged));

    public static readonly DependencyProperty AvatarSizeProperty = DependencyProperty.Register(
        nameof(AvatarSize),
        typeof(double),
        typeof(AuthorAvatar),
        new PropertyMetadata(18d, AvatarSizePropertyChanged));

    public string AuthorName
    {
        get => (string?)GetValue(AuthorNameProperty) ?? string.Empty;
        set => SetValue(AuthorNameProperty, value);
    }

    public string AuthorEmail
    {
        get => (string?)GetValue(AuthorEmailProperty) ?? string.Empty;
        set => SetValue(AuthorEmailProperty, value);
    }

    public double AvatarSize
    {
        get => (double)GetValue(AvatarSizeProperty);
        set => SetValue(AvatarSizeProperty, value);
    }

    internal void Configure(
        IAuthorAvatarService avatarService,
        IAppSettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(avatarService);
        ArgumentNullException.ThrowIfNull(settings);
        HistoryRenderDiagnostics.ExplicitAvatarConfigured();

        if (!ReferenceEquals(_settings, settings))
            DetachSettings();

        if (!ReferenceEquals(_avatarService, avatarService))
        {
            _avatarService = avatarService;
            Interlocked.Increment(ref _serviceGeneration);
        }

        _settings = settings;
        if (IsLoaded)
            AttachSettings();

        RefreshNow();
    }

    private static void IdentityPropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (Equals(args.OldValue, args.NewValue))
            return;

        HistoryRenderDiagnostics.AvatarIdentityChanged();
        ((AuthorAvatar)dependencyObject).ScheduleRefresh();
    }

    private static void AvatarSizePropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args) =>
        ((AuthorAvatar)dependencyObject).ApplySize();

    private void AuthorAvatar_Loaded(object sender, RoutedEventArgs args)
    {
        HistoryRenderDiagnostics.AvatarLoaded();
        EnsureServices();
        AttachSettings();
        RefreshNow();
    }

    private void AuthorAvatar_Unloaded(object sender, RoutedEventArgs args)
    {
        HistoryRenderDiagnostics.AvatarUnloaded();
        DetachSettings();
    }

    private void EnsureServices()
    {
        if (_avatarService is not null && _settings is not null)
            return;

        if (!AuthorAvatarServiceContext.TryGet(out var avatarService, out var settings))
            return;

        if (!ReferenceEquals(_avatarService, avatarService))
        {
            _avatarService = avatarService;
            Interlocked.Increment(ref _serviceGeneration);
        }

        _settings = settings;
    }

    private void AttachSettings()
    {
        if (_settings is null || _settingsSubscribed)
            return;

        _settings.Changed += Settings_Changed;
        _settingsSubscribed = true;
    }

    private void DetachSettings()
    {
        if (_settings is not null && _settingsSubscribed)
            _settings.Changed -= Settings_Changed;
        _settingsSubscribed = false;
    }

    private void Settings_Changed(object? sender, EventArgs args)
    {
        if (DispatcherQueue.HasThreadAccess)
            RefreshNow();
        else
            DispatcherQueue.TryEnqueue(RefreshNow);
    }

    private void ScheduleRefresh()
    {
        if (Interlocked.Exchange(ref _refreshScheduled, 1) != 0)
            return;

        if (DispatcherQueue.TryEnqueue(() =>
            {
                if (Interlocked.Exchange(ref _refreshScheduled, 0) != 0)
                    Refresh();
            }))
        {
            return;
        }

        Interlocked.Exchange(ref _refreshScheduled, 0);
        Refresh();
    }

    private void RefreshNow()
    {
        Interlocked.Exchange(ref _refreshScheduled, 0);
        Refresh();
    }

    private void Refresh()
    {
        HistoryRenderDiagnostics.AvatarRefresh();
        var settings = _settings;
        var effectiveState = new AuthorAvatarEffectiveState(
            AuthorAvatarLifecycleState.NormalizeIdentity(AuthorName, AuthorEmail),
            settings?.ShowAuthorAvatars ?? true,
            settings?.OnlineAvatarLookupEnabled ?? false,
            Volatile.Read(ref _serviceGeneration));

        var transition = _lifecycleState.Apply(effectiveState);
        if (transition.Changed)
        {
            HistoryRenderDiagnostics.AvatarEffectiveStateTransition();
            _resolvedImagePath = null;
            if (transition.CancelPending && _requestGate.Cancel())
                HistoryRenderDiagnostics.AvatarRequestCancelled();
        }

        if (!effectiveState.ShowAuthorAvatars)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        Visibility = Visibility.Visible;
        if (!IsLoaded)
            return;

        if (_lifecycleState.ResolutionStatus == AuthorAvatarResolutionStatus.ResolvedImage
            && !string.IsNullOrWhiteSpace(_resolvedImagePath))
        {
            ApplyResolvedImage(effectiveState.LookupKey, _resolvedImagePath);
            HistoryRenderDiagnostics.AvatarResolveDeduplicated();
            return;
        }

        ShowInitials();

        if (_avatarService is null || !effectiveState.OnlineAvatarLookupEnabled)
            return;

        if (!_lifecycleState.TryStartResolve(IsLoaded))
        {
            if (_lifecycleState.IsResolveDeduplicated(IsLoaded))
                HistoryRenderDiagnostics.AvatarResolveDeduplicated();
            return;
        }

        var request = _requestGate.Start(effectiveState.LookupKey);
        HistoryRenderDiagnostics.AvatarRequestStarted();
        _ = ResolveRemoteAsync(request, AuthorName, AuthorEmail);
    }

    private async Task ResolveRemoteAsync(
        AuthorAvatarRequest request,
        string authorName,
        string authorEmail)
    {
        var service = _avatarService;
        if (service is null)
            return;

        AuthorAvatarResult result;
        var startedAt = HistoryRenderDiagnostics.TimestampIfPerformanceCaptureActive();
        var resolveTask = service.ResolveAsync(authorName, authorEmail, request.CancellationToken);
        var completedSynchronously = resolveTask.IsCompleted;
        try
        {
            result = await resolveTask;
            HistoryRenderDiagnostics.AvatarResolveCompleted(startedAt, completedSynchronously);
        }
        catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            CompleteFaultedRequest(request);
            return;
        }

        if (!IsCurrentPendingRequest(request))
        {
            HistoryRenderDiagnostics.AvatarStaleResultIgnored();
            return;
        }

        if (!_requestGate.TryComplete(request))
        {
            HistoryRenderDiagnostics.AvatarStaleResultIgnored();
            return;
        }

        HistoryRenderDiagnostics.AvatarRequestCompleted();

        if (string.IsNullOrWhiteSpace(result.ImagePath))
        {
            if (_lifecycleState.TrySetResolvedNoImage(request.LookupKey))
                HistoryRenderDiagnostics.AvatarResolveCompletedNoImage();
            if (IsLoaded && _lifecycleState.IsCurrent(request.LookupKey))
                ShowInitials();
            return;
        }

        if (!_lifecycleState.TrySetResolvedImage(request.LookupKey))
        {
            HistoryRenderDiagnostics.AvatarStaleResultIgnored();
            return;
        }

        _resolvedImagePath = result.ImagePath;
        if (IsLoaded && _lifecycleState.IsCurrent(request.LookupKey))
            ApplyResolvedImage(request.LookupKey, result.ImagePath);
    }

    private void CompleteFaultedRequest(AuthorAvatarRequest request)
    {
        if (!IsCurrentPendingRequest(request))
        {
            HistoryRenderDiagnostics.AvatarStaleResultIgnored();
            return;
        }

        if (!_requestGate.TryComplete(request))
        {
            HistoryRenderDiagnostics.AvatarStaleResultIgnored();
            return;
        }

        HistoryRenderDiagnostics.AvatarRequestCompleted();
        if (_lifecycleState.TrySetFaulted(request.LookupKey))
            HistoryRenderDiagnostics.AvatarResolveFaulted();
        if (IsLoaded && _lifecycleState.IsCurrent(request.LookupKey))
            ShowInitials();
    }

    private bool IsCurrentPendingRequest(AuthorAvatarRequest request) =>
        _lifecycleState.HasEffectiveState
        && _lifecycleState.IsPending(request.LookupKey)
        && _requestGate.IsCurrent(request, _lifecycleState.EffectiveState.LookupKey);

    private void ApplyResolvedImage(AuthorAvatarLookupKey key, string imagePath)
    {
        if (_displayedRemoteKey == key && AvatarImage.Source is not null)
            return;

        try
        {
            var normalizedPath = Path.GetFullPath(imagePath);
            var imageUri = new UriBuilder(Uri.UriSchemeFile, string.Empty)
            {
                Path = normalizedPath
            }.Uri;
            AvatarImage.Source = new BitmapImage(imageUri);
            AvatarImage.Visibility = Visibility.Visible;
            InitialsText.Visibility = Visibility.Collapsed;
            _displayedRemoteKey = key;
            _fallbackIdentity = null;
            HistoryRenderDiagnostics.AvatarResultApplied();
        }
        catch
        {
            MarkDisplayedImageFailed(key);
        }
    }

    private void AvatarImage_ImageFailed(object sender, ExceptionRoutedEventArgs args)
    {
        if (_displayedRemoteKey is not { } key)
        {
            ShowInitials();
            return;
        }

        MarkDisplayedImageFailed(key);
    }

    private void MarkDisplayedImageFailed(AuthorAvatarLookupKey key)
    {
        _resolvedImagePath = null;
        _lifecycleState.TryMarkImageFailed(key);
        ShowInitials();

        if (!_lifecycleState.IsCurrent(key) || _avatarService is null)
            return;

        _ = _avatarService.InvalidateAsync(AuthorName, AuthorEmail);
    }

    private void ShowInitials()
    {
        var identity = AuthorAvatarLifecycleState.NormalizeIdentity(AuthorName, AuthorEmail);
        if (_displayedRemoteKey is null
            && string.Equals(_fallbackIdentity, identity, StringComparison.Ordinal))
        {
            return;
        }

        _displayedRemoteKey = null;
        _fallbackIdentity = identity;
        AvatarImage.Source = null;
        AvatarImage.Visibility = Visibility.Collapsed;
        InitialsText.Visibility = Visibility.Visible;
        InitialsText.Text = AuthorAvatarFallback.GetInitials(AuthorName, AuthorEmail);
        AvatarBorder.Background = new SolidColorBrush(
            BackgroundPalette[AuthorAvatarFallback.GetStableColorIndex(
                AuthorName,
                AuthorEmail,
                BackgroundPalette.Length)]);
        ToolTipService.SetToolTip(this, BuildToolTip());
    }

    private string BuildToolTip()
    {
        var name = AuthorName?.Trim() ?? string.Empty;
        var email = AuthorEmail?.Trim() ?? string.Empty;
        if (name.Length == 0)
            return email;
        if (email.Length == 0)
            return name;
        return name + Environment.NewLine + email;
    }

    private void ApplySize()
    {
        var size = Math.Max(1, AvatarSize);
        Width = size;
        Height = size;
        AvatarBorder.Width = size;
        AvatarBorder.Height = size;
        AvatarBorder.CornerRadius = new CornerRadius(size / 2);
        AvatarImage.Width = size;
        AvatarImage.Height = size;
        InitialsText.FontSize = Math.Max(8, size * 0.42);
    }

    internal bool HasCurrentIdentityForCheck(string authorName, string authorEmail)
    {
        var expectedIdentity = AuthorAvatarLifecycleState.NormalizeIdentity(authorName, authorEmail);
        var currentIdentity = AuthorAvatarLifecycleState.NormalizeIdentity(AuthorName, AuthorEmail);
        return string.Equals(currentIdentity, expectedIdentity, StringComparison.Ordinal)
            && (_displayedRemoteKey is null
                || string.Equals(
                    _displayedRemoteKey.Value.Identity,
                    expectedIdentity,
                    StringComparison.Ordinal));
    }
}
