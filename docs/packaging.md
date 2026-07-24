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
  ]
}
```

| Field | Meaning |
|---|---|
| `manifestVersion` | Schema version of the manifest itself; currently `1` (`OviPackageFormat.CurrentManifestVersion`). |
| `packageId` | Package identity, same grammar as node ids. |
| `nodes[].id/name/description` | The node's descriptor metadata. |
| `nodes[].kind` | `node` \| `agent` \| `tool` \| `trigger` \| `script` \| `workflow` (`PackagedNodeKind`, camelCase strings). |
| `nodes[].path` | Package-relative asset that defines the node (agent YAML/JSON, script `.py`); optional for nodes defined elsewhere (e.g. built-ins referenced by id). |

JSON conventions come from `OviJson`: camelCase properties, case-insensitive reads, camelCase enum
strings, nulls omitted.

## Authoring

```csharp
new OviPackageBuilder("acme/research-pack@1.0.0", "Research Pack", "Agents and scripts.")
    .AddTextFile("agents/researcher.yaml", AgentDefinitionSerializer.ToYaml(definition))
    .AddNode(new PackagedNodeEntry(definition.ToDescriptor(), PackagedNodeKind.Agent, "agents/researcher.yaml"))
    .Save("research-pack.ovipkg");
```

`Save` validates that every entry `path` refers to a file actually added to the package and writes
the zip with the generated manifest.

## Reading

```csharp
using var package = OviPackage.Open("research-pack.ovipkg");
package.Manifest.PackageId;                      // acme/research-pack@1.0.0
package.ReadAllText("agents/researcher.yaml");   // asset access
package.ExtractToDirectory(dir);                 // path-traversal safe
```

Opening validates the manifest's presence and parses it; an archive without a root `manifest.json`
is rejected as not-an-Ovi-package.

## Conventions and future work

- **Python dependencies**: a `requirements.txt` next to the script asset; installing it into the
  interpreter/venv is a runtime concern (see [python-script-execution.md](python-script-execution.md)).
- **Compiled nodes**: assemblies under `lib/` with `kind: "node"` entries pointing at them — the
  loading/`AssemblyLoadContext` strategy is runtime territory and deliberately unspecified here.
- **Signing/integrity**: not part of manifest v1; a future manifest version can add content hashes
  without breaking v1 readers (readers should tolerate unknown fields, which `OviJson` reads do).
