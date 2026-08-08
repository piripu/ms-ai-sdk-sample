using Ovi.Sdk.Nodes;

namespace Ovi.Sdk.Packaging;

/// <summary>
/// Where <see cref="OviPackageResolver"/> looks up a package by id. The seam a runtime plugs a real
/// package registry/CDN into later; <see cref="DirectoryPackageSource"/> is the one implementation
/// this SDK ships (a local directory of <c>.ovipkg</c> files), enough for a single-machine runtime
/// and for tests.
/// </summary>
public interface IPackageSource
{
    /// <summary>Attempts to open the package identified by <paramref name="packageId"/>.</summary>
    bool TryOpen(NodeId packageId, out OviPackage? package);
}

/// <summary>
/// Resolves a package id to a file in a directory, using the naming convention
/// <c>{organization}__{name}[@{version}].ovipkg</c>. Only opens the exact file the id maps to — it
/// never scans the directory — so resolving one package never touches unrelated files sitting next
/// to it.
/// </summary>
public sealed class DirectoryPackageSource : IPackageSource
{
    private readonly string _directory;

    public DirectoryPackageSource(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    /// <summary>The file name <paramref name="packageId"/> maps to under this source's directory.</summary>
    public static string ToFileName(NodeId packageId)
    {
        ArgumentNullException.ThrowIfNull(packageId);
        var version = packageId.Version is null ? string.Empty : $"@{packageId.Version}";
        return $"{packageId.Organization}__{packageId.Name}{version}{OviPackageFormat.FileExtension}";
    }

    /// <inheritdoc />
    public bool TryOpen(NodeId packageId, out OviPackage? package)
    {
        ArgumentNullException.ThrowIfNull(packageId);

        var path = Path.Combine(_directory, ToFileName(packageId));
        if (!File.Exists(path))
        {
            package = null;
            return false;
        }

        package = OviPackage.Open(path);
        return true;
    }
}
