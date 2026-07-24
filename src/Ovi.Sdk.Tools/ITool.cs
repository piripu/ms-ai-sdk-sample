using Microsoft.Extensions.AI;
using Ovi.Sdk.Nodes;

namespace Ovi.Sdk.Tools;

/// <summary>
/// A capability that can be attached to an agent. Tools carry the same identity metadata as nodes
/// (<see cref="Descriptor"/>) and expose themselves to the model layer as a
/// Microsoft.Extensions.AI <see cref="AIFunction"/>.
/// </summary>
/// <remarks>
/// Because <see cref="AIFunction"/> is also the shape MCP tools take (an MCP client tool is an
/// <see cref="AIFunction"/> subclass), this contract lets MCP-backed tools plug in later without any
/// change to agents: wrap the MCP function in an <see cref="AIFunctionTool"/>.
/// </remarks>
public interface ITool
{
    /// <summary>The tool's identity and display metadata (same scheme as nodes).</summary>
    NodeDescriptor Descriptor { get; }

    /// <summary>The Microsoft.Extensions.AI function form of this tool, as handed to chat clients.</summary>
    AIFunction AsAIFunction();
}
