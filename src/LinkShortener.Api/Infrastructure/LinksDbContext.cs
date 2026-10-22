using LinkShortener.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LinkShortener.Api.Infrastructure;

public sealed class LinksDbContext(DbContextOptions<LinksDbContext> options) : DbContext(options)
{
    public DbSet<Link> Links => Set<Link>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var link = modelBuilder.Entity<Link>();
        link.HasKey(item => item.Id);
        link.HasIndex(item => item.Code).IsUnique();
        link.Property(item => item.Code).HasMaxLength(20).IsRequired();
        link.Property(item => item.TargetUrl).HasMaxLength(2048).IsRequired();
        link.Property(item => item.ManagementKeyHash).HasMaxLength(64).IsRequired();
        link.Property(item => item.CreatedAtUtc).IsRequired();
        link.Property(item => item.ExpiresAtUtc).HasConversion(
            new ValueConverter<DateTimeOffset?, long?>(
                value => value.HasValue ? value.Value.UtcTicks : null,
                value => value.HasValue
                    ? new DateTimeOffset(value.Value, TimeSpan.Zero)
                    : null));
        link.Property(item => item.AccessCount).HasDefaultValue(0);
    }
}
