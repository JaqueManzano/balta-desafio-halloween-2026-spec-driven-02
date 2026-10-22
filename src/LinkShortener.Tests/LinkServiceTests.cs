using System.Net;
using LinkShortener.Api.Application;
using LinkShortener.Api.Domain;
using Microsoft.Extensions.Configuration;

namespace LinkShortener.Tests;

public sealed class LinkServiceTests
{
    [Theory]
    [InlineData("ABCD")]
    [InlineData("A1234567891234567891")]
    public async Task CreateAsync_accepts_custom_code_with_valid_length(string code)
    {
        var repository = new InMemoryLinkRepository();
        var service = CreateService(repository);

        var created = await service.CreateAsync(
            new CreateLinkRequest("https://example.com", code, null),
            "https://request.test",
            CancellationToken.None);

        Assert.Equal(code, created.Code);
        Assert.Equal(64, repository.Links.Single().ManagementKeyHash.Length);
    }

    [Theory]
    [InlineData("abcD")]
    [InlineData("ABC0")]
    [InlineData("ABC")]
    [InlineData("AB CD")]
    public async Task CreateAsync_rejects_invalid_custom_codes(string code)
    {
        var service = CreateService(new InMemoryLinkRepository());

        await Assert.ThrowsAsync<InvalidLinkRequestException>(() =>
            service.CreateAsync(
                new CreateLinkRequest("https://example.com", code, null),
                "https://request.test",
                CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_rejects_expiration_before_today_in_utc()
    {
        var service = CreateService(new InMemoryLinkRepository());

        await Assert.ThrowsAsync<InvalidLinkRequestException>(() =>
            service.CreateAsync(
                new CreateLinkRequest(
                    "https://example.com",
                    "ABCD",
                    DateTimeOffset.UtcNow.AddDays(-1)),
                "https://request.test",
                CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_retries_generated_code_when_code_is_already_used()
    {
        var repository = new InMemoryLinkRepository();
        repository.Links.Add(new Link
        {
            Code = "AAAAAAA",
            TargetUrl = "https://existing.example",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ManagementKeyHash = new string('0', 64)
        });
        var service = CreateService(
            repository,
            new SequenceCodeGenerator("AAAAAAA", "BBBBBBB"));

        var created = await service.CreateAsync(
            new CreateLinkRequest("https://example.com", null, null),
            "https://request.test",
            CancellationToken.None);

        Assert.Equal("BBBBBBB", created.Code);
        Assert.Equal(2, repository.Links.Count);
    }

    [Fact]
    public async Task RedirectAsync_uses_cache_and_increments_every_access()
    {
        var repository = new InMemoryLinkRepository();
        repository.Links.Add(new Link
        {
            Code = "CACHE1",
            TargetUrl = "https://example.com",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ManagementKeyHash = new string('0', 64)
        });
        var service = CreateService(repository);

        for (var index = 0; index < 3; index++)
        {
            var redirect = await service.RedirectAsync("CACHE1", CancellationToken.None);
            Assert.Equal(RedirectStatus.Found, redirect.Status);
        }

        Assert.Equal(3, repository.Links.Single().AccessCount);
        Assert.Equal(2, repository.FindCalls);
    }

    [Fact]
    public async Task RedirectAsync_does_not_count_an_expired_link()
    {
        var now = new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
        var repository = new InMemoryLinkRepository();
        repository.Links.Add(new Link
        {
            Code = "EXPIRE",
            TargetUrl = "https://example.com",
            CreatedAtUtc = now.AddDays(-1),
            ExpiresAtUtc = now.AddTicks(-1),
            ManagementKeyHash = new string('0', 64)
        });
        var service = CreateService(repository, timeProvider: new FixedTimeProvider(now));

        var result = await service.RedirectAsync("EXPIRE", CancellationToken.None);

        Assert.Equal(RedirectStatus.Expired, result.Status);
        Assert.Equal(0, repository.Links.Single().AccessCount);
    }

    private static LinkService CreateService(
        InMemoryLinkRepository repository,
        ILinkCodeGenerator? codeGenerator = null,
        TimeProvider? timeProvider = null)
    {
        var resolver = new TestAddressResolver();
        return new LinkService(
            repository,
            codeGenerator ?? new LinkCodeGenerator(),
            new TargetUrlValidator(resolver),
            new LinkCache(),
            new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["PUBLIC_BASE_URL"] = "https://short.test" }).Build(),
            timeProvider ?? TimeProvider.System);
    }

    private sealed class InMemoryLinkRepository : ILinkRepository
    {
        public List<Link> Links { get; } = [];
        public int FindCalls { get; private set; }

        public Task<Link?> FindByCodeAsync(string code, CancellationToken cancellationToken)
        {
            FindCalls++;
            return Task.FromResult(Links.SingleOrDefault(link => link.Code == code));
        }

        public Task CreateAsync(Link link, CancellationToken cancellationToken)
        {
            if (Links.Any(existing => existing.Code == link.Code))
            {
                throw new DuplicateLinkCodeException(link.Code);
            }

            Links.Add(link);
            return Task.CompletedTask;
        }

        public Task<bool> IncrementAccessCountAsync(
            string code,
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken) =>
            Task.FromResult(Increment(code, nowUtc));

        private bool Increment(string code, DateTimeOffset nowUtc)
        {
            var link = Links.SingleOrDefault(item => item.Code == code);
            if (link is null || link.ExpiresAtUtc < nowUtc)
            {
                return false;
            }

            link.AccessCount++;
            return true;
        }
    }

    private sealed class SequenceCodeGenerator(params string[] codes) : ILinkCodeGenerator
    {
        private int _index;

        public string Generate(int length = 7) => codes[_index++];
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
