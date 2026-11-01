using SmartRail.IdentityAccess.Contracts;
using SmartRail.LiveOperations.Application.Abstractions;
using SmartRail.RailNetwork.Contracts;
using SmartRail.SharedKernel.Application.Abstractions;

namespace SmartRail.LiveOperations.UnitTests.TestDoubles;

internal sealed class StubCurrentUser(Guid? staffId, string? role = "network_controller") : ICurrentUser
{
    public Guid? StaffId { get; } = staffId;

    public string? Role { get; } = role;
}

internal sealed class StubRailwayCalendar : IRailwayCalendar
{
    public DateOnly DayOf(DateTimeOffset instant) => DateOnly.FromDateTime(instant.UtcDateTime);

    public DateTimeOffset StartOfDay(DateOnly day) =>
        new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}

internal sealed class StubStaffDirectory : IStaffDirectory
{
    private readonly IReadOnlyDictionary<Guid, string> _names;

    public StubStaffDirectory(IReadOnlyDictionary<Guid, string>? names = null) =>
        _names = names ?? new Dictionary<Guid, string>();

    public List<IReadOnlyCollection<Guid>> RequestedStaffIds { get; } = [];

    public Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(
        IReadOnlyCollection<Guid> staffIds,
        CancellationToken cancellationToken = default)
    {
        RequestedStaffIds.Add(staffIds);
        IReadOnlyDictionary<Guid, string> result = staffIds
            .Where(_names.ContainsKey)
            .ToDictionary(id => id, id => _names[id]);
        return Task.FromResult(result);
    }
}

internal sealed class SpyAccessDenials : IAccessDenials
{
    public List<string> Actions { get; } = [];

    public Task AnnounceAsync(string action, CancellationToken cancellationToken = default)
    {
        Actions.Add(action);
        return Task.CompletedTask;
    }
}

internal sealed class StubNetworkCatalog(params PlatformDto[] platforms) : INetworkCatalog
{
    private readonly PlatformDto[] _platforms = platforms;

    public List<Guid> RequestedStationIds { get; } = [];

    public IReadOnlyList<StationDto> StationsToReturn { get; set; } = [];

    public Task<IReadOnlyList<PlatformDto>> GetPlatformsAsync(
        Guid stationId,
        CancellationToken cancellationToken = default)
    {
        RequestedStationIds.Add(stationId);
        IReadOnlyList<PlatformDto> result = [.. _platforms.Where(platform => platform.StationId == stationId)];
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<LineDto>> GetLinesAsync(
        ServiceDirection direction = ServiceDirection.Outbound,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This handler only reads station platforms.");

    public Task<IReadOnlyList<StationDto>> GetStationsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(StationsToReturn);

    public Task<StationDto?> GetStationAsync(Guid stationId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This handler only reads station platforms.");

    public Task<bool> IsInterchangeAsync(Guid stationId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("This handler only reads station platforms.");
}
