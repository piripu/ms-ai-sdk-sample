# Workflow definitions

`Ovi.Sdk.Workflows` is the declarative model of a workflow — the node instances it contains and the
connections between them, n8n-style — authorable in JSON or YAML. It is **definitions only**: a
workflow can be parsed, validated, and analyzed as a graph, but *running* it (binding node ids to
`INode` instances, scheduling, passing data along edges) is runtime work, deliberately out of scope
here, exactly as with the rest of the SDK.

A workflow contains individual nodes and defines how each is connected — this project is the piece
that ties the node SDK together.

## Format

```yaml
# yaml-language-server: $schema=../../schemas/workflow-definition.schema.json
id: acme/support-flow@1.0.0
name: Support Flow
description: Triage inbound chats and post a reply.
nodes:
  - key: inbound
    node: ./chat-trigger
  - key: triage
    node: acme/support-triage@0.1.0
    displayName: Triage the message
  - key: notify
    node: ./python-script
    config:
      path: scripts/notify.py
connections:
  - from: inbound
    to: triage
  - from: triage
    to: notify
```

The same document in JSON (the two formats parse identically — YAML is bridged onto the JSON object
model before deserialization, so one pipeline governs both):

```json
{
  "$schema": "../../schemas/workflow-definition.schema.json",
  "id": "acme/support-flow@1.0.0",
  "name": "Support Flow",
  "nodes": [
    { "key": "inbound", "node": "./chat-trigger" },
    { "key": "triage", "node": "acme/support-triage@0.1.0", "displayName": "Triage the message" },
    { "key": "notify", "node": "./python-script", "config": { "path": "scripts/notify.py" } }
  ],
  "connections": [
    { "from": "inbound", "to": "triage" },
    { "from": "triage", "to": "notify" }
  ]
}
```

### Fields

| Field | Required | Meaning |
|---|---|---|
| `id` | yes | The workflow's identity, from the shared node-id domain (`org/name@semver`, or `./name` for built-ins). |
| `name` | yes | Display name. |
| `description` | no | What the workflow does. |
| `nodes` | yes | The node instances (see below); at least one. |
| `connections` | no | The directed edges wiring instances together (defaults to none). |
| `metadata` | no | Free-form editor data (canvas viewport, notes) — outside the SDK's semantics. |

Each **node instance** (`nodes[]`) has:

| Field | Required | Meaning |
|---|---|---|
| `key` | yes | A workflow-unique instance key that connections refer to. Slug-shaped: `^[A-Za-z0-9][A-Za-z0-9_-]*$`. Unlike `NodeId` names, keys may end in `-` or `_` — they are internal identifiers, not published slugs. |
| `node` | yes | The node-*type* id this instance runs (`org/name@semver` or `./name`). |
| `config` | no | Free-form, instance-specific configuration handed to the node at bind time. The SDK does not interpret it. |
| `displayName` | no | Overrides the node type's name in editors. |
| `metadata` | no | Free-form editor data (canvas position, notes). |

A **connection** (`connections[]`) is a directed edge `{ from, to }` between two node keys: the
output of `from` flows to the input of `to`. Edges are single-output/single-input for now.

## Loading

`WorkflowDefinitionSerializer` mirrors `AgentDefinitionSerializer`: `FromJson`, `FromYaml`, and
`Load` (dispatched by file extension) all return `Result<WorkflowDefinition>`, and `ToJson`/`ToYaml`
serialize back out. Loading **parses and then validates the graph**, so a returned definition is
always structurally sound.

```csharp
var result = WorkflowDefinitionSerializer.Load("support-flow.yaml");  // or FromYaml / FromJson
if (result.IsFailure)
{
    Console.Error.WriteLine(result.Error);   // ValidationError (bad document or invalid graph) or ExecutionError (IO)
    return;
}

WorkflowDefinition definition = result.Value;
```

Failure modes, as `Result` values (never exceptions for expected problems):

- **`ValidationError`** — an empty/malformed document, a missing required field, or a graph that
  breaks a structural rule (see below). Carries a `Detail` naming the offending key/edge.
- **`ExecutionError`** — the file could not be read (wraps the `IOException`).

An optional `ILogger` parameter observes loads through source-generated events (`Loaded`,
`ParseFailed`, `ReadFailed`, `Invalid`); omit it and logging is a no-op.

## Validation rules

A workflow is validated as a graph (`WorkflowDefinition.Validate()` → `Result<WorkflowGraph>`, which
`WorkflowGraph.Create` implements). The rules, each producing a `ValidationError` whose `Detail`
names the offender:

1. **Non-empty** — at least one node.
2. **Keys are slug-shaped** — each `key` matches `^[A-Za-z0-9][A-Za-z0-9_-]*$`.
3. **Keys are unique** — no two instances share a key (ordinal comparison).
4. **Endpoints exist** — every connection's `from` and `to` name a declared node key.
5. **No self-loops** — a node may not connect to itself.
6. **No duplicate edges** — the same `(from, to)` pair may not appear twice.
7. **Acyclic** — the graph is a DAG; a cycle is rejected and the error names the nodes that could
   not be ordered.

Node-type ids (`node`) are validated as `NodeId`s during parsing, so an unversioned published id
(`acme/web-search` with no `@version`) fails to load as a `ValidationError` too.

## Graph analysis

`WorkflowGraph` is the validated, analyzable view. It computes, once, everything a runtime or an
editor needs to reason about the shape without executing anything:

| Member | Meaning |
|---|---|
| `Definition` | The definition the graph was built from. |
| `EntryNodes` | Node instances with no inbound connections (in definition order) — where a run begins. Whether an entry node is a *trigger* is a property of its node type, not the graph. |
| `ExecutionOrder` | A stable topological ordering of keys: every node appears after all its upstream nodes. Ties break by definition order, so the order is deterministic (Kahn's algorithm). |
| `GetDownstream(key)` / `GetUpstream(key)` | The immediate successors / predecessors of a node, in definition order. |
| `TryGetNode(key, out node)` | Look up an instance by key. |

```csharp
var graph = definition.Validate().Value;

foreach (var entry in graph.EntryNodes)
    Console.WriteLine($"starts at: {entry.Key}");

foreach (var key in graph.ExecutionOrder)
    Console.WriteLine(key);   // upstream-before-downstream, deterministic
```

## Editor wiring

The JSON Schema at [`../schemas/workflow-definition.schema.json`](../schemas/workflow-definition.schema.json)
governs both formats. Point your editor at it for validation and autocomplete:

```yaml
# yaml-language-server: $schema=../../schemas/workflow-definition.schema.json
```

```json
{ "$schema": "../../schemas/workflow-definition.schema.json" }
```

The schema is deliberately stricter than the loader (it forbids unknown properties, catching typos
while authoring) but cannot express the cross-field graph rules — the loader enforces those. Its
`nodeId` pattern is kept byte-identical to the agent schema's, and its `nodeKey` pattern is kept
equal to `WorkflowNodeDefinition.KeyPattern`, by tests. Working examples live in
[`../samples/workflows/`](../samples/workflows): `support-flow.yaml` (a linear flow) and
`data-pipeline.json` (a diamond-shaped DAG with fan-out and fan-in).

## Packaging

A workflow definition is a package asset like any other: carry the `.yaml`/`.json` file in an
`.ovipkg` and describe it with a `PackagedNodeEntry` of kind `PackagedNodeKind.Workflow`. See
[packaging.md](packaging.md).

## Future work

These are additive to the shape above — existing definitions keep loading unchanged:

- **Output ports / branching** — an optional port on a connection's `from` (n8n's IF true/false,
  Switch outputs). The single-edge model here is the zero-port case.
- **Loops** — the v1 DAG rule rejects cycles; loop constructs are a runtime execution feature with
  their own iteration semantics.
- **Sub-workflows as nodes** — a `node` id that resolves to another workflow, so workflows nest.
- **Binding + execution** — resolving each `node` id to a runnable `INode`, then scheduling the
  `ExecutionOrder` and passing data along edges. This is the runtime's job; the graph analysis here
  is the contract it builds on.
```
