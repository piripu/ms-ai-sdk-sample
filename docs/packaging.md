# The `.ovipkg` package format

An `.ovipkg` is **an ordinary zip archive with its extension renamed**, plus one required entry: a
root `manifest.json` describing the nodes the package carries. Any zip tooling can inspect one.
Package identity uses the same domain concept as node ids (`org/name@semver`).

## Layout

```
research-pack.ovipkg            (zip)
├── manifest.json               required, at the root
├── agents/researcher.yaml      assets referenced by manifest entries
└── scripts/total_orders.py
```

Entry paths are `/`-separated, relative, and validated: no rooted paths, no `.`/`..` segments
(`PackagePath.Normalize` enforces this on write and read). `manifest.json` is reserved — the
builder generates it.

## The manifest

```json
{
  "manifestVersion": 1,
  "packageId": "acme/research-pack@1.0.0",
  "name": "Research Pack",
  "description": "Agents and scripts for research workflows.",
  "nodes": [
    {
      "id": "acme/researcher@1.0.0",
      "name": "Researcher",
      "description": "Answers questions using its tools.",
      "kind": "agent",
      "path": "agents/researcher.yaml"
    },
    {
      "id": "acme/total-orders@1.0.0",
      "name": "Total Orders",
      "kind": "script",
      "path": "scripts/total_orders.py"
    }
  ],
  "dependencies": [
    { "packageId": "acme/shared-tools@1.4.0" }
  ],
  "defaultEntry": "acme/researcher@1.0.0"
}
```

| Field | Meaning |
|---|---|
| `manifestVersion` | Schema version of the manifest itself; currently `1` (`OviPackageFormat.CurrentManifestVersion`). |
| `packageId` | Package identity, same grammar as node ids. |
| `nodes[].id/name/description` | The node's descriptor metadata. |
| `nodes[].kind` | `node` \| `agent` \| `tool` \| `trigger` \| `script` \| `workflow` (`PackagedNodeKind`, camelCase strings). |
| `nodes[].path` | Package-relative asset that defines the node (agent YAML/JSON, script `.py`); optional for nodes defined elsewhere (e.g. built-ins referenced by id). |
| `dependencies[].packageId` | Another package this one needs at load time, **declared, not vendored** — see [Dependencies](#dependencies) below. Always present, empty by default (`[]`). |
| `defaultEntry` | Which `nodes[].id` is "the" workflow/agent this package runs with no other context — see [Default entry](#default-entry). Omitted when unset. |

JSON conventions come from `OviJson`: camelCase properties, case-insensitive reads, camelCase enum
strings, nulls omitted (`dependencies` is an array, so it's always present even when empty, the same
as `nodes`).

## Authoring

```csharp
new OviPackageBuilder("acme/research-pack@1.0.0", "Research Pack", "Agents and scripts.")
    .AddTextFile("agents/researcher.yaml", AgentDefinitionSerializer.ToYaml(definition))
    .AddNode(new PackagedNodeEntry(definition.ToDescriptor(), PackagedNodeKind.Agent, "agents/researcher.yaml"))
    .AddDependency("acme/shared-tools@1.4.0")
    .WithDefaultEntry(definition.Id)
    .Save("research-pack.ovipkg");
```

`Save` runs the [publish pipeline](#the-publish-pipeline): validate that every entry `path` refers
to a file actually added to the package, write the manifest, write the files.

## Reading

```csharp
using var package = OviPackage.Open("research-pack.ovipkg");
package.Manifest.PackageId;                      // acme/research-pack@1.0.0
package.ReadAllText("agents/researcher.yaml");   // asset access
package.ExtractToDirectory(dir);                 // path-traversal safe
```

Opening validates the manifest's presence and parses it; an archive without a root `manifest.json`
is rejected as not-an-Ovi-package.

## Dependencies

A package's `dependencies` list names other packages it needs **without vendoring them** — the same
idea as a Python wheel's `Requires-Dist`: the archive stays small and self-describing, and a loader
resolves only what a given run actually asks for. `OviPackageResolver` is that loader-side piece:

```csharp
var source = new DirectoryPackageSource("/var/ovi/packages"); // one IPackageSource implementation this SDK ships
using var resolver = new OviPackageResolver(source);

using var package = OviPackage.Open("research-pack.ovipkg");
var dependencyResults = resolver.ResolveDependencies(package); // one level; recurse yourself if you need transitive deps
```

`Resolve`/`ResolveDependencies` return `Result<OviPackage>` (a `ResolutionError` when a dependency
isn't available) and only ever open the exact package id asked for — `DirectoryPackageSource` never
scans its directory, it maps an id to one file name
(`{organization}__{name}[@{version}].ovipkg`) and opens only that. Resolved packages are cached on
the resolver and disposed together via `resolver.Dispose()`. A registry/CDN-backed `IPackageSource`
is future work; the interface is the seam.

## The publish pipeline

`OviPackageBuilder.Save` runs a `PackagePublishPipeline` — an ordered list of `IPackagePublishStep`s
— instead of one monolithic method:

```csharp
public sealed class PackagePublishPipeline
{
    public static PackagePublishPipeline Default { get; } // ValidateNodeFileReferencesStep, WriteManifestStep, WriteEntriesStep
    public void Run(PackagePublishContext context);
}
```

`Save(path)`/`Save(stream)` use `PackagePublishPipeline.Default` (byte-identical to what a single
`Save` method produced before this pipeline existed); pass a custom pipeline
(`builder.Save(path, myPipeline)`) to add capabilities the wire format doesn't define yet:

- **Content hashing** — a step that computes and records a digest of each file/of the archive.
- **Signing** — a step that signs the manifest (or the whole archive) with a chosen scheme.

Neither is implemented here — no hash algorithm or signing scheme has been decided — the same
"deliberate incompleteness" precedent as `IPythonScriptEngine`: the extension point (`Run` an
ordered list of steps against a shared `PackagePublishContext`) exists now so those capabilities are
additive later, not a rework of `Save`.

## Conventions and future work

- **Python dependencies**: a `requirements.txt` next to the script asset; installing it into the
  interpreter/venv is a runtime concern (see [python-script-execution.md](python-script-execution.md)).
  (Package-to-package dependencies — a different concern — are `manifest.dependencies`, above.)
- **Compiled nodes**: assemblies under `lib/` with `kind: "node"` entries pointing at them — the
  loading/`AssemblyLoadContext` strategy is runtime territory and deliberately unspecified here.
- **Signing/integrity**: see [The publish pipeline](#the-publish-pipeline) — the extension point
  exists, the scheme doesn't yet. Readers should tolerate unknown fields regardless (which `OviJson`
  reads do), so a future manifest version can add fields without breaking v1 readers.
- **Package registries**: `IPackageSource` beyond `DirectoryPackageSource` (HTTP/CDN-backed).
