using QuickBuilder.Sites.Domain;
using Xunit;

namespace QuickBuilder.UnitTests.Sites.Transfer;

/// <summary>
/// The transfer job's state machine (020 data-model §1.1, research ST-06, ST-15).
/// </summary>
public sealed class TransferJobTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid SiteId = Guid.CreateVersion7();
    private static readonly Guid AdminId = Guid.CreateVersion7();

    private static TransferJob CheckedImport(string mode, Guid? target, bool refused = false)
    {
        var job = TransferJob.ReserveUpload(TransferTriggers.Admin, AdminId, T0);
        job.Uploaded("installation/transfers/x", 1234, T0);
        Assert.True(job.RequestCheck(mode, target, T0).IsSuccess);
        job.Claim("worker-1", T0);
        job.Checked("{}", "{}", "fp-1", refused, T0.AddMinutes(1));
        return job;
    }

    // --- Export ---------------------------------------------------------------------------

    [Fact]
    public void An_export_runs_from_queued_to_succeeded_and_keeps_its_archive_for_seven_days()
    {
        var job = TransferJob.StartExport(SiteId, TransferTriggers.Admin, AdminId, T0);
        Assert.Equal(TransferStates.Queued, job.State);
        Assert.True(job.HoldsSiteSlot);

        job.Claim("worker-1", T0);
        Assert.Equal(TransferStates.Running, job.State);
        Assert.Equal(T0 + TransferJob.LeaseLength, job.LeaseUntilUtc);

        job.ExportSucceeded("site/transfers/x", 42, "{}", T0.AddMinutes(5));

        Assert.Equal(TransferStates.Succeeded, job.State);
        Assert.Equal(T0.AddMinutes(5) + TransferJob.ExportRetention, job.ArchiveExpiresAtUtc);
        Assert.Null(job.LeaseOwner);
        Assert.False(job.HoldsSiteSlot);
        Assert.True(job.IsDownloadable(T0.AddDays(6)));
        Assert.False(job.IsDownloadable(T0.AddDays(8)));
    }

    [Fact]
    public void A_deleted_export_archive_expires_the_job_and_is_no_longer_downloadable()
    {
        var job = TransferJob.StartExport(SiteId, TransferTriggers.Admin, AdminId, T0);
        job.Claim("worker-1", T0);
        job.ExportSucceeded("site/transfers/x", 42, "{}", T0);

        job.ArchiveDeleted(T0.AddHours(1));

        Assert.Equal(TransferStates.Expired, job.State);
        Assert.Null(job.ArchiveKey);
        Assert.False(job.IsDownloadable(T0.AddHours(2)));
    }

    [Fact]
    public void The_pre_restore_export_holds_no_site_slot()
    {
        var job = TransferJob.StartExport(SiteId, TransferTriggers.PreRestore, AdminId, T0);

        Assert.False(job.HoldsSiteSlot);
    }

    // --- Import: upload and check -----------------------------------------------------------

    [Fact]
    public void An_upload_is_checked_and_waits_for_confirmation_without_holding_the_site_slot()
    {
        var job = CheckedImport(TransferModes.Merge, SiteId);

        Assert.Equal(TransferStates.AwaitingConfirmation, job.State);
        Assert.Equal("fp-1", job.CheckFingerprint);
        Assert.False(job.HoldsSiteSlot);
        Assert.Null(job.LeaseOwner);
    }

    [Fact]
    public void A_queued_or_running_check_holds_no_site_slot()
    {
        var job = TransferJob.ReserveUpload(TransferTriggers.Admin, AdminId, T0);
        job.Uploaded("k", 1, T0);
        job.RequestCheck(TransferModes.Restore, SiteId, T0);
        Assert.False(job.HoldsSiteSlot);

        job.Claim("w", T0);
        Assert.Equal(TransferStates.Checking, job.State);
        Assert.False(job.HoldsSiteSlot);
    }

    [Fact]
    public void A_check_with_errors_refuses_the_archive_for_good()
    {
        var job = CheckedImport(TransferModes.NewSite, null, refused: true);

        Assert.Equal(TransferStates.Refused, job.State);
        Assert.True(job.IsFinal);
        Assert.True(job.RequestCheck(TransferModes.NewSite, null, T0).IsFailure);
    }

    [Theory]
    [InlineData(TransferModes.NewSite, true)]
    [InlineData(TransferModes.Merge, false)]
    [InlineData(TransferModes.Restore, false)]
    public void A_check_names_a_target_exactly_when_the_mode_needs_one(string mode, bool withTarget)
    {
        var job = TransferJob.ReserveUpload(TransferTriggers.Admin, AdminId, T0);
        job.Uploaded("k", 1, T0);

        var refused = job.RequestCheck(mode, withTarget ? SiteId : null, T0);

        Assert.Equal("transfer.invalid_options", refused.Error!.Code);
    }

    [Fact]
    public void An_unconfirmed_check_can_be_run_again_against_another_target()
    {
        var job = CheckedImport(TransferModes.NewSite, null);

        var again = job.RequestCheck(TransferModes.Merge, SiteId, T0.AddMinutes(2));

        Assert.True(again.IsSuccess);
        Assert.Equal(TransferStates.Queued, job.State);
        Assert.Null(job.CheckResult);
        Assert.Equal(SiteId, job.SiteId);
    }

    // --- Import: confirm --------------------------------------------------------------------

    [Fact]
    public void Confirming_a_new_site_takes_a_name_and_start_fresh_and_then_holds_no_slot_until_it_has_a_site()
    {
        var job = CheckedImport(TransferModes.NewSite, null);

        var confirmed = job.Confirm(new ImportOptions("  Acme copy ", StartFresh: true, ConflictPolicy: null, CheckFingerprint: "fp-1"), T0.AddMinutes(2));

        Assert.True(confirmed.IsSuccess);
        Assert.Equal(TransferStates.Confirmed, job.State);
        Assert.Equal("Acme copy", job.NewSiteName);
        Assert.True(job.StartFresh);
        Assert.False(job.HoldsSiteSlot);
    }

    [Fact]
    public void Confirming_a_merge_takes_the_site_slot_and_needs_a_policy()
    {
        var job = CheckedImport(TransferModes.Merge, SiteId);

        Assert.Equal("transfer.invalid_options", job.Confirm(new ImportOptions(null, false, null, null), T0).Error!.Code);
        Assert.True(job.Confirm(new ImportOptions(null, false, TransferPolicies.Rename, null), T0).IsSuccess);

        Assert.Equal(TransferPolicies.Rename, job.ConflictPolicy);
        Assert.True(job.HoldsSiteSlot);
    }

    [Theory]
    [InlineData(TransferModes.Merge, true, TransferPolicies.Skip)]
    [InlineData(TransferModes.Restore, true, null)]
    [InlineData(TransferModes.Restore, false, TransferPolicies.Replace)]
    [InlineData(TransferModes.NewSite, false, TransferPolicies.Skip)]
    public void Options_that_do_not_belong_to_the_mode_are_refused(string mode, bool startFresh, string? policy)
    {
        var job = CheckedImport(mode, mode == TransferModes.NewSite ? null : SiteId);

        var refused = job.Confirm(new ImportOptions("Name", startFresh, policy, null), T0);

        Assert.Equal("transfer.invalid_options", refused.Error!.Code);
        Assert.Equal(TransferStates.AwaitingConfirmation, job.State);
    }

    [Fact]
    public void A_check_older_than_24_hours_cannot_be_confirmed()
    {
        var job = CheckedImport(TransferModes.Restore, SiteId);

        var refused = job.Confirm(new ImportOptions(null, false, null, null), T0.AddHours(25));

        Assert.Equal("transfer.check_expired", refused.Error!.Code);
    }

    [Fact]
    public void A_confirmation_based_on_another_check_is_refused_as_a_target_change()
    {
        var job = CheckedImport(TransferModes.Restore, SiteId);

        var refused = job.Confirm(new ImportOptions(null, false, null, "fp-old"), T0);

        Assert.Equal("transfer.target_changed", refused.Error!.Code);
    }

    // --- Import: apply ----------------------------------------------------------------------

    [Fact]
    public void A_confirmed_import_runs_and_on_success_records_its_site_and_lets_the_upload_go()
    {
        var job = CheckedImport(TransferModes.NewSite, null);
        job.Confirm(new ImportOptions("Copy", false, null, null), T0);
        job.Claim("worker-2", T0.AddMinutes(3));
        Assert.Equal(TransferStates.Running, job.State);

        var newSite = Guid.CreateVersion7();
        job.ImportSucceeded(newSite, "{}", T0.AddMinutes(9));

        Assert.Equal(TransferStates.Succeeded, job.State);
        Assert.Equal(newSite, job.SiteId);
        Assert.Equal(T0.AddMinutes(9), job.ArchiveExpiresAtUtc);
    }

    [Fact]
    public void A_restore_records_its_pre_restore_export()
    {
        var job = CheckedImport(TransferModes.Restore, SiteId);
        job.Confirm(new ImportOptions(null, false, null, null), T0);
        job.Claim("w", T0);
        var export = Guid.CreateVersion7();

        job.PreRestoreStarted(export, T0);

        Assert.Equal(export, job.PreRestoreJobId);
        Assert.Equal(TransferSteps.PreRestore, job.Step);
    }

    // --- Leases, cancel and failure ------------------------------------------------------------

    [Fact]
    public void Only_the_lease_owner_renews_and_an_expired_lease_fails_the_job_as_interrupted()
    {
        var job = TransferJob.StartExport(SiteId, TransferTriggers.Cli, null, T0);
        job.Claim("worker-1", T0);

        Assert.False(job.RenewLease("worker-2", T0.AddSeconds(15)));
        Assert.True(job.RenewLease("worker-1", T0.AddSeconds(15)));
        Assert.False(job.FailIfLeaseExpired(T0.AddMinutes(2)));

        Assert.True(job.FailIfLeaseExpired(T0.AddMinutes(3)));
        Assert.Equal(TransferStates.Failed, job.State);
        Assert.Equal(TransferFailures.Interrupted, job.FailureCode);
        Assert.False(job.HoldsSiteSlot);
    }

    [Fact]
    public void Cancelling_a_job_no_worker_holds_stops_it_at_once()
    {
        var job = TransferJob.StartExport(SiteId, TransferTriggers.Admin, AdminId, T0);

        Assert.True(job.RequestCancel(T0).IsSuccess);

        Assert.Equal(TransferStates.Cancelled, job.State);
        Assert.False(job.IsClaimable);
    }

    [Fact]
    public void Cancelling_a_running_job_asks_its_worker_to_stop()
    {
        var job = TransferJob.StartExport(SiteId, TransferTriggers.Admin, AdminId, T0);
        job.Claim("w", T0);

        job.RequestCancel(T0);
        Assert.Equal(TransferStates.Running, job.State);
        Assert.True(job.CancelRequested);

        job.Cancelled(T0.AddSeconds(3));
        Assert.Equal(TransferStates.Cancelled, job.State);
        Assert.Null(job.LeaseOwner);
    }

    [Fact]
    public void A_finished_job_cannot_be_cancelled_and_a_failure_after_the_end_changes_nothing()
    {
        var job = TransferJob.StartExport(SiteId, TransferTriggers.Admin, AdminId, T0);
        job.Claim("w", T0);
        job.ExportSucceeded("k", 1, "{}", T0);

        Assert.Equal("transfer.finished", job.RequestCancel(T0).Error!.Code);
        job.Failed(TransferFailures.Unexpected, T0);
        Assert.Equal(TransferStates.Succeeded, job.State);
    }

    [Fact]
    public void A_job_cannot_be_claimed_twice_or_from_a_waiting_state()
    {
        var job = TransferJob.StartExport(SiteId, TransferTriggers.Admin, AdminId, T0);
        job.Claim("w", T0);

        Assert.Throws<InvalidOperationException>(() => job.Claim("w2", T0));
        Assert.Throws<InvalidOperationException>(() => CheckedImport(TransferModes.NewSite, null).Claim("w", T0));
    }

    [Fact]
    public void An_unconfirmed_check_whose_upload_is_deleted_expires()
    {
        var job = CheckedImport(TransferModes.NewSite, null);

        job.ArchiveDeleted(T0.AddHours(25));

        Assert.Equal(TransferStates.Expired, job.State);
        Assert.True(job.IsFinal);
    }

    [Fact]
    public void Progress_is_clamped_to_whole_percent()
    {
        var job = TransferJob.StartExport(SiteId, TransferTriggers.Admin, AdminId, T0);
        job.Claim("w", T0);

        job.ReportProgress("pages", 0.416, T0);
        Assert.Equal(42, job.Percent);
        job.ReportProgress("pages", 1.7, T0);
        Assert.Equal(100, job.Percent);
    }
}
