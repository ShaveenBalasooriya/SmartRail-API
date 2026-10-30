using SmartRail.LiveOperations.Domain.Changes;

namespace SmartRail.LiveOperations.UnitTests.Changes;

/// <summary>
/// The shape rules of a published change: each type accepts only the values that belong to it.
/// Business rules against the journey's live state (departed stops, journey status, platform
/// ranges) are applied by the publishing use cases, not here.
/// </summary>
public sealed class StatusChangeFactoryTests
{
    private static readonly DateTimeOffset PublishedAt = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    private static Guid JourneyId => Guid.CreateVersion7();

    private static Guid ControllerId => Guid.CreateVersion7();

    [Fact]
    public void A_delay_carries_its_starting_stop_and_minutes()
    {
        var result = StatusChange.PublishDelay(JourneyId, 3, 15, ControllerId, PublishedAt);

        Assert.True(result.IsSuccess);
        var change = result.Value;
        Assert.Equal(StatusChangeType.Delay, change.Type);
        Assert.Equal(3, change.StopSequence);
        Assert.Equal(15, change.DelayMinutes);
        Assert.Null(change.NewPlatformId);
        Assert.Null(change.PreviousPlatformId);
        Assert.Null(change.CorrectsChangeId);
        Assert.Equal(ChangeProvenance.AutomaticallyPublished, change.Provenance);
        Assert.Equal(PublishedAt, change.PublishedAt);
        Assert.NotEqual(Guid.Empty, change.Id);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void A_delay_needs_a_calling_stop(int stopSequence)
    {
        var result = StatusChange.PublishDelay(JourneyId, stopSequence, 15, ControllerId, PublishedAt);

        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.DelayStopSequenceRequired, result.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(721)]
    public void A_delay_is_one_to_720_minutes(int minutes)
    {
        var result = StatusChange.PublishDelay(JourneyId, 2, minutes, ControllerId, PublishedAt);

        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.DelayMinutesOutOfRange, result.Error);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(720)]
    public void A_delay_accepts_the_edges_of_the_range(int minutes)
    {
        Assert.True(StatusChange.PublishDelay(JourneyId, 2, minutes, ControllerId, PublishedAt).IsSuccess);
    }

    [Fact]
    public void A_cancellation_carries_no_values()
    {
        var result = StatusChange.PublishCancellation(JourneyId, ControllerId, PublishedAt);

        Assert.True(result.IsSuccess);
        var change = result.Value;
        Assert.Equal(StatusChangeType.Cancellation, change.Type);
        Assert.Null(change.StopSequence);
        Assert.Null(change.DelayMinutes);
        Assert.Null(change.NewPlatformId);
        Assert.Null(change.PreviousPlatformId);
        Assert.Null(change.CorrectsChangeId);
    }

    [Fact]
    public void A_termination_carries_the_last_stop_served()
    {
        var result = StatusChange.PublishTermination(JourneyId, 3, ControllerId, PublishedAt);

        Assert.True(result.IsSuccess);
        var change = result.Value;
        Assert.Equal(StatusChangeType.Termination, change.Type);
        Assert.Equal(3, change.StopSequence);
        Assert.Null(change.CorrectsChangeId);
    }

    [Fact]
    public void A_termination_needs_a_last_stop()
    {
        var result = StatusChange.PublishTermination(JourneyId, 0, ControllerId, PublishedAt);

        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.TerminationStopSequenceRequired, result.Error);
    }

    [Fact]
    public void A_platform_change_carries_both_platforms()
    {
        var previous = Guid.CreateVersion7();
        var next = Guid.CreateVersion7();

        var result = StatusChange.PublishPlatformChange(JourneyId, 2, next, previous, ControllerId, PublishedAt);

        Assert.True(result.IsSuccess);
        var change = result.Value;
        Assert.Equal(StatusChangeType.PlatformChange, change.Type);
        Assert.Equal(2, change.StopSequence);
        Assert.Equal(next, change.NewPlatformId);
        Assert.Equal(previous, change.PreviousPlatformId);
        Assert.Null(change.DelayMinutes);
        Assert.Null(change.CorrectsChangeId);
    }

    [Fact]
    public void A_platform_change_may_omit_the_previous_platform()
    {
        var result = StatusChange.PublishPlatformChange(
            JourneyId, 2, Guid.CreateVersion7(), previousPlatformId: null, ControllerId, PublishedAt);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.PreviousPlatformId);
    }

    [Fact]
    public void A_platform_change_needs_its_stop()
    {
        var result = StatusChange.PublishPlatformChange(
            JourneyId, 0, Guid.CreateVersion7(), null, ControllerId, PublishedAt);

        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.PlatformStopSequenceRequired, result.Error);
    }

    [Fact]
    public void A_platform_change_needs_a_new_platform()
    {
        var result = StatusChange.PublishPlatformChange(
            JourneyId, 2, Guid.Empty, null, ControllerId, PublishedAt);

        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.NewPlatformRequired, result.Error);
    }

    [Fact]
    public void A_platform_change_rejects_an_empty_previous_platform()
    {
        var result = StatusChange.PublishPlatformChange(
            JourneyId, 2, Guid.CreateVersion7(), Guid.Empty, ControllerId, PublishedAt);

        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.PreviousPlatformInvalid, result.Error);
    }

    [Fact]
    public void A_correction_carries_only_its_target()
    {
        var target = Guid.CreateVersion7();

        var result = StatusChange.PublishCorrection(JourneyId, target, ControllerId, PublishedAt);

        Assert.True(result.IsSuccess);
        var change = result.Value;
        Assert.Equal(StatusChangeType.Correction, change.Type);
        Assert.Equal(target, change.CorrectsChangeId);
        Assert.Null(change.StopSequence);
        Assert.Null(change.DelayMinutes);
        Assert.Null(change.NewPlatformId);
        Assert.Null(change.PreviousPlatformId);
    }

    [Fact]
    public void A_correction_needs_the_change_it_withdraws()
    {
        var result = StatusChange.PublishCorrection(JourneyId, Guid.Empty, ControllerId, PublishedAt);

        Assert.True(result.IsFailure);
        Assert.Equal(StatusChangeErrors.CorrectionTargetRequired, result.Error);
    }

    [Fact]
    public void Every_type_needs_the_journey_the_publisher_and_the_publish_time()
    {
        Assert.Equal(
            StatusChangeErrors.MissingJourneyId,
            StatusChange.PublishCancellation(Guid.Empty, ControllerId, PublishedAt).Error);
        Assert.Equal(
            StatusChangeErrors.MissingPublisher,
            StatusChange.PublishCancellation(JourneyId, Guid.Empty, PublishedAt).Error);
        Assert.Equal(
            StatusChangeErrors.MissingPublishedAt,
            StatusChange.PublishCancellation(JourneyId, ControllerId, default).Error);
    }
}
