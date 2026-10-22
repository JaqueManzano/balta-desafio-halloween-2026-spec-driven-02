using LinkShortener.Api.Domain;
using Microsoft.Extensions.Caching.Memory;

namespace LinkShortener.Api.Application;

public sealed class LinkCache : IDisposable
{
    private readonly MemoryCache _links = new(new MemoryCacheOptions { SizeLimit = 100 });
    private readonly MemoryCache _popularity = new(new MemoryCacheOptions { SizeLimit = 10_000 });

    public Link? Get(string code) => _links.Get<Link>(code);

    public void RecordSuccessfulAccess(Link link)
    {
        var visits = _popularity.GetOrCreate(link.Code, entry =>
        {
            entry.Size = 1;
            entry.SlidingExpiration = TimeSpan.FromMinutes(10);
            return 0;
        });

        if (visits < 2)
        {
            _popularity.Set(
                link.Code,
                visits + 1,
                new MemoryCacheEntryOptions
                {
                    Size = 1,
                    SlidingExpiration = TimeSpan.FromMinutes(10)
                });
        }

        if (visits + 1 >= 2)
        {
            _links.Set(
                link.Code,
                link,
                new MemoryCacheEntryOptions
                {
                    Size = 1,
                    SlidingExpiration = TimeSpan.FromMinutes(10)
                });
        }
    }

    public void Dispose()
    {
        _links.Dispose();
        _popularity.Dispose();
    }
}
