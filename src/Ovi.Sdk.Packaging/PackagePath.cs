namespace Ovi.Sdk.Packaging;

/// <summary>Normalizes and validates package-relative entry paths.</summary>
internal static class PackagePath
{
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var normalized = path.Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        if (normalized.Length == 0)
        {
            throw new ArgumentException("The package path must not be empty.", nameof(path));
        }

        var segments = normalized.Split('/');
        if (segments.Any(segment => segment is "" or "." or ".."))
        {
            throw new ArgumentException($"'{path}' is not a valid package path: empty, '.' and '..' segments are not allowed.", nameof(path));
        }

        return normalized;
    }
}
