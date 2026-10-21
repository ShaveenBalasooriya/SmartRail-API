using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using SmartRail.IdentityAccess.Api.Authentication;
using SmartRail.IdentityAccess.Application.Staff.GetStaffMemberBySubject;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.IdentityAccess.Api.Claims;

/// <summary>
/// Links an Authentik token to a SmartRail staff account and resolves the caller's role (design D2).
/// The subject is looked up once; an unknown subject gets no <c>staff_id</c> and is refused. The
/// role is the token's only catalogue group in <c>groups</c>; none or two groups means no role.
/// </summary>
/// <remarks>
/// The transformation must be idempotent: the authentication stack may call it more than once per
/// request, so each claim is only added when it is not already present.
/// </remarks>
internal sealed class StaffClaimsTransformation(ISender sender) : IClaimsTransformation
{
    private const string GroupsClaim = "groups";

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        foreach (var identity in principal.Identities)
        {
            if (!identity.IsAuthenticated
                || !string.Equals(identity.AuthenticationType, StaffAuthentication.Scheme, StringComparison.Ordinal))
            {
                continue;
            }

            var subject = identity.FindFirst(StaffAuthentication.SubjectClaim)?.Value;
            if (string.IsNullOrWhiteSpace(subject))
            {
                continue;
            }

            if (identity.FindFirst(SmartRailClaims.StaffId) is null)
            {
                var staffMember = await sender.Send(new GetStaffMemberBySubjectQuery(subject));

                if (staffMember.IsSuccess)
                {
                    identity.AddClaim(new Claim(
                        SmartRailClaims.StaffId,
                        staffMember.Value.StaffId.ToString()));
                }
            }

            if (identity.FindFirst(SmartRailClaims.Role) is not null)
            {
                continue;
            }

            if (ResolveRole(identity) is { } role)
            {
                identity.AddClaim(new Claim(SmartRailClaims.Role, role));
            }
        }

        return principal;
    }

    /// <summary>The single catalogue group the token names, or <see langword="null"/>.</summary>
    private static string? ResolveRole(ClaimsIdentity identity)
    {
        var groups = identity.FindAll(GroupsClaim)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.Ordinal);

        var roles = Roles.All.Where(groups.Contains).ToArray();

        return roles.Length == 1 ? roles[0] : null;
    }
}
