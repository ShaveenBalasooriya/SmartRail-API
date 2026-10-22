using System.Text.Json.Nodes;
using SmartRail.RailNetwork.Domain.Entities;
using SmartRail.RailNetwork.Infrastructure.Seeding;

namespace SmartRail.RailNetwork.UnitTests.Seeding;

/// <summary>
/// The validation and dataset rules of the network and timetable seed (design D6, D9). Broken inputs
/// are copies of the checked-in files, never the files themselves.
/// </summary>
public sealed class RailNetworkSeedReaderTests
{
    private static readonly string SeedRoot = Path.Combine(AppContext.BaseDirectory, "seed");

    [Fact]
    public void The_checked_in_seed_files_are_valid()
    {
        var seed = RailNetworkSeedReader.Read(SeedRoot);

        Assert.Equal(9, seed.Stations.Count);
        Assert.Equal(11, seed.Stations.Sum(station => station.Platforms.Count));
        Assert.Equal(2, seed.Lines.Count);
        Assert.Equal(52, seed.Services.Count);
        Assert.Equal(260, seed.Services.Sum(service => service.Stops.Count));
    }

    [Fact]
    public void An_unknown_station_reference_names_the_file_and_record()
    {
        using var copy = new SeedCopy();
        var timetable = ReadJson(copy.TimetablePath);
        var service = FindService(timetable, "Coastal 0010 Outbound");

        service["stops"]!.AsArray()[2]!["stationId"] = Guid.CreateVersion7().ToString();
        WriteJson(copy.TimetablePath, timetable);

        var exception = Assert.Throws<InvalidOperationException>(() => RailNetworkSeedReader.Read(copy.Root));

        Assert.Contains("timetable.json", exception.Message);
        Assert.Contains("Coastal 0010 Outbound", exception.Message);
        Assert.Contains("stop 3", exception.Message);
    }

    [Fact]
    public void Times_that_go_backwards_are_rejected()
    {
        using var copy = new SeedCopy();
        var timetable = ReadJson(copy.TimetablePath);
        var service = FindService(timetable, "Coastal 0010 Outbound");

        // Stop 3 departs at 00:24, so stop 4 arriving at 00:20 would cross midnight.
        service["stops"]!.AsArray()[3]!["arrival"] = "00:20";
        WriteJson(copy.TimetablePath, timetable);

        var exception = Assert.Throws<InvalidOperationException>(() => RailNetworkSeedReader.Read(copy.Root));

        Assert.Contains("timetable.json", exception.Message);
        Assert.Contains("Coastal 0010 Outbound", exception.Message);
        Assert.Contains("00:20", exception.Message);
    }

    [Fact]
    public void A_duplicate_station_code_is_rejected()
    {
        using var copy = new SeedCopy();
        var network = ReadJson(copy.NetworkPath);

        network["stations"]!.AsArray()[1]!["code"] = "CMB";
        WriteJson(copy.NetworkPath, network);

        var exception = Assert.Throws<InvalidOperationException>(() => RailNetworkSeedReader.Read(copy.Root));

        Assert.Contains("network.json", exception.Message);
        Assert.Contains("record 2", exception.Message);
        Assert.Contains("station code 'CMB' is used more than once", exception.Message);
    }

    [Fact]
    public void The_dataset_is_two_lines_of_five_with_one_interchange()
    {
        var seed = RailNetworkSeedReader.Read(SeedRoot);

        Assert.Equal(2, seed.Lines.Count);
        Assert.All(seed.Lines, line => Assert.Equal(5, line.StationIds.Count));
        Assert.Equal(9, seed.Stations.Select(station => station.Id).Distinct().Count());

        var interchange = seed.Stations
            .Where(station => seed.Lines.Count(line => line.StationIds.Contains(station.Id)) > 1)
            .ToList();

        Assert.Single(interchange);
        Assert.Equal(2, seed.Lines.Count(line => line.StationIds.Contains(interchange[0].Id)));
    }

    [Fact]
    public void Every_seeded_service_has_the_lines_stations_in_direction_order()
    {
        var seed = RailNetworkSeedReader.Read(SeedRoot);

        foreach (var service in seed.Services)
        {
            var line = seed.LineById[service.LineId];
            var expected = service.Direction == ServiceDirection.Outbound
                ? line.StationIds
                : line.StationIds.Reverse().ToList();

            Assert.Equal(expected.ToList(), service.Stops.Select(stop => stop.StationId).ToList());
            Assert.All(service.Stops, stop => Assert.True(stop.IsCalling));
        }
    }

    [Fact]
    public void At_any_local_time_a_service_is_running_or_departs_within_an_hour()
    {
        var seed = RailNetworkSeedReader.Read(SeedRoot);

        var windows = seed.Services
            .Select(service => (
                FirstDeparture: Minutes(service.Stops[0].ScheduledDeparture!.Value),
                LastArrival: Minutes(service.Stops[^1].ScheduledArrival!.Value)))
            .ToList();

        for (var minute = 0; minute < 24 * 60; minute++)
        {
            var covered = windows.Any(window =>
                (window.FirstDeparture <= minute && minute <= window.LastArrival)
                || (window.FirstDeparture - minute + 24 * 60) % (24 * 60) <= 60);

            Assert.True(
                covered,
                $"No service runs, or departs within 60 minutes, at {minute / 60:D2}:{minute % 60:D2}.");
        }
    }

    private static int Minutes(TimeOnly time) => (int)time.ToTimeSpan().TotalMinutes;

    private static JsonObject ReadJson(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();

    private static void WriteJson(string path, JsonObject json) => File.WriteAllText(path, json.ToJsonString());

    private static JsonObject FindService(JsonObject timetable, string name) =>
        timetable["services"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(service => service["name"]!.GetValue<string>() == name);

    /// <summary>A temporary seed root holding a copy of both checked-in files.</summary>
    private sealed class SeedCopy : IDisposable
    {
        public SeedCopy()
        {
            Root = Path.Combine(Path.GetTempPath(), $"smartrail-seed-{Guid.NewGuid():N}");
            var networkDirectory = Path.Combine(Root, "network");
            Directory.CreateDirectory(networkDirectory);

            foreach (var file in new[] { "network.json", "timetable.json" })
            {
                File.Copy(
                    Path.Combine(SeedRoot, "network", file),
                    Path.Combine(networkDirectory, file));
            }
        }

        public string Root { get; }

        public string NetworkPath => Path.Combine(Root, "network", "network.json");

        public string TimetablePath => Path.Combine(Root, "network", "timetable.json");

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
