using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Ovi.Sdk.Operators;

namespace Ovi.Sdk.Tools;

/// <summary>Resolves <see cref="ToolReference"/>s (from agent definitions) to concrete tools.</summary>
public interface IToolCatalog
{
    bool TryResolve(ToolReference reference, [NotNullWhen(true)] out ITool? tool);
}

/// <summary>
/// A simple in-memory <see cref="IToolCatalog"/>. Resolution first looks for an exact id match
/// (including version), then falls back to a version-insensitive match on organization + name.
/// </summary>
public sealed class ToolCatalog : IToolCatalog, IEnumerable<ITool>
{
    private readonly Dictionary<OperatorId, ITool> _tools = [];

    /// <summary>Adds (or replaces) a tool, keyed by its id.</summary>
    public ToolCatalog Add(ITool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        _tools[tool.Descriptor.Id] = tool;
        return this;
    }

    public bool TryResolve(ToolReference reference, [NotNullWhen(true)] out ITool? tool)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (_tools.TryGetValue(reference.Id, out tool))
        {
            return true;
        }

        tool = _tools.Values.FirstOrDefault(candidate => candidate.Descriptor.Id.IsSameOperator(reference.Id));
        return tool is not null;
    }

    public IEnumerator<ITool> GetEnumerator() => _tools.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
