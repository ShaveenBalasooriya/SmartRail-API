using System.Text.Json;
using SmartRail.Distribution.Application.Public;
using SmartRail.Host.Http;
using SmartRail.LiveOperations.Contracts;

namespace SmartRail.Distribution.UnitTests.Public;

/// <summary>
/// The wire shape of the public view (api-conventions spec): camel-case property names, enums as
/// lower-case snake_case strings, UTC instants, and no staff data anywhere in the payload.
/// </summary>
public sealed class PublicJourneyViewJsonTests
{
    private static readonly JsonSerializerOptions Options = ApiJson.CreateOptions();

    [Fact]
    public void The_view_is_camel_case_with_lower_case_enum_strings()
    {
        var journey = PublicJourneyViewFixture.Journey(status: JourneyLifecycleStatus.Cancelled);

        var json = JsonSerializer.Serialize(
            PublicJourneyViewBuilder.Build(journey, PublicJourneyViewFixture.Lookup()),
            Options);

        Assert.Contains($"\"journeyId\":\"{journey.JourneyId}\"", json, StringComparison.Ordinal);
        Assert.Contains("\"serviceName\":\"Kandy Express\"", json, StringComparison.Ordinal);
        Assert.Contains("\"line\":{\"id\":", json, StringComparison.Ordinal);
        Assert.Contains("\"serviceDate\":\"2026-10-28\"", json, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"cancelled\"", json, StringComparison.Ordinal);
        Assert.Contains("\"provenance\":\"automatically_published\"", json, StringComparison.Ordinal);
        Assert.Contains("\"isCalling\":false", json, StringComparison.Ordinal);
        Assert.Contains("\"lastUpdatedAt\":\"2026-10-28T07:30:00Z\"", json, StringComparison.Ordinal);

        // The view builder leaves guidance to the guidance builder, so a null member is written as absent.
        Assert.DoesNotContain("guidance", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_staff_identifier_in_the_live_state_never_reaches_the_public_payload()
    {
        var journey = PublicJourneyViewFixture.Journey();

        var json = JsonSerializer.Serialize(
            PublicJourneyViewBuilder.Build(journey, PublicJourneyViewFixture.Lookup()),
            Options);

        Assert.DoesNotContain(
            PublicJourneyViewFixture.GuardStaffId.ToString(),
            json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            PublicJourneyViewFixture.GuardStaffId.ToString("N"),
            json,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("staff", json, StringComparison.OrdinalIgnoreCase);
    }
}
