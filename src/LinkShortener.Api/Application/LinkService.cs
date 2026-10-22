using System.Security.Cryptography;
using System.Text;
using LinkShortener.Api.Domain;

namespace LinkShortener.Api.Application;

public sealed record CreateLinkRequest(
    string? TargetUrl,
    string? Code,
    DateTimeOffset? ExpiresAtUtc);

public sealed record CreatedLink(string ShortUrl, string Code, string ManagementKey);
public sealed record LinkStatistics(string Code, long AccessCount);
public sealed record RedirectResult(string TargetUrl, RedirectStatus Status);
public enum RedirectStatus { Found, NotFound, Expired }

public sealed class InvalidLinkRequestException(string message) : Exception(message);
public sealed class LinkCodeConflictException : Exception { }
public sealed class LinkCodeGenerationException : Exception { }

public sealed class LinkService(
    ILinkRepository repository,
    ILinkCodeGenerator codeGenerator,
    TargetUrlValidator urlValidator,
    LinkCache cache,
    IConfiguration configuration,
    TimeProvider timeProvider)
{
    private const string AllowedCodeCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ123456789";
    private const int MaximumGeneratedCodeAttempts = 10;

    public async Task<CreatedLink> CreateAsync(
        CreateLinkRequest request,
        string requestBaseUrl,
        CancellationToken cancellationToken)
    {
        if (!await urlValidator.IsValidAsync(request.TargetUrl, cancellationToken))
        {
            throw new InvalidLinkRequestException(
                "A URL deve ser pública, usar http ou https e ter no máximo 2048 caracteres.");
        }

        var now = timeProvider.GetUtcNow();
        DateTimeOffset? expiresAtUtc = null;
        if (request.ExpiresAtUtc is { } requestedExpiration)
        {
            var expirationDateUtc = requestedExpiration.UtcDateTime.Date;
            if (expirationDateUtc < now.UtcDateTime.Date)
            {
                throw new InvalidLinkRequestException(
                    "A data de expiração não pode estar no passado.");
            }

            expiresAtUtc = new DateTimeOffset(
                expirationDateUtc.AddDays(1).AddTicks(-1),
                TimeSpan.Zero);
        }

        var customCode = request.Code is not null;
        if (customCode && !IsValidCustomCode(request.Code!))
        {
            throw new InvalidLinkRequestException(
                "O código deve ter de 4 a 20 caracteres e usar somente A-Z e os dígitos de 1 a 9.");
        }

        var managementKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        for (var attempt = 0; attempt < (customCode ? 1 : MaximumGeneratedCodeAttempts); attempt++)
        {
            var code = customCode ? request.Code! : codeGenerator.Generate();
            if (await repository.FindByCodeAsync(code, cancellationToken) is not null)
            {
                if (customCode)
                {
                    throw new LinkCodeConflictException();
                }

                continue;
            }

            var link = new Link
            {
                Code = code,
                TargetUrl = request.TargetUrl!,
                CreatedAtUtc = now,
                ExpiresAtUtc = expiresAtUtc,
                AccessCount = 0,
                ManagementKeyHash = HashKey(managementKey)
            };

            try
            {
                await repository.CreateAsync(link, cancellationToken);
                var baseUrl = configuration["PUBLIC_BASE_URL"] ?? requestBaseUrl;
                return new CreatedLink($"{baseUrl.TrimEnd('/')}/{code}", code, managementKey);
            }
            catch (DuplicateLinkCodeException)
            {
                if (customCode)
                {
                    throw new LinkCodeConflictException();
                }
            }
        }

        throw new LinkCodeGenerationException();
    }

    public async Task<RedirectResult> RedirectAsync(
        string code,
        CancellationToken cancellationToken)
    {
        var link = cache.Get(code) ??
            await repository.FindByCodeAsync(code, cancellationToken);
        if (link is null)
        {
            return new RedirectResult(string.Empty, RedirectStatus.NotFound);
        }

        var now = timeProvider.GetUtcNow();
        if (link.ExpiresAtUtc is { } expiry && expiry < now)
        {
            return new RedirectResult(string.Empty, RedirectStatus.Expired);
        }

        if (!await repository.IncrementAccessCountAsync(code, now, cancellationToken))
        {
            return new RedirectResult(string.Empty, RedirectStatus.Expired);
        }

        cache.RecordSuccessfulAccess(link);
        return new RedirectResult(link.TargetUrl, RedirectStatus.Found);
    }

    public async Task<LinkStatistics?> GetStatisticsAsync(
        string code,
        string? managementKey,
        CancellationToken cancellationToken)
    {
        var link = await repository.FindByCodeAsync(code, cancellationToken);
        if (link is null || managementKey is null)
        {
            return null;
        }

        var expectedHash = Convert.FromHexString(link.ManagementKeyHash);
        var actualHash = Convert.FromHexString(HashKey(managementKey));
        if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
        {
            return null;
        }

        return new LinkStatistics(link.Code, link.AccessCount);
    }

    private static string HashKey(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool IsValidCustomCode(string code) =>
        code.Length is >= 4 and <= 20 &&
        code.All(AllowedCodeCharacters.Contains);
}
