using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Soenneker.Extensions.String;
using Soenneker.Playwrights.Crawler.Dtos;
using Soenneker.Playwrights.Crawler.Utils.Abstract;

namespace Soenneker.Playwrights.Crawler.Utils;

internal sealed class PlaywrightCrawlerUrlUtil : IPlaywrightCrawlerUrlUtil
{
    private const string ExternalDirectory = "_external";
    private static readonly Regex _percentPlaceholderRegex = new("%[A-Z][A-Z0-9_\\-]*%", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex _bracePlaceholderRegex = new("(\\$\\{[^}]+\\}|\\{[A-Z][A-Z0-9_\\-]*\\})", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public Uri ValidateAndNormalizeRootUrl(string url)
    {
        if (url.IsNullOrWhiteSpace())
            throw new ArgumentException("URL cannot be null or whitespace.", nameof(url));

        if (!TryNormalizeHttpUrl(url, out Uri? uri))
            throw new ArgumentException("URL must be a valid absolute HTTP or HTTPS URL.", nameof(url));

        return uri!;
    }

    public bool TryNormalizeHttpUrl(string url, out Uri? uri)
    {
        uri = null;

        if (url.IsNullOrWhiteSpace())
            return false;

        if (ContainsUnresolvedTemplateToken(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? candidate))
            return false;

        if (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps)
            return false;

        if (ContainsUnresolvedTemplateToken(candidate.AbsoluteUri))
            return false;

        uri = NormalizeUrl(candidate);
        return true;
    }

    public Uri NormalizeUrl(Uri uri)
    {
        var builder = new UriBuilder(uri)
        {
            Fragment = string.Empty
        };

        if (builder.Port == 80 && string.Equals(builder.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            builder.Port = -1;

        if (builder.Port == 443 && string.Equals(builder.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            builder.Port = -1;

        return builder.Uri;
    }

    public Uri NormalizePageUrl(Uri uri, bool ignoreQueryString)
    {
        Uri normalized = NormalizeUrl(uri);

        if (!ignoreQueryString)
            return normalized;

        var builder = new UriBuilder(normalized)
        {
            Query = string.Empty
        };

        return builder.Uri;
    }

    public bool UrisShareHost(Uri first, Uri second)
    {
        return string.Equals(first.Host, second.Host, StringComparison.OrdinalIgnoreCase) && first.Port == second.Port &&
               string.Equals(first.Scheme, second.Scheme, StringComparison.OrdinalIgnoreCase);
    }

    public string BuildRelativePath(Uri rootUri, Uri resourceUri, bool isHtmlDocument, string? contentType)
    {
        string path = Uri.UnescapeDataString(resourceUri.AbsolutePath);

        string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries)
                                .Select(SanitizePathSegment)
                                .Where(static segment => !string.IsNullOrWhiteSpace(segment))
                                .ToArray();

        var parts = new List<string>();

        if (!UrisShareHost(rootUri, resourceUri))
        {
            parts.Add(ExternalDirectory);
            parts.Add(SanitizePathSegment(resourceUri.Host));
        }

        if (isHtmlDocument)
        {
            if (segments.Length == 0 || resourceUri.AbsolutePath.EndsWith("/", StringComparison.Ordinal))
            {
                parts.AddRange(segments);
                parts.Add("index.html");
                return Path.Combine([.. parts]);
            }

            string fileName = segments[^1];
            string extension = Path.GetExtension(fileName);

            if (string.IsNullOrWhiteSpace(extension))
            {
                parts.AddRange(segments);
                parts.Add("index.html");
                return Path.Combine([.. parts]);
            }

            parts.AddRange(segments);
            return Path.Combine([.. parts]);
        }

        if (segments.Length == 0 || resourceUri.AbsolutePath.EndsWith("/", StringComparison.Ordinal))
        {
            parts.AddRange(segments);
            parts.Add($"index{ResolveAssetExtension(contentType)}");
            return Path.Combine([.. parts]);
        }

        string assetName = segments[^1];
        string assetExtension = Path.GetExtension(assetName);

        if (string.IsNullOrWhiteSpace(assetExtension))
            assetName = $"{assetName}{ResolveAssetExtension(contentType)}";

        segments[^1] = assetName;
        parts.AddRange(segments);

        return Path.Combine([.. parts]);
    }

    public bool ShouldQueuePage(Uri rootUri, Uri candidate, PlaywrightCrawlOptions options)
    {
        if (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps)
            return false;

        if (options.SameHostOnly && !UrisShareHost(rootUri, candidate))
            return false;

        if (options.AllowedPageUrls.Count > 0 && !IsAllowedPage(rootUri, candidate, options))
            return false;

        return true;
    }

    private bool IsAllowedPage(Uri rootUri, Uri candidate, PlaywrightCrawlOptions options)
    {
        string normalizedCandidate = NormalizePageUrl(candidate, options.IgnoreQueryStringsInDuplicateDetection).AbsoluteUri;

        foreach (string allowedPageUrl in options.AllowedPageUrls)
        {
            Uri? allowedUri = null;

            if (TryNormalizeHttpUrl(allowedPageUrl, out Uri? absoluteAllowedUri))
                allowedUri = absoluteAllowedUri;
            else if (allowedPageUrl.StartsWith("/", StringComparison.Ordinal) && Uri.TryCreate(rootUri, allowedPageUrl, out Uri? relativeAllowedUri))
                allowedUri = NormalizeUrl(relativeAllowedUri);

            if (allowedUri is null)
                continue;

            string normalizedAllowed = NormalizePageUrl(allowedUri, options.IgnoreQueryStringsInDuplicateDetection).AbsoluteUri;

            if (string.Equals(normalizedAllowed, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public bool ShouldSaveResource(Uri rootUri, Uri resourceUri, bool isHtmlDocument, PlaywrightCrawlOptions options)
    {
        if (isHtmlDocument)
            return !options.SameHostOnly || UrisShareHost(rootUri, resourceUri);

        if (UrisShareHost(rootUri, resourceUri))
            return true;

        return options.IncludeCrossOriginAssets;
    }

    public async Task<IReadOnlyList<string>> GetPageLinks(IPage page)
    {
        string[]? links = await page.EvaluateAsync<string[]>("""
                                                             () => Array.from(document.querySelectorAll('a[href]'), anchor => anchor.href)
                                                             """);

        if (links is null || links.Length == 0)
            return [];

        return links;
    }

    public async Task<IReadOnlyList<string>> GetPageResourceUrls(IPage page)
    {
        string[]? urls = await page.EvaluateAsync<string[]>(
            """
            () => {
                const urls = new Set();
                const directResourceAttributes = new Set([
                    'data',
                    'poster',
                    'src',
                    'data-bg',
                    'data-bg-src',
                    'data-background',
                    'data-background-image',
                    'data-css-url',
                    'data-js-url',
                    'data-lazy-src',
                    'data-original',
                    'data-script-url',
                    'data-src',
                    'data-url'
                ]);
                const srcSetAttributes = new Set(['srcset', 'data-lazy-srcset', 'data-srcset']);
                const resourceExtensions = new Set([
                    '.avif',
                    '.css',
                    '.gif',
                    '.ico',
                    '.jpeg',
                    '.jpg',
                    '.js',
                    '.mjs',
                    '.png',
                    '.svg',
                    '.webp',
                    '.woff',
                    '.woff2'
                ]);

                const shouldIgnore = value => {
                    if (!value) {
                        return true;
                    }

                    const normalized = value.trim().toLowerCase();

                    return normalized.length === 0 ||
                        normalized.startsWith('#') ||
                        normalized.startsWith('data:') ||
                        normalized.startsWith('blob:') ||
                        normalized.startsWith('javascript:') ||
                        normalized.startsWith('mailto:') ||
                        normalized.startsWith('tel:');
                };

                const hasResourceExtension = url => {
                    try {
                        const fileName = new URL(url).pathname.split('/').pop() || '';
                        const extension = fileName.match(/\.[^.]*$/)?.[0]?.toLowerCase();

                        return extension ? resourceExtensions.has(extension) : false;
                    } catch {
                        return false;
                    }
                };

                const addUrl = (value, allowRootFallback = false) => {
                    if (shouldIgnore(value)) {
                        return;
                    }

                    const trimmed = value.trim();
                    const shouldUseRootFirst = allowRootFallback && /^(wp-content|wp-includes)\//i.test(trimmed);

                    const addRootFallback = () => {
                        if (!allowRootFallback ||
                            trimmed.startsWith('/') ||
                            trimmed.startsWith('./') ||
                            trimmed.startsWith('../') ||
                            /^[a-z][a-z0-9+.-]*:/i.test(trimmed)) {
                            return;
                        }

                        try {
                            const rootResolved = new URL(`/${trimmed}`, window.location.origin).href;

                            if (hasResourceExtension(rootResolved)) {
                                urls.add(rootResolved);
                            }
                        } catch {
                        }
                    };

                    if (shouldUseRootFirst) {
                        addRootFallback();
                    }

                    try {
                        const resolved = new URL(trimmed, document.baseURI).href;

                        if (hasResourceExtension(resolved)) {
                            urls.add(resolved);
                        }
                    } catch {
                    }

                    if (!shouldUseRootFirst) {
                        addRootFallback();
                    }
                };

                const addSrcSet = value => {
                    if (shouldIgnore(value)) {
                        return;
                    }

                    for (const candidate of value.split(',')) {
                        addUrl(candidate.trim().split(/\s+/)[0]);
                    }
                };

                const addCssUrls = value => {
                    if (shouldIgnore(value)) {
                        return;
                    }

                    for (const match of value.matchAll(/url\((['"]?)(.*?)\1\)/gi)) {
                        addUrl(match[2]);
                    }
                };

                for (const element of document.querySelectorAll('*')) {
                    for (const attribute of element.attributes) {
                        const name = attribute.name.toLowerCase();
                        const value = attribute.value;

                        if (srcSetAttributes.has(name)) {
                            addSrcSet(value);
                            continue;
                        }

                        if (name === 'style') {
                            addCssUrls(value);
                            continue;
                        }

                        if (name === 'href') {
                            const tagName = element.tagName.toLowerCase();
                            const rel = (element.getAttribute('rel') || '').toLowerCase();

                            if (tagName === 'link' &&
                                (rel.includes('stylesheet') || rel.includes('preload') || rel.includes('modulepreload') || rel.includes('icon'))) {
                                addUrl(value);
                            }

                            continue;
                        }

                        if (directResourceAttributes.has(name)) {
                            addUrl(value, name.startsWith('data-'));
                        }
                    }
                }

                return Array.from(urls);
            }
            """);

        if (urls is null || urls.Length == 0)
            return [];

        return urls;
    }

    public bool IsChallengePage(string? title, string html)
    {
        if (title.HasContent())
        {
            if (title.Contains("just a moment", StringComparison.OrdinalIgnoreCase)
                || title.Contains("verify you are human", StringComparison.OrdinalIgnoreCase)
                || title.Contains("attention required", StringComparison.OrdinalIgnoreCase)
                || title.Contains("security verification", StringComparison.OrdinalIgnoreCase)
                || string.Equals(title.Trim(), "captcha", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return html.Contains("/cdn-cgi/challenge-platform/", StringComparison.OrdinalIgnoreCase)
               || html.Contains("cf-chl-", StringComparison.OrdinalIgnoreCase)
               || html.Contains("cf-challenge-running", StringComparison.OrdinalIgnoreCase)
               || html.Contains("verify you are human", StringComparison.OrdinalIgnoreCase)
               || html.Contains("attention required! | cloudflare", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string> ResolveIpKey(Uri uri, CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(uri.Host, out IPAddress? ipAddress))
            return ipAddress.ToString();

        try
        {
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken);
            IPAddress? address = addresses.FirstOrDefault(static address => address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6);

            return address?.ToString() ?? uri.Host;
        }
        catch
        {
            return uri.Host;
        }
    }

    public string GetDomainKey(Uri uri)
    {
        return $"{uri.Scheme}://{uri.Host}:{uri.Port}";
    }

    private static string SanitizePathSegment(string value)
    {
        if (value.IsNullOrWhiteSpace())
            return "_";

        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);

        foreach (char c in value)
        {
            builder.Append(invalid.Contains(c) ? '_' : c);
        }

        return builder.Length == 0 ? "_" : builder.ToString();
    }

    private static string ResolveAssetExtension(string? contentType)
    {
        if (contentType.IsNullOrWhiteSpace())
            return ".bin";

        string normalized = contentType.Split(';', 2, StringSplitOptions.TrimEntries)[0]
                                       .Trim()
                                       .ToLowerInvariant();

        return normalized switch
        {
            "text/css" => ".css",
            "application/javascript" => ".js",
            "text/javascript" => ".js",
            "application/json" => ".json",
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "image/svg+xml" => ".svg",
            "font/woff" => ".woff",
            "font/woff2" => ".woff2",
            "text/plain" => ".txt",
            "text/html" => ".html",
            _ => ".bin"
        };
    }

    private static bool ContainsUnresolvedTemplateToken(string value)
    {
        if (value.IsNullOrWhiteSpace())
            return false;

        if (_percentPlaceholderRegex.IsMatch(value) || _bracePlaceholderRegex.IsMatch(value))
            return true;

        try
        {
            string decoded = Uri.UnescapeDataString(value);
            return !ReferenceEquals(decoded, value) && (_percentPlaceholderRegex.IsMatch(decoded) || _bracePlaceholderRegex.IsMatch(decoded));
        }
        catch
        {
            return false;
        }
    }
}
