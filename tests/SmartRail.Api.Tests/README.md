# SmartRail.Api.Tests — the black-box API tests

These tests see only HTTP. They reference no module project: requests are written as JSON and
responses are read into local records (`Infrastructure/ApiModels.cs`), so a change to a request or
response shape breaks the test exactly as it breaks a client.

They are step 4 of `docs/quality/test-suite-review.md`: the HTTP contract only — the status codes the
epics promise, the error format, and the access rule over every write route. Business rules stay in
the unit tests; module-to-module behaviour stays in the host integration tests.

## Run

```powershell
# In process: the real Host against its own PostgreSQL database (needs Docker)
dotnet test tests/SmartRail.Api.Tests

# One class, or one test
dotnet test tests/SmartRail.Api.Tests --filter "FullyQualifiedName~PublishDelayTests"
```

## Choosing the target

`SMARTRAIL_TEST_TARGET` picks where the same tests run. All of the logic is in
`Infrastructure/ApiTarget.cs`.

| Value | What happens | Needs |
|---|---|---|
| unset, empty, `inprocess` | The real Host starts in this process through `SmartRailApiFactory`, against a database of its own on the run's PostgreSQL container. The Host migrates and seeds it, so every test starts from the seeded railway with no other test's writes in it. | Docker |
| `remote` | The tests call a deployed API at `SMARTRAIL_API_URL` with the credentials below. | A disposable, freshly seeded environment |

Every test needs no seeded identifiers: journeys, stops, platforms and changes are read back through
the API (`Infrastructure/TestData.cs`), and a test asserts only on ids the API returned to it. A
remote environment does have to be disposable, because the writes are real.

In remote mode a test whose prerequisites are missing is **skipped** with the list of variables it
needs (`ApiFact` / `ApiTheory` in `Infrastructure/ApiTest.cs`); it never silently runs against the
wrong environment.

### Environment variables (remote only)

| Variable | What it is |
|---|---|
| `SMARTRAIL_API_URL` | Base URL of the deployed API, for example `https://staging.example` |
| `SMARTRAIL_CONTROLLER_TOKEN` | A Network Controller bearer token |
| `SMARTRAIL_GUARD_TOKEN` | A Guard bearer token |
| `SMARTRAIL_AUDITOR_TOKEN` | An Auditor bearer token |
| `SMARTRAIL_PROVIDER_KEY` | The catering provider's API key, sent as `X-Api-Key` |

## What is covered

| Class | Route | Cases |
|---|---|---|
| `Access/WriteRouteAccessTests` | all 8 write routes | no credentials → 401 and nothing written (8 rows); wrong role → 403 and nothing written (6 staff routes); Auditor token on the 5 publishing routes → 403 and nothing written (5 rows); staff token on a provider route → 401 and nothing written (2 rows); positive control: the same request with the right credential → the route's success code and a changed snapshot (8 rows) |
| `Publishing/PublishDelayTests` | `POST /operations/journeys/{journeyId}/delays` | happy path 201; 400 for minutes 0 and 721; 404 unknown journey; 409 delay from a departed stop |
| `Publishing/PublishCancellationTests` | `POST .../cancellation` | happy path 201; 404; 409 after a departure |
| `Publishing/PublishPlatformChangeTests` | `POST .../platform-changes` | happy path 201; 400 platform of another station; 404; 409 platform unchanged |
| `Publishing/PublishTerminationTests` | `POST .../termination` | happy path 201; 409 not started; 400 at the final stop; 404 |
| `Publishing/PublishCorrectionTests` | `POST /operations/changes/{changeId}/corrections` | happy path 201 with the original kept and marked corrected; 404; 409 corrected twice |
| `Departures/LogDepartureTests` | `POST /guard/departure-logs` | happy path 201; identical resend 200 and one log (exactly once); 400 missing UTC offset; 404; 409 second log for the same stop |
| `Provider/ProviderWebhookTests` | `PUT` and `DELETE /provider/webhook` | 201 first registration; 200 replacement; 400 invalid URL; 204 deactivation; 404 deactivation without a subscription |
| `Errors/ProblemDetailsTests` | one route per status family | 400, 401, 403, 404 and 409 are `application/problem+json` with the correlation ID and no stack trace |

Every 4xx in every test is checked the same way (`Infrastructure/ApiAssertions.cs`), so the error
format is a standing assertion rather than one test's opinion.

"Nothing was written" only means something if the refused request would have written with the right
credential, so `Infrastructure/TestData.IdsAsync` prepares a real write for every route: the platform
change names another platform of the same station, the termination names a stop after a departure it
has already logged, and the provider's webhook is registered before its routes run (so `PUT` is a
replacement and `DELETE` has something to remove). The positive-control row makes each of those
requests with the right credential and watches the snapshot change; the refusal rows and the control
read the same `WriteRoute` data, so they cannot drift apart.

## Test data and isolation

One PostgreSQL container per run (`Infrastructure/ApiDatabase.cs`), one **database per test**
(`ApiTarget.Start`): the Host migrates and seeds it, and the database is dropped afterwards. No test
reads "the newest entry" and no test can see another test's writes.

The day's journeys are generated after the Host starts, and each reaches the guard's list when its
live state exists, so the access setup polls (30 s deadline, 200 ms interval) until the list holds
the three untouched journeys and the changeable interchange stop it needs, instead of treating the
first non-empty answer as the whole timetable.
