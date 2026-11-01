using MediatR;
using SmartRail.RailNetwork.Application.Network.GetLines;
using SmartRail.RailNetwork.Application.Network.GetStation;
using SmartRail.RailNetwork.Application.Network.GetStations;
using SmartRail.RailNetwork.Application.Timetable.GetScheduledJourneys;
using SmartRail.RailNetwork.Application.Timetable.GetService;
using SmartRail.RailNetwork.Contracts;
using SmartRail.SharedKernel.Results;

namespace SmartRail.RailNetwork.UnitTests.TestDoubles;

/// <summary>
/// Stands in for MediatR's sender in the module's contract services. It records every query, so a
/// test can check which query the service sent and with which arguments, and answers with the result
/// it was given, which is what the module's own handler would have returned.
/// </summary>
internal sealed class StubSender : ISender
{
    /// <summary>Every query the service under test sent, in call order.</summary>
    internal List<object> Sent { get; } = [];

    internal Result<IReadOnlyList<LineDto>> LinesResult { get; set; } =
        Result.Success<IReadOnlyList<LineDto>>([]);

    internal Result<IReadOnlyList<StationDto>> StationsResult { get; set; } =
        Result.Success<IReadOnlyList<StationDto>>([]);

    internal Result<StationDto> StationResult { get; set; } =
        Result.Success(TestData.Station());

    internal Result<IReadOnlyList<JourneyDto>> JourneysResult { get; set; } =
        Result.Success<IReadOnlyList<JourneyDto>>([]);

    internal Result<ServiceDto> ServiceResult { get; set; } =
        Result.Success(TestData.ScheduledService());

    public Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        Sent.Add(request);

        object result = request switch
        {
            GetLinesQuery => LinesResult,
            GetStationsQuery => StationsResult,
            GetStationQuery => StationResult,
            GetScheduledJourneysQuery => JourneysResult,
            GetServiceQuery => ServiceResult,
            _ => throw new NotSupportedException(
                $"The contract services under test send no {request.GetType().Name}."),
        };

        return Task.FromResult((TResponse)result);
    }

    public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The contract services send one typed query at a time.");

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest =>
        throw new NotSupportedException("The contract services send one typed query at a time.");

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The contract services do not stream.");

    public IAsyncEnumerable<object?> CreateStream(
        object request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The contract services do not stream.");
}
