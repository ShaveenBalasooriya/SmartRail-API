using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.IdentityAccess.UnitTests.TestDoubles;

/// <summary>The caller supplied by one test.</summary>
internal sealed class StubCurrentUser(Guid? staffId, string? role) : ICurrentUser
{
    public Guid? StaffId { get; } = staffId;

    public string? Role { get; } = role;
}
