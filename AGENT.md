# Agent instructions — Ovi SDK

You are working on the **Ovi SDK**: C# contracts for building workflow nodes (in the spirit of
n8n) that a separate runtime will later compose into workflows. This file grounds how to work in
this repository. The deep material lives in [`docs/`](docs) — read
[`docs/architecture.md`](docs/architecture.md) before changing any public surface.

## Build and test

```bash
dotnet build          # requires the .NET 10 SDK (pinned in global.json)
dotnet test           # xunit suite in tests/Ovi.Sdk.Tests — must be green before every commit
```

If `dotnet` is missing: `curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0`
and add `~/.dotnet` to `PATH`.

## Repository map

| Path | What it is | External deps |
|---|---|---|
| `src/Ovi.Sdk.Nodes` | Core contracts: `INode`/`INode<TIn,TOut>`, `Node<,>`, `DelegateNode`, `NodeId`, `Result<T>` + errors, `WorkflowExecutionContext` + features, state stores | **Microsoft.Extensions.*.Abstractions only** |
| `src/Ovi.Sdk.Tools` | `ITool`, sealed `Tool` (`FromDelegate`/`FromAIFunction`), `ToolCatalog` | Microsoft.Extensions.AI |
| `src/Ovi.Sdk.Agents` | `AgentNode`, `AgentDefinition` (YAML/JSON), `AgentChatFeature`, `MultipartHttpChatClient` | Microsoft.Extensions.AI, YamlDotNet |
| `src/Ovi.Sdk.Triggers` | Chat/webhook/schedule/manual trigger nodes | Cronos |
| `src/Ovi.Sdk.Scripting` | `PythonScriptNode` + `IPythonScriptEngine` seam (no engine by design) | none |
| `src/Ovi.Sdk.Packaging` | `.ovipkg` format (zip + `manifest.json`) | none |
| `tests/Ovi.Sdk.Tests` | One suite for everything; doubles as usage documentation | xunit |
| `schemas/`, `samples/` | Agent-definition JSON Schema + working YAML/JSON samples | — |

## Rules that must hold (tests enforce several of them)

1. **SDK ships contracts, not runtime implementations.** No schedulers, script engines, state
   persistence, or package loading in `Ovi.Sdk.*` — those belong to the future runtime
   (`Ovi.Runtime.*`). Deliberate incompleteness is a feature: `MultipartHttpChatClient.ParseResponse`
   and `IPythonScriptEngine` stay unimplemented until their wire contracts are decided. Do not
   "finish" them.
2. **`Ovi.Sdk.Nodes` references only `Microsoft.Extensions.*.Abstractions` packages** (today:
   Logging.Abstractions). Never an implementation package, never a third-party dependency — it is
   the de-facto abstractions package.
3. **Composition over inheritance.**
   - Run capabilities are typed **features** on `WorkflowExecutionContext`
     (`SetFeature`/`GetFeature`); never subclass the context to add a capability (subclasses like
     `AgentWorkflowExecutionContext` are sealed sugar that attach a feature).
   - The typed node contract is `INode<TIn,TOut>`; `Node<,>` is an optional base;
     `Node.Create(descriptor, (input, ctx) => …)` composes nodes from delegates. Concrete nodes are
     `sealed` — customization goes through mapping functions, delegate nodes, and decorators, not
     `virtual`/`override`.
   - Tools are composed (`Tool.FromDelegate`, `Tool.FromAIFunction`), never subclassed.
4. **Identity is a domain concept.** `NodeId` = `org/name@semver`, built-ins = `./name[@semver]`
   (only built-ins may omit the version); org/name compare case-insensitively. The regex in
   `schemas/agent-definition.schema.json` must stay in lockstep with `NodeId.TryParse` —
   `SchemaTests` fails if they drift.
5. **State is serializable by construction.** `IStateStore` values serialize to JSON on write;
   three scopes (workflow/global/instance). Never add non-serializable state to these stores;
   ephemeral things go on context features or `Properties`.
6. **Every node is atomically testable.** Anything you add must be executable with
   `WorkflowExecutionContext.CreateBuilder()` plus fakes (see `tests/.../Support`) — no engine, no
   network. Add tests in that style; test names are underscored sentences
   (`Chat_triggers_normalize_webhook_requests`).
7. **Resolution idiom** for external dependencies of a node (chat client, script engine):
   fixed-on-node → context feature → `RuntimeServices` → a `ResolutionError` failure result
   naming all three (with a fix-it hint).
8. **One JSON pipeline.** `OviJson` carries the conventions (camelCase, case-insensitive, camel
   enum strings); YAML is bridged onto the JSON node model (`YamlJsonBridge`) so both formats parse
   identically. New declarative formats must reuse this pipeline.
9. **Microsoft.Extensions.AI alignment.** Tools surface as `AIFunction`; agents accept any
   `IChatClient`; MCP arrives later via `Tool.FromAIFunction` — never invent a parallel abstraction.
10. **Expected failures are results.** Execution APIs return `Result<T>` with the closed error set
    (`ValidationError` / `ResolutionError` / `ExecutionError`; stable codes). Exceptions are only
    for programmer errors (argument validation) and cancellation; the untyped `INode` bridge
    converts mismatches and unhandled throws into failures. Don't grow the error set casually —
    it is shaped for a future discriminated union.
11. **Logging is built in and silent.** Resolve loggers via `context.GetLogger<T>()` (NullLogger
    fallback), emit events through source-generated `[LoggerMessage]` partials (Debug for normal
    flow, Warning for failure results, Error for exceptions), and keep execution tracing in the
    `WithLogging` decorator — never chatty base-class logging.

## Conventions

- File-scoped namespaces; XML docs on the significant public surface (`GenerateDocumentationFile`
  is on, CS1591 suppressed); records use `required` + `[SetsRequiredMembers]` constructors.
- Central package management: versions live only in `Directory.Packages.props`.
- When you change public surface or formats, update in the same commit: `README.md`, the relevant
  `docs/*.md`, `schemas/` + `samples/` if the definition format moved, and tests.

## Workflow

- Develop on the designated `claude/...` branch; never commit straight to `main`.
- Run `dotnet test` before every commit; commit messages explain *why*, not just *what*.
- If a PR for the branch was already merged, restart the branch from `origin/main` and rebase any
  unmerged commits onto it before pushing (force-with-lease).
