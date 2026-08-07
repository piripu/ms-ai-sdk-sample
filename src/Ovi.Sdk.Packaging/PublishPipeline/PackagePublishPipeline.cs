namespace Ovi.Sdk.Packaging.PublishPipeline;

/// <summary>
/// An ordered sequence of <see cref="IPackagePublishStep"/>s that together publish a package.
/// <see cref="Default"/> reproduces exactly what <c>OviPackageBuilder.Save</c> did before this
/// pipeline existed (validate node/file references, write the manifest, write the files); future
/// capabilities — computing a content hash, signing the manifest — are additional steps on a custom
/// pipeline, not new parameters on <c>Save</c>. Those two steps are deliberately not implemented yet
/// (no hash algorithm or signing scheme has been decided), the same "stays open until the runtime
/// decides" precedent as <c>IPythonScriptEngine</c>.
/// </summary>
public sealed class PackagePublishPipeline
{
    private readonly IReadOnlyList<IPackagePublishStep> _steps;

    public PackagePublishPipeline(IReadOnlyList<IPackagePublishStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        _steps = steps;
    }

    /// <summary>The pipeline <c>OviPackageBuilder.Save</c> uses when no pipeline is specified explicitly.</summary>
    public static PackagePublishPipeline Default { get; } = new(
    [
        new ValidateNodeFileReferencesStep(),
        new WriteManifestStep(),
        new WriteEntriesStep(),
    ]);

    /// <summary>Runs every step in order against <paramref name="context"/>.</summary>
    public void Run(PackagePublishContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var step in _steps)
        {
            step.Execute(context);
        }
    }
}
