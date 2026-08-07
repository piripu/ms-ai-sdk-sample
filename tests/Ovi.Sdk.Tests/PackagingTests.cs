using System.IO.Compression;
using Ovi.Sdk.Agents;
using Ovi.Sdk.Nodes;
using Ovi.Sdk.Packaging;
using Ovi.Sdk.Packaging.PublishPipeline;
using Xunit;

namespace Ovi.Sdk.Tests;

public class PackagingTests
{
    [Fact]
    public void A_package_round_trips_as_a_renamed_zip()
    {
        var definition = new AgentDefinition(
            NodeId.Parse("acme/researcher@1.0.0"),
            "Researcher",
            "Researches things.",
            "Be thorough.");
        var yaml = AgentDefinitionSerializer.ToYaml(definition);

        var directory = Directory.CreateTempSubdirectory("ovi-pkg-tests-");
        try
        {
            var path = Path.Combine(directory.FullName, "starter" + OviPackageFormat.FileExtension);

            new OviPackageBuilder(NodeId.Parse("acme/starter-pack@0.1.0"), "Starter Pack", "A demo package.")
                .AddTextFile("agents/researcher.yaml", yaml)
                .AddNode(new PackagedNodeEntry(definition.ToDescriptor(), PackagedNodeKind.Agent, "agents/researcher.yaml"))
                .Save(path);

            // An .ovipkg is a plain zip wearing a different extension.
            using (var zip = ZipFile.OpenRead(path))
            {
                Assert.Contains(zip.Entries, entry => entry.FullName == OviPackageFormat.ManifestEntryName);
            }

            using var package = OviPackage.Open(path);
            Assert.Equal(NodeId.Parse("acme/starter-pack@0.1.0"), package.Manifest.PackageId);
            Assert.Equal("Starter Pack", package.Manifest.Name);
            Assert.Equal(OviPackageFormat.CurrentManifestVersion, package.Manifest.ManifestVersion);

            var entry = Assert.Single(package.Manifest.Nodes);
            Assert.Equal(PackagedNodeKind.Agent, entry.Kind);
            Assert.Equal("agents/researcher.yaml", entry.Path);
            Assert.Equal(definition.Id, entry.Id);

            Assert.True(package.HasEntry("agents/researcher.yaml"));
            var loaded = AgentDefinitionSerializer.FromYaml(package.ReadAllText("agents/researcher.yaml")).Value;
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
    public void Save_rejects_node_entries_referencing_missing_files()
    {
        var builder = new OviPackageBuilder(NodeId.Parse("acme/broken@1.0.0"), "Broken")
            .AddNode(new PackagedNodeEntry(
                new NodeDescriptor(NodeId.Parse("acme/ghost@1.0.0"), "Ghost"),
                PackagedNodeKind.Agent,
                "agents/ghost.yaml"));

        using var stream = new MemoryStream();
        var error = Assert.Throws<InvalidOperationException>(() => builder.Save(stream));
        Assert.Contains("agents/ghost.yaml", error.Message);
    }

    [Fact]
    public void The_manifest_entry_name_is_reserved()
    {
        var builder = new OviPackageBuilder(NodeId.Parse("acme/pack@1.0.0"), "Pack");
        Assert.Throws<ArgumentException>(() => builder.AddTextFile("manifest.json", "{}"));
    }

    [Fact]
    public void Package_paths_may_not_escape_the_package()
    {
        var builder = new OviPackageBuilder(NodeId.Parse("acme/pack@1.0.0"), "Pack");
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
        var manifest = new OviPackageBuilder(NodeId.Parse("acme/starter-pack@0.1.0"), "Starter Pack")
            .AddNode(new PackagedNodeEntry(
                new NodeDescriptor(NodeId.BuiltIn("uppercase"), "Uppercase"),
                PackagedNodeKind.Node))
            .BuildManifest();

        var json = manifest.ToJson();

        Assert.Contains("\"packageId\": \"acme/starter-pack@0.1.0\"", json);
        Assert.Contains("\"kind\": \"node\"", json);
        Assert.Contains("\"./uppercase\"", json);

        var parsed = OviPackageManifest.FromJson(json);
        Assert.Equal(manifest.PackageId, parsed.PackageId);
        Assert.Single(parsed.Nodes);
    }

    [Fact]
    public void Dependencies_and_default_entry_round_trip_through_json()
    {
        var appId = NodeId.Parse("acme/app@1.0.0");
        var depId = NodeId.Parse("acme/lib@2.0.0");

        var manifest = new OviPackageBuilder(appId, "App")
            .AddNode(new PackagedNodeEntry(new NodeDescriptor(appId, "App"), PackagedNodeKind.Workflow, "workflow.yaml"))
            .AddTextFile("workflow.yaml", "id: acme/app@1.0.0")
            .AddDependency(depId)
            .WithDefaultEntry(appId)
            .BuildManifest();

        var json = manifest.ToJson();
        Assert.Contains("\"dependencies\"", json);
        Assert.Contains("\"defaultEntry\": \"acme/app@1.0.0\"", json);

        var parsed = OviPackageManifest.FromJson(json);
        var dependency = Assert.Single(parsed.Dependencies);
        Assert.Equal(depId, dependency.PackageId);
        Assert.Equal(appId, parsed.DefaultEntry);
    }

    [Fact]
    public void A_package_with_no_default_entry_omits_it_from_json_but_keeps_an_empty_dependency_list()
    {
        var manifest = new OviPackageBuilder(NodeId.Parse("acme/pack@1.0.0"), "Pack").BuildManifest();

        var json = manifest.ToJson();

        Assert.Contains("\"dependencies\": []", json);
        Assert.DoesNotContain("defaultEntry", json);

        var parsed = OviPackageManifest.FromJson(json);
        Assert.Empty(parsed.Dependencies);
        Assert.Null(parsed.DefaultEntry);
    }

    [Fact]
    public void The_default_publish_pipeline_produces_the_same_bytes_as_the_original_Save_implementation()
    {
        var builder = new OviPackageBuilder(NodeId.Parse("acme/pack@1.0.0"), "Pack")
            .AddTextFile("a.txt", "hello")
            .AddNode(new PackagedNodeEntry(new NodeDescriptor(NodeId.BuiltIn("x"), "X"), PackagedNodeKind.Node, "a.txt"));

        using var implicitPipeline = new MemoryStream();
        builder.Save(implicitPipeline);

        using var explicitPipeline = new MemoryStream();
        builder.Save(explicitPipeline, PackagePublishPipeline.Default);

        Assert.Equal(implicitPipeline.ToArray(), explicitPipeline.ToArray());
    }

    [Fact]
    public void A_custom_publish_pipeline_can_add_steps_after_the_default_ones()
    {
        var builder = new OviPackageBuilder(NodeId.Parse("acme/pack@1.0.0"), "Pack");
        var extraStepRan = false;
        var pipeline = new PackagePublishPipeline(
        [
            new ValidateNodeFileReferencesStep(),
            new WriteManifestStep(),
            new WriteEntriesStep(),
            new RecordingStep(() => extraStepRan = true),
        ]);

        using var stream = new MemoryStream();
        builder.Save(stream, pipeline);

        Assert.True(extraStepRan);
    }

    [Fact]
    public void The_resolver_only_opens_packages_it_is_actually_asked_to_resolve()
    {
        var directory = Directory.CreateTempSubdirectory("ovi-pkg-resolver-tests-");
        try
        {
            var depId = NodeId.Parse("acme/dep@1.0.0");
            var corruptId = NodeId.Parse("acme/corrupt@1.0.0");

            new OviPackageBuilder(depId, "Dep").Save(Path.Combine(directory.FullName, DirectoryPackageSource.ToFileName(depId)));

            // A file that would throw InvalidDataException if the resolver ever opened it (no manifest.json) —
            // if resolving root's one declared dependency still succeeds, the resolver never touched it.
            File.WriteAllBytes(Path.Combine(directory.FullName, DirectoryPackageSource.ToFileName(corruptId)), []);

            var rootId = NodeId.Parse("acme/root@1.0.0");
            var rootPath = Path.Combine(directory.FullName, DirectoryPackageSource.ToFileName(rootId));
            new OviPackageBuilder(rootId, "Root").AddDependency(depId).Save(rootPath);

            var source = new DirectoryPackageSource(directory.FullName);
            using var resolver = new OviPackageResolver(source);
            using var root = OviPackage.Open(rootPath);

            var results = resolver.ResolveDependencies(root);

            var result = Assert.Single(results);
            Assert.True(result.IsSuccess);
            Assert.Equal(depId, result.Value.Manifest.PackageId);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Resolving_an_unavailable_package_is_a_resolution_error()
    {
        var directory = Directory.CreateTempSubdirectory("ovi-pkg-resolver-tests-");
        try
        {
            var resolver = new OviPackageResolver(new DirectoryPackageSource(directory.FullName));

            var result = resolver.Resolve(NodeId.Parse("acme/missing@1.0.0"));

            Assert.True(result.IsFailure);
            Assert.IsType<ResolutionError>(result.Error);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class RecordingStep(Action onExecute) : IPackagePublishStep
    {
        public void Execute(PackagePublishContext context) => onExecute();
    }
}
