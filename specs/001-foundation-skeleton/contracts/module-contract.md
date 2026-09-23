# Contract: What a Module Is

**Feature**: `001-foundation-skeleton` | **Date**: 2026-09-22

The shape every later module copies. `Sites` is the worked example; this document is the
rule the architecture tests enforce (FR-025) and the contributor guide explains (FR-047).

---

## Two projects, always

```text
src/Modules/<Module>/
├── QuickBuilder.<Module>.Contracts/   # the ONLY project other modules may reference
└── QuickBuilder.<Module>/             # everything else, internal by default
```

**`Contracts`** holds interfaces other modules call, the DTOs those interfaces return, and
integration event types. Nothing else. No EF entities, no `DbContext`, no handlers, no
controllers.

**Implementation** holds `Domain/`, `Features/`, `Infrastructure/` and `<Module>Module.cs`.
Types are `internal` unless they are the module's registration entry point.

---

## Registration entry point (FR-004)

Exactly one public extension method per module. The host calls it and nothing else:

```csharp
public static class SitesModule
{
    public static IServiceCollection AddSitesModule(
        this IServiceCollection services, IConfiguration configuration);
}
```

Responsibilities: register the module's `DbContext`, its handlers, its validators, its
outbox store and its integration event handlers; add its controllers via
`AddApplicationPart`. Adding a module must require exactly one line in the host and no
edit to any other module — asserted by SC-009.

---

## Feature slice layout (FR-010)

One folder per operation, everything for it inside:

```text
Features/CreateSite/
├── CreateSiteController.cs   # [ApiController], route /api/v1/sites — thin (FR-007)
├── CreateSiteRequest.cs      # + CreateSiteResponse
├── CreateSiteCommand.cs
├── CreateSiteHandler.cs      # ICommandHandler<CreateSiteCommand, Result<SiteDto>>
├── CreateSiteValidator.cs    # IValidator<CreateSiteCommand>
└── CreateSiteTests.cs        # or the mirrored path under tests/
```

A new feature is a new folder. It must not require editing an existing feature's files.

---

## Dispatch abstractions

In-house, in `Shared.Kernel`; no mediator library (constitution Principle I).

```csharp
public interface ICommandHandler<in TCommand, TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
}

public interface IQueryHandler<in TQuery, TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken);
}

public interface IValidator<in T>
{
    ValidationResult Validate(T instance);
}
```

The dispatcher resolves the handler from DI and runs the validation decorator first — a
validation failure returns before the handler is constructed (FR-008).

Handlers return `Result` / `Result<T>`, which distinguishes success, expected failure with
an error code, and unexpected failure (FR-009). Controllers map that to HTTP without
catching exceptions.

---

## Data rules

- One schema per module, named after it (`sites`). One `DbContext`. Its own
  `__EFMigrationsHistory` inside that schema (FR-022).
- Handlers use the `DbContext` directly. No repository wrappers (constitution).
- Reads use `AsNoTracking()` and project to DTOs rather than returning entities.
- Entities holding a `SiteId` implement `ISiteOwned` (FR-013). An architecture test asserts
  no entity has a `SiteId` without the marker.
- **Declare a query filter for each of them yourself.** There is no automatic application,
  deliberately — see research.md D-08 for the EF model-caching trap that makes the obvious
  generic helper match nothing while looking correct. Expose the site as a property on the
  context and reference it through `this`:

  ```csharp
  internal Guid CurrentSiteId => siteContext.SiteId ?? Guid.Empty;

  entity.HasQueryFilter(h => h.SiteId == CurrentSiteId);
  ```

  Declaring them by hand means one can be forgotten, and a forgotten filter returns every
  site's rows. `SiteFilterCoverageTests` fails the build if an `ISiteOwned` entity reaches
  the model without one.
- Cross-site reads go through the annotated helper with a reason string (FR-013a).
  `IgnoreQueryFilters` is banned at compile time everywhere else (D-07).
- No foreign keys across module schemas, ever. Store the id, read through `Contracts`.
- No transaction spans two modules (FR-021).

---

## Enforced boundaries (FR-025)

The architecture tests fail the build when:

| Rule | Assertion |
|---|---|
| Module isolation | `QuickBuilder.<A>` must not reference `QuickBuilder.<B>`; only `QuickBuilder.<B>.Contracts` is referenceable |
| Contracts purity | `*.Contracts` references no EF Core, no ASP.NET Core, no other module's implementation |
| Host is composition only | `QuickBuilder.Host` contains no `DbContext`, no handler, no domain type |
| Shared stays generic | `Shared.Kernel` and `Shared.Infrastructure` reference no module |
| Internals stay internal | Public types in `QuickBuilder.<Module>` are limited to the registration entry point and controllers |
| Site scoping | Every entity with `SiteId` implements `ISiteOwned` |
| Deliberate bypass | `IgnoreQueryFilters` banned outside the annotated helper (compile-time, D-07) |

Each rule ships with a deliberately violating fixture proving the rule actually fires —
that is what SC-004 measures.

---

## Checklist for adding a module

1. Create the two projects under `src/Modules/<Module>/`.
2. Add `<Module>Module.cs` with `Add<Module>Module()`.
3. Add a `DbContext` with `HasDefaultSchema("<module>")` and its own migrations history
   table.
4. Add one line to the host.
5. Add the module's architecture-test entry.
6. First migration creates the schema; run it and confirm `/health/ready` still reports
   ready.

Nothing on this list touches an existing module. That is the property SC-009 verifies.
