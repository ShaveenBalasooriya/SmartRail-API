using SmartRail.LiveOperations.Application.Changes;
using SmartRail.LiveOperations.Domain.Changes;
using SmartRail.LiveOperations.UnitTests.TestDoubles;

namespace SmartRail.LiveOperations.UnitTests.Changes;

public sealed class StatusChangeMappingTests
{
    [Fact]
    public void Happy_path_maps_a_stored_change_to_the_controller_view()
    {
        // Arrange
        var change = TestData.Delay(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            stopSequence: 2,
            minutes: 25);

        // Act
        var view = StatusChangeMapping.ToView(change, corrected: true);

        // Assert
        Assert.Equal(change.Id, view.Id);
        Assert.Equal("delay", view.Type);
        Assert.Equal("2", view.Values["fromStopSequence"]);
        Assert.Equal("25", view.Values["minutes"]);
        Assert.Equal(change.PublishedBy, view.PublisherStaffId);
        Assert.Equal(change.PublishedAt, view.PublishedAt);
        Assert.Equal("automatically_published", view.Provenance);
        Assert.True(view.Corrected);
    }

    [Fact]
    public void Every_change_type_has_the_channel_text_the_clients_expect()
    {
        // Arrange
        var expected = new Dictionary<StatusChangeType, string>
        {
            [StatusChangeType.Delay] = "delay",
            [StatusChangeType.Cancellation] = "cancellation",
            [StatusChangeType.Termination] = "termination",
            [StatusChangeType.PlatformChange] = "platform_change",
            [StatusChangeType.Correction] = "correction",
        };

        // Act
        var actual = expected.Keys.ToDictionary(type => type, StatusChangeMapping.TypeText);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void A_cancellation_has_no_change_values()
    {
        // Arrange
        var type = StatusChangeType.Cancellation;

        // Act
        var values = StatusChangeMapping.ValuesOf(type, null, null, null, null, null);

        // Assert
        Assert.Empty(values);
    }

    [Fact]
    public void A_platform_change_includes_its_stop_and_both_platform_identifiers()
    {
        // Arrange
        var previousPlatformId = Guid.CreateVersion7();
        var newPlatformId = Guid.CreateVersion7();

        // Act
        var values = StatusChangeMapping.ValuesOf(
            StatusChangeType.PlatformChange,
            3,
            null,
            newPlatformId,
            previousPlatformId,
            null);

        // Assert
        Assert.Equal("3", values["stopSequence"]);
        Assert.Equal(previousPlatformId.ToString(), values["previousPlatformId"]);
        Assert.Equal(newPlatformId.ToString(), values["platformId"]);
    }

    [Fact]
    public void A_termination_includes_the_last_stop_served()
    {
        // Arrange
        const int lastStopSequence = 4;

        // Act
        var values = StatusChangeMapping.ValuesOf(
            StatusChangeType.Termination,
            lastStopSequence,
            null,
            null,
            null,
            null);

        // Assert
        Assert.Equal("4", Assert.Single(values).Value);
        Assert.Equal("lastStopSequence", values.Keys.Single());
    }

    [Fact]
    public void A_correction_identifies_the_change_it_withdraws()
    {
        // Arrange
        var originalChangeId = Guid.CreateVersion7();

        // Act
        var values = StatusChangeMapping.ValuesOf(
            StatusChangeType.Correction,
            null,
            null,
            null,
            null,
            originalChangeId);

        // Assert
        Assert.Equal(originalChangeId.ToString(), values["correctsChangeId"]);
        Assert.Single(values);
    }

    [Fact]
    public void An_unknown_change_type_is_rejected_instead_of_being_mapped_to_misleading_text()
    {
        // Arrange
        const StatusChangeType unknown = (StatusChangeType)999;

        // Act
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => StatusChangeMapping.TypeText(unknown));

        // Assert
        Assert.Equal("type", exception.ParamName);
    }

    [Fact]
    public void An_unknown_provenance_is_rejected_instead_of_being_mapped_to_misleading_text()
    {
        // Arrange
        const ChangeProvenance unknown = (ChangeProvenance)999;

        // Act
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => StatusChangeMapping.ProvenanceText(unknown));

        // Assert
        Assert.Equal("provenance", exception.ParamName);
    }

    [Fact]
    public void A_null_change_cannot_be_mapped_to_a_view()
    {
        // Arrange
        StatusChange change = null!;

        // Act
        var exception = Assert.Throws<ArgumentNullException>(() => StatusChangeMapping.ToView(change, false));

        // Assert
        Assert.Equal("change", exception.ParamName);
    }
}
