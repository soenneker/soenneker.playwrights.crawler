using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Soenneker.Asyncs.Locks;
using Soenneker.Extensions.String;
using Soenneker.Extensions.Task;
using Soenneker.Extensions.ValueTask;
using Soenneker.Html.Formatter.Abstract;
using Soenneker.Playwrights.Crawler.Dtos;
using Soenneker.Playwrights.Crawler.Utils.Abstract;
using Soenneker.Utils.Directory.Abstract;
using Soenneker.Utils.File.Abstract;

namespace Soenneker.Playwrights.Crawler.Utils;

///<inheritdoc cref="IPlaywrightCrawlerStorage"/>
internal sealed class PlaywrightCrawlerStorage : IPlaywrightCrawlerStorage
{
    private static readonly HashSet<string> _directDownloadContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/wasm",
        "application/octet-stream"
    };

    private static readonly HashSet<string> _directDownloadExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wasm",
        ".dat",
        ".dll",
        ".pdb",
        ".bin"
    };

    private readonly ILogger<PlaywrightCrawlerStorage> _logger;
    private readonly IFileUtil _fileUtil;
    private readonly IDirectoryUtil _directoryUtil;
    private readonly IPlaywrightCrawlerUrlUtil _urlUtil;
    private readonly IPlaywrightCrawlerPolicyUtil _policyUtil;
    private readonly IHtmlFormatter _htmlFormatter;

    private readonly ConcurrentDictionary<string, byte> _createdDirectories = new(StringComparer.OrdinalIgnoreCase);

    public PlaywrightCrawlerStorage(ILogger<PlaywrightCrawlerStorage> logger, IFileUtil fileUtil,
        IDirectoryUtil directoryUtil, IPlaywrightCrawlerUrlUtil urlUtil, IPlaywrightCrawlerPolicyUtil policyUtil,
        IHtmlFormatter htmlFormatter)
    {
        _logger = logger;
        _fileUtil = fileUtil;
        _directoryUtil = directoryUtil;
        _urlUtil = urlUtil;
        _policyUtil = policyUtil;
        _htmlFormatter = htmlFormatter;
    }

    public ValueTask DeleteDirectory(string directory, CancellationToken cancellationToken)
    {
        _createdDirectories.Clear();
        return _directoryUtil.DeleteIfExists(directory, cancellationToken);
    }

    public async ValueTask<bool> CreateDirectory(string directory, CancellationToken cancellationToken)
    {
        if (!_createdDirectories.TryAdd(directory, 0))
            return true;

        return await _directoryUtil.Create(directory, true, cancellationToken).NoSync();
    }

    public async ValueTask SaveRenderedDocument(Uri rootUri, Uri documentUri, string html,
        PlaywrightCrawlOptions options, PlaywrightCrawlResult result, ConcurrentDictionary<string, byte> savedUrls,
        AsyncLock resultLock, CancellationToken cancellationToken)
    {
        string htmlToSave = await PrepareHtmlForSave(rootUri, html, options, cancellationToken).NoSync();

        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(htmlToSave);
        string relativePath =
            _urlUtil.BuildRelativePath(rootUri, documentUri, isHtmlDocument: true, contentType: "text/html");

        await SaveFile(documentUri.AbsoluteUri, relativePath, bytes, isHtmlDocument: true, "text/html", options, result,
            savedUrls, resultLock, cancellationToken).NoSync();
    }

    public async ValueTask<IReadOnlyDictionary<string, string>> SaveObservedResponses(IBrowserContext context,
        IEnumerable<IResponse> responses, Uri rootUri, Uri mainDocumentUri, PlaywrightCrawlOptions options,
        PlaywrightCrawlResult result, ConcurrentDictionary<string, byte> savedUrls, AsyncLock resultLock,
        Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var externalResources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (IResponse response in responses)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_policyUtil.ShouldStop(options, result, stopwatch))
                break;

            if (!_urlUtil.TryNormalizeHttpUrl(response.Url, out Uri? resourceUri) || resourceUri is null)
                continue;

            string normalizedUrl = resourceUri.AbsoluteUri;

            if (string.Equals(normalizedUrl, mainDocumentUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
                continue;

            bool isHtmlDocument = string.Equals(response.Request.ResourceType, "document",
                StringComparison.OrdinalIgnoreCase);

            if (!_urlUtil.ShouldSaveResource(rootUri, resourceUri, isHtmlDocument, options))
                continue;

            if (ShouldSkipObservedResponse(response, resourceUri))
                continue;

            if (!response.Ok)
                continue;

            string? contentType = TryGetHeaderValue(response.Headers, "content-type");
            string relativePath = _urlUtil.BuildRelativePath(rootUri, resourceUri, isHtmlDocument, contentType);

            bool resourceAvailable;

            if (ShouldDirectDownload(resourceUri, contentType))
            {
                resourceAvailable = await SaveByDirectDownload(rootUri, resourceUri, normalizedUrl, relativePath,
                    isHtmlDocument, contentType, options, result, savedUrls, resultLock, context.APIRequest, response.Request.Headers,
                    cancellationToken).NoSync();
            }
            else
            {
                byte[] body;

                try
                {
                    body = await response.BodyAsync().NoSync();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex,
                        "Unable to read response body for {Url}; falling back to direct download (status: {StatusCode}, ok: {Ok}, resourceType: {ResourceType}, method: {Method}, contentType: {ContentType})",
                        response.Url, response.Status, response.Ok, response.Request.ResourceType,
                        response.Request.Method, contentType);

                    resourceAvailable = await SaveByDirectDownload(rootUri, resourceUri, normalizedUrl, relativePath,
                        isHtmlDocument, contentType, options, result, savedUrls, resultLock, context.APIRequest,
                        response.Request.Headers, cancellationToken).NoSync();

                    if (resourceAvailable && !isHtmlDocument && !_urlUtil.UrisShareHost(rootUri, resourceUri))
                        externalResources[normalizedUrl] = relativePath;

                    continue;
                }

                if (body.Length == 0)
                    continue;

                body = PrepareTextResourceForSave(rootUri, resourceUri, body, isHtmlDocument, contentType, options);

                resourceAvailable = await SaveFile(normalizedUrl, relativePath, body, isHtmlDocument, contentType,
                    options, result, savedUrls, resultLock, cancellationToken).NoSync();
            }

            if (resourceAvailable && !isHtmlDocument && !_urlUtil.UrisShareHost(rootUri, resourceUri))
                externalResources[normalizedUrl] = relativePath;
        }

        return externalResources;
    }

    public async ValueTask<IReadOnlyDictionary<string, string>> SaveDiscoveredResourceUrls(IBrowserContext context,
        IEnumerable<string> urls, Uri rootUri, Uri mainDocumentUri, PlaywrightCrawlOptions options,
        PlaywrightCrawlResult result, ConcurrentDictionary<string, byte> savedUrls, AsyncLock resultLock,
        Stopwatch stopwatch, CancellationToken cancellationToken)
    {
        var externalResources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var discoveredUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<string, string> requestHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["referer"] = mainDocumentUri.AbsoluteUri
        };

        foreach (string url in urls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_policyUtil.ShouldStop(options, result, stopwatch))
                break;

            if (!_urlUtil.TryNormalizeHttpUrl(url, out Uri? resourceUri) || resourceUri is null)
                continue;

            string normalizedUrl = resourceUri.AbsoluteUri;

            if (!discoveredUrls.Add(normalizedUrl))
                continue;

            if (string.Equals(normalizedUrl, mainDocumentUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!LooksLikeDirectDownloadResource(resourceUri))
                continue;

            if (!_urlUtil.ShouldSaveResource(rootUri, resourceUri, isHtmlDocument: false, options))
                continue;

            string relativePath = _urlUtil.BuildRelativePath(rootUri, resourceUri, isHtmlDocument: false, contentType: null);
            bool resourceAvailable = await SaveByDirectDownload(rootUri, resourceUri, normalizedUrl, relativePath,
                isHtmlDocument: false, contentType: null, options, result, savedUrls, resultLock, context.APIRequest,
                requestHeaders, cancellationToken).NoSync();

            if (resourceAvailable && !_urlUtil.UrisShareHost(rootUri, resourceUri))
                externalResources[normalizedUrl] = relativePath;
        }

        return externalResources;
    }

    public async ValueTask RewriteExternalResourceUrlsInSavedDocument(Uri rootUri, Uri documentUri, string html,
        IReadOnlyDictionary<string, string> externalResources, PlaywrightCrawlOptions options,
        PlaywrightCrawlResult result, AsyncLock resultLock, CancellationToken cancellationToken)
    {
        if (!options.IncludeCrossOriginAssets || externalResources.Count == 0)
            return;

        string originalHtml = await PrepareHtmlForSave(rootUri, html, options, cancellationToken).NoSync();

        string rewrittenHtml = RewriteExternalResourceUrls(rootUri, documentUri, html, externalResources);
        rewrittenHtml = await PrepareHtmlForSave(rootUri, rewrittenHtml, options, cancellationToken).NoSync();

        if (string.Equals(rewrittenHtml, originalHtml, StringComparison.Ordinal))
            return;

        string relativePath =
            _urlUtil.BuildRelativePath(rootUri, documentUri, isHtmlDocument: true, contentType: "text/html");
        string fullPath = Path.Combine(result.SaveDirectory, relativePath);
        byte[] rewrittenBytes = System.Text.Encoding.UTF8.GetBytes(rewrittenHtml);
        long sizeDelta = rewrittenBytes.LongLength - System.Text.Encoding.UTF8.GetByteCount(originalHtml);

        using (await resultLock.Lock(cancellationToken).NoSync())
        {
            if (sizeDelta > 0 && options.MaxStorageBytes.HasValue &&
                result.BytesWritten + sizeDelta > options.MaxStorageBytes.Value)
            {
                _logger.LogDebug(
                    "Skipping cross-origin URL rewrite for {Url} because the rewritten HTML would exceed the storage limit.",
                    documentUri.AbsoluteUri);
                return;
            }

            result.BytesWritten += sizeDelta;

            PlaywrightCrawlFileResult? file = result.Files.LastOrDefault(fileResult =>
                fileResult.Saved && fileResult.IsHtmlDocument && string.Equals(fileResult.Url, documentUri.AbsoluteUri,
                    StringComparison.OrdinalIgnoreCase));

            if (file is not null)
                file.SizeBytes = rewrittenBytes.LongLength;
        }

        await EnsureDirectoryForFile(fullPath, cancellationToken).NoSync();

        await _fileUtil.Write(fullPath, rewrittenBytes, log: true, cancellationToken).NoSync();
    }

    public async ValueTask<bool> SaveFile(string url, string relativePath, byte[] bytes, bool isHtmlDocument,
        string? contentType, PlaywrightCrawlOptions options, PlaywrightCrawlResult result,
        ConcurrentDictionary<string, byte> savedUrls, AsyncLock resultLock, CancellationToken cancellationToken)
    {
        string fullPath = Path.Combine(result.SaveDirectory, relativePath);

        if (!savedUrls.TryAdd(url, 0))
        {
            using (await resultLock.Lock(cancellationToken).NoSync())
            {
                result.Files.Add(new PlaywrightCrawlFileResult
                {
                    Url = url,
                    RelativePath = relativePath,
                    IsHtmlDocument = isHtmlDocument,
                    ContentType = contentType,
                    Saved = false,
                    SkipReason = "URL already saved during this crawl."
                });
            }

            return true;
        }

        using (await resultLock.Lock(cancellationToken).NoSync())
        {
            if (options.MaxStorageBytes.HasValue &&
                result.BytesWritten + bytes.LongLength > options.MaxStorageBytes.Value)
            {
                result.StorageLimitReached = true;
                result.Files.Add(new PlaywrightCrawlFileResult
                {
                    Url = url,
                    RelativePath = relativePath,
                    IsHtmlDocument = isHtmlDocument,
                    ContentType = contentType,
                    Saved = false,
                    SkipReason = "Saving this file would exceed the configured storage limit."
                });

                return false;
            }
        }

        if (!options.OverwriteExistingFiles && await _fileUtil.Exists(fullPath, cancellationToken).NoSync())
        {
            using (await resultLock.Lock(cancellationToken).NoSync())
            {
                result.Files.Add(new PlaywrightCrawlFileResult
                {
                    Url = url,
                    RelativePath = relativePath,
                    IsHtmlDocument = isHtmlDocument,
                    ContentType = contentType,
                    Saved = false,
                    SkipReason = "File already exists and overwriting is disabled."
                });
            }

            return true;
        }

        await EnsureDirectoryForFile(fullPath, cancellationToken).NoSync();

        await _fileUtil.Write(fullPath, bytes, log: true, cancellationToken).NoSync();

        using (await resultLock.Lock(cancellationToken).NoSync())
        {
            result.BytesWritten += bytes.LongLength;

            if (isHtmlDocument)
                result.HtmlFilesSaved++;
            else
                result.AssetFilesSaved++;

            result.Files.Add(new PlaywrightCrawlFileResult
            {
                Url = url,
                RelativePath = relativePath,
                IsHtmlDocument = isHtmlDocument,
                ContentType = contentType,
                SizeBytes = bytes.LongLength,
                Saved = true
            });
        }

        return true;
    }

    private async ValueTask<bool> SaveByDirectDownload(Uri rootUri, Uri resourceUri, string url, string relativePath,
        bool isHtmlDocument, string? contentType, PlaywrightCrawlOptions options, PlaywrightCrawlResult result,
        ConcurrentDictionary<string, byte> savedUrls, AsyncLock resultLock, IAPIRequestContext apiRequestContext,
        IReadOnlyDictionary<string, string> requestHeaders, CancellationToken cancellationToken)
    {
        string fullPath = Path.Combine(result.SaveDirectory, relativePath);

        if (!savedUrls.TryAdd(url, 0))
        {
            using (await resultLock.Lock(cancellationToken).NoSync())
            {
                result.Files.Add(new PlaywrightCrawlFileResult
                {
                    Url = url,
                    RelativePath = relativePath,
                    IsHtmlDocument = isHtmlDocument,
                    ContentType = contentType,
                    Saved = false,
                    SkipReason = "URL already saved during this crawl."
                });
            }

            return true;
        }

        using (await resultLock.Lock(cancellationToken).NoSync())
        {
            if (options.MaxStorageBytes.HasValue && result.BytesWritten >= options.MaxStorageBytes.Value)
            {
                result.StorageLimitReached = true;
                result.Files.Add(new PlaywrightCrawlFileResult
                {
                    Url = url,
                    RelativePath = relativePath,
                    IsHtmlDocument = isHtmlDocument,
                    ContentType = contentType,
                    Saved = false,
                    SkipReason = "Storage limit was already reached before direct download started."
                });

                return false;
            }
        }

        if (!options.OverwriteExistingFiles && await _fileUtil.Exists(fullPath, cancellationToken).NoSync())
        {
            using (await resultLock.Lock(cancellationToken).NoSync())
            {
                result.Files.Add(new PlaywrightCrawlFileResult
                {
                    Url = url,
                    RelativePath = relativePath,
                    IsHtmlDocument = isHtmlDocument,
                    ContentType = contentType,
                    Saved = false,
                    SkipReason = "File already exists and overwriting is disabled."
                });
            }

            return true;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            IAPIResponse response = await apiRequestContext.GetAsync(url, new APIRequestContextOptions
            {
                FailOnStatusCode = false,
                Headers = BuildDirectDownloadHeaders(requestHeaders),
                Timeout = options.NavigationTimeoutMs
            }).NoSync();

            cancellationToken.ThrowIfCancellationRequested();

            if (!response.Ok)
            {
                using (await resultLock.Lock(cancellationToken).NoSync())
                {
                    result.Files.Add(new PlaywrightCrawlFileResult
                    {
                        Url = url,
                        RelativePath = relativePath,
                        IsHtmlDocument = isHtmlDocument,
                        ContentType = contentType,
                        Saved = false,
                        SkipReason = $"Direct download returned HTTP {response.Status}."
                    });
                }

                return false;
            }

            contentType ??= TryGetHeaderValue(response.Headers, "content-type");

            long? contentLength = TryGetContentLength(response.Headers);

            using (await resultLock.Lock(cancellationToken).NoSync())
            {
                if (contentLength.HasValue && options.MaxStorageBytes.HasValue &&
                    result.BytesWritten + contentLength.Value > options.MaxStorageBytes.Value)
                {
                    result.StorageLimitReached = true;
                    result.Files.Add(new PlaywrightCrawlFileResult
                    {
                        Url = url,
                        RelativePath = relativePath,
                        IsHtmlDocument = isHtmlDocument,
                        ContentType = contentType,
                        Saved = false,
                        SkipReason = "Direct download would exceed the configured storage limit."
                    });

                    return false;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            byte[] body = await response.BodyAsync().NoSync();

            cancellationToken.ThrowIfCancellationRequested();

            if (body.Length == 0)
                return false;

            body = PrepareTextResourceForSave(rootUri, resourceUri, body, isHtmlDocument, contentType, options);

            using (await resultLock.Lock(cancellationToken).NoSync())
            {
                if (options.MaxStorageBytes.HasValue &&
                    result.BytesWritten + body.LongLength > options.MaxStorageBytes.Value)
                {
                    result.StorageLimitReached = true;
                    result.Files.Add(new PlaywrightCrawlFileResult
                    {
                        Url = url,
                        RelativePath = relativePath,
                        IsHtmlDocument = isHtmlDocument,
                        ContentType = contentType,
                        Saved = false,
                        SkipReason = "Direct download would exceed the configured storage limit."
                    });

                    return false;
                }
            }

            await EnsureDirectoryForFile(fullPath, cancellationToken).NoSync();

            await _fileUtil.Write(fullPath, body, true, cancellationToken).NoSync();

            using (await resultLock.Lock(cancellationToken).NoSync())
            {
                result.BytesWritten += body.LongLength;

                if (isHtmlDocument)
                    result.HtmlFilesSaved++;
                else
                    result.AssetFilesSaved++;

                result.Files.Add(new PlaywrightCrawlFileResult
                {
                    Url = url,
                    RelativePath = relativePath,
                    IsHtmlDocument = isHtmlDocument,
                    ContentType = contentType,
                    SizeBytes = body.LongLength,
                    Saved = true
                });
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Direct download failed for {Url}", url);

            using (await resultLock.Lock(cancellationToken).NoSync())
            {
                result.Files.Add(new PlaywrightCrawlFileResult
                {
                    Url = url,
                    RelativePath = relativePath,
                    IsHtmlDocument = isHtmlDocument,
                    ContentType = contentType,
                    Saved = false,
                    SkipReason = $"Direct download failed: {ex.Message}"
                });
            }

            return false;
        }
    }

    private string RewriteExternalResourceUrls(Uri rootUri, Uri documentUri, string html,
        IReadOnlyDictionary<string, string> externalResources)
    {
        string documentRelativePath =
            _urlUtil.BuildRelativePath(rootUri, documentUri, isHtmlDocument: true, contentType: "text/html");
        string documentDirectory = Path.GetDirectoryName(documentRelativePath) ?? string.Empty;
        string rewritten = html;

        foreach ((string originalUrl, string localRelativePath) in externalResources)
        {
            string replacementPath = BuildDocumentRelativeReference(documentDirectory, localRelativePath);

            rewritten = rewritten.Replace(originalUrl, replacementPath, StringComparison.OrdinalIgnoreCase);

            if (_urlUtil.TryNormalizeHttpUrl(originalUrl, out Uri? originalUri) && originalUri is not null)
            {
                var schemeRelativeUrl = $"//{originalUri.Authority}{originalUri.PathAndQuery}";
                rewritten = rewritten.Replace(schemeRelativeUrl, replacementPath, StringComparison.OrdinalIgnoreCase);
            }
        }

        return rewritten;
    }

    private async ValueTask<string> PrepareHtmlForSave(Uri rootUri, string html, PlaywrightCrawlOptions options,
        CancellationToken cancellationToken)
    {
        if (options.RewriteSameOriginAbsoluteUrls)
            html = RewriteSameOriginAbsoluteUrls(rootUri, html);

        return await PrettyPrintHtmlIfEnabled(html, options, cancellationToken).NoSync();
    }

    private static byte[] PrepareTextResourceForSave(Uri rootUri, Uri resourceUri, byte[] bytes, bool isHtmlDocument,
        string? contentType, PlaywrightCrawlOptions options)
    {
        if (!options.RewriteSameOriginAbsoluteUrls || !ShouldRewriteSameOriginAbsoluteUrls(resourceUri, isHtmlDocument, contentType))
            return bytes;

        string text = Encoding.UTF8.GetString(bytes);
        string rewritten = RewriteSameOriginAbsoluteUrls(rootUri, text);

        return string.Equals(text, rewritten, StringComparison.Ordinal) ? bytes : Encoding.UTF8.GetBytes(rewritten);
    }

    private static string RewriteSameOriginAbsoluteUrls(Uri rootUri, string html)
    {
        if (html.IsNullOrWhiteSpace())
            return html;

        string authorityPattern = BuildSameOriginAuthorityPattern(rootUri);
        string pattern =
            $@"(?:(?:{Regex.Escape(rootUri.Scheme)}:)?//){authorityPattern}(?:(?<marker>[/?#])|(?=$|[""'<>\s)\]}}]))";

        return Regex.Replace(html, pattern, match =>
        {
            string marker = match.Groups["marker"].Value;

            return marker switch
            {
                "" => "/",
                "/" => "/",
                _ => "/" + marker
            };
        }, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static string BuildSameOriginAuthorityPattern(Uri rootUri)
    {
        string host = rootUri.HostNameType == UriHostNameType.IPv6
            ? $@"\[{Regex.Escape(rootUri.Host)}\]"
            : Regex.Escape(rootUri.Host);

        if (!rootUri.IsDefaultPort)
            return $"{host}:{rootUri.Port}";

        string? defaultPort = null;

        if (string.Equals(rootUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            defaultPort = "80";
        else if (string.Equals(rootUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            defaultPort = "443";

        return defaultPort is null ? host : $"{host}(?::{defaultPort})?";
    }

    private static bool ShouldRewriteSameOriginAbsoluteUrls(Uri resourceUri, bool isHtmlDocument, string? contentType)
    {
        if (isHtmlDocument)
            return true;

        string? normalizedContentType = NormalizeContentType(contentType);

        if (string.Equals(normalizedContentType, "text/css", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedContentType, "text/html", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedContentType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
            return true;

        string extension = Path.GetExtension(resourceUri.AbsolutePath);

        return extension.Equals(".css", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".htm", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeDirectDownloadResource(Uri resourceUri)
    {
        string extension = Path.GetExtension(resourceUri.AbsolutePath);

        if (extension.IsNullOrWhiteSpace())
            return false;

        return extension.Equals(".avif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".css", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ico", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".js", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".mjs", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".svg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".woff", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".woff2", StringComparison.OrdinalIgnoreCase);
    }

    private async ValueTask<string> PrettyPrintHtmlIfEnabled(string html, PlaywrightCrawlOptions options,
        CancellationToken cancellationToken)
    {
        if (!options.PrettyPrintHtml)
            return html;

        return await _htmlFormatter.PrettyPrint(html, cancellationToken).NoSync();
    }

    private async ValueTask EnsureDirectoryForFile(string fullPath, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(fullPath);

        if (!directory.HasContent())
            return;

        if (!_createdDirectories.TryAdd(directory, 0))
            return;

        await _directoryUtil.Create(directory, true, cancellationToken).NoSync();
    }

    private static string BuildDocumentRelativeReference(string documentDirectory, string targetRelativePath)
    {
        string baseDirectory = documentDirectory.HasContent() ? documentDirectory : ".";
        string relativePath = Path.GetRelativePath(baseDirectory, targetRelativePath);

        return relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static string? TryGetHeaderValue(IReadOnlyDictionary<string, string> headers, string key)
    {
        if (headers.TryGetValue(key, out string? value))
            return value;

        foreach ((string headerKey, string headerValue) in headers)
        {
            if (string.Equals(headerKey, key, StringComparison.OrdinalIgnoreCase))
                return headerValue;
        }

        return null;
    }

    private static bool ShouldSkipObservedResponse(IResponse response, Uri resourceUri)
    {
        if (response.Status is 204 or 205 or 304)
            return true;

        if (string.Equals(response.Request.Method, "HEAD", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(response.Request.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase))
            return true;

        if (TryHasEmptyContentLength(response.Headers))
            return true;

        string path = resourceUri.AbsolutePath;
        string? contentType = TryGetHeaderValue(response.Headers, "content-type");

        if (path.StartsWith("/.well-known/vercel/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_vercel/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_next/data/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_next/flight", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_next/webpack-hmr", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.StartsWith("/_next/image", StringComparison.OrdinalIgnoreCase) && !IsImageContentType(contentType))
            return true;

        if (contentType is not null && (contentType.Contains("text/x-component", StringComparison.OrdinalIgnoreCase) ||
                                        contentType.Contains("application/x-component",
                                            StringComparison.OrdinalIgnoreCase)))
            return true;

        bool hasExtension = !string.IsNullOrWhiteSpace(Path.GetExtension(path));

        if (!hasExtension && response.Request.ResourceType is "fetch" or "xhr" && contentType is not null &&
            (contentType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
             contentType.Contains("text/plain", StringComparison.OrdinalIgnoreCase) ||
             contentType.Contains("octet-stream", StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    private static bool IsImageContentType(string? contentType)
    {
        string? normalized = NormalizeContentType(contentType);

        return normalized is not null && normalized.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldDirectDownload(Uri resourceUri, string? contentType)
    {
        if (contentType is not null)
        {
            string normalized = NormalizeContentType(contentType) ?? "";

            if (_directDownloadContentTypes.Contains(normalized))
                return true;
        }

        string extension = Path.GetExtension(resourceUri.AbsolutePath);

        if (!string.IsNullOrWhiteSpace(extension) && _directDownloadExtensions.Contains(extension))
            return true;

        return resourceUri.AbsolutePath.Contains("/_framework/", StringComparison.OrdinalIgnoreCase) &&
               (resourceUri.AbsolutePath.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase) ||
                resourceUri.AbsolutePath.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) ||
                resourceUri.AbsolutePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                resourceUri.AbsolutePath.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryHasEmptyContentLength(IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue("content-length", out string? contentLength))
            return false;

        return long.TryParse(contentLength, out long length) && length == 0;
    }

    private static long? TryGetContentLength(IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue("content-length", out string? contentLength))
            return null;

        return long.TryParse(contentLength, out long length) ? length : null;
    }

    private static string? NormalizeContentType(string? contentType)
    {
        if (contentType.IsNullOrWhiteSpace())
            return null;

        return contentType.Split(';', 2, StringSplitOptions.TrimEntries)[0].Trim();
    }

    private static Dictionary<string, string> BuildDirectDownloadHeaders(
        IReadOnlyDictionary<string, string> requestHeaders)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach ((string key, string value) in requestHeaders)
        {
            if (ShouldSkipDirectDownloadHeader(key))
                continue;

            headers[key] = value;
        }

        return headers;
    }

    private static bool ShouldSkipDirectDownloadHeader(string key)
    {
        return key.StartsWith(':') || key.Equals("host", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("content-length", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("connection", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("transfer-encoding", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("cookie", StringComparison.OrdinalIgnoreCase);
    }
}
