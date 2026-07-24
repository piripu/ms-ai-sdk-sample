# Python script execution — design plan

`Ovi.Sdk.Scripting` ships the **contract** for Python scripting nodes; it deliberately ships no
execution engine. This document is the plan for adding one later: the script contract engines must
honor, the seam they plug into, the candidate engine designs with trade-offs, and the security and
packaging considerations that come with running user scripts.

## The script contract (fixed now)

A script targets one entry point, `run` by default:

```python
def run(input, context):
    orders = input["orders"]
    return {
        "total": sum(o["amount"] for o in orders),
        "workflow": context["workflow"]["workflowId"],
    }
```

- **`input`** — the node's input (`JsonNode?` on the .NET side), decoded to plain Python values.
- **`context`** — a read-only dict: the JSON form of `PythonScriptContextSnapshot`:

  ```json
  {
    "workflow": { "workflowId": "…", "runId": "…", "workflowName": "…", "startedAt": "…" },
    "workflowState": { },
    "globalState": { },
    "instanceState": { }
  }
  ```

- **Return value** — encoded back to JSON and becomes the node's output. Returning `None`
  yields a null output.
- **Errors** — an uncaught Python exception fails the node; the engine must surface the
  traceback in the thrown .NET exception.

JSON ↔ Python mapping is the obvious one: object ↔ `dict`, array ↔ `list`, string ↔ `str`,
number ↔ `int`/`float`, boolean ↔ `bool`, null ↔ `None`.

State is **read-only** in v1: scripts observe snapshots. Writing back is a planned extension — the
natural shape is an additional return channel (e.g. `return output, {"workflowState": {…}}`) or a
`context["state"].set(...)` API recorded as a mutation list in the engine response, applied by the
runtime after validation. Deciding this belongs with the runtime work, not the contract.

## The seam (fixed now)

`PythonScriptNode : Node<JsonNode?, JsonNode?>` delegates to:

```csharp
public interface IPythonScriptEngine
{
    ValueTask<JsonNode?> ExecuteAsync(PythonScriptExecutionRequest request, CancellationToken cancellationToken = default);
}
```

Resolution order mirrors the agent node's chat-client resolution: engine fixed on the node →
`IPythonScriptEngine` in `RuntimeServices` → clear error. Because the engine is an interface taking
a fully serializable request, script nodes are **atomically testable today** with a fake engine,
and every engine below is a drop-in.

## Candidate engines (choose at runtime-build time)

| | CPython subprocess | Python.NET (pythonnet) | IronPython | Pyodide/WASM |
|---|---|---|---|---|
| Python fidelity | Full CPython, any version | Full CPython (installed) | ~Python 3.4 language | CPython in WASM |
| pip / C extensions | Yes | Yes | No | Pure-Python + bundled wheels |
| Extra install needed | Python on host | Python on host | None (NuGet) | None (bundle runtime) |
| Isolation | Process boundary (strong) | In-process (weak) | In-process (weak) | Sandbox (strongest) |
| Cancellation | Kill process (reliable) | Hard (GIL, interrupts) | Thread abort issues | Terminate instance |
| Startup cost per call | ~30–100 ms | Near zero after init | Near zero | High first-load |
| Complexity | Low | Medium (GIL, lifecycle) | Low | High |

### 1. CPython subprocess (recommended default)

Launch `python3` (configurable interpreter path, e.g. a venv) per invocation. The engine writes a
composite program: the user code verbatim, then a bootstrap that speaks JSON over stdio:

```python
# --- user script (verbatim) ---
# def run(input, context): ...

# --- bootstrap appended by the engine ---
if __name__ == "__main__":
    import json, sys, traceback
    _request = json.load(sys.stdin)
    try:
        _entry = globals()[_request["entryPoint"]]
        _output = _entry(_request.get("input"), _request.get("context"))
        json.dump({"ok": True, "output": _output}, sys.stdout)
    except Exception:
        json.dump({"ok": False, "error": traceback.format_exc()}, sys.stdout)
```

Engine responsibilities: write the request (`entryPoint`, `input`, `context`) to stdin; read the
single response object from stdout; map `ok: false` to a `PythonScriptException` carrying the
traceback; capture stderr for diagnostics; kill the process on `CancellationToken` cancellation and
enforce an optional wall-clock timeout; non-zero exit without a response is an engine error
(interpreter missing, syntax error — stderr has the details). Options object:
interpreter path, working directory, environment variables (scrubbed by default), timeout.

Why default: real Python (full stdlib + pip), strongest practical isolation for cheap, trivially
cancellable, and the JSON-over-stdio protocol is language-agnostic — a future JavaScript or shell
script node can reuse it wholesale.

### 2. Python.NET (pythonnet)

Embeds the installed CPython in-process. Wins when call latency dominates (thousands of small
invocations) or scripts must exchange live .NET objects. Costs: `PythonEngine` global lifecycle,
GIL management, one interpreter version per process, cancellation is cooperative at best, and a
misbehaving script can take the host down. Fit for a trusted-scripts, high-throughput runtime tier.

### 3. IronPython

Pure managed NuGet dependency — nothing to install, runs wherever .NET runs. Language level is
~Python 3.4 and there is no pip/C-extension ecosystem, which contradicts most users' expectations
of "Python". Fit only as a zero-dependency fallback for simple expression-style scripts.

### 4. Pyodide / WASM (future)

CPython compiled to WebAssembly, executed in a WASM sandbox (e.g. wasmtime). The strongest isolation
story for untrusted marketplace scripts, at the cost of runtime bundle size and slower cold starts.
Worth revisiting when third-party `.ovipkg` scripts become a reality.

## Security considerations (for any engine)

Scripts are arbitrary code; the runtime must decide who is trusted to author them.

- **Process/sandbox isolation** for untrusted scripts (subprocess minimum; WASM ideal).
- **Resource limits**: wall-clock timeout (engine option + `CancellationToken`), memory/CPU limits
  (cgroups/job objects at the runtime tier).
- **Environment hygiene**: spawn with a scrubbed environment; never inherit runtime secrets.
- **Filesystem/network policy**: default the working directory to a scratch dir; restrict egress at
  the runtime tier if scripts are untrusted.
- **State integrity**: engines receive snapshots, not live stores — a hostile script cannot corrupt
  runtime state; future write-back goes through validated mutation lists.

## Packaging

A script node ships in an `.ovipkg` as its `.py` asset plus a manifest entry with
`kind: "script"` (`PackagedNodeKind.Script`) and `path` pointing at the asset:

```json
{ "id": "acme/total-orders@1.0.0", "name": "Total Orders", "kind": "script", "path": "scripts/total_orders.py" }
```

Python dependencies for subprocess engines can travel as a conventional `requirements.txt` next to
the script; installing them into the interpreter/venv is a runtime responsibility.

## Testing story

- **Now**: substitute `IPythonScriptEngine` with a fake (see `tests/.../FakePythonScriptEngine`) —
  script nodes execute atomically like every other node.
- **With a real engine**: the engine gets its own integration suite (real `python3`, golden
  request/response cases, cancellation, timeout, traceback surfacing), and the node tests stay
  engine-free.
