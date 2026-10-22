using System.Net;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.LiveOperations.Application.LiveState;
using SmartRail.LiveOperations.Application.LiveState.ApplyJourneyFact;
using SmartRail.LiveOperations.Contracts;
using SmartRail.LiveOperations.Domain.Calculation;
using SmartRail.RailNetwork.Contracts;

namespace SmartRail.Host.IntegrationTests.LiveOperations;

/// <summary>
/// The one risky point of applying a fact (design D4): one fact produces one event with the next
/// sequence number, an identical fact produces none, and the following different fact continues
/// the numbering.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class JourneyFactSinkTests : IDisposable
{
    private readonly PostgresFixture _postgres;
    private readonly TestFactReader _reader = new();
    private readonly SmartRailApiFactory _factory;

    public JourneyFactSinkTests(PostgresFixture postgres)
    {
        _postgres = postgres;
        _factory = new SmartRailApiFactory(
            postgres.ConnectionString,
            configureServices: services =>
            {
                services.RemoveAll<IJourneyFactReader>();
                services.AddSingleton<IJourneyFactReader>(_reader);
            });
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task One_fact_appends_one_event_and_an_identical_fact_appends_none()
    {
        using var client = _factory.CreateApiClient();
        var ready = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

        var journeyId = Guid.CreateVersion7();
        var departAt = new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

        using var scope = _factory.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        await publisher.Publish(new JourneyGenerated(
            journeyId,
            Guid.CreateVersion7(),
            new DateOnly(2026, 10, 20),
            Guid.CreateVersion7(),
            ServiceDirection.Outbound,
            Guid.CreateVersion7(),
            [
                new JourneyStopDto(1, Guid.CreateVersion7(), true, null, departAt, Guid.CreateVersion7()),
                new JourneyStopDto(2, Guid.CreateVersion7(), true, departAt.AddMinutes(10), departAt.AddMinutes(11), null),
            ]));

        var first = new DepartureFact(1, departAt.AddMinutes(3), DateTimeOffset.UtcNow, 1);
        _reader.Facts = [first];
        var firstCommand = new ApplyJourneyFactCommand(
            journeyId,
            first,
            JourneyEventType.DepartureLogged,
            Guid.CreateVersion7(),
            first.DepartedAt,
            first.RecordedAt);

        var firstResult = await sender.Send(firstCommand);
        Assert.True(firstResult.IsSuccess);

        var appended = Assert.Single(await EventsAsync(journeyId));
        Assert.Equal(1, appended.SequenceNumber);
        Assert.True(appended.StreamPosition > 0);
        Assert.Equal("departure_logged", appended.EventType);

        // The snapshot names the status instead of storing the numeric enum value. The first of two
        // calling stops has departed, so the journey is running unless the clock has passed the
        // terminus's expected arrival.
        using var payload = JsonDocument.Parse(appended.Payload);
        var status = payload.RootElement.GetProperty("status").GetString();
        Assert.True(
            status is "running" or "completed",
            $"The stored snapshot should name its status, but was '{status}'.");

        // The same fact again changes nothing, so it appends nothing.
        var replayResult = await sender.Send(firstCommand);
        Assert.True(replayResult.IsSuccess);
        Assert.Single(await EventsAsync(journeyId));

        var second = new DepartureFact(2, departAt.AddMinutes(15), DateTimeOffset.UtcNow, 2);
        _reader.Facts = [first, second];
        var secondResult = await sender.Send(new ApplyJourneyFactCommand(
            journeyId,
            second,
            JourneyEventType.DepartureLogged,
            Guid.CreateVersion7(),
            second.DepartedAt,
            second.RecordedAt));
        Assert.True(secondResult.IsSuccess);

        var events = await EventsAsync(journeyId);
        Assert.Equal([1L, 2L], events.Select(appendedEvent => appendedEvent.SequenceNumber));
    }

    private Task<List<EventRow>> EventsAsync(Guid journeyId)
    {
        return _postgres.QueryAsync(
            $"""
            SELECT sequence_number, stream_position, event_type, payload
            FROM operations.journey_events
            WHERE journey_id = '{journeyId}'
            ORDER BY sequence_number
            """,
            reader => new EventRow(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetString(3)));
    }

    private sealed record EventRow(
        long SequenceNumber,
        long StreamPosition,
        string EventType,
        string Payload);

    /// <summary>
    /// The real reader arrives with the departure-log and status-change changes; this test supplies
    /// the facts the sink should compute from.
    /// </summary>
    private sealed class TestFactReader : IJourneyFactReader
    {
        public IReadOnlyList<Fact> Facts { get; set; } = [];

        public Task<IReadOnlyList<Fact>> GetFactsAsync(
            Guid journeyId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Facts);
        }
    }
}
