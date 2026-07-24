using Ovi.Sdk.Nodes;
using Ovi.Sdk.Workflows;
using Xunit;

namespace Ovi.Sdk.Tests;

public class WorkflowGraphTests
{
    private static WorkflowNodeDefinition Node(string key) => new(key, NodeId.BuiltIn("python-script"));

    private static WorkflowConnection Edge(string from, string to) => new(from, to);

    private static WorkflowDefinition Workflow(IReadOnlyList<WorkflowNodeDefinition> nodes, params WorkflowConnection[] edges)
        => new(NodeId.Parse("acme/test-flow@1.0.0"), "Test Flow", nodes, edges);

    /// <summary>A diamond DAG: trigger → extract → {sales, inventory} → load.</summary>
    private static WorkflowDefinition Diamond() => Workflow(
        [Node("trigger"), Node("extract"), Node("sales"), Node("inventory"), Node("load")],
        Edge("trigger", "extract"),
        Edge("extract", "sales"),
        Edge("extract", "inventory"),
        Edge("sales", "load"),
        Edge("inventory", "load"));

    [Fact]
    public void A_single_node_workflow_is_valid()
    {
        var result = Workflow([Node("only")]).Validate();

        Assert.True(result.IsSuccess);
        Assert.Equal(["only"], result.Value.ExecutionOrder);
        Assert.Equal(["only"], result.Value.EntryNodes.Select(node => node.Key));
    }

    [Fact]
    public void An_empty_workflow_is_a_validation_error()
    {
        var result = Workflow([]).Validate();

        Assert.True(result.IsFailure);
        Assert.IsType<ValidationError>(result.Error);
    }

    [Fact]
    public void Entry_nodes_are_those_without_inbound_connections()
    {
        var graph = Workflow(
            [Node("t1"), Node("t2"), Node("merge")],
            Edge("t1", "merge"),
            Edge("t2", "merge")).Validate().Value;

        Assert.Equal(["t1", "t2"], graph.EntryNodes.Select(node => node.Key));
    }

    [Fact]
    public void Execution_order_places_every_node_after_its_upstream()
    {
        var graph = Diamond().Validate().Value;
        var order = graph.ExecutionOrder;

        foreach (var connection in graph.Definition.Connections)
        {
            Assert.True(
                order.ToList().IndexOf(connection.From) < order.ToList().IndexOf(connection.To),
                $"{connection.From} should come before {connection.To}");
        }
    }

    [Fact]
    public void Execution_order_is_stable_and_definition_ordered()
    {
        var definition = Diamond();

        var first = definition.Validate().Value.ExecutionOrder;
        var second = definition.Validate().Value.ExecutionOrder;

        Assert.Equal(["trigger", "extract", "sales", "inventory", "load"], first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Downstream_and_upstream_return_direct_neighbors_in_definition_order()
    {
        var graph = Diamond().Validate().Value;

        Assert.Equal(["sales", "inventory"], graph.GetDownstream("extract"));
        Assert.Equal(["sales", "inventory"], graph.GetUpstream("load"));
        Assert.Empty(graph.GetDownstream("load"));
        Assert.Empty(graph.GetUpstream("trigger"));
    }

    [Fact]
    public void TryGetNode_finds_nodes_and_rejects_unknown_keys()
    {
        var graph = Diamond().Validate().Value;

        Assert.True(graph.TryGetNode("extract", out var node));
        Assert.Equal("extract", node!.Key);
        Assert.False(graph.TryGetNode("ghost", out var missing));
        Assert.Null(missing);
    }

    [Fact]
    public void The_graph_exposes_the_definition_it_was_built_from()
    {
        var definition = Diamond();

        var graph = definition.Validate().Value;

        Assert.Same(definition, graph.Definition);
    }

    [Fact]
    public void Create_and_Validate_produce_the_same_graph()
    {
        var definition = Diamond();

        var viaValidate = definition.Validate().Value;
        var viaCreate = WorkflowGraph.Create(definition).Value;

        Assert.Equal(viaValidate.ExecutionOrder, viaCreate.ExecutionOrder);
    }

    [Fact]
    public void Duplicate_keys_are_a_validation_error_naming_the_key()
    {
        var result = Workflow([Node("a"), Node("a")]).Validate();

        Assert.True(result.IsFailure);
        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal("a", error.Detail);
        Assert.Contains("Duplicate", error.Message);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("-leading")]
    [InlineData("with/slash")]
    [InlineData("under_score.dot")]
    public void Non_slug_keys_are_validation_errors_naming_the_key(string key)
    {
        var result = Workflow([Node(key)]).Validate();

        Assert.True(result.IsFailure);
        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(key, error.Detail);
    }

    [Theory]
    [InlineData("trailing-")]
    [InlineData("trailing_")]
    [InlineData("a-b_c")]
    public void Keys_may_end_in_hyphen_or_underscore(string key)
    {
        // Instance keys are internal identifiers, not published slugs, so unlike NodeId names they
        // are allowed to end in '-' or '_'. This is deliberate; keep it.
        var result = Workflow([Node(key)]).Validate();

        Assert.True(result.IsSuccess);
        Assert.Equal([key], result.Value.ExecutionOrder);
    }

    [Fact]
    public void A_connection_from_an_unknown_key_is_a_validation_error()
    {
        var result = Workflow([Node("a")], Edge("ghost", "a")).Validate();

        Assert.True(result.IsFailure);
        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal("ghost", error.Detail);
    }

    [Fact]
    public void A_connection_to_an_unknown_key_is_a_validation_error()
    {
        var result = Workflow([Node("a")], Edge("a", "ghost")).Validate();

        Assert.True(result.IsFailure);
        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal("ghost", error.Detail);
    }

    [Fact]
    public void A_self_loop_is_a_validation_error()
    {
        var result = Workflow([Node("a")], Edge("a", "a")).Validate();

        Assert.True(result.IsFailure);
        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal("a", error.Detail);
        Assert.Contains("self-loop", error.Message);
    }

    [Fact]
    public void A_duplicate_edge_is_a_validation_error()
    {
        var result = Workflow([Node("a"), Node("b")], Edge("a", "b"), Edge("a", "b")).Validate();

        Assert.True(result.IsFailure);
        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("Duplicate connection", error.Message);
    }

    [Fact]
    public void A_cycle_is_a_validation_error_naming_the_stuck_nodes()
    {
        var result = Workflow(
            [Node("a"), Node("b"), Node("c")],
            Edge("a", "b"),
            Edge("b", "c"),
            Edge("c", "a")).Validate();

        Assert.True(result.IsFailure);
        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Contains("cycle", error.Message);
        Assert.Equal("a,b,c", error.Detail);
    }

    [Fact]
    public void A_cycle_downstream_of_an_entry_names_only_the_stuck_nodes()
    {
        // entry → a → b → a: 'entry' schedules, but 'a' and 'b' are trapped in the cycle.
        var result = Workflow(
            [Node("entry"), Node("a"), Node("b")],
            Edge("entry", "a"),
            Edge("a", "b"),
            Edge("b", "a")).Validate();

        Assert.True(result.IsFailure);
        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal("a,b", error.Detail);
        Assert.DoesNotContain("entry", error.Detail!);
    }
}
