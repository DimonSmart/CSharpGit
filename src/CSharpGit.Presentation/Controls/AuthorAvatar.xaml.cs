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
    private bool _settingsSubscribed;
    private int _refreshScheduled;
    private string? _displayedRemoteIdentity;
    private string? _fallbackIdentity;

    public AuthorAvatar()
    {
        HistoryRenderDiagnostics.AuthorAvatarCreated();
        InitializeComponent();
        Loaded += AuthorAvatar_Loaded;
        Unloaded += AuthorAvatar_Unloaded;
        ApplySize();
        ShowInitials();
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

        _avatarService = avatarService;
        _settings = settings;
        if (IsLoaded)
            AttachSettings();

        RefreshNow();
    }

    private static void IdentityPropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        HistoryRenderDiagnostics.AvatarIdentityChanged();
        ((AuthorAvatar)dependencyObject).ScheduleRefresh();
    }

    private static void AvatarSizePropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var control = (AuthorAvatar)dependencyObject;
        control.ApplySize();
        control.ScheduleRefresh();
    }

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
        Interlocked.Exchange(ref _refreshScheduled, 0);
        if (_requestGate.Cancel())
            HistoryRenderDiagnostics.AvatarRequestCancelled();
        DetachSettings();
    }

    private void EnsureServices()
    {
        if (_avatarService is not null && _settings is not null)
            return;

        if (!AuthorAvatarServiceContext.TryGet(out var avatarService, out var settings))
            return;

        _avatarService = avatarService;
        _settings = settings;
    }

    private void AttachSettings()
    {
        if (_settings is null || _settingsSubscribed) return;
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
        var identity = CurrentIdentity();
        if (settings is { ShowAuthorAvatars: true, OnlineAvatarLookupEnabled: true }
            && IsLoaded
            && string.Equals(_displayedRemoteIdentity, identity, StringComparison.Ordinal))
        {
            Visibility = Visibility.Visible;
            return;
        }

        if (_requestGate.Cancel())
            HistoryRenderDiagnostics.AvatarRequestCancelled();
        ShowInitials();

        settings = _settings;
        if (settings is null)
        {
            Visibility = Visibility.Visible;
            return;
        }

        if (!settings.ShowAuthorAvatars)
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        Visibility = Visibility.Visible;
        if (!settings.OnlineAvatarLookupEnabled || _avatarService is null || !IsLoaded)
            return;

        var request = _requestGate.Start(CurrentIdentity());
        HistoryRenderDiagnostics.AvatarRequestStarted();
        _ = ResolveRemoteAsync(request);
    }

    private async Task ResolveRemoteAsync(AuthorAvatarRequest request)
    {
        var service = _avatarService;
        if (service is null) return;

        AuthorAvatarResult result;
        var startedAt = HistoryRenderDiagnostics.TimestampIfPerformanceCaptureActive();
        var resolveTask = service.ResolveAsync(AuthorName, AuthorEmail, request.CancellationToken);
        var completedSynchronously = resolveTask.IsCompleted;
        try
        {
            result = await resolveTask;
            HistoryRenderDiagnostics.AvatarResolveCompleted(startedAt, completedSynchronously);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            return;
        }

        if (!_requestGate.IsCurrent(request, CurrentIdentity())
            || _settings is not { ShowAuthorAvatars: true, OnlineAvatarLookupEnabled: true })
        {
            HistoryRenderDiagnostics.AvatarStaleResultIgnored();
            return;
        }

        if (string.IsNullOrWhiteSpace(result.ImagePath))
            return;

        try
        {
            var normalizedPath = Path.GetFullPath(result.ImagePath);
            var imageUri = new UriBuilder(Uri.UriSchemeFile, string.Empty)
            {
                Path = normalizedPath
            }.Uri;
            var bitmap = new BitmapImage(imageUri);
            AvatarImage.Source = bitmap;
            AvatarImage.Visibility = Visibility.Visible;
            InitialsText.Visibility = Visibility.Collapsed;
            _displayedRemoteIdentity = request.Identity;
            HistoryRenderDiagnostics.AvatarResultApplied();
        }
        catch
        {
            ShowInitials();
            try
            {
                await service.InvalidateAsync(AuthorName, AuthorEmail);
            }
            catch
            {
            }
        }
    }

    private void AvatarImage_ImageFailed(object sender, ExceptionRoutedEventArgs args)
    {
        var failedIdentity = _displayedRemoteIdentity;
        ShowInitials();
        if (failedIdentity is null
            || !string.Equals(failedIdentity, CurrentIdentity(), StringComparison.Ordinal)
            || _avatarService is null)
            return;

        _ = _avatarService.InvalidateAsync(AuthorName, AuthorEmail);
    }

    private void ShowInitials()
    {
        var identity = CurrentIdentity();
        if (_displayedRemoteIdentity is null
            && string.Equals(_fallbackIdentity, identity, StringComparison.Ordinal))
        {
            return;
        }

        _displayedRemoteIdentity = null;
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
        if (name.Length == 0) return email;
        if (email.Length == 0) return name;
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
        var expectedIdentity =
            (authorEmail ?? string.Empty).Trim().ToLowerInvariant()
            + "\n"
            + (authorName ?? string.Empty).Trim();
        return string.Equals(CurrentIdentity(), expectedIdentity, StringComparison.Ordinal)
            && (_displayedRemoteIdentity is null
                || string.Equals(_displayedRemoteIdentity, expectedIdentity, StringComparison.Ordinal));
    }

    private string CurrentIdentity() =>
        (AuthorEmail ?? string.Empty).Trim().ToLowerInvariant()
        + "\n"
        + (AuthorName ?? string.Empty).Trim();


}
