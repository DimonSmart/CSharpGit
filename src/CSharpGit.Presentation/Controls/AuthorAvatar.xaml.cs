using System.Security.Cryptography;
using System.Text;
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
    private CancellationTokenSource? _resolveCts;
    private long _generation;
    private bool _settingsSubscribed;
    private string? _displayedRemoteIdentity;

    public AuthorAvatar()
    {
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

        if (!ReferenceEquals(_settings, settings))
            DetachSettings();

        _avatarService = avatarService;
        _settings = settings;
        if (IsLoaded)
            AttachSettings();

        Refresh();
    }

    internal static string GetInitials(string? authorName, string? authorEmail)
    {
        var words = (authorName ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (words.Length == 1)
            return FirstTextElement(words[0]);
        if (words.Length > 1)
            return FirstTextElement(words[0]) + FirstTextElement(words[^1]);

        var email = authorEmail?.Trim();
        if (!string.IsNullOrEmpty(email))
        {
            var at = email.IndexOf('@');
            var local = at > 0 ? email[..at] : email;
            if (local.Length > 0)
                return FirstTextElement(local);
        }

        return "?";
    }

    internal static int GetStableColorIndex(string? authorName, string? authorEmail)
    {
        var identity = !string.IsNullOrWhiteSpace(authorEmail)
            ? authorEmail.Trim().ToLowerInvariant()
            : (authorName ?? string.Empty).Trim().ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return bytes[0] % BackgroundPalette.Length;
    }

    private static void IdentityPropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args) =>
        ((AuthorAvatar)dependencyObject).Refresh();

    private static void AvatarSizePropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        var control = (AuthorAvatar)dependencyObject;
        control.ApplySize();
        control.Refresh();
    }

    private void AuthorAvatar_Loaded(object sender, RoutedEventArgs args)
    {
        AttachSettings();
        Refresh();
    }

    private void AuthorAvatar_Unloaded(object sender, RoutedEventArgs args)
    {
        CancelConsumerRequest();
        DetachSettings();
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
            Refresh();
        else
            DispatcherQueue.TryEnqueue(Refresh);
    }

    private void Refresh()
    {
        var generation = Interlocked.Increment(ref _generation);
        CancelConsumerRequest();
        ShowInitials();

        var settings = _settings;
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

        _resolveCts = new CancellationTokenSource();
        _ = ResolveRemoteAsync(
            generation,
            CurrentIdentity(),
            _resolveCts.Token);
    }

    private async Task ResolveRemoteAsync(
        long generation,
        string identity,
        CancellationToken cancellationToken)
    {
        var service = _avatarService;
        if (service is null) return;

        AuthorAvatarResult result;
        try
        {
            result = await service.ResolveAsync(AuthorName, AuthorEmail, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            return;
        }

        if (cancellationToken.IsCancellationRequested
            || generation != Volatile.Read(ref _generation)
            || !string.Equals(identity, CurrentIdentity(), StringComparison.Ordinal)
            || _settings is not { ShowAuthorAvatars: true, OnlineAvatarLookupEnabled: true }
            || string.IsNullOrWhiteSpace(result.ImagePath))
            return;

        try
        {
            var bitmap = new BitmapImage(new Uri(result.ImagePath, UriKind.Absolute));
            AvatarImage.Source = bitmap;
            AvatarImage.Visibility = Visibility.Visible;
            InitialsText.Visibility = Visibility.Collapsed;
            _displayedRemoteIdentity = identity;
        }
        catch
        {
            ShowInitials();
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
        _displayedRemoteIdentity = null;
        AvatarImage.Source = null;
        AvatarImage.Visibility = Visibility.Collapsed;
        InitialsText.Visibility = Visibility.Visible;
        InitialsText.Text = GetInitials(AuthorName, AuthorEmail);
        AvatarBorder.Background = new SolidColorBrush(
            BackgroundPalette[GetStableColorIndex(AuthorName, AuthorEmail)]);
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

    private void CancelConsumerRequest()
    {
        var cts = Interlocked.Exchange(ref _resolveCts, null);
        if (cts is null) return;
        cts.Cancel();
        cts.Dispose();
    }

    private string CurrentIdentity() =>
        (AuthorEmail ?? string.Empty).Trim().ToLowerInvariant()
        + "\n"
        + (AuthorName ?? string.Empty).Trim();

    private static string FirstTextElement(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var codePoint = char.ConvertToUtf32(value, 0);
        return char.ConvertFromUtf32(codePoint).ToUpperInvariant();
    }
}
