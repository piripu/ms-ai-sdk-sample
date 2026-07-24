using System.Text.Json.Nodes;

namespace Ovi.Sdk.Scripting;

/// <summary>
/// The execution seam of the scripting node. The SDK deliberately ships no implementation — how
/// Python actually runs (CPython subprocess, embedded interpreter, …) is a runtime decision; the
/// candidate designs and their trade-offs are documented in <c>docs/python-script-execution.md</c>.
/// Tests substitute a fake engine, which keeps script nodes atomically testable today.
/// </summary>
public interface IPythonScriptEngine
{
    /// <summary>
    /// Runs one script invocation: call <c>request.Script.EntryPoint</c> with the request's input
    /// and context (decoded to Python values) and return the entry point's result encoded as JSON.
    /// A script error should surface as a thrown exception carrying the Python traceback.
    /// </summary>
    ValueTask<JsonNode?> ExecuteAsync(PythonScriptExecutionRequest request, CancellationToken cancellationToken = default);
}
