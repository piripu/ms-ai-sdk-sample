using Ovi.Sdk.Nodes;

namespace Ovi.Sdk.Packaging;

/// <summary>
/// Resolves <see cref="OviPackageManifest.Dependencies"/> against an <see cref="IPackageSource"/>,
/// wheel-style: a package declares what it needs without vendoring it, and this loads only the
/// packages actually asked for — never the whole dependency graph up front. Resolved packages are
/// cached (and later disposed together via <see cref="Dispose"/>) so resolving the same id twice
/// doesn't reopen the archive.
/// </summary>
public sealed class OviPackageResolver : IDisposable
{
    private readonly IPackageSource _source;
    private readonly Dictionary<NodeId, OviPackage> _opened = [];

    public OviPackageResolver(IPackageSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    /// <summary>
    /// Opens (and caches) the package for <paramref name="packageId"/>. Does not follow its
    /// dependencies — call <see cref="Resolve"/> again on those ids only when you actually need them.
    /// </summary>
    public Result<OviPackage> Resolve(NodeId packageId)
    {
        ArgumentNullException.ThrowIfNull(packageId);

        if (_opened.TryGetValue(packageId, out var cached))
        {
            return cached;
        }

        if (!_source.TryOpen(packageId, out var package) || package is null)
        {
            return new ResolutionError(
                $"Package '{packageId}' could not be resolved.",
                hint: "Make the package available in the configured IPackageSource (e.g. the directory passed to DirectoryPackageSource).");
        }

        _opened[packageId] = package;
        return package;
    }

    /// <summary>Resolves every direct dependency declared by <paramref name="package"/>'s manifest (one level, not recursive).</summary>
    public IReadOnlyList<Result<OviPackage>> ResolveDependencies(OviPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return [.. package.Manifest.Dependencies.Select(dependency => Resolve(dependency.PackageId))];
    }

    /// <summary>Disposes every package opened through this resolver.</summary>
    public void Dispose()
    {
        foreach (var package in _opened.Values)
        {
            package.Dispose();
        }

        _opened.Clear();
    }
}
