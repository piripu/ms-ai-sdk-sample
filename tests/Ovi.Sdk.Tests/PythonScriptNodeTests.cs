using System.Text.Json.Nodes;
using Ovi.Sdk.Nodes;
using Ovi.Sdk.Scripting;
using Ovi.Sdk.Tests.Support;
using Xunit;

namespace Ovi.Sdk.Tests;

public class PythonScriptNodeTests
{
    private const string Code = """
        def run(input, context):
            return {"total": sum(input["amounts"])}
        """;

    [Fact]
    public async Task Delegates_to_the_engine_with_script_input_and_context_snapshot()
    {
        var engine = new FakePythonScriptEngine(request => new JsonObject { ["total"] = 6 });
        var op = new PythonScriptNode(PythonScript.FromCode(Code), engine: engine);

        var context = WorkflowExecutionContext.CreateBuilder().WithWorkflow("wf-7", "Totals").Build();
        context.WorkflowState.SetValue("attempt", 2);

        var input = new JsonObject { ["amounts"] = new JsonArray(1, 2, 3) };
        var output = await op.ExecuteAsync(input, context);

        Assert.Equal(6, output!["total"]!.GetValue<int>());

        var (request, cancellationToken) = Assert.Single(engine.Calls);
        Assert.Same(op.Script, request.Script);
        Assert.Equal("run", request.Script.EntryPoint);
        Assert.Same(input, request.Input);
        Assert.Equal("wf-7", request.Context.Workflow.WorkflowId);
        Assert.Equal(2, request.Context.WorkflowState["attempt"]!.GetValue<int>());
        Assert.Equal(context.CancellationToken, cancellationToken);
    }

    [Fact]
    public async Task Resolves_the_engine_from_runtime_services()
    {
        var engine = new FakePythonScriptEngine(_ => JsonValue.Create("ok"));
        var op = new PythonScriptNode(PythonScript.FromCode(Code));
        var context = WorkflowExecutionContext.CreateBuilder()
            .WithService<IPythonScriptEngine>(engine)
            .Build();

        var output = await op.ExecuteAsync(null, context);

        Assert.Equal("ok", output!.GetValue<string>());
        Assert.Single(engine.Calls);
    }

    [Fact]
    public async Task Fails_clearly_when_no_engine_is_available()
    {
        var op = new PythonScriptNode(PythonScript.FromCode(Code));
        var context = WorkflowExecutionContext.CreateBuilder().Build();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => op.ExecuteAsync(null, context).AsTask());
        Assert.Contains("Python engine", error.Message);
        Assert.Contains("docs/python-script-execution.md", error.Message);
    }

    [Fact]
    public void Scripts_validate_their_code_and_entry_point()
    {
        Assert.Throws<ArgumentException>(() => PythonScript.FromCode("   "));
        Assert.Throws<ArgumentException>(() => PythonScript.FromCode(Code, entryPoint: "not an identifier"));
        Assert.Throws<ArgumentException>(() => PythonScript.FromCode(Code, entryPoint: "1starts_with_digit"));

        var custom = PythonScript.FromCode(Code, entryPoint: "handle_event");
        Assert.Equal("handle_event", custom.EntryPoint);
    }

    [Fact]
    public void Scripts_load_from_files()
    {
        var directory = Directory.CreateTempSubdirectory("ovi-script-tests-");
        try
        {
            var path = Path.Combine(directory.FullName, "totals.py");
            File.WriteAllText(path, Code);

            var script = PythonScript.FromFile(path);

            Assert.Equal(Code, script.Code);
            Assert.Equal(path, script.SourcePath);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void The_context_snapshot_serializes_with_camel_case()
    {
        var context = WorkflowExecutionContext.CreateBuilder().WithWorkflow("wf-1").Build();
        context.GlobalState.SetValue("region", "eu");

        var json = PythonScriptContextSnapshot.Capture(context).ToJsonObject();

        Assert.Equal("wf-1", json["workflow"]!["workflowId"]!.GetValue<string>());
        Assert.Equal("eu", json["globalState"]!["region"]!.GetValue<string>());
        Assert.NotNull(json["workflowState"]);
        Assert.NotNull(json["instanceState"]);
    }

    [Fact]
    public void Script_nodes_are_ordinary_nodes()
    {
        var op = new PythonScriptNode(PythonScript.FromCode(Code));

        Assert.IsAssignableFrom<INode>(op);
        Assert.True(op.Id.IsBuiltIn);
        Assert.Equal("python-script", op.Id.Name);
        Assert.Equal(typeof(JsonNode), op.InputType);
        Assert.Equal(typeof(JsonNode), op.OutputType);
    }
}
