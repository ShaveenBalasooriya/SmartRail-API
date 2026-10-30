namespace SmartRail.Host.IntegrationTests.Flows.Support;

/// <summary>
/// The seeded network a flow builds its journeys from (<c>seed/network/network.json</c>): the Coastal
/// Line, three of its stations in order, and two platforms of Colombo Fort, because a platform change
/// has to move a calling stop to another platform of the same station.
/// </summary>
internal static class SeededNetwork
{
    /// <summary>The Coastal Line, which serves the stations below.</summary>
    public static Guid CoastalLine { get; } = Guid.Parse("f8493f1c-67b5-76e1-acb6-215d4d9f60ae");

    /// <summary>
    /// The seeded "Coastal 1210 Outbound" service, whose first stop is Colombo Fort platform 2: the
    /// timetable's own platform, which is what a display's move notice reads to know where a journey
    /// came from.
    /// </summary>
    public static Guid Coastal1210 { get; } = Guid.Parse("3448e508-b0cc-71d2-a7ce-1be9b30683c0");

    /// <summary>Colombo Fort, the first stop of a flow's journey.</summary>
    public static Guid ColomboFort { get; } = Guid.Parse("04624d0f-9410-72e3-967f-501ed71f2441");

    /// <summary>Kollupitiya, the middle stop of a flow's journey.</summary>
    public static Guid Kollupitiya { get; } = Guid.Parse("dc1ed19b-f522-70a9-ae61-15e47102a42f");

    /// <summary>Bambalapitiya, the third calling stop of a four-stop flow journey.</summary>
    public static Guid Bambalapitiya { get; } = Guid.Parse("6f818201-ebf9-7374-bbad-74adbab9df38");

    /// <summary>Mount Lavinia, the final stop of a flow's journey.</summary>
    public static Guid MountLavinia { get; } = Guid.Parse("c078a9aa-2f80-7422-a65b-bb7313795bd5");

    /// <summary>Colombo Fort platform 2, the platform a flow's first stop starts on.</summary>
    public static Guid ColomboFortPlatformTwo { get; } = Guid.Parse("61e67fba-522e-73bf-beab-5cb90e861c86");

    /// <summary>Colombo Fort platform 3, the platform a platform change moves the stop to.</summary>
    public static Guid ColomboFortPlatformThree { get; } = Guid.Parse("573394b2-45be-7539-9a43-fbc6448c7ad7");

    /// <summary>Kollupitiya's platform.</summary>
    public static Guid KollupitiyaPlatform { get; } = Guid.Parse("e86062e1-aa04-7673-b2dc-51892f077a58");

    /// <summary>Bambalapitiya's platform.</summary>
    public static Guid BambalapitiyaPlatform { get; } = Guid.Parse("8f4b8be6-fea4-7c57-bd1d-64638e6273af");

    /// <summary>Mount Lavinia's platform.</summary>
    public static Guid MountLaviniaPlatform { get; } = Guid.Parse("579f8b1e-e3fe-789b-9ba3-20aae4342df3");
}

/// <summary>
/// The railway calendar a flow runs on. Every journey is dated with today in the railway's zone, and
/// the Host's clock is frozen at noon of that day, which is safely inside the day: the seeded timetable
/// lines up, a departure is neither ahead of the server nor before the service date, and the frozen
/// instant is what a refusal's recorded time has to be, so the audit assertion is exact.
/// </summary>
internal static class RailwayDay
{
    private static readonly TimeZoneInfo RailwayZone =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo");

    /// <summary>Today in the railway's time zone.</summary>
    public static DateOnly Today() => DateOnly.FromDateTime(
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, RailwayZone).DateTime);

    /// <summary>Noon in the railway's time zone on <paramref name="serviceDate"/>, as UTC.</summary>
    public static DateTimeOffset Noon(DateOnly serviceDate) => new DateTimeOffset(
        serviceDate.ToDateTime(new TimeOnly(12, 0)),
        TimeSpan.FromHours(5.5)).ToUniversalTime();
}
