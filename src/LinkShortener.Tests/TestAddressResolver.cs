using System.Net;
using LinkShortener.Api.Application;

namespace LinkShortener.Tests;

public sealed class TestAddressResolver : IHostAddressResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
        Task.FromResult<IPAddress[]>([IPAddress.Parse("93.184.216.34")]);
}
