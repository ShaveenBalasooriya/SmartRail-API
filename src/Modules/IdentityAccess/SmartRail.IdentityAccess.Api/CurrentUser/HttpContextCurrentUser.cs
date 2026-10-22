using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.IdentityAccess.Api.CurrentUser;

/// <summary>
/// The Shared Kernel <see cref="ICurrentUser"/> port, backed by the request's claims (design D3).
/// A handler asks this instead of reading claims itself, so it never references Identity.
/// </summary>
internal sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? StaffId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(SmartRailClaims.StaffId), out var staffId)
            ? staffId
            : null;

    public string? Role => accessor.HttpContext?.User.FindFirstValue(SmartRailClaims.Role);
}
