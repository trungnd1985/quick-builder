# Quickstart results: Forms and Submissions

**Feature**: `018-forms-submissions` | **Run**: 2026-10-22 | **Guide**: [quickstart.md](./quickstart.md)

This run covered every quickstart step that can be automated, on the test stack and the
reference compose deployment (2 CPUs, 4 GB). The steps that need a person at a browser are
listed under **Still manual**, with the automated evidence that exists for each. They have
not been done.

## Automated results

| Suite | Result |
|---|---|
| .NET unit tests | 2,228 passed |
| Architecture tests | 40 passed (including `FormsLoggingTests`) |
| Integration tests (Testcontainers, PostgreSQL 16, Mailpit v1.30.7) | 1,314 passed, 3 skipped (the 3 skips predate 018), on commit `199a1b5`; the tests added after it pass on their own |
| Admin (vitest, axe) | 1,409 passed, lint clean |
| Smoke journey (`ReferenceDeploymentSmokeTests`, built image) | passed, including step 9k: a published form submitted on the site's host name and found in Submissions |
| Line coverage, Forms module | 93.4 % overall; `SubmitPolicy` and `FormValueRules` 100 %, `SubmitHandler` 96.3 %, `SubmitController` 92.2 %, `RateWindows` 100 %, `FormsSweepService` 91.9 %, `NotificationWorker` 88.6 % (target 80 %) |

## Measurements

| Criterion | Budget | Measured (p95 unless noted) |
|---|---|---|
| SC-006 list, 100,000 submissions | < 2 s | 8.7 ms |
| SC-006 filtered list (form, unread) | < 2 s | 5.5 ms |
| SC-006 search, one word | < 2 s | 31.2 ms |
| SC-006 search matching every row | < 2 s | 4.9 ms |
| SC-006 export of 10,158 rows | < 30 s | 228 ms (single run) |
| Submit endpoint (plan goal) | 300 ms | 12.8 ms (p50 10.8 ms), 200 submits |
| SC-009 page with two forms, not cached | 200 ms | 10.8 ms (p50 8.7 ms) |
| SC-009 page with two forms, cached | 20 ms | 5.9 ms (p50 4.6 ms) |
| SC-005 notification handed to the mail server | 1 min | within the worker's 5 s poll (`NotificationTests`, hosted worker) |

## Step by step

| Quickstart step | Evidence | Status |
|---|---|---|
| §1 Build a form; publish refused when incomplete | `FormBlock.spec.ts`, `FormPublishGuardTests`, `FormPublishRulesTests` | automated; the 5-minute timing (SC-001) not measured |
| §2 Submit with and without scripts; crafted requests; 413; unknown form | `SubmitEndpointTests`, `FormScriptTests`, `formScript.spec.ts` | automated; the 1-minute visitor timing (SC-002) not measured |
| §3 Read, search, export, roles, other site | `SubmissionEndpointsTests`, `SubmissionExportTests`, `CsvWriterTests`, `FormsIsolationProbe`, admin specs | automated; opening the CSV in a spreadsheet application not done |
| §4 Notifications: recipients, values off, retry after an outage, header injection, email off, test email | `NotificationTests`, `SmtpEmailSenderTests`, `EmailEndpointsTests`, `TestEmail.spec.ts`, `FormNotifyControl.spec.ts` | automated (the outage is made with Mailpit's chaos switch, not by stopping it) |
| §5 Spam and limits | `SpamAndRateLimitTests`, `SpamView.spec.ts`, `FormGoldenTests` (trap markup) | automated; screen-reader check of the trap field is manual |
| §6 Retention and clean-up | `FormsSweepTests`, `FormRetentionControl.spec.ts` | automated |
| §7 Accessibility and output | axe in the admin specs; `FormGoldenTests`; CSP in `SubmitEndpointTests` and `UpgradeFrom024Tests` | automated part done; screen-reader pass manual |
| §8 Upgrade | `UpgradeFrom024Tests` | automated |

## Still manual

- **Screen-reader pass** (NVDA + Firefox, VoiceOver + Safari) on a published form with and
  without scripts: labels, required markers, errors, summary focus, success announcement, and
  that the trap field is never reached. Waits for a person or the browser smoke test of #25
  (exception E-5).
- **SC-001 and SC-002 timings**: a Designer building a six-field form in under 5 minutes, a
  visitor completing one in under a minute.
- **Opening the export in a spreadsheet application** (§3.5). The file's encoding, quoting and
  formula neutralisation are covered by `CsvWriterTests` and `SubmissionExportTests`.

## Deviations from the guide

- §6 refers to `tests/fixtures/forms/seed-old-submissions.sql`, which was not created; the
  sweep tests age rows with SQL in the test instead. Seeding a dev database by hand needs an
  equivalent `UPDATE forms.submissions SET received_at_utc = …`.
- §4.3 stops Mailpit to simulate an outage. The tests use Mailpit's chaos switch instead,
  because restarting a container maps a new port; an unreachable server is covered by
  `SmtpEmailSenderTests` with a closed port.
- The rate limit counts a request after its body is read (tasks T084, *Done as*), not before
  the body-size check of data-model §4 row 2.
