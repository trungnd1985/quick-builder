# Contract: The Cms Module's Surface

**Feature**: [../spec.md](../spec.md) | **Research**: CS-01, CS-04, CS-10, CS-14, CS-15

What this feature adds to the shared registries, and what other code may rely on.

## 1. Projects and references

| Project | References | Notes |
|---|---|---|
| `QuickBuilder.Cms.Contracts` | `Shared.Kernel` only | `CollectionSchema` (versions, `OpenSchema(version)` over the embedded file), `CollectionSchemaLimits`, events. No EF Core, no ASP.NET Core (Contracts purity test). |
| `QuickBuilder.Cms` | `Shared.Kernel`, `Shared.Infrastructure`, `Cms.Contracts`, `Pages.Contracts`, `Media.Contracts`, `Sites.Contracts`, `Identity.Contracts` | Internal by default; `CmsModule.AddCmsModule()` is the entry point. |

`ModuleBoundaryTests.ModuleImplementations` and `ModuleContracts` gain the two assemblies.
No other module references Cms in this feature.

## 2. Change to `Pages.Contracts`

`SchemaSubsetValidator` gains:

```csharp
/// Checks <paramref name="instance"/> against <c>#/$defs/{definition}</c> of the loaded schema.
public SchemaFailure? ValidateDefinition(string definition, JsonElement instance);
```

The TypeScript port (`src/admin/src/editor/schema/subsetValidator.ts`) gains
`validateDefinition(definition, instance)`. Cms uses it with the page schema's `rtDoc` for
rich text values, and with its own `$defs` for item values. This is additive: existing
callers are unchanged.

## 2a. Addition to `Media.Contracts`

`IMediaContentTypes.GetAsync(siteId, mediaIds)` returns the content type of each of the named
site's files (research CS-12). Cms uses it to tell images from documents in a schema change.
Implemented by Media over its own context, reading the named site as its admin endpoints do.

## 3. Capabilities (added to 002 contracts/authorization.md §2)

| Capability | Scope | Administrator | Designer | Editor |
|---|---|---|---|---|
| `cms.collections.view` | Site | ✓ | ✓ | ✓ |
| `cms.collections.manage` | Site | ✓ | ✓ | — |

## 4. Integration events (added to 001 contracts/integration-events.md)

| Event | Published by | Payload | When |
|---|---|---|---|
| `Cms.CollectionCreated` | Cms | `siteId`, `collectionId`, `revision`, `actorUserId` | A collection was created |
| `Cms.CollectionChanged` | Cms | `siteId`, `collectionId`, `revision`, `schemaChanged`, `itemsRewritten`, `actorUserId` | A save produced a new revision. `schemaChanged` is false for detail-only saves; `itemsRewritten` says whether any item value changed. |
| `Cms.CollectionDeleted` | Cms | `siteId`, `collectionId`, `actorUserId` | A collection and all its items were deleted |

Ids only; no names or values. No consumer in this feature; #16 subscribes.

## 5. Obligations on later features

- **Every item write** (#15) goes through `CollectionWriteGate` (data-model §3.3) and
  validates values against the schema it returns.
- **Readers of item values** (#15, #16) accept every `schemaVersion` still stored (contract
  §9) and treat a missing key as "no value".
- **Bindings** (#16) refer to fields by **id**, never by key or name, so renames never break
  them (Principle VI).
