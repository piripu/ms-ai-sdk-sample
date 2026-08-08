using System.Text;

namespace Ovi.Sdk.Packaging.PublishPipeline;

/// <summary>Fails the publish when a <see cref="PackagedNodeEntry"/> references a file that was never added to the builder.</summary>
public sealed class ValidateNodeFileReferencesStep : IPackagePublishStep
{
    public void Execute(PackagePublishContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var missing = context.Manifest.Nodes
            .Where(entry => entry.Path is not null && !context.Files.ContainsKey(entry.Path))
            .Select(entry => $"{entry.Id} -> {entry.Path}")
            .ToArray();

        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Node entries reference files that were not added to the package: {string.Join(", ", missing)}.");
        }
    }
}

/// <summary>Writes the root <c>manifest.json</c> entry.</summary>
public sealed class WriteManifestStep : IPackagePublishStep
{
    public void Execute(PackagePublishContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var entry = context.Archive.CreateEntry(OviPackageFormat.ManifestEntryName);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(context.Manifest.ToJson());
    }
}

/// <summary>Writes every asset file added to the builder, in a stable (path-sorted) order.</summary>
public sealed class WriteEntriesStep : IPackagePublishStep
{
    public void Execute(PackagePublishContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var (path, content) in context.Files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var entry = context.Archive.CreateEntry(path);
            using var entryStream = entry.Open();
            entryStream.Write(content);
        }
    }
}
