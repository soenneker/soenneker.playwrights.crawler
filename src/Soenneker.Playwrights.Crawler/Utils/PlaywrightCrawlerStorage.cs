using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
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
    private static readonly HttpClient _httpClient = CreateHttpClient();

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

    public PlaywrightCrawlerStorage(ILogger<PlaywrightCrawlerStorage> logger, IFileUtil fileUtil, IDirectoryUtil directoryUtil,
        IPlaywrightCrawlerUrlUtil urlUtil, IPlaywrightCrawlerPolicyUtil policyUtil, IHtmlFormatter htmlFormatter)
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

    public async ValueTask SaveRenderedDocument(Uri rootUri, Uri documentUri, string html, PlaywrightCrawlOptions options, PlaywrightCrawlResult result,
        ConcurrentDictionary<string, byte> savedUrls, AsyncLock resultLock, CancellationToken cancellationToken)
    {
        string htmlToSave = await PrettyPrintHtmlIfEnabled(html, options, cancellationToken).NoSync();

        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(htmlToSave);
        string relativePath = _urlUtil.BuildRelativePath(rootUri, documentUri, isHtmlDocument: true, contentType: "text/html");

        await SaveFile(documentUri.AbsoluteUri, relativePath, bytes, isHtmlDocument: true, "text/html", options, result, savedUrls, resultLock,
            cancellationToken).NoSync();
    }

    public async ValueTask<IReadOnlyDictionary<string, string>> SaveObservedResponses(IEnumerable<IResponse> responses, Uri rootUri, Uri mainDocumentUri,
        PlaywrightCrawlOptions options, PlaywrightCrawlResult result, ConcurrentDictionary<string, byte> savedUrls, AsyncLock resultLock, Stopwatch stopwatch,
        CancellationToken cancellationToken)
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

            bool isHtmlDocument = string.Equals(response.Request.ResourceType, "document", StringComparison.OrdinalIgnoreCase);

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
                resourceAvailable = await SaveByDirectDownload(normalizedUrl, relativePath, isHtmlDocument, contentType, options, result, savedUrls, resultLock,
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
                        "Unable to read response body for {Url} (status: {StatusCode}, ok: {Ok}, resourceType: {ResourceType}, method: {Method}, contentType: {ContentType})",
                        response.Url, response.Status, response.Ok, response.Request.ResourceType, response.Request.Method, contentType);
                    continue;
                }

                if (body.Length == 0)
                    continue;

                resourceAvailable = await SaveFile(normalizedUrl, relativePath, body, isHtmlDocument, contentType, options, result, savedUrls, resultLock,
                    cancellationToken).NoSync();
            }

            if (resourceAvailable && !isHtmlDocument && !_urlUtil.UrisShareHost(rootUri, resourceUri))
                externalResources[normalizedUrl] = relativePath;
        }

        return externalResources;
    }

    public async ValueTask RewriteExternalResourceUrlsInSavedDocument(Uri rootUri, Uri documentUri, string html,
        IReadOnlyDictionary<string, string> externalResources, PlaywrightCrawlOptions options, PlaywrightCrawlResult result, AsyncLock resultLock,
        CancellationToken cancellationToken)
    {
        if (!options.IncludeCrossOriginAssets || externalResources.Count == 0)
            return;

        string originalHtml = await PrettyPrintHtmlIfEnabled(html, options, cancellationToken).NoSync();

        string rewrittenHtml = RewriteExternalResourceUrls(rootUri, documentUri, html, externalResources);
        rewrittenHtml = await PrettyPrintHtmlIfEnabled(rewrittenHtml, options, cancellationToken).NoSync();

        if (string.Equals(rewrittenHtml, originalHtml, StringComparison.Ordinal))
            return;

        string relativePath = _urlUtil.BuildRelativePath(rootUri, documentUri, isHtmlDocument: true, contentType: "text/html");
        string fullPath = Path.Combine(result.SaveDirectory, relativePath);
        byte[] rewrittenBytes = System.Text.Encoding.UTF8.GetBytes(rewrittenHtml);
        long sizeDelta = rewrittenBytes.LongLength - System.Text.Encoding.UTF8.GetByteCount(originalHtml);

        using (await resultLock.Lock(cancellationToken).NoSync())
        {
            if (sizeDelta > 0 && options.MaxStorageBytes.HasValue && result.BytesWritten + sizeDelta > options.MaxStorageBytes.Value)
            {
                _logger.LogDebug("Skipping cross-origin URL rewrite for {Url} because the rewritten HTML would exceed the storage limit.",
                    documentUri.AbsoluteUri);
                return;
            }

            result.BytesWritten += sizeDelta;

            PlaywrightCrawlFileResult? file = result.Files.LastOrDefault(fileResult =>
                fileResult.Saved && fileResult.IsHtmlDocument && string.Equals(fileResult.Url, documentUri.AbsoluteUri, StringComparison.OrdinalIgnoreCase));

            if (file is not null)
                file.SizeBytes = rewrittenBytes.LongLength;
        }

        await EnsureDirectoryForFile(fullPath, cancellationToken).NoSync();

        await _fileUtil.Write(fullPath, rewrittenBytes, log: true, cancellationToken).NoSync();
    }

    public async ValueTask<bool> SaveFile(string url, string relativePath, byte[] bytes, bool isHtmlDocument, string? contentType,
        PlaywrightCrawlOptions options, PlaywrightCrawlResult result, ConcurrentDictionary<string, byte> savedUrls, AsyncLock resultLock,
        CancellationToken cancellationToken)
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
            if (options.MaxStorageBytes.HasValue && result.BytesWritten + bytes.LongLength > options.MaxStorageBytes.Value)
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

    private async ValueTask<bool> SaveByDirectDownload(string url, string relativePath, bool isHtmlDocument, string? contentType,
        PlaywrightCrawlOptions options, PlaywrightCrawlResult result, ConcurrentDictionary<string, byte> savedUrls, AsyncLock resultLock,
        CancellationToken cancellationToken)
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
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            using HttpResponseMessage response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).NoSync();

            if (!response.IsSuccessStatusCode)
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
                        SkipReason = $"Direct download returned HTTP {(int)response.StatusCode}."
                    });
                }

                return false;
            }

            long? contentLength = response.Content.Headers.ContentLength;

            using (await resultLock.Lock(cancellationToken).NoSync())
            {
                if (contentLength.HasValue && options.MaxStorageBytes.HasValue && result.BytesWritten + contentLength.Value > options.MaxStorageBytes.Value)
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

            await using Stream httpStream = await response.Content.ReadAsStreamAsync(cancellationToken).NoSync();

            await using Stream fileStream = _fileUtil.OpenWrite(fullPath, log: true);

            byte[] buffer = new byte[81920];
            long bytesWritten = 0;

            while (true)
            {
                int read = await httpStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).NoSync();

                if (read == 0)
                    break;

                using (await resultLock.Lock(cancellationToken).NoSync())
                {
                    if (options.MaxStorageBytes.HasValue && result.BytesWritten + bytesWritten + read > options.MaxStorageBytes.Value)
                    {
                        result.StorageLimitReached = true;
                        result.Files.Add(new PlaywrightCrawlFileResult
                        {
                            Url = url,
                            RelativePath = relativePath,
                            IsHtmlDocument = isHtmlDocument,
                            ContentType = contentType,
                            Saved = false,
                            SkipReason = "Direct download exceeded the configured storage limit mid-stream."
                        });

                        return false;
                    }
                }

                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).NoSync();

                bytesWritten += read;
            }

            await fileStream.FlushAsync(cancellationToken).NoSync();

            using (await resultLock.Lock(cancellationToken).NoSync())
            {
                result.BytesWritten += bytesWritten;

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
                    SizeBytes = bytesWritten,
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

    private string RewriteExternalResourceUrls(Uri rootUri, Uri documentUri, string html, IReadOnlyDictionary<string, string> externalResources)
    {
        string documentRelativePath = _urlUtil.BuildRelativePath(rootUri, documentUri, isHtmlDocument: true, contentType: "text/html");
        string documentDirectory = Path.GetDirectoryName(documentRelativePath) ?? string.Empty;
        string rewritten = html;

        foreach ((string originalUrl, string localRelativePath) in externalResources)
        {
            string replacementPath = BuildDocumentRelativeReference(documentDirectory, localRelativePath);

            rewritten = rewritten.Replace(originalUrl, replacementPath, StringComparison.OrdinalIgnoreCase);

            if (_urlUtil.TryNormalizeHttpUrl(originalUrl, out Uri? originalUri) && originalUri is not null)
            {
                string schemeRelativeUrl = $"//{originalUri.Authority}{originalUri.PathAndQuery}";
                rewritten = rewritten.Replace(schemeRelativeUrl, replacementPath, StringComparison.OrdinalIgnoreCase);
            }
        }

        return rewritten;
    }

    private async ValueTask<string> PrettyPrintHtmlIfEnabled(string html, PlaywrightCrawlOptions options, CancellationToken cancellationToken)
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
        return headers.TryGetValue(key, out string? value) ? value : null;
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

        if (path.StartsWith("/.well-known/vercel/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/_vercel/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_next/data/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/_next/flight", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/_next/webpack-hmr", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/_next/image", StringComparison.OrdinalIgnoreCase))
            return true;

        string? contentType = TryGetHeaderValue(response.Headers, "content-type");

        if (contentType is not null && (contentType.Contains("text/x-component", StringComparison.OrdinalIgnoreCase) ||
                                        contentType.Contains("application/x-component", StringComparison.OrdinalIgnoreCase)))
            return true;

        bool hasExtension = !string.IsNullOrWhiteSpace(Path.GetExtension(path));

        if (!hasExtension && response.Request.ResourceType is "fetch" or "xhr" && contentType is not null &&
            (contentType.Contains("json", StringComparison.OrdinalIgnoreCase) || contentType.Contains("text/plain", StringComparison.OrdinalIgnoreCase) ||
             contentType.Contains("octet-stream", StringComparison.OrdinalIgnoreCase)))
            return true;

        return false;
    }

    private static bool ShouldDirectDownload(Uri resourceUri, string? contentType)
    {
        if (contentType is not null)
        {
            string normalized = contentType.Split(';', 2, StringSplitOptions.TrimEntries)[0].Trim();

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

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate | System.Net.DecompressionMethods.Brotli
        };

        var client = new HttpClient(handler, disposeHandler: true);
        client.Timeout = TimeSpan.FromMinutes(5);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Soenneker.Playwrights.Crawler/1.0");

        return client;
    }
}