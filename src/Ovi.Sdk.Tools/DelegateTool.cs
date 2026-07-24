using Microsoft.Extensions.AI;
using Ovi.Sdk.Nodes;

namespace Ovi.Sdk.Tools;

/// <summary>
/// A tool implemented by a .NET delegate. The delegate's parameters become the function's JSON
/// schema via <see cref="AIFunctionFactory"/>; the tool's id name (slug) becomes the LLM-facing
/// function name and its description becomes the function description.
/// </summary>
public sealed class DelegateTool : Tool
{
    private readonly Delegate _implementation;
    private AIFunction? _function;

    public DelegateTool(NodeDescriptor descriptor, Delegate implementation)
        : base(descriptor)
    {
        ArgumentNullException.ThrowIfNull(implementation);
        _implementation = implementation;
    }

    /// <summary>Convenience factory for the common "id + description + delegate" case.</summary>
    public static DelegateTool Create(NodeId id, string displayName, string description, Delegate implementation) =>
        new(new NodeDescriptor(id, displayName, description), implementation);

    public override AIFunction AsAIFunction() =>
        _function ??= AIFunctionFactory.Create(_implementation, new AIFunctionFactoryOptions
        {
            // The slug name is LLM/tool-call safe; the display name may contain spaces.
            Name = Descriptor.Id.Name,
            Description = Descriptor.Description ?? Descriptor.Name,
        });
}
