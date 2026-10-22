namespace LinkShortener.Api.Domain;

public sealed class Link
{
    public long Id { get; set; }
    public required string Code { get; set; }
    public required string TargetUrl { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public long AccessCount { get; set; }
    public required string ManagementKeyHash { get; set; }
}
