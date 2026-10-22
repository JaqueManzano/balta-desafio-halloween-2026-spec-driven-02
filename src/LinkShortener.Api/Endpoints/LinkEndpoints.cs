using LinkShortener.Api.Application;
using Microsoft.AspNetCore.Mvc;

namespace LinkShortener.Api.Endpoints;

public static class LinkEndpoints
{
    public static IEndpointRouteBuilder MapLinkEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api/links");
        api.MapPost("/", CreateAsync)
            .WithName("CreateLink")
            .WithSummary("Cria um link curto")
            .WithDescription("Cria um link e devolve seu código e chave de gerenciamento.")
            .Produces<CreatedLink>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        api.MapGet("/{code}/stats", GetStatisticsAsync)
            .WithName("GetLinkStatistics")
            .WithSummary("Consulta as estatísticas de um link")
            .WithDescription("Exige a chave de gerenciamento no header X-Management-Key.")
            .Produces<LinkStatistics>()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        endpoints.MapGet("/{code}", RedirectAsync)
            .WithName("RedirectToTarget")
            .WithSummary("Redireciona para o destino do link")
            .Produces(StatusCodes.Status302Found)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status410Gone);
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        CreateLinkRequest request,
        HttpRequest httpRequest,
        LinkService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var requestBaseUrl = $"{httpRequest.Scheme}://{httpRequest.Host}";
            var created = await service.CreateAsync(
                request,
                requestBaseUrl,
                cancellationToken);
            return Results.Created(created.ShortUrl, created);
        }
        catch (InvalidLinkRequestException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Dados inválidos",
                detail: exception.Message);
        }
        catch (LinkCodeConflictException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Código indisponível",
                detail: "O código informado já está em uso.");
        }
        catch (LinkCodeGenerationException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Não foi possível criar o link",
                detail: "Tente novamente.");
        }
    }

    private static async Task<IResult> RedirectAsync(
        string code,
        LinkService service,
        CancellationToken cancellationToken)
    {
        var result = await service.RedirectAsync(code, cancellationToken);
        return result.Status switch
        {
            RedirectStatus.Found => Results.Redirect(result.TargetUrl),
            RedirectStatus.Expired => Results.Problem(
                statusCode: StatusCodes.Status410Gone,
                title: "Link expirado",
                detail: "Este link não está mais disponível."),
            _ => Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Link não encontrado",
                detail: "O código informado não existe.")
        };
    }

    private static async Task<IResult> GetStatisticsAsync(
        string code,
        [FromHeader(Name = "X-Management-Key")] string? managementKey,
        LinkService service,
        CancellationToken cancellationToken)
    {
        var statistics = await service.GetStatisticsAsync(
            code,
            managementKey,
            cancellationToken);

        return statistics is null
            ? Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Acesso não autorizado",
                detail: "A chave de gerenciamento é inválida.")
            : Results.Ok(statistics);
    }
}
