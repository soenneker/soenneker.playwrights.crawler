using System;

namespace Soenneker.Playwrights.Crawler.Dtos;

internal sealed class CrawlTarget
{
    public CrawlTarget(Uri uri, int depth)
    {
        Uri = uri;
        Depth = depth;
    }

    public Uri Uri { get; }

    public int Depth { get; }
}
