using System.Security.Claims;
using Carter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartRail.IdentityAccess.Api.Authentication;
using SmartRail.SharedKernel.Api.Authorization;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.IdentityAccess.Api.Staff;

/// <summary>
/// The one staff endpoint of MVP 1: the caller's own account and resolved role (design D5). Later
/// changes add their own staff endpoints; every one of them names the roles it allows.
/// </summary>
public sealed class StaffEndpoints : ICarterModule
{
    /// <inheritdoc />
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGroup("/identity")
            .WithTags("Identity")
            .MapGet("/me", GetCurrentStaffMember)
            .WithName("GetCurrentStaffMember")
            .Produces<CurrentStaffMemberResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireStaffRole([.. Roles.All]);
    }

    private static IResult GetCurrentStaffMember(ICurrentUser currentUser, ClaimsPrincipal user)
    {
        var username = user.FindFirstValue(StaffAuthentication.SubjectClaim);

        // The staff policy guarantees both claims; the guard keeps a misconfiguration a refusal
        // rather than a crash.
        if (currentUser.StaffId is not { } staffId
            || currentUser.Role is not { } role
            || string.IsNullOrWhiteSpace(username))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication is required.");
        }

        return TypedResults.Ok(new CurrentStaffMemberResponse(staffId, username, role));
    }
}

/// <summary>The caller's own account and role, as <c>GET /identity/me</c> returns them.</summary>
/// <param name="StaffId">The SmartRail staff account the token's subject resolved to.</param>
/// <param name="Username">The token's subject, which is the Authentik username.</param>
/// <param name="Role">The single catalogue role the token's groups resolved to.</param>
public sealed record CurrentStaffMemberResponse(Guid StaffId, string Username, string Role);
