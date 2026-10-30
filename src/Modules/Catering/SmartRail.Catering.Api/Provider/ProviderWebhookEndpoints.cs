using System.Security.Claims;
using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartRail.Catering.Application.Webhooks;
using SmartRail.Catering.Application.Webhooks.DeactivateWebhook;
using SmartRail.Catering.Application.Webhooks.GetWebhook;
using SmartRail.Catering.Application.Webhooks.RegisterWebhook;
using SmartRail.SharedKernel.Api;
using SmartRail.SharedKernel.Api.Authorization;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.Catering.Api.Provider;

/// <summary>
/// The provider's callback registration (design D3; webhook-subscriptions spec). A provider manages
/// only its own subscription, which the API key identifies, so no route names a provider.
/// </summary>
public sealed class ProviderWebhookEndpoints : ICarterModule
{
    /// <inheritdoc />
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/provider").WithTags("Provider").RequireProvider();

        group
            .MapPut("/webhook", RegisterAsync)
            .WithName("RegisterProviderWebhook")
            .Produces<WebhookSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<WebhookSubscriptionResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group
            .MapGet("/webhook", GetAsync)
            .WithName("GetProviderWebhook")
            .Produces<WebhookSubscriptionResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group
            .MapDelete("/webhook", DeactivateAsync)
            .WithName("DeactivateProviderWebhook")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> RegisterAsync(
        RegisterWebhookRequest request,
        ClaimsPrincipal user,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (!TryGetProviderId(user, out var providerId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(
            new RegisterWebhookCommand(providerId, request.Url),
            cancellationToken);

        if (result.IsFailure)
        {
            return result.ToProblemDetails();
        }

        return result.Value.Created
            ? TypedResults.Created("/api/v1/provider/webhook", result.Value.Subscription)
            : TypedResults.Ok(result.Value.Subscription);
    }

    private static async Task<IResult> GetAsync(
        ClaimsPrincipal user,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (!TryGetProviderId(user, out var providerId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(new GetWebhookQuery(providerId), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblemDetails();
    }

    private static async Task<IResult> DeactivateAsync(
        ClaimsPrincipal user,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (!TryGetProviderId(user, out var providerId))
        {
            return TypedResults.Unauthorized();
        }

        var result = await sender.Send(new DeactivateWebhookCommand(providerId), cancellationToken);

        return result.IsSuccess ? TypedResults.NoContent() : result.ToProblemDetails();
    }

    /// <summary>The provider the API key authenticated, which the provider scheme put on the principal.</summary>
    private static bool TryGetProviderId(ClaimsPrincipal user, out Guid providerId) =>
        Guid.TryParse(user.FindFirstValue(SmartRailClaims.ProviderId), out providerId);
}

/// <summary>The body of a callback registration: the one URL the provider wants SmartRail to call.</summary>
/// <param name="Url">An absolute <c>http</c> or <c>https</c> URL of at most 2,048 characters, without credentials.</param>
public sealed record RegisterWebhookRequest(string Url);
