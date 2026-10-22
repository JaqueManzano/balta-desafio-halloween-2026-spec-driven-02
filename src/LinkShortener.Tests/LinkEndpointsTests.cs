using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LinkShortener.Api.Application;
using LinkShortener.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LinkShortener.Tests;

public sealed class LinkEndpointsTests
{
    [Fact]
    public async Task Create_redirect_stats_and_duplicate_code_work()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.BaseAddress = new Uri("http://localhost:62382");

        var created = await CreateLinkAsync(client, "HAPPY1");
        Assert.True(
            created.StatusCode == HttpStatusCode.Created,
            await created.Content.ReadAsStringAsync());
        var link = await created.Content.ReadFromJsonAsync<CreatedLink>();
        Assert.NotNull(link);
        Assert.Equal("http://localhost:62382/HAPPY1", link!.ShortUrl);

        using var duplicate = await client.PostAsJsonAsync(
            "/api/links",
            new { targetUrl = "https://example.com", code = "HAPPY1" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        using var redirect = await client.GetAsync("/HAPPY1");
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("https://example.com/", redirect.Headers.Location?.ToString());

        using var denied = await client.GetAsync("/api/links/HAPPY1/stats");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        using var statsRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/links/HAPPY1/stats");
        statsRequest.Headers.Add("X-Management-Key", link!.ManagementKey);
        using var stats = await client.SendAsync(statsRequest);
        Assert.Equal(HttpStatusCode.OK, stats.StatusCode);
        using var body = JsonDocument.Parse(await stats.Content.ReadAsStringAsync());
        Assert.Equal(1, body.RootElement.GetProperty("accessCount").GetInt64());
    }

    [Fact]
    public async Task Create_rejects_invalid_url_with_problem_details()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/links",
            new { targetUrl = "ftp://example.com" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Create_generates_a_seven_character_code_when_omitted()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/api/links",
            new { targetUrl = "https://example.com" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var link = await response.Content.ReadFromJsonAsync<CreatedLink>();
        Assert.NotNull(link);
        Assert.Equal(7, link!.Code.Length);
        Assert.All(link.Code, character =>
            Assert.Contains(character, "ABCDEFGHJKMNPQRSTUVWXYZ123456789"));
    }

    [Fact]
    public async Task Swagger_ui_and_document_describe_all_endpoints_in_development()
    {
        using var factory = CreateFactory().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Development"));
        using var client = factory.CreateClient();

        using var ui = await client.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, ui.StatusCode);
        Assert.Contains("Swagger UI", await ui.Content.ReadAsStringAsync());

        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var createPath = paths.EnumerateObject()
            .Single(path => path.Name.TrimEnd('/') == "/api/links");
        Assert.True(createPath.Value.TryGetProperty("post", out var createOperation));
        Assert.Equal("Cria um link curto", createOperation.GetProperty("summary").GetString());

        var stats = paths.GetProperty("/api/links/{code}/stats").GetProperty("get");
        Assert.Contains(
            "X-Management-Key",
            stats.GetProperty("description").GetString());
        Assert.Contains(
            stats.GetProperty("parameters").EnumerateArray(),
            parameter =>
                parameter.GetProperty("name").GetString() == "X-Management-Key" &&
                parameter.GetProperty("in").GetString() == "header");
        Assert.Equal(
            "Redireciona para o destino do link",
            paths.GetProperty("/{code}").GetProperty("get")
                .GetProperty("summary").GetString());
    }

    [Fact]
    public async Task Swagger_ui_is_not_available_outside_development()
    {
        using var factory = CreateFactory().WithWebHostBuilder(builder =>
            builder.UseEnvironment("Production"));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Expired_link_returns_gone_without_counting_access()
    {
        var clock = new TestTimeProvider(new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero));
        using var factory = CreateFactory(clock);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var created = await client.PostAsJsonAsync(
            "/api/links",
            new
            {
                targetUrl = "https://example.com",
                code = "EXPIRE",
                expiresAtUtc = new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero)
            });
        Assert.True(
            created.StatusCode == HttpStatusCode.Created,
            await created.Content.ReadAsStringAsync());
        var link = await created.Content.ReadFromJsonAsync<CreatedLink>();
        Assert.NotNull(link);

        clock.SetUtcNow(new DateTimeOffset(2026, 10, 24, 0, 0, 0, TimeSpan.Zero));
        using var expired = await client.GetAsync("/EXPIRE");
        Assert.Equal(HttpStatusCode.Gone, expired.StatusCode);

        using var statsRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/links/EXPIRE/stats");
        statsRequest.Headers.Add("X-Management-Key", link!.ManagementKey);
        using var stats = await client.SendAsync(statsRequest);
        using var body = JsonDocument.Parse(await stats.Content.ReadAsStringAsync());
        Assert.Equal(0, body.RootElement.GetProperty("accessCount").GetInt64());
    }

    [Fact]
    public async Task Parallel_redirects_increment_counter_without_lost_updates()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        using var created = await CreateLinkAsync(client, "PARALL");
        Assert.True(
            created.StatusCode == HttpStatusCode.Created,
            await created.Content.ReadAsStringAsync());
        var link = await created.Content.ReadFromJsonAsync<CreatedLink>();
        Assert.NotNull(link);

        var redirects = await Task.WhenAll(
            Enumerable.Range(0, 24).Select(async _ =>
            {
                using var response = await client.GetAsync("/PARALL");
                return response.StatusCode;
            }));
        Assert.All(redirects, status => Assert.Equal(HttpStatusCode.Redirect, status));

        using var statsRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/links/PARALL/stats");
        statsRequest.Headers.Add("X-Management-Key", link!.ManagementKey);
        using var stats = await client.SendAsync(statsRequest);
        using var body = JsonDocument.Parse(await stats.Content.ReadAsStringAsync());
        Assert.Equal(24, body.RootElement.GetProperty("accessCount").GetInt64());

        using var invalidKey = new HttpRequestMessage(HttpMethod.Get, "/api/links/PARALL/stats");
        invalidKey.Headers.Add("X-Management-Key", "wrong-key");
        using var denied = await client.SendAsync(invalidKey);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    private static Task<HttpResponseMessage> CreateLinkAsync(HttpClient client, string code) =>
        client.PostAsJsonAsync(
            "/api/links",
            new { targetUrl = "https://example.com", code });

    private static WebApplicationFactory<Program> CreateFactory(TestTimeProvider? clock = null)
    {
        var connectionString =
            $"Data Source=link-shortener-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        return new TestApplicationFactory(connectionString, clock);
    }

    private sealed class TestApplicationFactory(
        string connectionString,
        TestTimeProvider? clock) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureTestServices(services =>
            {
                services.Replace(
                    ServiceDescriptor.Singleton<IHostAddressResolver, TestAddressResolver>());
                services.RemoveAll<LinksDbContext>();
                services.RemoveAll<DbContextOptions<LinksDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<LinksDbContext>>();
                var keepAliveConnection = new SqliteConnection(connectionString);
                keepAliveConnection.Open();
                services.AddSingleton(keepAliveConnection);
                services.AddDbContext<LinksDbContext>(options =>
                    options.UseSqlite(connectionString));
                if (clock is not null)
                {
                    services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
                }
            });
        }
    }

    private sealed class TestTimeProvider(DateTimeOffset initialUtc) : TimeProvider
    {
        private DateTimeOffset _now = initialUtc;

        public override DateTimeOffset GetUtcNow() => _now;
        public void SetUtcNow(DateTimeOffset value) => _now = value;
    }

    private sealed class CreatedLink
    {
        public required string ShortUrl { get; init; }
        public required string Code { get; init; }
        public required string ManagementKey { get; init; }
    }
}
