# Authoring nodes

Practical recipes for building on the Ovi SDK. Everything here runs without a workflow engine —
that is the point: build a node, execute it atomically, test it with fakes. The
[test suite](../tests/Ovi.Sdk.Tests) contains a working example of every recipe.

## A plain node

Derive from the convenience base when the node has real behavior worth a class:

```csharp
sealed class UppercaseNode() : Node<string, string>(
    new NodeDescriptor(NodeId.BuiltIn("uppercase"), "Uppercase", "Uppercases the input text."))
{
    public override ValueTask<string> ExecuteAsync(string input, WorkflowExecutionContext context)
        => ValueTask.FromResult(input.ToUpperInvariant());
}
```

Or compose one from a delegate — no type required:

```csharp
var reverse = Node.Create(
    new NodeDescriptor(NodeId.BuiltIn("reverse"), "Reverse", "Reverses text."),
    (string input, WorkflowExecutionContext _) => new string(input.Reverse().ToArray()));
```

Execute either atomically:

```csharp
var context = WorkflowExecutionContext.CreateBuilder().Build();
var result = await new UppercaseNode().ExecuteAsync("hello", context); // "HELLO"
```

Conventions: give built-ins `./slug` ids; published nodes need `org/name@semver`. Seal concrete
node classes — customization belongs in constructor parameters, mapping functions, or decorators,
not subclassing.

## Cross-cutting behavior: decorators

Because the contract is `INode<TIn,TOut>`, cross-cutting concerns wrap nodes instead of living in a
base class:

```csharp
sealed class RetryNode<TIn, TOut>(INode<TIn, TOut> inner, int attempts)
    : Node<TIn, TOut>(inner.Descriptor)
{
    public override async ValueTask<TOut> ExecuteAsync(TIn input, WorkflowExecutionContext context)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return await inner.ExecuteAsync(input, context); }
            catch when (attempt < attempts) { }
        }
    }
}
```

## Using context state and features

```csharp
public override ValueTask<int> ExecuteAsync(int input, WorkflowExecutionContext context)
{
    var seen = context.WorkflowState.GetValueOrDefault<int>("seen");   // per-run
    context.WorkflowState.SetValue("seen", seen + 1);                  // serialized on write
    context.CancellationToken.ThrowIfCancellationRequested();
    var custom = context.GetFeature<MyCapability>();                   // typed capability, may be null
    ...
}
```

State values must be JSON-serializable (writes fail fast otherwise). Anything ephemeral goes on a
feature or `Properties`, never into a state store.

## A tool

```csharp
var echo = Tool.FromDelegate("./echo", "Echo", "Echoes text back.", (string text) => text);
var mcp  = Tool.FromAIFunction(descriptor, someAIFunction); // e.g. an MCP client tool
```

The id slug (`echo`) becomes the LLM-facing function name; the description becomes the function
description. Register tools in a `ToolCatalog` to resolve agent-definition references (exact id
match first, then version-insensitive).

## An agent

In code:

```csharp
var agent = new AgentNode(
    new NodeDescriptor(NodeId.Parse("acme/assistant@1.0.0"), "Assistant"),
    instructions: "You are terse.",
    tools: [echo],
    chatClient: client);            // optional — see resolution below
```

Or declaratively (see [../schemas/agent-definition.schema.json](../schemas/agent-definition.schema.json)
and [../samples/agents/](../samples/agents)):

```yaml
# yaml-language-server: $schema=../../schemas/agent-definition.schema.json
id: acme/researcher@1.0.0
name: Researcher
instructions: |
  You are a careful research assistant.
tools:
  - ./echo
  - id: acme/web-search@2.0.0
    options: { maxResults: 5 }
```

```csharp
var definition = AgentDefinitionSerializer.Load("researcher.yaml");
var agent = AgentNode.FromDefinition(definition, catalog, chatClient);
```

**Chat client resolution** (the standard idiom): fixed on the node → the context's
`AgentChatFeature` → `IChatClient` in `RuntimeServices`. To give a run conversation memory, attach
the feature:

```csharp
var context = WorkflowExecutionContext.CreateBuilder()
    .WithFeature(new AgentChatFeature(client))      // ChatHistory accumulates across turns
    .Build();
```

`AgentWorkflowExecutionContext` is equivalent sugar for the same feature.

## A trigger

Triggers are nodes whose input is the external event. All are manually fireable:

```csharp
var payload = ChatTriggerNode.FromWebhook(new WebhookRequest
{
    Body = """{"message": "Hello!", "sessionId": "s-1"}""",
});
var output = await new ChatTriggerNode().FireAsync(payload, context);

var schedule = Schedule.FromCron("0 9 * * MON-FRI");
schedule.GetNextOccurrence(DateTimeOffset.UtcNow);          // plan without a scheduler
await new ScheduleTriggerNode(schedule).FireAsync(ScheduleTick.Manual(), context);
```

## A Python script node

```csharp
var node = new PythonScriptNode(PythonScript.FromFile("totals.py"));
var output = await node.ExecuteAsync(input, context);   // engine resolved node → services
```

The script defines `def run(input, context)`; the SDK ships no engine — tests substitute a fake
`IPythonScriptEngine`, and the engine plan lives in
[python-script-execution.md](python-script-execution.md).

## Testing checklist for a new node type

1. Execute it via `WorkflowExecutionContext.CreateBuilder()` — typed path and, if it matters, the
   untyped `INode` path.
2. Fake every external collaborator (see `tests/.../Support/FakeChatClient`,
   `FakePythonScriptEngine`) and assert on the recorded calls.
3. Cover the resolution error (no collaborator anywhere → clear message).
4. If it reads/writes state, assert through `ToJsonObject()` round-trips.
5. If it has a declarative form, add schema + samples + lockstep tests (see `SchemaTests`).
