using System.Net;
using LinkShortener.Api.Application;

namespace LinkShortener.Tests;

public sealed class TargetUrlValidatorTests
{
    private readonly TargetUrlValidator _validator =
        new(new FixedAddressResolver(IPAddress.Parse("93.184.216.34")));

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com/path")]
    public async Task IsValidAsync_accepts_public_http_urls(string value)
    {
        Assert.True(await _validator.IsValidAsync(value, CancellationToken.None));
    }

    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("not a url")]
    [InlineData("http://localhost")]
    public async Task IsValidAsync_rejects_invalid_or_non_public_urls(string value)
    {
        Assert.False(await _validator.IsValidAsync(value, CancellationToken.None));
    }

    [Fact]
    public async Task IsValidAsync_rejects_urls_longer_than_2048_characters()
    {
        var value = $"https://example.com/{new string('a', 2048)}";

        Assert.False(await _validator.IsValidAsync(value, CancellationToken.None));
    }

    [Fact]
    public async Task IsValidAsync_rejects_private_dns_addresses()
    {
        var validator = new TargetUrlValidator(
            new FixedAddressResolver(IPAddress.Parse("192.168.1.10")));

        Assert.False(
            await validator.IsValidAsync("https://internal.example", CancellationToken.None));
    }

    private sealed class FixedAddressResolver(IPAddress address) : IHostAddressResolver
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult<IPAddress[]>([address]);
    }
}
