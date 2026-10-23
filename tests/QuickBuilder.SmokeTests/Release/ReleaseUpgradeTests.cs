using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace QuickBuilder.SmokeTests.Release;

/// <summary>
/// The release upgrade gate: one earlier release's snapshot, upgraded by the candidate image
/// (019 contracts/snapshot.md §4, FR-008, FR-012, FR-014, FR-016, FR-019, FR-036).
/// </summary>
/// <remarks>
/// The release workflow runs this once per upgrade source (research RL-09), with:
/// <list type="bullet">
/// <item><c>QB_SMOKE_IMAGE</c> — the candidate, by digest;</item>
/// <item><c>QB_UPGRADE_FROM</c> — a version whose snapshot is in <c>QB_SNAPSHOT_DIR</c>, or
/// <c>baseline</c> for 0.1.0, which builds the image at <c>QB_BASELINE_REF</c> (default
/// <c>pre-0.1.0-baseline</c>) and fills it with the content journey (RL-15);</item>
/// <item><c>QB_PREVIOUS_IMAGE</c> — that release's image (not needed for the baseline);</item>
/// <item><c>QB_RELEASE_VERSION</c> — the candidate's version, naming its snapshot;</item>
/// <item><c>QB_SNAPSHOT_OUT</c> — set only on the leg from the previous release: that leg also
/// writes the candidate's own snapshot, and drills the rollback.</item>
/// </list>
/// Excluded from the normal test run like every smoke test; it takes several minutes.
/// </remarks>
[Trait("Category", "ReleaseUpgrade")]
[Collection(DeploymentGroup.Name)]
public sealed class ReleaseUpgradeTests(ITestOutputHelper output)
{
    private static readonly TimeSpan UpgradeReadyBudget = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RollbackBudget = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RefusalBudget = TimeSpan.FromSeconds(30);

    [SkippableFact]
    public async Task The_candidate_upgrades_an_earlier_release_without_losing_anything()
    {
        var deployment = new ComposeDeployment { ExplicitImages = true };
        Skip.IfNot(await deployment.DetectDockerAsync().ConfigureAwait(true), "Docker is not available on this machine.");

        var candidate = Required("QB_SMOKE_IMAGE");
        var from = Environment.GetEnvironmentVariable("QB_UPGRADE_FROM") is { Length: > 0 } f ? f : "baseline";
        var releaseVersion = Environment.GetEnvironmentVariable("QB_RELEASE_VERSION") is { Length: > 0 } v ? v : "0.1.0-dev";
        var snapshotOut = Environment.GetEnvironmentVariable("QB_SNAPSHOT_OUT") is { Length: > 0 } o ? o : null;
        var work = Path.Combine(Path.GetTempPath(), "qb-release-gate", Guid.NewGuid().ToString("N"));

        try
        {
            // --- The source: an earlier release's snapshot, or the pre-release baseline ------
            var (source, previousImage) = from == "baseline"
                ? await BaselineAsync(deployment, work).ConfigureAwait(true)
                : (new SnapshotBundle(Required("QB_SNAPSHOT_DIR"), from), Required("QB_PREVIOUS_IMAGE"));
            var before = source.ReadExpectations();
            var allowList = VisibleChangeAllowList.Load(Path.Combine(deployment.RepositoryRoot, "docs", "releases", "unreleased"));
            var administrator = before.Members.First(m => m.Role == "Administrator");
            output.WriteLine($"upgrading {from} ({before.Pages.Count} pages, {before.Records.Count} tables) with {candidate}");

            // --- 1–2. Restore, start the candidate, ready within the budget (SC-001) ---------
            await source.RestoreAsync(deployment, candidate).ConfigureAwait(true);
            deployment.Image = candidate;
            var readyIn = await StartAppAsync(deployment).ConfigureAwait(true);
            output.WriteLine($"step 2: ready {readyIn.TotalSeconds:F0}s after start");
            Assert.True(readyIn <= UpgradeReadyBudget, $"The upgrade took {readyIn} to become ready; the budget is {UpgradeReadyBudget} (SC-001).");

            using var admin = await SignInAsync(administrator).ConfigureAwait(true);

            // --- 3–4. Pages and records (SC-002) -------------------------------------------
            var after = await ReleaseExpectations.CaptureAsync(deployment, admin, releaseVersion, from, before.Members, before.Drafts).ConfigureAwait(true);
            var compared = ExpectationComparer.Compare(before, after, allowList);
            AssertSame(compared, "steps 3–4");
            foreach (var change in compared.Allowed)
            {
                output.WriteLine($"allowed visible change: {change}");
            }

            output.WriteLine($"steps 3–4: {before.Pages.Count} pages and {before.Records.Count} tables as before");

            // --- 5. Every member signs in; every draft opens ------------------------------
            foreach (var member in before.Members)
            {
                using var client = await SignInAsync(member).ConfigureAwait(true);
            }

            foreach (var draft in before.Drafts)
            {
                using var response = await admin.GetAsync(new Uri(draft.Endpoint, UriKind.Relative)).ConfigureAwait(true);
                Assert.True(response.StatusCode == HttpStatusCode.OK, $"The {draft.Kind} draft at {draft.Endpoint} answered {(int)response.StatusCode}.");
            }

            output.WriteLine($"step 5: {before.Members.Count} members signed in; {before.Drafts.Count} drafts opened");

            // --- 6. Background rewrites finish; pages still the same (FR-019) -------------
            await WaitForBackgroundWorkAsync(deployment).ConfigureAwait(true);
            var settled = await ReleaseExpectations.CapturePagesAsync(admin).ConfigureAwait(true);
            AssertSame(ExpectationComparer.Compare(before, after with { Pages = settled }, allowList), "step 6");
            output.WriteLine("step 6: background work done; pages unchanged");

            // --- 7. A restart writes nothing (FR-016, SC-007) ------------------------------
            var unchanged = await ReleaseExpectations.FingerprintTablesAsync(deployment).ConfigureAwait(true);
            await Must(deployment.ComposeAsync("restart app"), "restarting the application").ConfigureAwait(true);
            await StartAppAsync(deployment, alreadyUp: true).ConfigureAwait(true);
            AssertNoWrites(unchanged, await ReleaseExpectations.FingerprintTablesAsync(deployment).ConfigureAwait(true), "step 7 (a restart)");
            output.WriteLine("step 7: a restart wrote nothing");

            if (snapshotOut is null)
            {
                return;
            }

            // --- 8. The candidate's own snapshot: everything before, plus its journey -------
            var journey = await new ContentJourney(deployment, releaseVersion).RunAsync(administrator).ConfigureAwait(true);
            using (journey.Admin)
            {
                var captured = await ReleaseExpectations
                    .CaptureAsync(deployment, journey.Admin, releaseVersion, from, journey.Members, journey.Drafts)
                    .ConfigureAwait(true);
                var bundle = new SnapshotBundle(snapshotOut, releaseVersion);
                await bundle.DumpAsync(deployment, ExpectationComparer.Merge(before, captured)).ConfigureAwait(true);
                output.WriteLine($"step 8: snapshot of {releaseVersion} written to {snapshotOut}");
            }

            // --- 9. The previous release on the upgraded database -------------------------
            await PreviousOnUpgradedAsync(deployment, previousImage, from, releaseVersion).ConfigureAwait(true);

            // --- 10. Rollback as the guide describes (FR-036, SC-010) ----------------------
            var rollback = Stopwatch.StartNew();
            await source.RestoreAsync(deployment, previousImage).ConfigureAwait(true);
            deployment.Image = previousImage;
            await StartAppAsync(deployment).ConfigureAwait(true);
            rollback.Stop();
            using (var restoredAdmin = await SignInAsync(administrator).ConfigureAwait(true))
            {
                var restoredPages = await ReleaseExpectations.CapturePagesAsync(restoredAdmin).ConfigureAwait(true);
                AssertSame(ExpectationComparer.Compare(before, before with { Pages = restoredPages, Records = await ReleaseExpectations.CountRowsAsync(deployment, checkVolatileList: false).ConfigureAwait(true) }), "step 10");
            }

            output.WriteLine($"step 10: rolled back to {from} in {rollback.Elapsed.TotalSeconds:F0}s");
            Assert.True(rollback.Elapsed <= RollbackBudget, $"Rolling back took {rollback.Elapsed}; the budget is {RollbackBudget} (SC-010).");
        }
        finally
        {
            await deployment.StopAsync().ConfigureAwait(true);
            TryDelete(work);
        }
    }

    /// <summary>
    /// Step 9: an older minor must refuse the upgraded database without writing (SC-006); an
    /// older patch of the same minor may run on it (clarification Q1); the baseline predates
    /// the check, so it only has to start.
    /// </summary>
    private async Task PreviousOnUpgradedAsync(ComposeDeployment deployment, string previousImage, string from, string releaseVersion)
    {
        var sameMinor = from != "baseline" && SameMinor(from, releaseVersion);
        var unchanged = await ReleaseExpectations.FingerprintTablesAsync(deployment).ConfigureAwait(true);

        deployment.Image = previousImage;
        if (from == "baseline" || sameMinor)
        {
            await StartAppAsync(deployment).ConfigureAwait(true);
            output.WriteLine($"step 9: {from} starts on the upgraded database, as it may");
            return;
        }

        await Must(deployment.ComposeAsync("up -d app"), "starting the previous release").ConfigureAwait(true);
        var refused = Stopwatch.StartNew();
        using var probe = new HttpClient { BaseAddress = new Uri(ComposeDeployment.BaseAddress) };
        string? state = null;
        while (refused.Elapsed < RefusalBudget && state != "Incompatible")
        {
            try
            {
                using var ready = await probe.GetAsync(new Uri("/health/ready", UriKind.Relative)).ConfigureAwait(true);
                state = (await ready.Content.ReadFromJsonAsync<JsonElement>().ConfigureAwait(true)).GetProperty("state").GetString();
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }

            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(true);
        }

        Assert.True(state == "Incompatible", $"{from} on a database {releaseVersion} migrated reported '{state}' after {refused.Elapsed}, not Incompatible (SC-006).");
        var logs = await deployment.ComposeAsync("logs --no-color app").ConfigureAwait(true);
        Assert.Contains(releaseVersion, logs.Output, StringComparison.Ordinal);
        Assert.Contains("cannot run on it", logs.Output, StringComparison.Ordinal);
        AssertNoWrites(unchanged, await ReleaseExpectations.FingerprintTablesAsync(deployment).ConfigureAwait(true), "step 9 (the refused start)");
        output.WriteLine($"step 9: {from} refused the upgraded database in {refused.Elapsed.TotalSeconds:F0}s and wrote nothing");
    }

    /// <summary>
    /// The pre-release baseline (RL-15): the image at <c>QB_BASELINE_REF</c>, set up and filled
    /// by the content journey, then dumped like a release's snapshot.
    /// </summary>
    private async Task<(SnapshotBundle Bundle, string Image)> BaselineAsync(ComposeDeployment deployment, string work)
    {
        const string Image = "quickbuilder:baseline";
        var reference = Environment.GetEnvironmentVariable("QB_BASELINE_REF") is { Length: > 0 } r ? r : "pre-0.1.0-baseline";

        var tree = Path.Combine(work, "baseline-src");
        await Must(deployment.RunAsync("git", $"worktree add --detach \"{tree}\" {reference}"), $"checking out {reference}").ConfigureAwait(true);
        try
        {
            await Must(deployment.RunAsync("docker", $"build -q -t {Image} \"{tree}\""), $"building the baseline image at {reference}").ConfigureAwait(true);
        }
        finally
        {
            await deployment.RunAsync("git", $"worktree remove --force \"{tree}\"").ConfigureAwait(true);
        }

        await Must(deployment.ComposeAsync("down -v"), "removing any earlier deployment").ConfigureAwait(true);
        deployment.Image = Image;
        await StartAppAsync(deployment).ConfigureAwait(true);

        var journey = await new ContentJourney(deployment, "baseline").RunAsync(administrator: null).ConfigureAwait(true);
        using (journey.Admin)
        {
            var expectations = await ReleaseExpectations
                .CaptureAsync(deployment, journey.Admin, "baseline", "none", journey.Members, journey.Drafts, checkVolatileList: false)
                .ConfigureAwait(true);
            var bundle = new SnapshotBundle(Path.Combine(work, "baseline"), "baseline");
            await bundle.DumpAsync(deployment, expectations).ConfigureAwait(true);
            output.WriteLine($"baseline: {reference} built, filled ({expectations.Pages.Count} pages) and dumped");
            return (bundle, Image);
        }
    }

    /// <summary>Starts (or waits for) the application and returns how long it took to report ready.</summary>
    private static async Task<TimeSpan> StartAppAsync(ComposeDeployment deployment, bool alreadyUp = false)
    {
        if (!alreadyUp)
        {
            await Must(deployment.ComposeAsync("up -d"), "starting the deployment").ConfigureAwait(false);
        }

        using var client = new HttpClient { BaseAddress = new Uri(ComposeDeployment.BaseAddress) };
        return await deployment.WaitForReadyAsync(client).ConfigureAwait(false);
    }

    /// <summary>Waits until no item is indexed below the newest index version (≤ 5 min).</summary>
    private static async Task WaitForBackgroundWorkAsync(ComposeDeployment deployment)
    {
        const string Pending = "SELECT count(*) FROM cms.items WHERE index_version < (SELECT coalesce(max(index_version), 0) FROM cms.items)";
        var deadline = DateTimeOffset.UtcNow.AddMinutes(5);
        while (true)
        {
            var (exitCode, text) = await deployment.ComposeAsync($"exec -T db psql -U quickbuilder -d quickbuilder -At -c \"{Pending}\"").ConfigureAwait(false);
            if (exitCode == 0 && text.Trim() == "0")
            {
                return;
            }

            Assert.True(DateTimeOffset.UtcNow < deadline, $"Background rewrites did not finish within 5 minutes:\n{text}");
            await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
    }

    private static async Task<HttpClient> SignInAsync(ExpectedMember member)
    {
        var client = new HttpClient { BaseAddress = new Uri(ComposeDeployment.BaseAddress) };
        client.DefaultRequestHeaders.Add("X-QuickBuilder-Request", "1");
        using var response = await ContentJourney.SignInAsync(client, member).ConfigureAwait(false);
        Assert.True(response.IsSuccessStatusCode, $"{member.Email} ({member.Role}) could not sign in: {(int)response.StatusCode}.");
        ContentJourney.UseSession(client, response);
        return client;
    }

    private static void AssertSame(ComparisonResult result, string step) =>
        Assert.True(result.Passed, $"{step}: what the earlier release kept is not all here:\n" + string.Join("\n", result.Failures));

    private static void AssertNoWrites(IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after, string step)
    {
        var changed = before.Keys.Union(after.Keys)
            .Where(t => !before.TryGetValue(t, out var b) || !after.TryGetValue(t, out var a) || a != b)
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.True(changed.Count == 0, $"{step} wrote to: {string.Join(", ", changed)}");
    }

    private static bool SameMinor(string left, string right)
    {
        var a = left.Split('.', '-');
        var b = right.Split('.', '-');
        return a.Length >= 2 && b.Length >= 2 && a[0] == b[0] && a[1] == b[1];
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{name} must be set for the release upgrade gate.");

    private static async Task Must(Task<(int ExitCode, string Output)> step, string what)
    {
        var (exitCode, text) = await step.ConfigureAwait(false);
        Assert.True(exitCode == 0, $"{what} failed ({exitCode}):\n{text}");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary directory is harmless.
        }
    }
}
