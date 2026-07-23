using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ovi.Sdk.Nodes;

/// <summary>
/// The domain identity of a node, formatted as <c>organization/name@version</c>, e.g.
/// <c>acme/web-search@1.2.0</c>. Built-in nodes use <c>.</c> as their organization
/// (<c>./manual-trigger</c>) and may leave the version implicit; all other nodes must carry an
/// explicit semantic version.
/// </summary>
/// <remarks>
/// Organization and name comparisons are case-insensitive. The same identity scheme is reused for
/// package ids (see <c>Ovi.Sdk.Packaging</c>) so that nodes and the packages that carry them
/// live in one addressing domain.
/// </remarks>
[JsonConverter(typeof(NodeIdJsonConverter))]
public sealed class NodeId : IEquatable<NodeId>
{
    /// <summary>The reserved organization segment (<c>.</c>) that marks a node as built-in.</summary>
    public const string BuiltInOrganization = ".";

    public NodeId(string organization, string name, SemanticVersion? version = null)
    {
        ArgumentNullException.ThrowIfNull(organization);
        ArgumentNullException.ThrowIfNull(name);

        if (!IsValidOrganization(organization))
        {
            throw new ArgumentException(
                $"'{organization}' is not a valid organization. Use '{BuiltInOrganization}' for built-ins or an alphanumeric slug (dots, dashes and underscores allowed inside).",
                nameof(organization));
        }

        if (!IsValidNameSegment(name))
        {
            throw new ArgumentException(
                $"'{name}' is not a valid node name. Use an alphanumeric slug (dots, dashes and underscores allowed inside).",
                nameof(name));
        }

        Organization = organization;
        Name = name;
        Version = version;

        if (version is null && !IsBuiltIn)
        {
            throw new ArgumentException(
                $"Node id '{organization}/{name}' must specify a version; only built-in ids ('{BuiltInOrganization}/{name}') may leave it implicit.",
                nameof(version));
        }
    }

    /// <summary>The publishing organization, or <see cref="BuiltInOrganization"/> for built-ins.</summary>
    public string Organization { get; }

    /// <summary>The node's slug name within its organization (not the display name).</summary>
    public string Name { get; }

    /// <summary>The semantic version; <see langword="null"/> means "implicit", which only built-ins are allowed.</summary>
    public SemanticVersion? Version { get; }

    public bool IsBuiltIn => Organization == BuiltInOrganization;

    public bool HasExplicitVersion => Version is not null;

    /// <summary>Creates a built-in id, e.g. <c>NodeId.BuiltIn("manual-trigger")</c> → <c>./manual-trigger</c>.</summary>
    public static NodeId BuiltIn(string name, SemanticVersion? version = null) => new(BuiltInOrganization, name, version);

    public static NodeId Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return TryParse(text, out var id)
            ? id
            : throw new FormatException(
                $"'{text}' is not a valid node id. Expected 'organization/name@version', or '{BuiltInOrganization}/name[@version]' for built-ins.");
    }

    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out NodeId? id)
    {
        id = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();

        var slash = text.IndexOf('/');
        if (slash < 1 || text.IndexOf('/', slash + 1) >= 0)
        {
            return false;
        }

        var organization = text[..slash];
        var rest = text[(slash + 1)..];

        string name;
        SemanticVersion? version = null;
        var at = rest.IndexOf('@');
        if (at >= 0)
        {
            name = rest[..at];
            if (!SemanticVersion.TryParse(rest[(at + 1)..], out version))
            {
                return false;
            }
        }
        else
        {
            name = rest;
        }

        if (!IsValidOrganization(organization) || !IsValidNameSegment(name))
        {
            return false;
        }

        if (version is null && organization != BuiltInOrganization)
        {
            return false;
        }

        id = new NodeId(organization, name, version);
        return true;
    }

    /// <summary>Returns a copy of this id with a different (or cleared, for built-ins) version.</summary>
    public NodeId WithVersion(SemanticVersion? version) => new(Organization, Name, version);

    /// <summary>
    /// Determines whether two ids refer to the same node, ignoring the version
    /// (case-insensitive organization and name match).
    /// </summary>
    public bool IsSameNode(NodeId? other) =>
        other is not null
        && string.Equals(Organization, other.Organization, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

    public bool Equals(NodeId? other) => IsSameNode(other) && Equals(Version, other!.Version);

    public override bool Equals(object? obj) => Equals(obj as NodeId);

    public override int GetHashCode() => HashCode.Combine(
        StringComparer.OrdinalIgnoreCase.GetHashCode(Organization),
        StringComparer.OrdinalIgnoreCase.GetHashCode(Name),
        Version);

    public static bool operator ==(NodeId? left, NodeId? right) => left?.Equals(right) ?? right is null;

    public static bool operator !=(NodeId? left, NodeId? right) => !(left == right);

    /// <summary>Parses a string as a <see cref="NodeId"/>; throws <see cref="FormatException"/> when invalid.</summary>
    public static implicit operator NodeId(string text) => Parse(text);

    public override string ToString() => Version is null
        ? $"{Organization}/{Name}"
        : $"{Organization}/{Name}@{Version}";

    private static bool IsValidOrganization(string organization) =>
        organization == BuiltInOrganization || IsValidNameSegment(organization);

    private static bool IsValidNameSegment(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (!char.IsAsciiLetterOrDigit(value[0]) || !char.IsAsciiLetterOrDigit(value[^1]))
        {
            return false;
        }

        foreach (var ch in value)
        {
            if (!char.IsAsciiLetterOrDigit(ch) && ch is not ('-' or '_' or '.'))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Serializes <see cref="NodeId"/> as its canonical string form (<c>org/name@version</c>).</summary>
public sealed class NodeIdJsonConverter : JsonConverter<NodeId>
{
    public override NodeId? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (text is null)
        {
            return null;
        }

        return NodeId.TryParse(text, out var id)
            ? id
            : throw new JsonException($"'{text}' is not a valid node id (expected 'org/name@version' or './name[@version]').");
    }

    public override void Write(Utf8JsonWriter writer, NodeId value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
