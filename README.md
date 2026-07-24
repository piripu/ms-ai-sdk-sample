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
| `Ovi.Sdk.Nodes` | The core: `Node<TInput, TOutput>`, node identity (`NodeId`), `WorkflowExecutionContext` + serializable state stores. No dependencies. |
| `Ovi.Sdk.Tools` | `ITool` — Ovi identity around a Microsoft.Extensions.AI `AIFunction`. `DelegateTool` (from a .NET delegate), `AIFunctionTool` (wrap any `AIFunction`; the future MCP bridge), `ToolCatalog`. |
| `Ovi.Sdk.Agents` | `AgentNode` (a node with tool connections), YAML/JSON `AgentDefinition`s, `AgentWorkflowExecutionContext` (chat client + chat history), and the intentionally incomplete `MultipartHttpChatClient`. |
| `Ovi.Sdk.Triggers` | Workflow starting points: chat (webhook-shaped), webhook, schedule (interval or cron), manual — all manually fireable for testing. |
| `Ovi.Sdk.Packaging` | The `.ovipkg` format: a zip with a `manifest.json`, carrying nodes/agents/tools/triggers. Authoring (`OviPackageBuilder`) and reading (`OviPackage`). |
| `Ovi.Sdk.Scripting` | `PythonScriptNode` — a node whose behavior is a Python script (`def run(input, context)`). Contracts + the `IPythonScriptEngine` seam; execution engines come with the runtime ([design plan](docs/python-script-execution.md)). |

Dependency layering (arrows = "references"):

```
Tools ──▶ Nodes ◀── Triggers
  ▲            ▲
  └── Agents ──┘         Packaging ──▶ Nodes
                         Scripting ──▶ Nodes
```

## Core concepts

### Nodes

A **node** is the atomic unit of a workflow. It defines its own input and output types and
executes against a shared context:

```csharp
sealed class UppercaseNode() : Node<string, string>(
    new NodeDescriptor(NodeId.BuiltIn("uppercase"), "Uppercase", "Uppercases the input text."))
{
    public override ValueTask<string> ExecuteAsync(string input, WorkflowExecutionContext context)
        => ValueTask.FromResult(input.ToUpperInvariant());
}
```

Nodes are **atomic**: any instance can be executed (and unit tested) on its own — no engine:

```csharp
var context = WorkflowExecutionContext.CreateBuilder().Build();
var result = await new UppercaseNode().ExecuteAsync("hello", context); // "HELLO"
```

Every node also has an untyped `INode` surface (`ExecuteAsync(object?, context)`) that a
workflow runtime uses to wire heterogeneous nodes together.

### Node identity

`NodeId` is a domain concept: `organization/name@semver`, where built-ins use `.` as the
organization and may leave the version implicit:

```
acme/web-search@1.2.0     published node (version required)
./manual-trigger          built-in, implicit version
./schedule-trigger@0.3.1  built-in, explicit version
```

Organization/name compare case-insensitively; versions are full semver (prerelease + build
metadata). The same identity scheme addresses packages.

### Execution context and state

All execution flows through `WorkflowExecutionContext`:

| Member | Meaning |
|---|---|
| `RuntimeServices` (`IServiceProvider`) | Services the runtime exposes to nodes. |
| `WorkflowState` (`IStateStore`) | Shared per-run state. |
| `GlobalState` (`IStateStore`) | Persisted across invocations, runtime-wide. |
| `InstanceState` (`IStateStore`) | Persisted across invocations, scoped to the node's package instance. |
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

An **agent is a node** (`AgentRequest` → `AgentResponse`) with extra connections: tools now,
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
var agent = AgentNode.FromDefinition(definition, catalog, chatClient);

var response = await agent.ExecuteAsync("What is Ovi?", context);
Console.WriteLine(response.Text);
```

The chat client resolves per execution: fixed on the node → `AgentWorkflowExecutionContext.ChatClient`
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
tests keep the schema's id pattern in lockstep with `NodeId`.

`MultipartHttpChatClient` is the SDK's custom client skeleton: it HTTP-POSTs the conversation as
`MultipartFormDataContent` to an arbitrary endpoint. **It is intentionally incomplete** — the
request side works; response parsing (`ParseResponse`), streaming, and binary parts await a real
wire contract.

### Tools

A tool is Ovi identity (`NodeDescriptor`) around an `AIFunction`. The id's slug name becomes the
LLM-facing function name; the description becomes the function description.

MCP is a planned extension, not a rework: MCP client tools *are* `AIFunction`s, so they'll arrive by
wrapping them in `AIFunctionTool` — agents won't change.

### Triggers

A **trigger is the node that starts a workflow**: its input is the external event payload, its
output enters the workflow. All triggers can be fired manually (`FireAsync`) for tests and "run
now" tooling.

| Trigger | Payload | Notes |
|---|---|---|
| `ChatTriggerNode` | `ChatTriggerPayload` | A chat trigger is a specialized webhook — `FromWebhook()` lifts a JSON body `{message, sessionId, userId}`. |
| `WebhookTriggerNode` | `WebhookRequest` | Transport-agnostic HTTP request snapshot. |
| `ScheduleTriggerNode` | `ScheduleTick` | `Schedule.FromInterval(TimeSpan)` or `Schedule.FromCron("*/15 * * * *")` (Cronos-validated; `GetNextOccurrence` for planning). |
| `ManualTriggerNode<T>` | any | Pass-through, for tests and on-demand runs. |

```csharp
var trigger = new ScheduleTriggerNode(Schedule.FromCron("0 9 * * MON-FRI"));
var tick = await trigger.FireAsync(ScheduleTick.Manual(), context); // manual firing for tests
```

### Scripting (Python)

A **script is a node too**: `PythonScriptNode` takes JSON in and JSON out
(`Node<JsonNode?, JsonNode?>`), so scripted nodes compose with everything else. The script
defines an entry point (default `run`) that receives the input and a read-only context snapshot
(workflow info + the three state scopes) and returns the output:

```python
def run(input, context):
    return {"total": sum(input["amounts"]), "run": context["workflow"]["runId"]}
```

```csharp
var node = new PythonScriptNode(PythonScript.FromFile("totals.py"));
var output = await node.ExecuteAsync(input, context); // engine resolved from node or RuntimeServices
```

Execution is deliberately decoupled behind `IPythonScriptEngine` — the SDK ships the contract, the
runtime supplies the engine. The candidate engine designs (CPython subprocess with a JSON-over-stdio
bootstrap, pythonnet, IronPython, WASM) and the security/packaging plan live in
[`docs/python-script-execution.md`](docs/python-script-execution.md). Fake engines keep script
nodes atomically testable today.

### Packaging (`.ovipkg`)

An `.ovipkg` is **a zip file with its extension renamed**, plus a root `manifest.json` describing
the nodes inside. Package ids share the node identity domain.

```csharp
new OviPackageBuilder("acme/starter-pack@0.1.0", "Starter Pack")
    .AddTextFile("agents/researcher.yaml", AgentDefinitionSerializer.ToYaml(definition))
    .AddNode(new PackagedNodeEntry(definition.ToDescriptor(), PackagedNodeKind.Agent, "agents/researcher.yaml"))
    .Save("starter-pack.ovipkg");

using var package = OviPackage.Open("starter-pack.ovipkg");
var manifest = package.Manifest; // packageId, name, nodes [{id, name, kind, path}]
var yaml = package.ReadAllText("agents/researcher.yaml");
```

Loading packages into a live runtime is deliberately out of scope here — this SDK owns the
contracts; the runtime comes later.

## Building

```bash
dotnet build
dotnet test
```

Requires the .NET 10 SDK (see `global.json`). The test suite (83 tests) doubles as usage
documentation for every area above.

## Deliberately deferred

- **Runtime**: workflow graphs, node wiring, scheduling, package loading/activation.
- **MCP tools**: arrive via `AIFunctionTool` once an MCP client is wired in.
- **Agent memory**: lands on `AgentWorkflowExecutionContext` next to chat history.
- **`MultipartHttpChatClient` response contract**: `ParseResponse`, streaming, binary parts.
