using LinkShortener.Api.Domain;

namespace LinkShortener.Api.Application;

public interface ILinkRepository
{
    Task<Link?> FindByCodeAsync(string code, CancellationToken cancellationToken);
    Task CreateAsync(Link link, CancellationToken cancellationToken);
    Task<bool> IncrementAccessCountAsync(
        string code,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);
}

public sealed class DuplicateLinkCodeException(string code) : Exception(code);
