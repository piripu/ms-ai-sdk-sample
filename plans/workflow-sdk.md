# Workflow SDK: n8n-style definitions in JSON/YAML

Add `Ovi.Sdk.Workflows`: the declarative model of a workflow — node instances + the connections
between them, n8n-style — authorable in JSON or YAML, validated and graph-analyzable without any
runtime. This is the piece that ties the existing node SDK together: a workflow contains individual
nodes and defines how each is connected.

## Decisions (recorded per this repo's convention; user grounded the use case as "similar to n8n, JSON or YAML for now")

- **D1 — Definitions only, no engine.** Consistent with SDK doctrine: the SDK owns the workflow
  *model* (parse, validate, analyze); executing a workflow (scheduling, data passing between nodes,
  binding node-type ids to `INode` instances) is runtime work. Graph analysis (entry nodes,
  execution order) ships now so runtimes and tests have something real to build on.
- **D2 — Shape**: workflow `id` uses the existing `NodeId` domain (`org/name@semver`); `nodes` are
  instances `{key, node, config?, metadata?}` where `key` is a workflow-unique slug, `node` is the
  node-type `NodeId`, `config` is free-form JSON for the instance, `metadata` is free-form editor
  data (position, notes — the n8n "position" concern, kept out of the SDK's semantics);
  `connections` are `{from, to}` pairs of keys. Single-output/single-input edges for now — ports
  (n8n's IF true/false branches) are a documented follow-up, additive to this shape.
- **D3 — Workflows are DAGs in v1.** Cycles are rejected by validation (n8n's loop constructs are a
  runtime feature; documented as future work). Entry nodes = nodes with no inbound connections;
  trigger-ness is a property of the node type, not the workflow shape.
- **D4 — One serialization pipeline.** YAML bridges onto the JSON model exactly like agent
  definitions. The internal `YamlJsonBridge` moves to a shared-source file
  (`src/Shared/YamlJsonBridge.cs`, namespace `Ovi.Sdk.Internal`) compiled into both Agents and
  Workflows — no new public surface, no cross-package coupling.
- **D5 — House idioms apply**: loading returns `Result<WorkflowDefinition>` (ValidationError with
  parser/semantic detail, ExecutionError for IO), optional `ILogger` parameters with
  source-generated events, JSON Schema + samples + lockstep tests, `PackagedNodeKind.Workflow` so
  packages can carry workflow assets.

## For Future Agents
As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to
`Complete` and write its **Phase Summary**; run the phase's **Verification Plan** and record the
result before moving on. When all phases are done, fill in **Final Recap** and **Deployment Plan**.
Branch: `claude/csharp-sdk-agents-tools-bopdpg` (restarted from `main` @ bd5b56d). If the PR for
this branch gets merged mid-work, restart the branch from `origin/main` and rebase unmerged commits.
Read AGENT.md rules first — especially Results (10), logging (11), schema lockstep (4).

## Phase 1: Project + definition model + shared YAML bridge
Status: Complete

- [x] Move `src/Ovi.Sdk.Agents/YamlJsonBridge.cs` to `src/Shared/YamlJsonBridge.cs`
      (namespace `Ovi.Sdk.Internal`); link it into Agents via `<Compile Include>`; fix the
      serializer's using; Agents still builds.
- [x] New project `src/Ovi.Sdk.Workflows` (refs: Nodes project; YamlDotNet +
      Logging.Abstractions packages; links the shared bridge); add to solution.
- [x] `WorkflowDefinition` record: required `Id` (NodeId), required `Name`, `Description?`,
      required `Nodes` (IReadOnlyList<WorkflowNodeDefinition>), `Connections` (default []),
      `Metadata?` (JsonObject); `[SetsRequiredMembers]` ctor; `Validate()` delegating to
      `WorkflowGraph.Create`.
- [x] `WorkflowNodeDefinition` record: required `Key` (slug `[A-Za-z0-9][A-Za-z0-9_-]*`),
      required `Node` (NodeId), `Config?`/`Metadata?` (JsonObject), `DisplayName?`.
- [x] `WorkflowConnection` record: required `From`, required `To`.
- [x] `WorkflowDefinitionSerializer`: `FromJson`/`FromYaml`/`Load` → `Result<WorkflowDefinition>`
      (parse via OviJson, then semantic validation via `Validate()`), `ToJson`/`ToYaml`; optional
      `ILogger` params; `WorkflowLog` `[LoggerMessage]` partial (Loaded/ParseFailed/ReadFailed/Invalid).

### Verification Plan
- `dotnet build` — clean, 0 warnings, 7 src projects.

### Phase Summary
Done. The YAML→JSON bridge now lives at `src/Shared/YamlJsonBridge.cs` (namespace `Ovi.Sdk.Internal`),
linked as `internal` into both Agents and Workflows via `<Compile Include=".../Shared/YamlJsonBridge.cs"
Link="Internal/YamlJsonBridge.cs" />` — one deserialization convention, no new public surface, no
cross-package reference. `src/Ovi.Sdk.Workflows` (refs Nodes + YamlDotNet + Logging.Abstractions) holds
the three records and the serializer; added to `Ovi.Sdk.slnx`. `WorkflowDefinition.Validate()` delegates
to `WorkflowGraph.Create` (Phase 2). The serializer parses via `OviJson`, then runs graph validation
before returning success, so a loaded definition is always structurally sound; `WorkflowLog` adds a 4th
event (`Invalid`) beyond the agent serializer's three. Key-shape lives on `WorkflowNodeDefinition`
(`KeyPattern` const + `IsValidKey`, source-generated regex) so the schema (Phase 3) and tests (Phase 4)
share one source of truth.
**Verification:** `dotnet build Ovi.Sdk.slnx` → Build succeeded, 0 Warning(s), 0 Error(s) (7 src projects + tests).

## Phase 2: Graph validation + analysis
Status: Complete

- [x] `WorkflowGraph.Create(definition)` → `Result<WorkflowGraph>` validating: ≥1 node; keys
      unique (ordinal) and slug-shaped; connection endpoints exist; no self-loops; no duplicate
      edges; no cycles (Kahn's algorithm; error lists the keys stuck in the cycle). All failures are
      `ValidationError` with the offending detail.
- [x] Graph surface: `Definition`, `EntryNodes` (no inbound), `ExecutionOrder` (topological,
      stable), `GetDownstream(key)` / `GetUpstream(key)` (direct neighbors), `TryGetNode(key)`.

### Verification Plan
- `dotnet build` — clean.

### Phase Summary
Done in `src/Ovi.Sdk.Workflows/WorkflowGraph.cs`. `Create` validates in order: non-empty; each key
slug-shaped then unique (ordinal dict `TryAdd`); each connection's `From`/`To` exist, are not equal
(self-loop), and are not a duplicate edge (HashSet of tuples); then a topological sort. Cycles are
detected by Kahn's algorithm (`TopologicalSort`) — if fewer keys are emitted than exist, the missing
keys are exactly the nodes a cycle prevented from scheduling, and the `ValidationError` names them.
Stability: the ready set is a `SortedSet<int>` of definition indices, so ties break by definition order
and `ExecutionOrder` is deterministic. `EntryNodes` = nodes with no upstream, in definition order.
`GetDownstream`/`GetUpstream` return immediate neighbors (definition order, empty for unknown keys);
`TryGetNode` looks up by key. Correctness (order respects edges, each failure mode) is proven by the
Phase 4 tests.
**Verification:** `dotnet build Ovi.Sdk.slnx` → Build succeeded, 0 Warning(s), 0 Error(s).

## Phase 3: Schema, samples, packaging kind
Status: Complete

- [x] `schemas/workflow-definition.schema.json` (draft 2020-12): `$defs` for `nodeId` (same regex
      as the agent schema), `nodeKey`, node entry, connection; required `id`/`name`/`nodes`;
      `additionalProperties: false` with `$schema` + `metadata` allowances.
- [x] Samples: `samples/workflows/support-flow.yaml` (chat trigger → triage agent → python script;
      yaml-language-server modeline) and `samples/workflows/data-pipeline.json` (`$schema`
      property; schedule trigger → script fan-out) — ids consistent with `samples/agents/`.
- [x] Add `Workflow` to `PackagedNodeKind`; update the kinds row in `docs/packaging.md`.

### Verification Plan
- `python3 -c "import json; json.load(open('schemas/workflow-definition.schema.json'))"` — parses.

### Phase Summary
Done. `schemas/workflow-definition.schema.json` (draft 2020-12) reuses the agent schema's `nodeId`
pattern **verbatim** (verified byte-identical) and adds `nodeKey` (`^[A-Za-z0-9][A-Za-z0-9_-]*$`, matching
`WorkflowNodeDefinition.KeyPattern`), `nodeDefinition`, and `connection` `$defs`; required
`id`/`name`/`nodes`, `additionalProperties: false`, with `$schema` and `metadata` allowed. Samples:
`samples/workflows/support-flow.yaml` is a linear chat-trigger → `acme/support-triage@0.1.0` → python
script (with the yaml-language-server modeline); `samples/workflows/data-pipeline.json` is a diamond DAG
(schedule trigger → extract → two transforms → load) carrying the `$schema` property — exercising
fan-out/fan-in for the graph tests. Built-in node-type ids used (`./chat-trigger`, `./schedule-trigger`,
`./python-script`) match the real descriptors in Triggers/Scripting. `PackagedNodeKind.Workflow` added;
packaging.md kinds row updated.
**Verification:** schema + `data-pipeline.json` parse as JSON; `support-flow.yaml` parses as YAML (3 nodes);
agent vs workflow `nodeId` patterns identical; Packaging builds clean.

## Phase 4: Tests
Status: Complete

- [x] `WorkflowDefinitionTests`: YAML load with full asserts; YAML/JSON equivalence; ToJson/ToYaml
      round-trips; `Load` by extension incl. unsupported-extension and missing-file failures;
      missing required metadata → ValidationError.
- [x] `WorkflowGraphTests`: entry nodes; execution order respects edges; downstream/upstream;
      duplicate key, dangling connection, self-loop, duplicate edge, and cycle each →
      ValidationError naming the offender; single-node workflow valid.
- [x] `WorkflowSchemaTests`: schema shape; nodeId pattern matches agent schema's; samples load
      through the serializer; sample ids/keys match schema patterns (link schema + samples into the
      test csproj output like the agent ones).
- [x] Full suite green.

### Verification Plan
- `dotnet test` — 0 failed; total ≥ 130.

### Phase Summary
Done. Three test classes added (`WorkflowDefinitionTests`, `WorkflowGraphTests`, `WorkflowSchemaTests`),
and the test csproj now references the Workflows project and copies the workflow schema + `samples/workflows/*`
to output. Coverage: serialization (full YAML asserts, YAML/JSON equivalence, JSON+YAML round-trips,
load-by-extension with unsupported-extension and missing-file failures, malformed docs, load-time graph
validation), graph analysis (entry nodes, edge-respecting + stable execution order, downstream/upstream,
`TryGetNode`, `Create`≡`Validate`, `Definition` identity), and every failure mode (empty, duplicate key,
non-slug key, dangling from/to, self-loop, duplicate edge, pure cycle, cycle-downstream-of-entry) each
asserting the `ValidationError` and its `Detail`. Schema tests keep the `nodeId` pattern byte-identical to
the agent schema and the `nodeKey` pattern equal to `WorkflowNodeDefinition.KeyPattern`, and load both
samples through the serializer. One authored test expectation was corrected (keys deliberately may end in
`-`/`_`, unlike NodeId slugs) and a positive test now pins that behavior.
**Verification:** `dotnet test Ovi.Sdk.slnx` → Passed! Failed: 0, Passed: 166, Skipped: 0, Total: 166
(was 111 before this work; +55 workflow tests).

## Phase 5: Docs + finalize
Status: Complete

- [x] `docs/workflow-definitions.md`: format spec (fields, key rules, DAG rule), validation list,
      graph analysis API, editor wiring (modeline/$schema), future work (ports/branching, loops,
      sub-workflows as nodes, binding + execution in the runtime).
- [x] README: Workflows project row + dependency diagram + "Workflows" concept section with the
      YAML example; docs index entry.
- [x] `docs/architecture.md`: Workflows in the layer diagram + a "Workflows" concept subsection.
- [x] AGENT.md: repository-map row for `src/Ovi.Sdk.Workflows` (+ `src/Shared` row, rule 8 note).
- [x] Fill **Final Recap** + **Deployment Plan**; `dotnet test` green; commit + push; PR on request.

### Verification Plan
- `dotnet test` green; README/docs mention workflow definitions; plan fully checked off.

### Phase Summary
Done. New `docs/workflow-definitions.md` (format, field tables, key rules, the 7 validation rules,
graph-analysis API, editor wiring, future work). README gained a Workflows project-table row, a
`Workflows ──▶ Nodes` edge in the dependency diagram, a "Workflows" concept section with the YAML +
graph snippet, a docs-index entry, an updated test count (83→166), and a rewritten "Deliberately
deferred" split (runtime execution vs. additive shape follow-ups). `docs/architecture.md` gained the
Workflows layer-diagram row, an updated intro/boundary paragraph, and a "Workflows" concept subsection.
AGENT.md gained repository-map rows for `src/Ovi.Sdk.Workflows` and `src/Shared`, and rule 8 now points
at the shared bridge.
**Verification:** `dotnet build` 0 warnings; `dotnet test` → 166 passed / 0 failed; all plan boxes checked.

## Final Recap
Added **`Ovi.Sdk.Workflows`**, the declarative n8n-style workflow layer, on top of the existing node SDK
— definitions only, no engine, consistent with SDK doctrine.

- **Model** (`WorkflowDefinition` / `WorkflowNodeDefinition` / `WorkflowConnection`): a workflow is an
  identity (`NodeId`) plus node *instances* (workflow-unique slug `key`, node-type `NodeId`, free-form
  `config`/`metadata`, optional `displayName`) plus `connections` (`from`/`to` keys). Authorable in JSON
  or YAML through the same bridged `OviJson` pipeline as agents.
- **Graph** (`WorkflowGraph`): `Validate()`/`Create()` enforce the v1 DAG rules (non-empty; slug-shaped,
  unique keys; endpoints exist; no self-loops; no duplicate edges; acyclic via Kahn's algorithm) as
  `Result<WorkflowGraph>` with `ValidationError` detail naming each offender, and expose `EntryNodes`, a
  stable `ExecutionOrder`, `GetUpstream`/`GetDownstream`, and `TryGetNode`.
- **Serialization** (`WorkflowDefinitionSerializer`): `FromJson`/`FromYaml`/`Load` → `Result` (parse then
  graph-validate), `ToJson`/`ToYaml`, optional `ILogger` with 4 source-generated events.
- **Shared bridge**: `YamlJsonBridge` moved to `src/Shared/` (namespace `Ovi.Sdk.Internal`), compiled
  `internal` into both Agents and Workflows — one convention, no new public surface, no cross-package
  reference.
- **Schema + samples**: `schemas/workflow-definition.schema.json` (draft 2020-12; `nodeId` byte-identical
  to the agent schema, `nodeKey` equal to `WorkflowNodeDefinition.KeyPattern`); `samples/workflows/`
  support-flow.yaml + data-pipeline.json.
- **Packaging**: `PackagedNodeKind.Workflow` added; packaging.md updated.
- **Tests**: +55 (111 → **166**, 0 failed), across serialization, graph analysis, every failure mode, and
  schema/sample lockstep.
- **Docs**: new workflow-definitions.md; README, architecture.md, AGENT.md updated in the same change.

House idioms held throughout: `Result<T>` + closed error set, optional `ILogger` + `[LoggerMessage]`,
one JSON/YAML pipeline, schema/`NodeId` lockstep enforced by tests, sealed types, `net10.0`, 0 warnings.

## Deployment Plan
This SDK ships contracts (no runtime), so "deployment" is landing the branch and (later) publishing
packages.

1. **Verify locally** (.NET 10 SDK; if missing, `curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir $HOME/.dotnet` and add `$HOME/.dotnet` to `PATH`):
   - `dotnet build Ovi.Sdk.slnx` → 0 warnings, 0 errors (7 src projects + tests).
   - `dotnet test Ovi.Sdk.slnx` → 166 passed, 0 failed.
2. **Commit** on `claude/csharp-sdk-agents-tools-bopdpg` (restarted from `origin/main` @ bd5b56d):
   new `src/Ovi.Sdk.Workflows/**`, `src/Shared/YamlJsonBridge.cs`, `schemas/workflow-definition.schema.json`,
   `samples/workflows/**`, `docs/workflow-definitions.md`, test files, and the edits to the Agents csproj +
   serializer, `Ovi.Sdk.slnx`, packaging enum/doc, README, architecture.md, AGENT.md, and this plan.
3. **Push** with `git push -u origin claude/csharp-sdk-agents-tools-bopdpg` (retry with backoff on
   network errors).
4. **PR** only when the user asks. If the branch's prior PR was already merged, this is a *new* PR from
   the restarted branch — do not reuse a merged one.
5. **Post-merge (future, not part of this change)**: NuGet packaging/versioning for `Ovi.Sdk.Workflows`
   rides the same `Directory.Build.props` metadata as the other projects when the repo starts publishing.
