using LinkShortener.Api.Application;
using LinkShortener.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LinkShortener.Api.Infrastructure;

public sealed class LinkRepository(LinksDbContext context) : ILinkRepository
{
    public Task<Link?> FindByCodeAsync(string code, CancellationToken cancellationToken) =>
        context.Links.AsNoTracking().SingleOrDefaultAsync(
            link => link.Code == code,
            cancellationToken);

    public async Task CreateAsync(Link link, CancellationToken cancellationToken)
    {
        context.Links.Add(link);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            context.Entry(link).State = EntityState.Detached;
            if (await context.Links.AsNoTracking().AnyAsync(
                existing => existing.Code == link.Code,
                cancellationToken))
            {
                throw new DuplicateLinkCodeException(link.Code);
            }

            throw new InvalidOperationException("Não foi possível salvar o link.", exception);
        }
    }

    public async Task<bool> IncrementAccessCountAsync(
        string code,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var updated = await context.Links
            .Where(link => link.Code == code &&
                (link.ExpiresAtUtc == null || link.ExpiresAtUtc >= nowUtc))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    link => link.AccessCount,
                    link => link.AccessCount + 1),
                cancellationToken);

        return updated == 1;
    }
}
