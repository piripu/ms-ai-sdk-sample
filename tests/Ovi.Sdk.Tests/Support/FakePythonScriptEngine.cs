using System.Text.Json.Nodes;
using Ovi.Sdk.Scripting;

namespace Ovi.Sdk.Tests.Support;

/// <summary>A scriptable IPythonScriptEngine test double that records every request it receives.</summary>
internal sealed class FakePythonScriptEngine : IPythonScriptEngine
{
    private readonly Func<PythonScriptExecutionRequest, JsonNode?> _respond;

    public FakePythonScriptEngine(Func<PythonScriptExecutionRequest, JsonNode?>? respond = null) =>
        _respond = respond ?? (_ => null);

    public List<(PythonScriptExecutionRequest Request, CancellationToken CancellationToken)> Calls { get; } = [];

    public ValueTask<JsonNode?> ExecuteAsync(PythonScriptExecutionRequest request, CancellationToken cancellationToken = default)
    {
        Calls.Add((request, cancellationToken));
        return ValueTask.FromResult(_respond(request));
    }
}
