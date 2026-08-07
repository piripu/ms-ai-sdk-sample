using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Ovi.Sdk.Nodes;

namespace Ovi.Sdk.Packaging;

/// <summary>What a packaged entry is, from the runtime's point of view.</summary>
public enum PackagedNodeKind
{
    Node,
    Agent,
    Tool,
    Trigger,
    Script,
    Workflow,
}

/// <summary>One node carried by a package: its descriptor metadata, kind, and (optionally) the
/// package-relative asset that defines it (e.g. an agent's YAML file).</summary>
public sealed record PackagedNodeEntry
{
    public PackagedNodeEntry()
    {
    }

    [SetsRequiredMembers]
    public PackagedNodeEntry(NodeDescriptor descriptor, PackagedNodeKind kind, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        Id = descriptor.Id;
        Name = descriptor.Name;
        Description = descriptor.Description;
        Kind = kind;
        Path = path;
    }

    public required NodeId Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public PackagedNodeKind Kind { get; init; } = PackagedNodeKind.Node;

    /// <summary>The package-relative path of the asset defining this node (e.g. <c>agents/researcher.yaml</c>), when file-defined.</summary>
    public string? Path { get; init; }

    public NodeDescriptor ToDescriptor() => new(Id, Name, Description);
}

/// <summary>
/// A reference to another <c>.ovipkg</c> this package needs at load time. Like a wheel's
/// <c>Requires-Dist</c>, this declares the dependency without vendoring it — packages stay
/// self-contained zips, and resolving <see cref="PackageId"/> to an actual package is a loader
/// concern (<c>OviPackageResolver</c>), not something baked into the archive.
/// </summary>
public sealed record PackageReference
{
    public PackageReference()
    {
    }

    [SetsRequiredMembers]
    public PackageReference(NodeId packageId)
    {
        ArgumentNullException.ThrowIfNull(packageId);
        PackageId = packageId;
    }

    /// <summary>The dependency's package identity (<c>org/name@version</c>).</summary>
    public required NodeId PackageId { get; init; }

    public static implicit operator PackageReference(NodeId packageId) => new(packageId);
}

/// <summary>
/// The <c>manifest.json</c> at the root of every <c>.ovipkg</c>. The package id lives in the same
/// identity domain as node ids (<c>org/name@version</c>).
/// </summary>
public sealed record OviPackageManifest
{
    public OviPackageManifest()
    {
    }

    [SetsRequiredMembers]
    public OviPackageManifest(NodeId packageId, string name, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        PackageId = packageId;
        Name = name;
        Description = description;
    }

    public int ManifestVersion { get; init; } = OviPackageFormat.CurrentManifestVersion;

    /// <summary>The package's identity (<c>org/name@version</c>, same domain concept as node ids).</summary>
    public required NodeId PackageId { get; init; }

    /// <summary>The package's display name.</summary>
    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>The nodes this package carries.</summary>
    public IReadOnlyList<PackagedNodeEntry> Nodes { get; init; } = [];

    /// <summary>
    /// Other packages this one depends on, declared (not vendored) so a loader resolves and opens
    /// only the packages it actually needs — the "only load whichever is necessary" property a
    /// wheel-style dependency list makes possible. Empty for a self-contained package.
    /// </summary>
    public IReadOnlyList<PackageReference> Dependencies { get; init; } = [];

    /// <summary>
    /// Which <see cref="Nodes"/> entry (by id) is "the" workflow or agent this package runs when
    /// there's no other context — how a runtime finds "the default workflow" in a package. Optional:
    /// when null and exactly one <see cref="PackagedNodeKind.Workflow"/> or
    /// <see cref="PackagedNodeKind.Agent"/> entry exists, that one is the implicit default.
    /// </summary>
    public NodeId? DefaultEntry { get; init; }

    public string ToJson() => JsonSerializer.Serialize(this, OviJson.IndentedOptions);

    public static OviPackageManifest FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<OviPackageManifest>(json, OviJson.DefaultOptions)
            ?? throw new InvalidOperationException("The JSON document does not contain a package manifest.");
    }
}
