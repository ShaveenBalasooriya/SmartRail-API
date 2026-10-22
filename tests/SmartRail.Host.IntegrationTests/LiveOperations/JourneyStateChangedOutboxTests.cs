using System.Net;
using MediatR;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartRail.Host.IntegrationTests.Infrastructure;
using SmartRail.LiveOperations.Application.Abstractions;
using SmartRail.LiveOperations.Application.LiveState;
using SmartRail.LiveOperations.Application.LiveState.ApplyJourneyFact;
using SmartRail.LiveOperations.Contracts;
using SmartRail.LiveOperations.Domain.Calculation;
using SmartRail.LiveOperations.Infrastructure.Persistence;
using SmartRail.RailNetwork.Contracts;
using SmartRail.RailNetwork.Infrastructure.Persistence;
using SmartRail.SharedKernel.Application.Events;
using SmartRail.SharedKernel.Results;

namespace SmartRail.Host.IntegrationTests.LiveOperations;

/// <summary>
/// The one risky point of moving <see cref="JourneyStateChanged"/> onto the event bus
/// (event-publishing spec: recorded atomically with the change): an applied fact records exactly one
/// announcement in the module's outbox, in the same transaction as the journey event, and a fact
/// that fails after both rows were written leaves neither behind.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class JourneyStateChangedOutboxTests
{
    private static readonly DateTimeOffset DepartAt = new(2026, 10, 20, 8, 0, 0, TimeSpan.Zero);

    private readonly PostgresFixture _postgres;
    private readonly TestFactReader _reader = new();

    public JourneyStateChangedOutboxTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task An_applied_fact_records_one_announcement_with_the_journey_event_id_and_journey_key()
    {
        using var factory = new SmartRailApiFactory(
            _postgres.ConnectionString,
            configureServices: UseTestFactReader);

        var journeyId = await CreateLiveJourneyAsync(factory);
        var fact = new DepartureFact(1, DepartAt.AddMinutes(3), DateTimeOffset.UtcNow, 1);
        _reader.Facts = [fact];

        var result = await SendAsync(factory, FactCommand(journeyId, fact));
        Assert.True(result.IsSuccess);

        var journeyEventId = Assert.Single(await _postgres.QueryAsync(
            $"SELECT id FROM operations.journey_events WHERE journey_id = '{journeyId}'",
            reader => reader.GetGuid(0)));

        var message = Assert.Single(await _postgres.QueryAsync(
            $"""
            SELECT id, ordering_key, event_type
            FROM operations.outbox_messages
            WHERE ordering_key = '{journeyId}'
            """,
            reader => new OutboxRow(reader.GetGuid(0), reader.GetString(1), reader.GetString(2))));

        Assert.Equal(journeyEventId, message.Id);
        Assert.Equal(journeyId.ToString(), message.OrderingKey);
        Assert.Equal(nameof(JourneyStateChanged), message.EventType);
    }

    [Fact]
    public async Task A_fact_that_fails_after_its_rows_were_written_leaves_no_announcement_and_no_event()
    {
        // The handler saves the journey event and its outbox row, then commits; this unit of work
        // fails the commit, so the rows are written and the transaction truly rolls back.
        using var factory = new SmartRailApiFactory(
            _postgres.ConnectionString,
            configureServices: services =>
            {
                UseTestFactReader(services);

                services.RemoveAll<IUnitOfWork>();
                services.AddScoped<IUnitOfWork>(provider => new FailingCommitUnitOfWork(
                    provider.GetRequiredService<LiveOperationsDbContext>()));
            });

        var journeyId = await CreateLiveJourneyAsync(factory);
        var fact = new DepartureFact(1, DepartAt.AddMinutes(3), DateTimeOffset.UtcNow, 1);
        _reader.Facts = [fact];

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => SendAsync(factory, FactCommand(journeyId, fact)));

        Assert.Equal(0, await CountAsync(
            $"SELECT count(*) FROM operations.journey_events WHERE journey_id = '{journeyId}'"));
        Assert.Equal(0, await CountAsync(
            $"SELECT count(*) FROM operations.outbox_messages WHERE ordering_key = '{journeyId}'"));
    }

    /// <summary>The facts the sink should compute from, since the real reader arrives later.</summary>
    private void UseTestFactReader(IServiceCollection services)
    {
        services.RemoveAll<IJourneyFactReader>();
        services.AddSingleton<IJourneyFactReader>(_reader);
    }

    /// <summary>Creates the journey's live state through the real generator announcement.</summary>
    private async Task<Guid> CreateLiveJourneyAsync(SmartRailApiFactory factory)
    {
        using var client = factory.CreateApiClient();
        var ready = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

        var journeyId = Guid.CreateVersion7();

        using var scope = factory.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredKeyedService<IIntegrationEventPublisher>(
            IntegrationEventPublisherKeys.Network);
        var database = scope.ServiceProvider.GetRequiredService<RailNetworkDbContext>();

        publisher.Publish(
            new JourneyGenerated(
                journeyId,
                Guid.CreateVersion7(),
                new DateOnly(2026, 10, 20),
                Guid.CreateVersion7(),
                ServiceDirection.Outbound,
                Guid.CreateVersion7(),
                [
                    new JourneyStopDto(1, Guid.CreateVersion7(), true, null, DepartAt, Guid.CreateVersion7()),
                    new JourneyStopDto(2, Guid.CreateVersion7(), true, DepartAt.AddMinutes(10), DepartAt.AddMinutes(11), null),
                ]),
            journeyId);

        await database.SaveChangesAsync();

        // The live-operations.journey-generated consumer creates the state on the dispatcher's poll.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (await CountAsync(
                   $"SELECT count(*) FROM operations.journey_live_states WHERE journey_id = '{journeyId}'") == 0
               && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        Assert.Equal(1, await CountAsync(
            $"SELECT count(*) FROM operations.journey_live_states WHERE journey_id = '{journeyId}'"));

        return journeyId;
    }

    private static ApplyJourneyFactCommand FactCommand(Guid journeyId, DepartureFact fact) => new(
        journeyId,
        fact,
        JourneyEventType.DepartureLogged,
        Guid.CreateVersion7(),
        fact.DepartedAt,
        fact.RecordedAt);

    private static async Task<Result> SendAsync(
        SmartRailApiFactory factory,
        ApplyJourneyFactCommand command)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command);
    }

    private async Task<int> CountAsync(string sql)
    {
        var count = Assert.Single(await _postgres.QueryAsync(sql, reader => reader.GetInt64(0)));
        return (int)count;
    }

    private sealed record OutboxRow(Guid Id, string OrderingKey, string EventType);

    /// <summary>
    /// The module's unit of work with a commit that fails, after the handler has saved the journey
    /// event and the outbox row, so the rollback the outbox promise depends on is the real one.
    /// </summary>
    private sealed class FailingCommitUnitOfWork(LiveOperationsDbContext database) : IUnitOfWork
    {
        public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(
            CancellationToken cancellationToken = default)
        {
            return new FailingCommitTransaction(
                await database.BeginTransactionAsync(cancellationToken));
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return database.SaveChangesAsync(cancellationToken);
        }

        private sealed class FailingCommitTransaction(IUnitOfWorkTransaction transaction)
            : IUnitOfWorkTransaction
        {
            public Task CommitAsync(CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException(
                    "Simulated failure after the fact's rows were written.");
            }

            public ValueTask DisposeAsync() => transaction.DisposeAsync();
        }
    }

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
