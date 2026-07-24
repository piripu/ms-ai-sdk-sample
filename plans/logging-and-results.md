# Logging + Result pattern across the Ovi SDK

Add structured logging (Microsoft.Extensions.Logging.Abstractions) throughout the SDK, and switch
the public *execution* APIs (nodes, triggers, agents, scripts, definition loading) to a Result
pattern with typed error objects — a naive in-house `Result<T>` designed for painless migration to
C# discriminated unions later.

## Decisions (user deferred; recorded here as the source of truth)

- **D1 — Nodes dependency rule amended**: `Ovi.Sdk.Nodes` may reference `Microsoft.Extensions.*.Abstractions`
  packages only (Logging.Abstractions 10.0.10 via CPM). AGENT.md rule #2 and docs updated to match.
- **D2 — Logging depth**: boundaries + meaningful events, quiet by default (Debug/Trace), using
  source-generated `[LoggerMessage]`. Loggers resolve via `context.GetLogger<T>()` →
  `ILoggerFactory` in `RuntimeServices` → `NullLogger` fallback. Execution tracing is a
  `LoggingNode` decorator (composition doctrine), not base-class noise.
- **D3 — Result implementation is in-house, not CSharpFunctionalExtensions**: a third-party type in
  every public contract would couple all SDK consumers to that package's versioning and break the
  Nodes dependency rule. The naive `Result<T>` (sealed, two states, `Match`/`Map`/`Bind`, implicit
  conversions) mirrors CSharpFunctionalExtensions ergonomics and maps 1:1 onto a future
  discriminated union. CSharpFunctionalExtensions remains usable by consumers on top.
- **D4 — Result scope = expected runtime failures**: `ExecuteAsync`/`FireAsync`,
  `AgentNode.FromDefinition`, `ChatTriggerNode.FromWebhook`, `AgentDefinitionSerializer.FromJson/FromYaml/Load`.
  Constructor/argument validation stays exceptions (programmer errors), `NodeId.TryParse` stays,
  `MultipartHttpChatClient` keeps throwing (`IChatClient` is an external contract we cannot change),
  Packaging APIs keep exceptions for now (noted as possible follow-up).
- **D5 — Error objects are a closed set** (DU-ready): abstract `Error` + `ValidationError`,
  `ResolutionError`, `ExecutionError` records with stable string codes.

## For Future Agents
As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to
`Complete` and write its **Phase Summary** (what was done, key decisions, anything needed to continue
with zero context); run the phase's **Verification Plan** and record the result before moving on.
When all phases are done, fill in **Final Recap** and **Deployment Plan**.
Branch: `claude/csharp-sdk-agents-tools-bopdpg` (restarted from `main` @ d92abc0). If the PR for
this branch gets merged mid-work, restart the branch from `origin/main` and rebase unmerged commits.

## Phase 1: Logging plumbing + dependency policy
Status: Complete

- [x] Add `Microsoft.Extensions.Logging.Abstractions` 10.0.10 to `Directory.Packages.props`.
- [x] Add the package reference to all six `src/*` csproj files.
- [x] Add `GetLogger<T>()` and `GetLogger(string category)` to `WorkflowExecutionContext`
      (resolve `ILoggerFactory` from `RuntimeServices`, fall back to `NullLogger`).
- [x] Amend AGENT.md rule #2 and `docs/architecture.md` dependency policy to "abstractions-only
      references allowed in Ovi.Sdk.Nodes".

### Verification Plan
- `dotnet build` — clean, 0 warnings. ✅ ran: Build succeeded, 0 warnings.
- `grep -l Logging.Abstractions src/*/*.csproj | wc -l` → 6. ✅

### Phase Summary
Logging.Abstractions 10.0.10 pinned via CPM and referenced by all six src projects.
`WorkflowExecutionContext.GetLogger<T>()`/`GetLogger(category)` resolve `ILoggerFactory` from
RuntimeServices with a NullLogger fallback, so logging is always available and silent by default.
AGENT.md rule #2 and docs/architecture.md now state the refined policy: Ovi.Sdk.Nodes may reference
Microsoft.Extensions.*.Abstractions packages only.

## Phase 2: Result core in Ovi.Sdk.Nodes
Status: Complete

- [x] `Error` family in `src/Ovi.Sdk.Nodes/Errors.cs`: abstract record `Error(string Code, string Message)`
      with sealed `ValidationError`, `ResolutionError` (What/Hint), `ExecutionError` (optional
      `Exception`), each with factory helpers; xmldoc notes the DU migration path.
- [x] `Result<T>` in `src/Ovi.Sdk.Nodes/Result.cs`: sealed class, `IsSuccess`/`IsFailure`,
      `Value` (throws on failure)/`Error` (throws on success), static `Success`/`Failure`,
      implicit conversions from `T` and from `Error`, `Match`, `Map`, `Bind`, `GetValueOrDefault`,
      `ToString`; xmldoc notes DU migration.
- [x] Switch the node contract: `INode.ExecuteAsync` → `ValueTask<Result<object?>>`,
      `INode<TIn,TOut>.ExecuteAsync` → `ValueTask<Result<TOutput>>`; `Node<,>` untyped bridge maps
      input-type mismatch to `ValidationError` and catches unhandled exceptions into
      `ExecutionError` (logged via `context.GetLogger`); `DelegateNode`/`Node.Create` overloads
      accept both `Result<TOutput>`-returning and plain-`TOutput` delegates.
- [x] `LoggingNode<TIn,TOut>` decorator + `WithLogging` extension: Debug start, Debug success with
      elapsed ms, Warning on failure result with error code, Error on thrown exception; uses
      `[LoggerMessage]` source generators.

### Verification Plan
- `dotnet build` on `src/Ovi.Sdk.Nodes` — clean. ✅ Build succeeded, 0 warnings.

### Phase Summary
`Error` (abstract, Code+Message) with sealed ValidationError/ResolutionError(Hint)/ExecutionError(Exception)
in Errors.cs; naive DU-ready `Result<T>` + non-generic `Result` factories in Result.cs (implicit
conversions from T and Error, Match/Map/Bind/TryGetValue). Node contract switched: INode →
`ValueTask<Result<object?>>`, INode<TIn,TOut> → `ValueTask<Result<TOutput>>`; the Node<,> untyped
bridge converts input-type mismatches to ValidationError and unhandled exceptions to ExecutionError
(cancellation rethrows; logged via NodeLog). DelegateNode/Node.Create keep two overloads
(async-result and sync-plain). LoggingNode decorator + WithLogging extension log Debug
start/success(+ms), Warning failure results, Error thrown exceptions under category
"Ovi.Sdk.Nodes.LoggingNode" via [LoggerMessage] source generators (NodeLog).

## Phase 3: Apply Result + logging to components
Status: Complete

- [x] `AgentNode.ExecuteAsync` → `Result<AgentResponse>`: empty request → `ValidationError`;
      missing chat client → `ResolutionError`; chat-client exception → `ExecutionError`; Debug logs
      for client-resolution source, message/tool counts, response length.
- [x] `AgentNode.FromDefinition` → `Result<AgentNode>` (unresolvable tool → `ResolutionError`).
- [x] `PythonScriptNode.ExecuteAsync` → `Result<JsonNode?>`: missing engine → `ResolutionError`;
      engine exception → `ExecutionError`; Debug logs for engine type + entry point.
- [x] Trigger nodes: `ExecuteAsync` returns `Result` success passthroughs; `TriggerNode.FireAsync`
      → `ValueTask<Result<TOutput>>`; Debug log per fire; `ChatTriggerNode.FromWebhook` →
      `Result<ChatTriggerPayload>` (`ValidationError` instead of `FormatException`).
- [x] `AgentDefinitionSerializer.FromJson/FromYaml/Load` → `Result<AgentDefinition>`
      (`ValidationError` with parse detail; unsupported extension → `ValidationError`); Debug log per load.
- [x] `MultipartHttpChatClient`: keep throwing (external `IChatClient` contract) but add optional
      `ILogger` constructor parameter + Debug logs (request built: endpoint/message count; response
      status) via `[LoggerMessage]`.

### Verification Plan
- `dotnet build` — src projects clean, 0 warnings ✅ (tests intentionally red until Phase 4).

### Phase Summary
AgentNode: resolution/validation/execution failures are typed errors with hints; AgentLog records
client-resolution source, send counts, response size. FromDefinition returns Result<AgentNode>.
PythonScriptNode: ResolutionError/ExecutionError + ScriptLog (engine type, entry point). Triggers:
Result passthroughs, FireAsync logs under "Ovi.Sdk.Triggers.TriggerNode", ChatTriggerNode.FromWebhook
returns Result with defensive JSON handling. AgentDefinitionSerializer: Result-returning loads with
optional ILogger parameter (ValidationError for malformed/empty/unsupported, ExecutionError for IO);
writes still throw. MultipartHttpChatClient keeps IChatClient exception semantics but gains optional
ILogger + request/response Debug logs (ChatClientLog).

## Phase 4: Tests
Status: Complete

- [x] Add `Support/CapturingLogger.cs` (ILoggerFactory/ILogger test double recording entries).
- [x] New `ResultTests.cs`: success/failure semantics, implicit conversions, `Match`/`Map`/`Bind`,
      `Value`/`Error` guard exceptions, error records' codes.
- [x] New `LoggingTests.cs`: `GetLogger` falls back to `NullLogger` without a factory and resolves a
      registered factory; `WithLogging` decorator emits start/success with duration, warning on
      failure result, error on exception.
- [x] Update existing tests to Result assertions (`Assert.True(result.IsSuccess)` /
      `result.Error is ResolutionError` replace `Assert.ThrowsAsync` on execution paths) across
      NodeAtomicity/AgentNode/PythonScriptNode/Trigger/AgentDefinition/Schema tests.
- [x] Full suite green.

### Verification Plan
- `dotnet test` — 0 failed; total ≥ 100. ✅ ran: 111 passed, 0 failed.

### Phase Summary
CapturingLoggerFactory test double records (category, level, eventId, message, exception).
ResultTests (8) cover success/failure semantics, guards, implicit conversions, Match/Map/Bind,
TryGetValue/GetValueOrDefault, and error-record codes/details. LoggingTests (6) cover the NullLogger
fallback, factory resolution, the WithLogging decorator (success+duration, Warning on failure
result, Error+rethrow on exception), and the untyped bridge converting unhandled throws to
ExecutionError with a log. All existing suites migrated to Result assertions; typeless throw-lambdas
use the DelegateNode constructor to avoid Node.Create overload ambiguity. 111/111 green.

## Phase 5: Docs + finalize
Status: Complete

- [x] README: error-handling paragraph (Result on execution paths, exceptions for programmer
      errors), logging paragraph (GetLogger, WithLogging), update code snippets to Result usage.
- [x] `docs/architecture.md`: new "Errors and results" + "Logging" sections; amended dependency policy.
- [x] `docs/authoring-nodes.md`: recipes updated (returning results, error taxonomy, WithLogging).
- [x] AGENT.md: add Result idiom + logging idiom to the rules; amend rule #2.
- [x] Fill **Final Recap** + **Deployment Plan** here; commit + push branch; PR on user request.

### Verification Plan
- `dotnet test` green ✅ (111/111); README/docs/AGENT.md carry the Result + logging guidance ✅.

### Phase Summary
README gained an "Errors, results, and logging" section and Result-aware snippets;
docs/architecture.md gained "Errors and results" + "Logging" sections, updated resolution idiom, and
principles 7–8; docs/authoring-nodes.md gained Result-aware recipes, a "Returning errors" section,
the WithLogging note, and an extended testing checklist; AGENT.md gained rules 10–11 and the updated
resolution idiom.

## Final Recap
Logging and a Result pattern now span the SDK. Logging: Microsoft.Extensions.Logging.Abstractions
10.0.10 in all six projects (the one permitted Nodes reference), `context.GetLogger<T>()` with
NullLogger fallback, source-generated [LoggerMessage] events in agents/scripts/triggers/serializer/
multipart client, and execution tracing via the `WithLogging` decorator. Results: in-house DU-ready
`Result<T>` + closed `Error` set (ValidationError/ResolutionError/ExecutionError) on every execution
API — node/trigger ExecuteAsync + FireAsync, AgentNode.FromDefinition, ChatTriggerNode.FromWebhook,
AgentDefinitionSerializer loads — with the untyped INode bridge converting mismatches and unhandled
exceptions into failures. Exceptions remain for programmer errors, cancellation, and the external
IChatClient contract. CSharpFunctionalExtensions was evaluated and deliberately not taken as a
dependency (decision D3). Tests: 111/111. Docs and AGENT.md rules updated in lockstep.

## Deployment Plan
1. `dotnet test` on the branch — expect 111/111 green.
2. Push `claude/csharp-sdk-agents-tools-bopdpg` (done) and open a PR into `main`; merge via GitHub.
3. Consumers of pre-Result APIs must migrate call sites: `await node.ExecuteAsync(...)` now returns
   `Result<T>` (use `.Value`, `Match`, or `IsFailure`); `FromDefinition`/`FromWebhook`/serializer
   loads likewise. No data-format or schema changes; `.ovipkg` and agent definitions are untouched.
4. Optional follow-ups noted in decisions: Result-ify Packaging APIs; wire a real ILoggerFactory in
   the future runtime to surface the built-in events.
