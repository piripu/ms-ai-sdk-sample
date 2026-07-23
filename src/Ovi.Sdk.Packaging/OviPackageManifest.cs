using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Ovi.Sdk.Operators;

namespace Ovi.Sdk.Packaging;

/// <summary>What a packaged entry is, from the runtime's point of view.</summary>
public enum PackagedOperatorKind
{
    Operator,
    Agent,
    Tool,
    Trigger,
}

/// <summary>One operator carried by a package: its descriptor metadata, kind, and (optionally) the
/// package-relative asset that defines it (e.g. an agent's YAML file).</summary>
public sealed record PackagedOperatorEntry
{
    public PackagedOperatorEntry()
    {
    }

    [SetsRequiredMembers]
    public PackagedOperatorEntry(OperatorDescriptor descriptor, PackagedOperatorKind kind, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        Id = descriptor.Id;
        Name = descriptor.Name;
        Description = descriptor.Description;
        Kind = kind;
        Path = path;
    }

    public required OperatorId Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public PackagedOperatorKind Kind { get; init; } = PackagedOperatorKind.Operator;

    /// <summary>The package-relative path of the asset defining this operator (e.g. <c>agents/researcher.yaml</c>), when file-defined.</summary>
    public string? Path { get; init; }

    public OperatorDescriptor ToDescriptor() => new(Id, Name, Description);
}

/// <summary>
/// The <c>manifest.json</c> at the root of every <c>.ovipkg</c>. The package id lives in the same
/// identity domain as operator ids (<c>org/name@version</c>).
/// </summary>
public sealed record OviPackageManifest
{
    public OviPackageManifest()
    {
    }

    [SetsRequiredMembers]
    public OviPackageManifest(OperatorId packageId, string name, string? description = null)
    {
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        PackageId = packageId;
        Name = name;
        Description = description;
    }

    public int ManifestVersion { get; init; } = OviPackageFormat.CurrentManifestVersion;

    /// <summary>The package's identity (<c>org/name@version</c>, same domain concept as operator ids).</summary>
    public required OperatorId PackageId { get; init; }

    /// <summary>The package's display name.</summary>
    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>The operators this package carries.</summary>
    public IReadOnlyList<PackagedOperatorEntry> Operators { get; init; } = [];

    public string ToJson() => JsonSerializer.Serialize(this, OviJson.IndentedOptions);

    public static OviPackageManifest FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize<OviPackageManifest>(json, OviJson.DefaultOptions)
            ?? throw new InvalidOperationException("The JSON document does not contain a package manifest.");
    }
}
