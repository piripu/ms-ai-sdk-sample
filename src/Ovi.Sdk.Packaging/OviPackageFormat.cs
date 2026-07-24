namespace Ovi.Sdk.Packaging;

/// <summary>
/// Constants of the <c>.ovipkg</c> package format. A package is an ordinary zip archive with the
/// <c>.ovipkg</c> extension and a <c>manifest.json</c> at its root describing the nodes inside.
/// </summary>
public static class OviPackageFormat
{
    public const string FileExtension = ".ovipkg";

    public const string ManifestEntryName = "manifest.json";

    /// <summary>The manifest schema version this SDK writes.</summary>
    public const int CurrentManifestVersion = 1;
}
