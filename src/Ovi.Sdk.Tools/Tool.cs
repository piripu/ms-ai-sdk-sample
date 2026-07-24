using Microsoft.Extensions.AI;
using Ovi.Sdk.Nodes;

namespace Ovi.Sdk.Tools;

/// <summary>
/// A tool: Ovi node identity composed with a Microsoft.Extensions.AI <see cref="AIFunction"/>.
/// Instances are composed, not subclassed: <see cref="FromDelegate(NodeDescriptor, Delegate)"/>
/// turns a .NET delegate into a tool, and <see cref="FromAIFunction"/> wraps an existing
/// <see cref="AIFunction"/> — which is also the MCP path, since an MCP client tool is an
/// <see cref="AIFunction"/>.
/// </summary>
public sealed class Tool : ITool
{
    private readonly Func<AIFunction> _functionFactory;
    private AIFunction? _function;

    private Tool(NodeDescriptor descriptor, Func<AIFunction> functionFactory)
    {
        Descriptor = descriptor;
        _functionFactory = functionFactory;
    }

    /// <inheritdoc />
    public NodeDescriptor Descriptor { get; }

    /// <summary>The tool's identity (<c>org/name@version</c>).</summary>
    public NodeId Id => Descriptor.Id;

    /// <summary>The tool's display name.</summary>
    public string Name => Descriptor.Name;

    /// <summary>The tool's description, also used as the LLM-facing function description.</summary>
    public string? Description => Descriptor.Description;

    /// <inheritdoc />
    public AIFunction AsAIFunction() => _function ??= _functionFactory();

    /// <summary>
    /// Composes a tool from a .NET delegate. The delegate's parameters become the function's JSON
    /// schema via <see cref="AIFunctionFactory"/>; the id's slug name becomes the LLM-facing
    /// function name and the description becomes the function description.
    /// </summary>
    public static Tool FromDelegate(NodeDescriptor descriptor, Delegate implementation)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(implementation);

        return new Tool(descriptor, () => AIFunctionFactory.Create(implementation, new AIFunctionFactoryOptions
        {
            // The slug name is LLM/tool-call safe; the display name may contain spaces.
            Name = descriptor.Id.Name,
            Description = descriptor.Description ?? descriptor.Name,
        }));
    }

    /// <summary>Convenience overload for the common "id + description + delegate" case.</summary>
    public static Tool FromDelegate(NodeId id, string displayName, string description, Delegate implementation) =>
        FromDelegate(new NodeDescriptor(id, displayName, description), implementation);

    /// <summary>Wraps an existing <see cref="AIFunction"/> (e.g. an MCP client tool) in Ovi tool identity.</summary>
    public static Tool FromAIFunction(NodeDescriptor descriptor, AIFunction function)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(function);

        return new Tool(descriptor, () => function);
    }
}
