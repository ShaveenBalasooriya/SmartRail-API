using System.Net;
using SmartRail.Api.Tests.Infrastructure;

namespace SmartRail.Api.Tests.Access;

/// <summary>
/// EP-02 "Done when": every write endpoint rejects a caller that may not make that write, and the
/// refusal changes nothing. One theory row per write route — the five publishing routes, the guard's
/// departure log, and the provider's webhook registration and deactivation — called with no
/// credentials, then with a credential for the wrong role, then with the Auditor's read-only token
/// on the publishing routes.
/// </summary>
/// <remarks>
/// <para>
/// The two provider routes are a separate theory because a provider route has no role to get wrong:
/// only an API key is accepted there, so a staff token is refused as unauthenticated (401) rather
/// than forbidden (403). That is the API's documented rule (identity design D7), not a defect.
/// </para>
/// <para>
/// A refused write is audited on purpose (EP-02), so "writes nothing" means the resource the route
/// targets is untouched: the journey, its changes, or the provider's subscription, read back through
/// the API. The denial record itself is the Audit module's and Flow 4's test.
/// </para>
/// <para>
/// "Writes nothing" is only evidence if the same request with the right credential would write, so
/// <see cref="A_write_route_with_the_right_credential_is_accepted_and_writes"/> is the control: the
/// same <see cref="WriteRoute"/>, the same <see cref="ApiIds"/> and the same body, accepted, with the
/// snapshot changing. The access setup therefore hands every row a valid write, not merely a
/// well-formed request, and a route whose request stopped being a real write fails there first.
/// </para>
/// </remarks>
[Collection(ApiDatabase.Name)]
public sealed class WriteRouteAccessTests(ApiDatabaseFixture database) : ApiTest(database)
{
    /// <summary>All eight write routes, which every refusal and the control are exercised over.</summary>
    public static TheoryData<string> AllRoutes { get; } =
        WriteRoute.Names(WriteAudience.NetworkController, WriteAudience.Guard, WriteAudience.Provider);

    /// <summary>The six staff write routes, called with a token for a role that may not use them.</summary>
    public static TheoryData<string> StaffRoutesWithTheWrongRole { get; } =
        WriteRoute.Names(WriteAudience.NetworkController, WriteAudience.Guard);

    /// <summary>The five publishing write routes, called with the Auditor's read-only token.</summary>
    public static TheoryData<string> PublishingRoutesWithAnAuditorToken { get; } =
        WriteRoute.Names(WriteAudience.NetworkController);

    /// <summary>The two provider write routes, called with a staff token instead of an API key.</summary>
    public static TheoryData<string> ProviderRoutesWithAStaffToken { get; } =
        WriteRoute.Names(WriteAudience.Provider);

    [ApiTheory]
    [MemberData(nameof(AllRoutes))]
    public async Task A_write_route_with_no_credentials_is_unauthorized_and_writes_nothing(string routeName)
    {
        // Arrange
        var route = WriteRoute.ByName(routeName);
        var ids = await Api.IdsAsync();
        var before = await Api.SnapshotAsync(route, ids);

        // Act
        using var response = await Api.SendAsync(route, ids, Credential.None);

        // Assert
        await response.IsProblemAsync(HttpStatusCode.Unauthorized);
        await WriteRoute.AssertUnchangedAsync(Api, route, ids, before);
    }

    [ApiTheory]
    [MemberData(nameof(StaffRoutesWithTheWrongRole))]
    public async Task A_staff_write_route_with_the_wrong_role_is_forbidden_and_writes_nothing(string routeName)
    {
        // Arrange
        var route = WriteRoute.ByName(routeName);
        var ids = await Api.IdsAsync();
        var before = await Api.SnapshotAsync(route, ids);

        // Act
        using var response = await Api.SendAsync(route, ids, route.WrongCredential(Api.Credentials));

        // Assert
        await response.IsProblemAsync(HttpStatusCode.Forbidden);
        await WriteRoute.AssertUnchangedAsync(Api, route, ids, before);
    }

    [ApiTheory]
    [MemberData(nameof(PublishingRoutesWithAnAuditorToken))]
    public async Task A_publishing_route_with_an_auditor_token_is_forbidden_and_writes_nothing(string routeName)
    {
        // Arrange
        var route = WriteRoute.ByName(routeName);
        var ids = await Api.IdsAsync();
        var before = await Api.SnapshotAsync(route, ids);

        // Act
        using var response = await Api.SendAsync(route, ids, Api.Auditor);

        // Assert
        await response.IsProblemAsync(HttpStatusCode.Forbidden);
        await WriteRoute.AssertUnchangedAsync(Api, route, ids, before);
    }

    [ApiTheory]
    [MemberData(nameof(ProviderRoutesWithAStaffToken))]
    public async Task A_provider_write_route_with_a_staff_token_is_unauthenticated_and_writes_nothing(string routeName)
    {
        // Arrange
        var route = WriteRoute.ByName(routeName);
        var ids = await Api.IdsAsync();
        var before = await Api.SnapshotAsync(route, ids);

        // Act
        using var response = await Api.SendAsync(route, ids, route.WrongCredential(Api.Credentials));

        // Assert
        await response.IsProblemAsync(HttpStatusCode.Unauthorized);
        await WriteRoute.AssertUnchangedAsync(Api, route, ids, before);
    }

    [ApiTheory]
    [MemberData(nameof(AllRoutes))]
    public async Task A_write_route_with_the_right_credential_is_accepted_and_writes(string routeName)
    {
        // Arrange
        var route = WriteRoute.ByName(routeName);
        var ids = await Api.IdsAsync();
        var before = await Api.SnapshotAsync(route, ids);

        // Act
        using var response = await Api.SendAsync(route, ids, route.RightCredential(Api.Credentials));

        // Assert
        await response.HasStatusAsync(route.Success);
        await WriteRoute.AssertChangedAsync(Api, route, ids, before);
    }
}
