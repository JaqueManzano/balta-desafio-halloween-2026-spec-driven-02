using LinkShortener.Api.Application;
using LinkShortener.Api.Endpoints;
using LinkShortener.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Links") ??
    "Data Source=links.db";
builder.Services.AddDbContext<LinksDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddScoped<ILinkRepository, LinkRepository>();
builder.Services.AddScoped<LinkService>();
builder.Services.AddSingleton<ILinkCodeGenerator, LinkCodeGenerator>();
builder.Services.AddSingleton<IHostAddressResolver, DnsHostAddressResolver>();
builder.Services.AddSingleton<TargetUrlValidator>();
builder.Services.AddSingleton<LinkCache>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
    options.SwaggerDoc(
        "v1",
        new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "Link Shortener API",
            Version = "v1"
        }));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<LinksDbContext>();
    await database.Database.MigrateAsync();
}

app.MapLinkEndpoints();
app.Run();

public partial class Program { }
