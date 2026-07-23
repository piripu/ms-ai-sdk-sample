using System.IO.Compression;
using Ovi.Sdk.Agents;
using Ovi.Sdk.Operators;
using Ovi.Sdk.Packaging;
using Xunit;

namespace Ovi.Sdk.Tests;

public class PackagingTests
{
    [Fact]
    public void A_package_round_trips_as_a_renamed_zip()
    {
        var definition = new AgentDefinition(
            OperatorId.Parse("acme/researcher@1.0.0"),
            "Researcher",
            "Researches things.",
            "Be thorough.");
        var yaml = AgentDefinitionSerializer.ToYaml(definition);

        var directory = Directory.CreateTempSubdirectory("ovi-pkg-tests-");
        try
        {
            var path = Path.Combine(directory.FullName, "starter" + OviPackageFormat.FileExtension);

            new OviPackageBuilder(OperatorId.Parse("acme/starter-pack@0.1.0"), "Starter Pack", "A demo package.")
                .AddTextFile("agents/researcher.yaml", yaml)
                .AddOperator(new PackagedOperatorEntry(definition.ToDescriptor(), PackagedOperatorKind.Agent, "agents/researcher.yaml"))
                .Save(path);

            // An .ovipkg is a plain zip wearing a different extension.
            using (var zip = ZipFile.OpenRead(path))
            {
                Assert.Contains(zip.Entries, entry => entry.FullName == OviPackageFormat.ManifestEntryName);
            }

            using var package = OviPackage.Open(path);
            Assert.Equal(OperatorId.Parse("acme/starter-pack@0.1.0"), package.Manifest.PackageId);
            Assert.Equal("Starter Pack", package.Manifest.Name);
            Assert.Equal(OviPackageFormat.CurrentManifestVersion, package.Manifest.ManifestVersion);

            var entry = Assert.Single(package.Manifest.Operators);
            Assert.Equal(PackagedOperatorKind.Agent, entry.Kind);
            Assert.Equal("agents/researcher.yaml", entry.Path);
            Assert.Equal(definition.Id, entry.Id);

            Assert.True(package.HasEntry("agents/researcher.yaml"));
            var loaded = AgentDefinitionSerializer.FromYaml(package.ReadAllText("agents/researcher.yaml"));
            Assert.Equal(definition.Id, loaded.Id);
            Assert.Equal(definition.Instructions, loaded.Instructions);

            var extracted = Path.Combine(directory.FullName, "extracted");
            package.ExtractToDirectory(extracted);
            Assert.True(File.Exists(Path.Combine(extracted, "agents", "researcher.yaml")));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Save_rejects_operator_entries_referencing_missing_files()
    {
        var builder = new OviPackageBuilder(OperatorId.Parse("acme/broken@1.0.0"), "Broken")
            .AddOperator(new PackagedOperatorEntry(
                new OperatorDescriptor(OperatorId.Parse("acme/ghost@1.0.0"), "Ghost"),
                PackagedOperatorKind.Agent,
                "agents/ghost.yaml"));

        using var stream = new MemoryStream();
        var error = Assert.Throws<InvalidOperationException>(() => builder.Save(stream));
        Assert.Contains("agents/ghost.yaml", error.Message);
    }

    [Fact]
    public void The_manifest_entry_name_is_reserved()
    {
        var builder = new OviPackageBuilder(OperatorId.Parse("acme/pack@1.0.0"), "Pack");
        Assert.Throws<ArgumentException>(() => builder.AddTextFile("manifest.json", "{}"));
    }

    [Fact]
    public void Package_paths_may_not_escape_the_package()
    {
        var builder = new OviPackageBuilder(OperatorId.Parse("acme/pack@1.0.0"), "Pack");
        Assert.Throws<ArgumentException>(() => builder.AddTextFile("../evil.txt", "boom"));
        Assert.Throws<ArgumentException>(() => builder.AddTextFile("a/../../evil.txt", "boom"));
    }

    [Fact]
    public void Opening_a_zip_without_a_manifest_fails()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("readme.txt");
        }

        stream.Position = 0;
        Assert.Throws<InvalidDataException>(() => OviPackage.Open(stream));
    }

    [Fact]
    public void The_manifest_serializes_with_camel_case_and_string_ids()
    {
        var manifest = new OviPackageBuilder(OperatorId.Parse("acme/starter-pack@0.1.0"), "Starter Pack")
            .AddOperator(new PackagedOperatorEntry(
                new OperatorDescriptor(OperatorId.BuiltIn("uppercase"), "Uppercase"),
                PackagedOperatorKind.Operator))
            .BuildManifest();

        var json = manifest.ToJson();

        Assert.Contains("\"packageId\": \"acme/starter-pack@0.1.0\"", json);
        Assert.Contains("\"kind\": \"operator\"", json);
        Assert.Contains("\"./uppercase\"", json);

        var parsed = OviPackageManifest.FromJson(json);
        Assert.Equal(manifest.PackageId, parsed.PackageId);
        Assert.Single(parsed.Operators);
    }
}
