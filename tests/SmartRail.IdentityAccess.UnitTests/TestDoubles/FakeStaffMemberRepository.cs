using SmartRail.IdentityAccess.Application.Staff;

namespace SmartRail.IdentityAccess.UnitTests.TestDoubles;

/// <summary>An in-memory staff directory whose responses belong to one test.</summary>
internal sealed class FakeStaffMemberRepository : IStaffMemberRepository
{
    private readonly Dictionary<string, StaffMemberResponse> _bySubject = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, string> _displayNames = [];

    public List<string> RequestedSubjects { get; } = [];

    public List<IReadOnlyCollection<Guid>> RequestedIdSets { get; } = [];

    public Exception? ExceptionToThrow { get; set; }

    public void Add(StaffMemberResponse staffMember, string subject)
    {
        _bySubject[subject] = staffMember;
        _displayNames[staffMember.StaffId] = staffMember.DisplayName;
    }

    public Task<StaffMemberResponse?> GetBySubjectAsync(
        string subject,
        CancellationToken cancellationToken = default)
    {
        RequestedSubjects.Add(subject);
        ThrowIfRequested();
        return Task.FromResult(_bySubject.GetValueOrDefault(subject));
    }

    public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(
        IReadOnlyCollection<Guid> staffIds,
        CancellationToken cancellationToken = default)
    {
        RequestedIdSets.Add(staffIds);
        ThrowIfRequested();
        return Task.FromResult<IReadOnlyDictionary<Guid, string>>(
            staffIds.Where(_displayNames.ContainsKey).ToDictionary(id => id, id => _displayNames[id]));
    }

    private void ThrowIfRequested()
    {
        if (ExceptionToThrow is not null)
        {
            throw ExceptionToThrow;
        }
    }
}
