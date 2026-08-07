# The Runtime Host

`Ovi.Runtime.Host` (under `runtime/`, its own solution folder — not `src/`) is the first piece of
the runtime layer `docs/architecture.md` calls "future": a **single-workflow host** that turns an
`Ovi.Sdk.Workflows.WorkflowDefinition` (or a bare `Ovi.Sdk.Agents.AgentDefinition`) into something
that actually runs, behind one entry point regardless of what triggered it.

This document assumes familiarity with the SDK concepts in `docs/architecture.md` (nodes,
`WorkflowExecutionContext`, features, `Result<T>`).

## One host, one workflow, no `id`

`WorkflowHost` is built once, bound to exactly one definition, and stays bound for its lifetime:

```csharp
var host = WorkflowHost.CreateBuilder()
    .UseWorkflow(definition)          // or .UseAgent(agentDefinition, agentNode) / .UsePackage(package)
    .MapNode(new UppercaseNode())     // bind each NodeId the definition references to a runnable INode
    .Build();

var result = await host.RunAsync(new RunRequest { Input = "hello" });
```

There is no `id` parameter anywhere on `WorkflowHost` — "which workflow" was answered once, at
`Build()` time. A process hosts one agent or workflow; running a second one means building a second
`WorkflowHost`.

`Ovi.Sdk.Workflows` deliberately stops at "binding ids to instances... is runtime work" — that
binding is `INodeRegistry`/`NodeRegistry`, populated via `WorkflowHostBuilder.MapNode`.

## One request/response shape for every trigger

`RunRequest`/`RunResult` are the single shape manual calls, normalized webhooks, and background
triggers (a schedule) all use:

```csharp
public sealed record RunRequest
{
    public TriggerKind TriggerKind { get; init; } = TriggerKind.Manual; // Manual | Webhook | Schedule
    public object? Input { get; init; }                                 // entry input for a plain workflow
    public string? Prompt { get; init; }                                // entry input for an agent-wrapped host
    public IReadOnlyList<ChatMessage>? Messages { get; init; }          // ditto
    public JsonObject? State { get; init; }                             // merged into WorkflowState before the run
    public JsonObject? Metadata { get; init; }
    public string? CorrelationId { get; init; }
}
```

`RunResult` carries the outcome (`Result<object?> Output` — the sink node's value on success, the
first failure's `Error` otherwise) and `RunDiagnostics` (start time, total duration, per-node
durations, the echoed correlation id).

Manual and webhook callers use `RunAsync` directly — that's what "manual/webhook always there, no
configuration needed" means. A schedule is the one opt-in trigger this SDK ships
(`WorkflowHostBuilder.WithTrigger(new ScheduleTriggerStrategy(schedule))`): it just calls
`host.RunAsync` on its own timer, through the exact same entry point. Additional trigger kinds
(events, queues) plug in the same way, via `ITriggerStrategy`.

## Middleware only ever sees the context

Individual nodes keep their stable, atomically-testable, typed contract
(`INode<TIn,TOut>.ExecuteAsync(TInput, WorkflowExecutionContext)`) — that's settled SDK surface
(`docs/architecture.md`) and this host doesn't touch it. But at the *host* boundary — the run as a
whole, and each node as the host walks the graph — inputs and outputs travel on the context itself,
the way `HttpContext.Request`/`.Response` do in ASP.NET Core, not as parameters:

```csharp
public interface IRunMiddleware
{
    ValueTask InvokeAsync(WorkflowExecutionContext context, RunMiddlewareDelegate next);
}

public interface INodeMiddleware
{
    ValueTask InvokeAsync(WorkflowExecutionContext context, NodeMiddlewareDelegate next);
}
```

`RunIoFeature` (whole run) and `NodeIoFeature` (one node, re-attached before each node executes)
are the context features middleware reads/writes: `context.GetRequiredFeature<RunIoFeature>().Input`,
`.Output = ...`. Composing them is exactly `IApplicationBuilder.Use`: registered middleware wraps
around the next, terminating at the actual work (the graph walk, or one `INode.ExecuteAsync` call).

Two run middlewares and two node middlewares are on by default (`WorkflowHostBuilder.
WithoutDefaultMiddleware()` opts out):

| Middleware | Scope | Does |
|---|---|---|
| `ExceptionHandlingRunMiddleware` | run (outermost) | The one whole-run `try/catch`; converts an unhandled exception into an `ExecutionError` result instead of an unhandled exception at the caller. Cancellation still propagates. |
| `TimingRunMiddleware` | run | Records total duration into `RunDiagnostics`; feeds a configured `IRunTimeoutPolicy`. |
| `TimingNodeMiddleware` | node (outer) | Records each node's duration, keyed by workflow instance key. |
| `NodeTimeoutMiddleware` | node (inner) | Enforces per-node timeouts (see below). |

`Ovi.Sdk.Nodes.LoggingNode`/`.WithLogging()` still works standalone; it just isn't wired into this
pipeline automatically — add it as another `INodeMiddleware`, or wrap individual mapped nodes with
it before `MapNode`.

## Timeouts: global (adaptive) and per-node

Both, because they answer different questions — "is this one run taking too long overall" vs "is
this one node stuck":

- **Per-node** (`WorkflowHostBuilder.WithNodeTimeout(key, timeout)`, enforced by
  `NodeTimeoutMiddleware`): a **soft** timeout. `WorkflowExecutionContext.CancellationToken` is one
  fixed token for the whole run, so a timed-out node's own task is not force-cancelled — it keeps
  running in the background until it finishes or the run's own cancellation fires. This is the same
  limitation every cooperative, non-preemptive timeout has without a dedicated per-operation token;
  `NodeTimeoutMiddleware` races the node via `Task.WaitAsync` and returns a timeout `ExecutionError`
  the moment the clock runs out, rather than pretending to cancel something it can't.
- **Global, adaptive** (`WorkflowHostBuilder.WithTimeoutPolicy(...)`, an `IRunTimeoutPolicy`):
  `AdaptiveTimeoutPolicy` keeps a rolling window of recent run durations and, once a few runs have
  happened, sets the timeout to `mean + multiplier × stddev` — a multiplier that shrinks toward a
  floor as more samples accumulate, so the budget tightens as the run-time distribution becomes
  better known. This is **phi-accrual-inspired**, not a literal port: the real phi-accrual failure
  detector reasons about heartbeat inter-arrival times, not run durations, but the shape — normalize
  to the observed distribution instead of one fixed guess, and get more confident as evidence
  accumulates — is the same idea. `FixedTimeoutPolicy` is the plain constant-timeout alternative.
  Before enough samples exist, `AdaptiveTimeoutPolicy` returns a configured fallback. The result is
  always clamped to a configured `[min, max]` band.

## Concurrency

`WorkflowHostBuilder.WithConcurrencyMode(...)`:

- `Serialize` (default) — an internal `SemaphoreSlim(1,1)` means a second concurrent `RunAsync` call
  waits its turn.
- `JoinInFlight` — a second concurrent call is handed the in-flight run's own result task instead of
  starting a new run. Its own (possibly different) `RunRequest` is never actually used — only sound
  when concurrent triggers are known to be equivalent, e.g. several schedule ticks piling up while a
  slow run is still in progress.

## The default workflow / how an agent runs

A bare `AgentDefinition` runs through the exact same `WorkflowHost` code path as a full workflow:
`AgentWorkflowFactory.Wrap` lifts it into a synthetic one-node `WorkflowDefinition` (a single
instance, key `"agent"`, node type = the agent's own id) — no trigger node is needed since
`RunAsync` *is* the entry point. `WorkflowHostBuilder.UseAgent(definition, agentNode)` does the
wrapping and node registration in one call; `RunRequest.Prompt`/`.Messages` become the entry node's
`AgentRequest`.

If `WorkflowHostBuilder.WithChatClient(...)` is also called, the host attaches an
`AgentChatFeature` before every run, backed by conversation history that **persists across calls to
this host** (not per-run) — this is "the context has a feature to get current chat history" from
the design brief, and `AgentChatFeature.LastMessage` answers "and its last message" directly.
Without a configured chat client, agent nodes still work through their own existing resolution chain
(fixed client → feature → `RuntimeServices`); the host just doesn't maintain cross-run history.

For a package (`WorkflowHostBuilder.UsePackage(package, ...)`), "find the location of the default
workflow" is: `OviPackageManifest.DefaultEntry` when the manifest sets it, else the package's one
`Workflow`/`Agent`-kind node entry when there's exactly one — ambiguous or missing cases throw at
build time rather than guessing.

## Process lifecycle: `HostProcessManager`

`Ovi.Runtime.Host` has **no entry point** — it's a library; something else (a thin `Program.cs`
outside this repo, a test, a host process) owns `Main`. `HostProcessManager` is the piece that
process wants: it registers `SIGTERM`/`SIGINT`/Ctrl+C, exposes a `ShutdownRequested`
`CancellationToken` and a `HostLifecycleState` (`Starting → Ready → Draining → Stopped`), and on
shutdown waits — bounded by a configurable grace period — for `WorkflowHost.WaitForIdleAsync` before
completing, so an in-flight run gets a chance to finish instead of being cut off mid-run.

```csharp
await using var manager = new HostProcessManager(host, shutdownGracePeriod: TimeSpan.FromSeconds(30));
await host.StartTriggersAsync();
await manager.RunUntilShutdownAsync(); // blocks until a signal arrives and drain completes
await host.StopTriggersAsync();
```

No prior implementation of this existed anywhere accessible when it was built — it's a standard,
documented design (signal registration + bounded drain), not a port of "the old implementation."

## Deliberate v1 limitations

Consistent with the rest of this codebase's "deliberate incompleteness" precedent
(`IPythonScriptEngine`, `MultipartHttpChatClient.ParseResponse`):

- **Fan-in is rejected, not guessed.** `Ovi.Sdk.Workflows` connections are single-output/single-input
  today (a documented SDK follow-up is output ports). A node with more than one upstream connection
  has no defined merge semantics yet, so `WorkflowHostBuilder.Build()` throws
  `InvalidOperationException` for such a definition rather than picking an arbitrary merge policy.
- **Multiple sink nodes** produce a `Dictionary<string, object?>` keyed by sink key as the run's
  output, rather than an error — a workflow with independent output branches is valid, just not a
  single scalar result.
- **Event-based triggers** beyond `ScheduleTriggerStrategy` are not shipped; `ITriggerStrategy` is
  the extension point.
