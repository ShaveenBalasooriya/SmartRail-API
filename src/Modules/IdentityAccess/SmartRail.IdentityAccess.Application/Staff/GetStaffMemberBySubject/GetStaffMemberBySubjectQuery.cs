using SmartRail.SharedKernel.Application.Messaging;

namespace SmartRail.IdentityAccess.Application.Staff.GetStaffMemberBySubject;

/// <summary>
/// One staff account by identity-provider subject. A subject with no account is a not-found result,
/// which is what makes an otherwise valid token from an unknown user a refusal (design D2).
/// </summary>
public sealed record GetStaffMemberBySubjectQuery(string Subject) : IQuery<StaffMemberResponse>;
