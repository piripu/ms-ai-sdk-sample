using Microsoft.Extensions.AI;
using Ovi.Sdk.Operators;

namespace Ovi.Sdk.Tools;

/// <summary>Base class for Ovi tools. See <see cref="ITool"/>.</summary>
public abstract class Tool : ITool
{
    protected Tool(OperatorDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        Descriptor = descriptor;
    }

    /// <inheritdoc />
    public OperatorDescriptor Descriptor { get; }

    /// <summary>The tool's identity (<c>org/name@version</c>).</summary>
    public OperatorId Id => Descriptor.Id;

    /// <summary>The tool's display name.</summary>
    public string Name => Descriptor.Name;

    /// <summary>The tool's description, also used as the LLM-facing function description.</summary>
    public string? Description => Descriptor.Description;

    /// <inheritdoc />
    public abstract AIFunction AsAIFunction();
}
