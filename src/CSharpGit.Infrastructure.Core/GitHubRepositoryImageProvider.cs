using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace CSharpGit.Infrastructure;

internal sealed class GitHubRepositoryImageProvider(
    HttpClient httpClient,
    SemaphoreSlim requestGate,
    long maxHtmlBytes,
    long maxImageBytes) : IRepositoryImageProvider
{
    private static readonly Regex MetaTagRegex = new(
        @"<meta\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex AttributeRegex = new(
        @"(?<name>[\w:-]+)\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>[^\s>]+))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<RepositoryImageProviderResult> ResolveAsync(
        RepositoryImageContext context,
        CancellationToken cancellationToken)
    {
        if (context.GitHubRepositoryUrl is null)
            return RepositoryImageProviderResult.NotApplicable();

        await requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var pageResponse = await httpClient.GetAsync(
                context.GitHubRepositoryUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (pageResponse.StatusCode == HttpStatusCode.NotFound)
                return RepositoryImageProviderResult.NotFound();
            if (!pageResponse.IsSuccessStatusCode)
                return RepositoryImageProviderResult.Transient();

            var htmlBytes = await ReadLimitedAsync(pageResponse.Content, maxHtmlBytes, cancellationToken)
                .ConfigureAwait(false);
            if (htmlBytes is null) return RepositoryImageProviderResult.Transient();

            var imageUrl = FindOpenGraphImage(Encoding.UTF8.GetString(htmlBytes));
            if (imageUrl is null || !IsAllowedGitHubImageHost(imageUrl))
                return RepositoryImageProviderResult.NotFound();

            using var imageResponse = await httpClient.GetAsync(
                imageUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (!imageResponse.IsSuccessStatusCode)
                return RepositoryImageProviderResult.Transient();
            if (imageResponse.Content.Headers.ContentLength is > 0
                && imageResponse.Content.Headers.ContentLength > maxImageBytes)
                return RepositoryImageProviderResult.Transient();

            var bytes = await ReadLimitedAsync(imageResponse.Content, maxImageBytes, cancellationToken)
                .ConfigureAwait(false);
            if (bytes is null || !RepositoryImageHelpers.TryDetectImageExtension(bytes, out var extension))
                return RepositoryImageProviderResult.Transient();

            return RepositoryImageProviderResult.Found(
                new RepositoryImageSource(RepositoryImageCacheKind.Remote, bytes, extension));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RepositoryImageProviderResult.Transient();
        }
        catch (HttpRequestException)
        {
            return RepositoryImageProviderResult.Transient();
        }
        finally
        {
            requestGate.Release();
        }
    }

    private static Uri? FindOpenGraphImage(string html)
    {
        foreach (Match tag in MetaTagRegex.Matches(html))
        {
            string? property = null;
            string? content = null;
            foreach (Match attribute in AttributeRegex.Matches(tag.Value))
            {
                var name = attribute.Groups["name"].Value;
                var value = WebUtility.HtmlDecode(attribute.Groups["value"].Value);
                if (name.Equals("property", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("name", StringComparison.OrdinalIgnoreCase))
                    property = value;
                else if (name.Equals("content", StringComparison.OrdinalIgnoreCase))
                    content = value;
            }

            if (!string.Equals(property, "og:image", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(content))
                continue;

            if (Uri.TryCreate(content, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps)
                return uri;
        }

        return null;
    }

    private static bool IsAllowedGitHubImageHost(Uri uri)
    {
        var host = uri.Host;
        return host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("githubassets.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".githubassets.com", StringComparison.OrdinalIgnoreCase)
            || host.Equals("githubusercontent.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<byte[]?> ReadLimitedAsync(
        HttpContent content,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > 0 && content.Headers.ContentLength > maxBytes)
            return null;

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[32 * 1024];
        long total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) return output.ToArray();
            total += read;
            if (total > maxBytes) return null;
            output.Write(buffer, 0, read);
        }
    }
}
