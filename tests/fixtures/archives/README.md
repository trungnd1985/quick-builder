# Site archives from earlier releases

One `.qbsite` archive per release, named `<major>.<minor>.qbsite`, exported from the seeded
site of `FullSiteSeeder` (020 T068). `ArchiveFromPreviousReleaseTests` imports every archive here
as a new site on the current release and renders its pages, so a release that can no longer read
an earlier release's archive fails the build (020 SC-008, FR-038; constitution Principle III).

To add this release's archive, run once with `QB_WRITE_ARCHIVE_FIXTURE=1`:

```bash
QB_WRITE_ARCHIVE_FIXTURE=1 dotnet test tests/QuickBuilder.IntegrationTests \
  --filter "FullyQualifiedName~This_release_writes_its_archive_fixture_when_asked"
```

Never edit or replace an archive once it is committed: it stands for what that release wrote.
