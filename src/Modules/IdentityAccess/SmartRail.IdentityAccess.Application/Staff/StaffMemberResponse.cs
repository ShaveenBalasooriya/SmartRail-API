namespace SmartRail.IdentityAccess.Application.Staff;

/// <summary>The staff account a validated token's subject resolves to.</summary>
public sealed record StaffMemberResponse(Guid StaffId, string Username, string DisplayName);
