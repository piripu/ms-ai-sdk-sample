# Ovi SDK architecture

The Ovi SDK defines what a **workflow node** is — its identity, its execution contract, how it
ships — without shipping a workflow engine. A workflow contains individual nodes and defines how
they connect; composing and running workflows is the job of a future runtime built on these
contracts.

## The two-layer picture

```
┌────────────────────────────────────────────────────────────┐
│  Ovi.Runtime.* (future)                                    │
│  graph model · scheduler · script engines · persistence ·  │
│  package loading · chat-client wiring                      │
├────────────────────────────────────────────────────────────┤
│  Ovi.Sdk.* (this repo — contracts + authoring)             │
│                                                            │
│  Tools ──▶ Nodes ◀── Triggers        Packaging ──▶ Nodes   │
│    ▲         ▲                       Scripting ──▶ Nodes   │
│    └─ Agents ┘                                             │
└────────────────────────────────────────────────────────────┘
```

The SDK/runtime boundary **is** the abstractions boundary. There is deliberately no
`Ovi.Sdk.Abstractions` package: `Ovi.Sdk.Nodes` plays that role (zero dependencies, contracts plus
the in-memory defaults needed for atomic testing), and every implementation-shaped concern —
schedulers, engines, persistence, loading — lands in future runtime packages rather than here.

### Dependency policy

- `Ovi.Sdk.Nodes` references only `Microsoft.Extensions.*.Abstractions` packages (today:
  `Microsoft.Extensions.Logging.Abstractions`) — never implementation packages or third-party
  dependencies.
- Per-concept packages keep dependency granularity: a node author takes `Nodes` (no external
  deps), a trigger author adds Cronos, only tool/agent authors take Microsoft.Extensions.AI.
- Deliberate incompleteness is part of the design: `MultipartHttpChatClient.ParseResponse` (no wire
  contract yet) and `IPythonScriptEngine` (no engine choice yet — see
  [python-script-execution.md](python-script-execution.md)) stay open until the runtime decides.

## Core concepts

### Nodes

A node is the atomic unit of a workflow, defined by:

- **Identity** — a `NodeDescriptor`: `NodeId` + display name + description.
- **Data contract** — `TInput`/`TOutput`, expressed by `INode<TInput, TOutput>`.
- **Execution** — `ExecuteAsync(input, WorkflowExecutionContext)`.

Three ways to make one, all equivalent to the rest of the system:

1. Implement `INode<TIn,TOut>` directly.
2. Derive from the optional convenience base `Node<TIn,TOut>` (identity plumbing + the untyped
   `INode` bridge for free).
3. Compose from a delegate: `Node.Create(descriptor, (input, ctx) => …)`.

The untyped `INode` surface (`ExecuteAsync(object?, context)`, `InputType`, `OutputType`) exists so
a runtime can wire heterogeneous nodes; it validates input types and bridges to the typed call.

### Identity

`NodeId` is a domain concept shared by nodes, tools, agents and packages:

```
acme/web-search@1.2.0      published (version required, full semver)
./manual-trigger           built-in (version may be implicit)
./schedule-trigger@0.3.1   built-in with explicit version
```

Organization and name are case-insensitive slugs. `NodeId.IsSameNode` compares ignoring version.
The agent-definition JSON Schema embeds the same grammar as a regex; `SchemaTests` keeps the two in
lockstep.

### Execution context

`WorkflowExecutionContext` is the one context every node executes against:

| Member | Role |
|---|---|
| `Workflow` | Workflow id/name, run id, start time |
| `RuntimeServices` | `IServiceProvider` the runtime exposes to nodes |
| `WorkflowState` / `GlobalState` / `InstanceState` | The three serializable state scopes |
| `CancellationToken` | Run cancellation |
| `Properties` | Untyped, non-serialized bag |
| `GetFeature<T>` / `SetFeature<T>` | **Typed capabilities** (see below) |

**Features, not subclasses.** A run capability (chat today, memory tomorrow) is a typed object
attached with `SetFeature` and read with `GetFeature<T>` — the `IFeatureCollection` idea from
ASP.NET Core. Capabilities therefore stack arbitrarily on one context instead of forcing a subclass
per combination. Context subclasses that exist (`AgentWorkflowExecutionContext`) are sealed sugar
whose only job is attaching a feature; nodes must read the feature, never type-test the context.

**State is serializable by construction.** `InMemoryStateStore` serializes values to JSON on write
(non-serializable values fail fast at `SetValue`) and round-trips whole stores via
`ToJsonObject`/`FromJsonObject`, so the runtime can persist state scopes without cooperation from
node authors.

### Dependency resolution idiom

When a node needs an external collaborator, resolution is always, in order:

1. the instance fixed on the node (constructor),
2. the context feature (e.g. `AgentChatFeature.ChatClient`),
3. `RuntimeServices`,
4. a `ResolutionError` failure result naming all three options.

`AgentNode` (chat client) and `PythonScriptNode` (script engine) both follow it; new node types
with external needs must too. A resolution that comes up empty is not an exception — it is a
`ResolutionError` failure result carrying that same guidance as its hint.

### Errors and results

Execution APIs (`ExecuteAsync`, `FireAsync`, `AgentNode.FromDefinition`,
`ChatTriggerNode.FromWebhook`, `AgentDefinitionSerializer` loads) return `Result<T>`: exactly one of
`Success(value)` or `Failure(error)`. The error set is closed and DU-ready — `ValidationError`
(bad input/definition, with parser detail), `ResolutionError` (missing collaborator, with a fix-it
hint), `ExecutionError` (a collaborator or node body threw; carries the exception). Design intent:

- **Expected failures are values**, so runtimes branch on them (`Match`, `IsFailure`, error codes)
  instead of catching exceptions across a graph of heterogeneous nodes.
- **Exceptions remain** for programmer errors (argument validation), cancellation
  (`OperationCanceledException` always propagates), and external contracts the SDK cannot change
  (`IChatClient` — agents translate its exceptions into `ExecutionError`).
- The untyped `INode` bridge converts input-type mismatches to `ValidationError` and unhandled
  exceptions to `ExecutionError`, logging the latter.
- `Result<T>` is deliberately naive and in-house: two states, `Match`/`Map`/`Bind`, implicit
  conversions from `T` and `Error`. It mirrors CSharpFunctionalExtensions ergonomics without
  putting a third-party type into every public contract, and maps 1:1 onto a future C#
  discriminated union.

### Logging

`Microsoft.Extensions.Logging.Abstractions` is the one permitted reference in `Ovi.Sdk.Nodes`.
The idiom:

- `context.GetLogger<T>()` / `GetLogger(category)` resolve an `ILoggerFactory` from
  `RuntimeServices` and fall back to `NullLogger` — logging is always available, always silent
  unless the runtime wires a factory.
- Components emit meaningful events via source-generated `[LoggerMessage]` methods, quiet by
  default: agent send/receive with client-resolution source (Debug), missing collaborators
  (Warning), collaborator exceptions (Error), script-engine dispatch, definition loads, trigger
  fires, multipart client request/response.
- Execution tracing is composition, not base-class noise: `node.WithLogging()` wraps any
  `INode<TIn,TOut>` with start/success-with-duration/failure/exception logging under the
  `Ovi.Sdk.Nodes.LoggingNode` category.

### Tools and agents

A **tool** is Ovi identity composed with a Microsoft.Extensions.AI `AIFunction` — the id's slug
becomes the LLM-facing function name. `Tool.FromDelegate` builds one from a .NET delegate;
`Tool.FromAIFunction` wraps an existing function, which is also the MCP path (an MCP client tool
*is* an `AIFunction`), so MCP support is an adapter call, not a redesign.

An **agent is a node** (`AgentRequest → AgentResponse`) with tool connections. Any `IChatClient`
works; when the agent has tools, the client is wrapped with `UseFunctionInvocation()` so tool calls
execute automatically. Agents are also declarable in YAML/JSON (`AgentDefinition` +
`schemas/agent-definition.schema.json`); YAML is bridged onto the JSON object model
(`YamlJsonBridge`) so one deserialization pipeline — `OviJson` conventions, converters, validation —
governs both formats.

### Triggers

A **trigger is the node that starts a workflow**: input = the external event payload (webhook
request, chat message, schedule tick), output = what enters the workflow. Every trigger is manually
fireable (`FireAsync`) with a hand-crafted payload — that is the "test any trigger without
infrastructure" story. Schedules (`Schedule.FromInterval` / `Schedule.FromCron`) validate eagerly
and expose `GetNextOccurrence` so planning is testable; the actual timer belongs to the runtime.

### Scripting

`PythonScriptNode` runs `def run(input, context)` scripts as nodes with JSON in/out. The SDK fixes
the script contract and the `IPythonScriptEngine` seam; engine candidates and the security model
are in [python-script-execution.md](python-script-execution.md).

### Packaging

`.ovipkg` is a zip with a root `manifest.json` describing the nodes it carries; package ids share
the node identity domain. Format details: [packaging.md](packaging.md). Loading packages into a
process is runtime territory.

## Design principles (summary)

1. **Contracts first** — the SDK must stay runnable-nowhere and testable-everywhere.
2. **Composition over inheritance** — features on the context, `INode<TIn,TOut>` + delegates +
   decorators, composed tools, sealed concretes.
3. **Atomic testability** — every node executes with `WorkflowExecutionContext.CreateBuilder()`
   plus fakes; the test suite is the reference usage documentation.
4. **One identity domain** — `org/name@semver` everywhere, schema-verified.
5. **Serializable state, always** — fail at write time, not persistence time.
6. **Align with Microsoft.Extensions.AI** — `AIFunction`, `IChatClient`, function invocation;
   never a parallel abstraction.
7. **Expected failures are results, not exceptions** — `Result<T>` with the closed error set on
   every execution API; exceptions only for programmer errors and cancellation.
8. **Observable by default, silent by default** — `GetLogger` + `[LoggerMessage]` events +
   `WithLogging` decorator; zero cost until a runtime registers an `ILoggerFactory`.
