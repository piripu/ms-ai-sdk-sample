using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ovi.Sdk.Operators;

/// <summary>
/// A Semantic Versioning 2.0.0 version (<see href="https://semver.org"/>):
/// <c>MAJOR.MINOR.PATCH[-PRERELEASE][+BUILD]</c>.
/// </summary>
/// <remarks>
/// Equality considers every component, including <see cref="BuildMetadata"/>. Ordering via
/// <see cref="CompareTo"/> follows semver precedence rules, which ignore build metadata.
/// </remarks>
[JsonConverter(typeof(SemanticVersionJsonConverter))]
public sealed record SemanticVersion : IComparable<SemanticVersion>
{
    public SemanticVersion(int major, int minor, int patch, string? prerelease = null, string? buildMetadata = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(major);
        ArgumentOutOfRangeException.ThrowIfNegative(minor);
        ArgumentOutOfRangeException.ThrowIfNegative(patch);

        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = NormalizeIdentifiers(prerelease, nameof(prerelease));
        BuildMetadata = NormalizeIdentifiers(buildMetadata, nameof(buildMetadata));
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    /// <summary>The dot-separated prerelease identifiers (e.g. <c>beta.1</c>), or <see langword="null"/> for a release version.</summary>
    public string? Prerelease { get; }

    /// <summary>The dot-separated build metadata (the part after <c>+</c>), or <see langword="null"/> when absent.</summary>
    public string? BuildMetadata { get; }

    public bool IsPrerelease => Prerelease is not null;

    public static SemanticVersion Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return TryParse(text, out var version)
            ? version
            : throw new FormatException($"'{text}' is not a valid semantic version (expected 'major.minor.patch[-prerelease][+build]').");
    }

    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out SemanticVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var span = text.Trim();
        string? buildMetadata = null;
        string? prerelease = null;

        var plus = span.IndexOf('+');
        if (plus >= 0)
        {
            buildMetadata = span[(plus + 1)..];
            span = span[..plus];
        }

        var dash = span.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = span[(dash + 1)..];
            span = span[..dash];
        }

        var parts = span.Split('.');
        if (parts.Length != 3
            || !TryParseNumericComponent(parts[0], out var major)
            || !TryParseNumericComponent(parts[1], out var minor)
            || !TryParseNumericComponent(parts[2], out var patch))
        {
            return false;
        }

        if ((prerelease is not null && !AreValidIdentifiers(prerelease))
            || (buildMetadata is not null && !AreValidIdentifiers(buildMetadata)))
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch, prerelease, buildMetadata);
        return true;
    }

    /// <inheritdoc />
    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var result = Major.CompareTo(other.Major);
        if (result != 0)
        {
            return result;
        }

        result = Minor.CompareTo(other.Minor);
        if (result != 0)
        {
            return result;
        }

        result = Patch.CompareTo(other.Patch);
        return result != 0 ? result : ComparePrerelease(Prerelease, other.Prerelease);
    }

    public override string ToString()
    {
        var builder = new StringBuilder()
            .Append(Major).Append('.')
            .Append(Minor).Append('.')
            .Append(Patch);

        if (Prerelease is not null)
        {
            builder.Append('-').Append(Prerelease);
        }

        if (BuildMetadata is not null)
        {
            builder.Append('+').Append(BuildMetadata);
        }

        return builder.ToString();
    }

    private static string? NormalizeIdentifiers(string? value, string parameterName)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return AreValidIdentifiers(value)
            ? value
            : throw new ArgumentException(
                $"'{value}' is not a valid dot-separated identifier sequence (identifiers must be non-empty and use only [0-9A-Za-z-]).",
                parameterName);
    }

    private static bool AreValidIdentifiers(string value)
    {
        foreach (var identifier in value.Split('.'))
        {
            if (identifier.Length == 0)
            {
                return false;
            }

            foreach (var ch in identifier)
            {
                if (!char.IsAsciiLetterOrDigit(ch) && ch != '-')
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool TryParseNumericComponent(string value, out int result) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result)
        && (value.Length == 1 || value[0] != '0');

    private static int ComparePrerelease(string? left, string? right)
    {
        if (left is null && right is null)
        {
            return 0;
        }

        // A release version has higher precedence than any of its prereleases.
        if (left is null)
        {
            return 1;
        }

        if (right is null)
        {
            return -1;
        }

        var leftIds = left.Split('.');
        var rightIds = right.Split('.');
        var length = Math.Max(leftIds.Length, rightIds.Length);

        for (var i = 0; i < length; i++)
        {
            if (i >= leftIds.Length)
            {
                return -1;
            }

            if (i >= rightIds.Length)
            {
                return 1;
            }

            var leftIsNumeric = int.TryParse(leftIds[i], NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
            var rightIsNumeric = int.TryParse(rightIds[i], NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);

            var result = (leftIsNumeric, rightIsNumeric) switch
            {
                (true, true) => leftNumber.CompareTo(rightNumber),
                (true, false) => -1, // numeric identifiers always have lower precedence
                (false, true) => 1,
                (false, false) => string.CompareOrdinal(leftIds[i], rightIds[i]),
            };

            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }
}

/// <summary>Serializes <see cref="SemanticVersion"/> as its canonical string form.</summary>
public sealed class SemanticVersionJsonConverter : JsonConverter<SemanticVersion>
{
    public override SemanticVersion? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (text is null)
        {
            return null;
        }

        return SemanticVersion.TryParse(text, out var version)
            ? version
            : throw new JsonException($"'{text}' is not a valid semantic version.");
    }

    public override void Write(Utf8JsonWriter writer, SemanticVersion value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
