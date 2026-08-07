using System.IO.Compression;

namespace Ovi.Sdk.Packaging.PublishPipeline;

/// <summary>What a <see cref="IPackagePublishStep"/> reads and writes while a package is being published.</summary>
public sealed class PackagePublishContext
{
    public PackagePublishContext(OviPackageManifest manifest, IReadOnlyDictionary<string, byte[]> files, ZipArchive archive)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(archive);

        Manifest = manifest;
        Files = files;
        Archive = archive;
    }

    /// <summary>The manifest being published.</summary>
    public OviPackageManifest Manifest { get; }

    /// <summary>The asset files added to the builder, keyed by their normalized package-relative path.</summary>
    public IReadOnlyDictionary<string, byte[]> Files { get; }

    /// <summary>The zip archive being written; open for writing until the pipeline finishes.</summary>
    public ZipArchive Archive { get; }
}

/// <summary>
/// One stage of publishing a package. <see cref="OviPackageBuilder.Save(Stream)"/> runs the
/// <see cref="PackagePublishPipeline.Default"/> pipeline — validate, write the manifest, write the
/// files — and this is the extension point for capabilities the wire format doesn't define yet
/// (content hashing, signing): add a step to a custom <see cref="PackagePublishPipeline"/> rather
/// than forking the builder. Steps run in order and throw to fail the publish, matching the rest of
/// this package's (still exception-based, not <see cref="Ovi.Sdk.Nodes.Result{T}"/>) API surface.
/// </summary>
public interface IPackagePublishStep
{
    void Execute(PackagePublishContext context);
}
