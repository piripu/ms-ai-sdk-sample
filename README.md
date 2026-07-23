# Ovi SDK

C# SDK contracts for building **workflow nodes** — in the spirit of n8n — that will later be composed
into full workflows by a separate runtime. A workflow contains individual nodes and defines how they
connect; this SDK defines what a node *is*, how it identifies itself, how it executes, and how it
ships.

> The name "Ovi" comes from the package format, `.ovipkg`. Rename-friendly: nothing outside the
> `Ovi.*` prefix depends on it.

## Projects

| Project | What it holds |
|---|---|
| `Ovi.Sdk.Operators` | The core: `Operator<TInput, TOutput>` (a node), operator identity (`OperatorId`), `WorkflowExecutionContext` + serializable state stores. No dependencies. |
| `Ovi.Sdk.Tools` | `ITool` — Ovi identity around a Microsoft.Extensions.AI `AIFunction`. `DelegateTool` (from a .NET delegate), `AIFunctionTool` (wrap any `AIFunction`; the future MCP bridge), `ToolCatalog`. |
| `Ovi.Sdk.Agents` | `AgentOperator` (an operator with tool connections), YAML/JSON `AgentDefinition`s, `AgentWorkflowExecutionContext` (chat client + chat history), and the intentionally incomplete `MultipartHttpChatClient`. |
| `Ovi.Sdk.Triggers` | Workflow starting points: chat (webhook-shaped), webhook, schedule (interval or cron), manual — all manually fireable for testing. |
| `Ovi.Sdk.Packaging` | The `.ovipkg` format: a zip with a `manifest.json`, carrying operators/agents/tools/triggers. Authoring (`OviPackageBuilder`) and reading (`OviPackage`). |

Dependency layering (arrows = "references"):

```
Tools ──▶ Operators ◀── Triggers
  ▲            ▲
  └── Agents ──┘         Packaging ──▶ Operators
```

## Core concepts

### Operators (nodes)

An **operator** is the atomic unit of a workflow. It defines its own input and output types and
executes against a shared context:

```csharp
sealed class UppercaseOperator() : Operator<string, string>(
    new OperatorDescriptor(OperatorId.BuiltIn("uppercase"), "Uppercase", "Uppercases the input text."))
{
    public override ValueTask<string> ExecuteAsync(string input, WorkflowExecutionContext context)
        => ValueTask.FromResult(input.ToUpperInvariant());
}
```

Operators are **atomic**: any instance can be executed (and unit tested) on its own — no engine:

```csharp
var context = WorkflowExecutionContext.CreateBuilder().Build();
var result = await new UppercaseOperator().ExecuteAsync("hello", context); // "HELLO"
```

Every operator also has an untyped `IOperator` surface (`ExecuteAsync(object?, context)`) that a
workflow runtime uses to wire heterogeneous nodes together.

### Operator identity

`OperatorId` is a domain concept: `organization/name@semver`, where built-ins use `.` as the
organization and may leave the version implicit:

```
acme/web-search@1.2.0     published operator (version required)
./manual-trigger          built-in, implicit version
./schedule-trigger@0.3.1  built-in, explicit version
```

Organization/name compare case-insensitively; versions are full semver (prerelease + build
metadata). The same identity scheme addresses packages.

### Execution context and state

All execution flows through `WorkflowExecutionContext`:

| Member | Meaning |
|---|---|
| `RuntimeServices` (`IServiceProvider`) | Services the runtime exposes to operators. |
| `WorkflowState` (`IStateStore`) | Shared per-run state. |
| `GlobalState` (`IStateStore`) | Persisted across invocations, runtime-wide. |
| `InstanceState` (`IStateStore`) | Persisted across invocations, scoped to the operator's package instance. |
| `CancellationToken` | Run cancellation. |
| `Workflow` (`WorkflowInfo`) | Workflow id/name, run id, start time. |
| `Properties` | Non-serialized extension bag ("others as necessary"). |

State stores are **serializable by construction**: values are serialized to JSON on write (failing
fast on non-serializable values) and the whole store exports/imports via `ToJsonObject()` /
`InMemoryStateStore.FromJsonObject()`.

`AgentWorkflowExecutionContext` derives from the shared context and adds the current run's
`ChatClient` (`IChatClient`) and mutable `ChatHistory` — future agent-scoped capabilities (memory)
land there too.

### Agents

An **agent is an operator** (`AgentRequest` → `AgentResponse`) with extra connections: tools now,
memory later. Agents build on **Microsoft.Extensions.AI** — any `IChatClient` works (an Ollama
client such as OllamaSharp's, a cloud provider's, or a custom one), and tools surface to the model
as `AIFunction`s. When an agent has tools, the chat client is wrapped with
`UseFunctionInvocation()` so tool calls execute automatically (opt out via
`autoInvokeFunctions: false`).

Agents can be authored declaratively in YAML or JSON:

```yaml
id: acme/researcher@1.0.0
name: Researcher
description: Answers questions using its tools.
instructions: |
  You are a careful research assistant.
tools:
  - ./echo                       # bare id reference
  - id: acme/web-search@2.0.0    # reference with options
    options:
      maxResults: 5
```

```csharp
var definition = AgentDefinitionSerializer.Load("researcher.yaml"); // or FromYaml/FromJson
var catalog = new ToolCatalog
{
    DelegateTool.Create("./echo", "Echo", "Echoes text back.", (string text) => text),
    DelegateTool.Create("acme/web-search@2.0.0", "Web Search", "Searches the web.", Search),
};
var agent = AgentOperator.FromDefinition(definition, catalog, chatClient);

var response = await agent.ExecuteAsync("What is Ovi?", context);
Console.WriteLine(response.Text);
```

The chat client resolves per execution: fixed on the operator → `AgentWorkflowExecutionContext.ChatClient`
→ `IChatClient` in `RuntimeServices`.

#### Definition schema

Agent definitions have a JSON Schema at [`schemas/agent-definition.schema.json`](schemas/agent-definition.schema.json)
that governs both formats (YAML is bridged onto the JSON object model before parsing). Wire it up for
editor validation and autocomplete:

```yaml
# yaml-language-server: $schema=../../schemas/agent-definition.schema.json
id: acme/researcher@1.0.0
```

```json
{ "$schema": "../../schemas/agent-definition.schema.json", "id": "acme/researcher@1.0.0" }
```

Working examples live in [`samples/agents/`](samples/agents). The schema is deliberately stricter
than the runtime loader (which skips unknown properties), so typos are caught while authoring;
tests keep the schema's id pattern in lockstep with `OperatorId`.

`MultipartHttpChatClient` is the SDK's custom client skeleton: it HTTP-POSTs the conversation as
`MultipartFormDataContent` to an arbitrary endpoint. **It is intentionally incomplete** — the
request side works; response parsing (`ParseResponse`), streaming, and binary parts await a real
wire contract.

### Tools

A tool is Ovi identity (`OperatorDescriptor`) around an `AIFunction`. The id's slug name becomes the
LLM-facing function name; the description becomes the function description.

MCP is a planned extension, not a rework: MCP client tools *are* `AIFunction`s, so they'll arrive by
wrapping them in `AIFunctionTool` — agents won't change.

### Triggers

A **trigger is the operator that starts a workflow**: its input is the external event payload, its
output enters the workflow. All triggers can be fired manually (`FireAsync`) for tests and "run
now" tooling.

| Trigger | Payload | Notes |
|---|---|---|
| `ChatTriggerOperator` | `ChatTriggerPayload` | A chat trigger is a specialized webhook — `FromWebhook()` lifts a JSON body `{message, sessionId, userId}`. |
| `WebhookTriggerOperator` | `WebhookRequest` | Transport-agnostic HTTP request snapshot. |
| `ScheduleTriggerOperator` | `ScheduleTick` | `Schedule.FromInterval(TimeSpan)` or `Schedule.FromCron("*/15 * * * *")` (Cronos-validated; `GetNextOccurrence` for planning). |
| `ManualTriggerOperator<T>` | any | Pass-through, for tests and on-demand runs. |

```csharp
var trigger = new ScheduleTriggerOperator(Schedule.FromCron("0 9 * * MON-FRI"));
var tick = await trigger.FireAsync(ScheduleTick.Manual(), context); // manual firing for tests
```

### Packaging (`.ovipkg`)

An `.ovipkg` is **a zip file with its extension renamed**, plus a root `manifest.json` describing
the operators inside. Package ids share the operator identity domain.

```csharp
new OviPackageBuilder("acme/starter-pack@0.1.0", "Starter Pack")
    .AddTextFile("agents/researcher.yaml", AgentDefinitionSerializer.ToYaml(definition))
    .AddOperator(new PackagedOperatorEntry(definition.ToDescriptor(), PackagedOperatorKind.Agent, "agents/researcher.yaml"))
    .Save("starter-pack.ovipkg");

using var package = OviPackage.Open("starter-pack.ovipkg");
var manifest = package.Manifest; // packageId, name, operators [{id, name, kind, path}]
var yaml = package.ReadAllText("agents/researcher.yaml");
```

Loading packages into a live runtime is deliberately out of scope here — this SDK owns the
contracts; the runtime comes later.

## Building

```bash
dotnet build
dotnet test
```

Requires the .NET 10 SDK (see `global.json`). The test suite (70 tests) doubles as usage
documentation for every area above.

## Deliberately deferred

- **Runtime**: workflow graphs, node wiring, scheduling, package loading/activation.
- **MCP tools**: arrive via `AIFunctionTool` once an MCP client is wired in.
- **Agent memory**: lands on `AgentWorkflowExecutionContext` next to chat history.
- **`MultipartHttpChatClient` response contract**: `ParseResponse`, streaming, binary parts.
