using Microsoft.Extensions.AI;
using Ovi.Sdk.Operators;

namespace Ovi.Sdk.Tools;

/// <summary>
/// Wraps an existing <see cref="AIFunction"/> in Ovi tool identity. This is the adapter that will
/// carry MCP tools: an MCP client tool is an <see cref="AIFunction"/>, so it can be given an Ovi id
/// and attached to agents without any further glue.
/// </summary>
public sealed class AIFunctionTool : Tool
{
    private readonly AIFunction _function;

    public AIFunctionTool(OperatorDescriptor descriptor, AIFunction function)
        : base(descriptor)
    {
        ArgumentNullException.ThrowIfNull(function);
        _function = function;
    }

    public override AIFunction AsAIFunction() => _function;
}
