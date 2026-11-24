# Contract: Setting Attributes

**Feature**: 026 · **Requirements**: FR-009 – FR-016 · **Research**: OD-01, OD-02, OD-03

The attributes every options class uses to describe itself to operators. They live in
`QuickBuilder.Shared.Kernel` (namespace `QuickBuilder.Shared.Kernel.Settings`), carry no
behaviour, and are read only by `qb-docs` and its tests. The product never reads them.

## 1. Attributes

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class SettingsSectionAttribute(string key) : Attribute
{
    public string Key { get; } = key;            // must equal the class's SectionName
    public required string Area { get; init; }   // data-model §2
    public required string Title { get; init; }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class SettingAttribute(string description) : Attribute
{
    public string Description { get; } = description; // operator voice, plain language
    public bool Secret { get; init; }
    public string? DefaultText { get; init; }     // only when the product computes the default
    public string? Format { get; init; }          // required with [RegularExpression]
    public string? Allowed { get; init; }         // accepted values a validator (not an attribute) enforces
    public string? EntryName { get; init; }       // list/map placeholder: "n" (default), "name", or "" for the bare key
    public string? AppliesWhen { get; init; }     // "Storage:Provider = S3"
    public string? ObservedLazily { get; init; }  // reason the boot check cannot see it
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class SettingsGroupAttribute(string title) : Attribute
{
    public string Title { get; } = title;
    public string? AppliesWhen { get; init; }     // inherited by every setting in the group
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class NotASettingAttribute(string reason) : Attribute
{
    public string Reason { get; } = reason;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class DeprecatedSettingAttribute(string removeIn) : Attribute
{
    public string RemoveIn { get; } = removeIn;
    public string? Replacement { get; init; }
}
```

## 2. Example

```csharp
[SettingsSection(SectionName, Area = "database", Title = "Database")]
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Apply migrations automatically during startup (FR-023). …</summary>
    [Setting("Apply migrations during startup. Set false to apply them yourself with the " +
             "migrate command; readiness then reports MigrationsPending until you do.")]
    public bool AutoMigrate { get; set; } = true;
}

public sealed class StorageOptions
{
    [SettingsGroup("S3-compatible", AppliesWhen = "Storage:Provider = S3")]
    public S3StorageSettings S3 { get; set; } = new();

    [NotASetting("Filled in by the host with its own directories; never read from configuration.")]
    public List<string> ForbiddenRoots { get; } = [];
}

public sealed class S3StorageSettings
{
    [Setting("The secret access key, with AccessKeyId.", Secret = true)]
    public string? SecretAccessKey { get; set; }
}
```

XML comments stay as they are for developers; the `[Setting]` text is what operators read.

## 3. Rules the check enforces (`qb-docs settings check`, unit test)

1. Every class with `[SettingsSection]` has a `SectionName` const equal to `Key`.
2. Every public property with a setter or a collection getter on a section or group type is
   exactly one of `[Setting]`, `[SettingsGroup]`, `[NotASetting]`.
3. `Description` is non-empty, ends with `.`, and contains no requirement identifier
   (regex `\b(FR|SC|OD|RL|D)-\d`).
4. A property with `[RegularExpression]` has `Format`.
5. `AppliesWhen` parses as `<declared key> = <value>`.
6. `Secret` is required on properties whose name ends in `Password`, `Secret`, `Key` (not
   `KeyPrefix`), `ConnectionString` or `Token`, unless `[Setting(Secret=false)]` is written
   explicitly — so a new credential cannot slip in unmarked.
7. No two definitions produce the same key.
8. The committed reference equals a fresh generation.

## 4. Boot observation (integration test `EveryReadSettingIsDeclared`)

- A `RecordingConfigurationSource` is added **last** to the host's configuration. Its
  provider returns `false` from `TryGet` and records the key; `GetChildKeys` records the
  parent path.
- Runs: defaults; `Storage:Provider=AzureBlob` against `AzuriteFixture`; `Storage:Provider=S3`
  against the S3 container fixture `RemoteFileStorageConformanceTests` uses (both in
  `tests/QuickBuilder.IntegrationTests/Storage/`), so the startup probe succeeds against real
  emulators. Host command configuration paths are observed by resolving each command's
  services from the booted provider without executing them.
- After booting, the test also resolves `IOptions<T>` for every documented section, as the
  product does when a feature first needs it, so options bound lazily are observed too.
- A section with nothing configured is read by listing its children: the binder finds none
  and asks for no property. A setting therefore counts as read when its own key, or any path
  containing it down to its section key, was asked for.
- Public properties that cannot be set from configuration (computed or static) are asked for
  by the binder; the catalog lists them as exclusions of kind `computed`.
- Assertions: observed ⊆ declared ∪ external ∪ exclusions; declared ⊆ observed ∪
  `ObservedLazily`. Failure output lists each key with the fix ("add `[Setting]` to …",
  "add to the framework allow-list with a reason", "remove the dead declaration").

## 5. Framework allow-list (in `qb-docs`, reviewed)

`Logging:*`, `AllowedHosts`, `Kestrel:*`, `urls`, `http_ports`, `https_ports`,
`ASPNETCORE_*`, `DOTNET_*`, `applicationName`, `contentRoot`, `environment`,
`HostingStartupAssemblies`, `HostingStartupExcludeAssemblies`, `PreventHostingStartup`,
`detailedErrors`, `captureStartupErrors`, `suppressStatusMessages`, `webroot`,
`shutdownTimeoutSeconds`, `preferHostingUrls`, `Microsoft:*`, and, as found by the first run of
the observation test: `ForwardedHeaders_Enabled`, `Metrics`, `OTEL_*`,
`servicesStartConcurrently`, `servicesStopConcurrently`, `startupTimeoutSeconds` and
`Serilog:*` (Serilog's own sinks and enrichers; the documented log levels are declared before
exclusions are consulted). Each entry has a reason string printed by the
check. Additions need a reason; the initial list is confirmed by the first run of the
observation test and trimmed to what is actually observed.

## 6. Literal reads (unit test `LiteralConfigurationReadsTests`)

The boot observation cannot see what `Program.cs` reads before the host is built: a test's
extra configuration source is attached only at build time. The unit test scans `src/` for
literal keys (`configuration["A:B"]`, `GetSection("A:B")`, `GetConnectionString("X")`,
`GetValue<T>("A:B")`) and fails on any that is neither a declared setting, a section or group
path, nor an exclusion. Keys built at run time remain the boot observation's to catch.
