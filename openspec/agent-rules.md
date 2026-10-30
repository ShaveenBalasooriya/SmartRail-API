# Rules for every coding agent working on SmartRail-API

Read this file first, then your brief. The brief names your package; this file is how every package is done.

## Context you cannot see

- The owner asked the orchestrator (Claude Sonnet 5.5) to complete the remaining OpenSpec changes up to, but not including, `add-critical-e2e-flows`, by handing the coding to small OpenCode work packages and verifying each one. You are one package. The owner's wording: "think go through them, verify them and hand off the coding work to OpenCode subagents". Earlier owner instruction: "plan out the work and deploy a few [subagents] so there is less likely for them to hallucinate".
- You run on `opencode-go/deepseek-v4.1-flash`, variant high. Decisions in your brief marked **owner** came from the owner; marked **orchestrator** were made by the orchestrator and are awaiting owner confirmation. Do not make new design decisions: if something is unclear, stop and report it.
- Repo: `C:\Users\ShaveenBalasooriya\repos\SmartRail`; backend in `SmartRail-API`; branch `test`; nothing is committed (do not commit, push or change branches).

## Hard rules

1. Read the change's `proposal.md`, `design.md` and `specs/**/spec.md` before changing code, and the neighbouring code you will extend. Do not invent requirements and do not edit specs; report gaps instead.
2. Match the surrounding code exactly: file-scoped namespaces, XML doc comments where neighbours have them, `ArgumentNullException.ThrowIfNull`, `Result`/`ToProblemDetails`, Carter modules for endpoints (copy `RailNetwork` and `IdentityAccess` patterns), MediatR commands/queries in each module's Application layer, ports in Application with implementations in Infrastructure.
3. Dependency rules are enforced by `tests/SmartRail.ArchitectureTests`: Domain depends only on SharedKernel; Application has no EF Core/ASP.NET Core; Api has no Infrastructure/EF Core; no module Domain/Application/Contracts references `SmartRail.SharedKernel.Infrastructure`; a module never references another module's layers (only `Contracts`).
4. No new NuGet packages unless your brief says so (versions are central in `Directory.Packages.props`).
5. Migrations: `dotnet ef migrations add <Name> --project <module Infrastructure project>` (dotnet-ef 10 is installed; each module has a design-time factory). Never hand-edit a Designer or snapshot file.
6. Infrastructure available: event bus (`IIntegrationEventPublisher` keyed publishers, `IEventConsumer<T>`, `AddEventConsumer`, `AddOutboxSource`, `.AddOutboxSignal`), shared request transaction (`IRequestTransaction`, enlisted by every module context; the Host middleware commits state-changing requests), `IAuditRecorder` once its package is done. Test host: `WebApplicationFactory<Program>` in `E2E` env, `TestTokens`, `TestEndpoints`, Testcontainers Postgres. Docker Desktop must be running for integration tests.
7. Seeded staff for manual checks: `controller.mahesh` (NetworkController), `guard.ishara` (Guard), `auditor.wasantha` (Auditor); see `seed/identity/staff.json` and the existing integration tests for how tokens are produced.
8. Verify with real output before you finish: `dotnet build SmartRail.sln -warnaserror` clean, plus every test your brief names. Report the exact commands and their real results. Never claim a command passed that you did not run. If a test fails for a reason outside your package, say so; do not weaken or delete tests to pass.
9. Do not tick `openspec/changes/*/tasks.md`; the orchestrator does that after review.
10. Do not leave test data in the shared database, stray files, TODO stubs, or commented-out code.
11. **AI interaction log (mandatory):** before finishing, append exactly one row to `docs/ai-usage/AI-INTERACTION-LOG.md` at the repository root (a Markdown table; follow the `ai-usage-log` skill at `.opencode/skills/ai-usage-log/SKILL.md`; use the next number after the highest row; never edit earlier rows). Record the request, the model, what you built, how you validated it, what you rejected or got wrong. Do not fabricate.
12. Final report format: files created/changed (grouped), commands run with real pass/fail counts, anything skipped or uncertain, spec gaps found. Keep it short and factual.
