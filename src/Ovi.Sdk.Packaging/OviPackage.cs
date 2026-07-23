using System.IO.Compression;
using Ovi.Sdk.Nodes;

namespace Ovi.Sdk.Packaging;

/// <summary>
/// Reads a <c>.ovipkg</c> package: exposes its <see cref="Manifest"/> and the files inside. Because
/// the format is plain zip, any zip tooling can inspect a package too — the extension rename is the
/// whole trick.
/// </summary>
public sealed class OviPackage : IDisposable
{
    private readonly ZipArchive _archive;
    private readonly Stream? _ownedStream;

    private OviPackage(ZipArchive archive, Stream? ownedStream, OviPackageManifest manifest)
    {
        _archive = archive;
        _ownedStream = ownedStream;
        Manifest = manifest;
    }

    /// <summary>The parsed root <c>manifest.json</c>.</summary>
    public OviPackageManifest Manifest { get; }

    /// <summary>All file entries in the package, including the manifest.</summary>
    public IReadOnlyList<string> Entries =>
        [.. _archive.Entries.Where(entry => !entry.FullName.EndsWith('/')).Select(entry => entry.FullName)];

    public static OviPackage Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
        try
        {
            return Open(stream, leaveOpen: false);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public static OviPackage Open(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen);
        try
        {
            var manifestEntry = archive.GetEntry(OviPackageFormat.ManifestEntryName)
                ?? throw new InvalidDataException(
                    $"The archive is not an Ovi package: it has no root '{OviPackageFormat.ManifestEntryName}'.");

            using var reader = new StreamReader(manifestEntry.Open());
            var manifest = OviPackageManifest.FromJson(reader.ReadToEnd());

            return new OviPackage(archive, leaveOpen ? null : stream, manifest);
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    public bool HasEntry(string packagePath) => FindEntry(packagePath) is not null;

    /// <summary>Opens a file inside the package for reading.</summary>
    public Stream OpenEntry(string packagePath) =>
        (FindEntry(packagePath) ?? throw new FileNotFoundException($"The package has no entry '{packagePath}'.", packagePath))
        .Open();

    public string ReadAllText(string packagePath)
    {
        using var reader = new StreamReader(OpenEntry(packagePath));
        return reader.ReadToEnd();
    }

    public byte[] ReadAllBytes(string packagePath)
    {
        using var stream = OpenEntry(packagePath);
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>Extracts the package contents into a directory (path-traversal safe).</summary>
    public void ExtractToDirectory(string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        _archive.ExtractToDirectory(destination, overwriteFiles: true);
    }

    public void Dispose()
    {
        _archive.Dispose();
        _ownedStream?.Dispose();
    }

    private ZipArchiveEntry? FindEntry(string packagePath) =>
        _archive.GetEntry(PackagePath.Normalize(packagePath));
}
