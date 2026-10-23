# Quickstart: validating Forms and Submissions

**Feature**: `018-forms-submissions` | **Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

How to prove the feature works end to end. Automated suites are listed in
[research.md](./research.md) FS-22; this guide is the manual and run-through part.

## 0. Prerequisites

- The dev stack: `docker compose -f docker-compose.dev.yml up` (app + PostgreSQL), or
  `./qb.ps1` as for earlier features.
- A mail catcher for notifications:
  `docker run -d --name mailpit -p 8025:8025 -p 1025:1025 axllent/mailpit`, then set
  `Email__Smtp__Host=localhost`, `Email__Smtp__Port=1025`, `Email__Smtp__Security=None`,
  `Email__Smtp__From=site@example.test` and `Hosting__PublicUrl=http://localhost:8080`.
  Mail appears at `http://localhost:8025`.
- A site with a host name (for example `acme.localhost`), a Designer, an Editor and an
  Administrator.

Automated: `dotnet test` (unit, integration with Testcontainers, smoke) and
`npm --prefix src/admin test`.

## 1. Build a form (User Story 1, SC-001)

1. As the Designer, open the Contact page, add a **Form** block, name it "Contact enquiry".
2. Add Name (text, required), Email (email, required), Phone, Topic (dropdown: Sales,
   Support, Other), Message (multi-line, required, max 2,000), and a required consent with a
   link to the Privacy page. Reorder with the move buttons and with the keyboard.
3. Remove Topic's options: the panel lists the problem; **Publish** is refused with
   `form_invalid` naming the form. Restore them.
4. Set the success message and publish. Time it: under 5 minutes.

Expected: the published page shows every field in order with labels, help and required
markers; reloading the editor shows the form exactly as saved.

## 2. Submit with and without scripts (User Story 2, SC-002, SC-003)

1. Open `http://acme.localhost:8080/contact`. Submit with Message empty and Email `jane@`.
   - Expected: both fields show messages; the summary at the top links to them and has focus;
     typed values stay.
2. Fix and submit. Expected: the success message replaces the form within 2 seconds.
3. Turn scripts off in the browser and repeat 1 and 2. Expected:
   - the refusal is a `422` page with the same messages and values;
   - success is a `303` to `/contact#qb-sent-…`, showing the message;
   - reloading sends nothing.
4. Set a redirect to a "Thank you" page, publish, submit. Expected: you land on it.
5. Crafted requests (from a terminal):
   ```bash
   curl -i -H "Host: acme.localhost" -d "_page=<pageId>&_path=/contact&f.<emailId>=x" \
        http://localhost:8080/_forms/<formId>
   ```
   Expected: `422`; an extra key `f.bogus=1` is never stored; a body over 256 KB is `413`;
   an unknown form id is `503` with the error message.

## 3. Read, search, export (User Story 3, SC-006, SC-007)

1. As the Editor, open **Submissions**: newest first, unread marked, a badge in navigation.
2. Filter by form and the last 30 days, search "hosting", open one (it becomes read), mark it
   unread again.
3. Try to delete as the Editor: no delete action; the API answers `403`.
4. As the Designer, delete one; check the submission log entry.
5. Export to CSV, open in a spreadsheet application. Include a submission whose message is
   `=HYPERLINK("http://x")` and one in Vietnamese with line breaks. Expected: correct
   characters, the formula shown as text, one column per field label.
6. As a member of another site only, request this site's submissions. Expected: `404`.

Performance (SC-006) is measured by `SubmissionListPerformanceTests` (100,000 rows); record
its numbers in `quickstart-results.md`.

## 4. Notifications (User Story 4, SC-005)

1. Add two recipients, use Email as the reply field, publish, submit. Expected: one email in
   Mailpit to both, subject as set, every value, a link to the submission, `Reply-To` the
   visitor.
2. Turn "include values" off, publish, submit. Expected: no values, no `Reply-To`.
3. Stop Mailpit, submit, then check the submission: notification `pending`. Start Mailpit
   within an hour: it arrives on a retry and shows `sent`.
4. Submit a value with `\r\nBcc: x@y` in the name. Expected: no extra header.
5. Remove the `Email__Smtp__*` settings and restart: the app starts; the editor warns on the
   form; new submissions show "notification not sent".
6. As Administrator, send a test email from installation settings: success, or the server's
   error when Mailpit is stopped.

## 5. Spam and limits (User Story 5, SC-004)

1. With browser tools, fill `_hp` and submit valid values. Expected: success shown; the
   submission is in **Spam**, not in the inbox, no email. Restore it: it appears unread and an
   email is sent.
2. Fill `_hp` with invalid values: success shown, nothing stored, counter `trapped_dropped`
   increases.
3. Submit six times within a minute from one address. Expected: the sixth is `429` with the
   rate-limit message and `Retry-After`.
4. Double-click submit with scripts off (or replay the same request within 10 s). Expected:
   one submission, one email.
5. With a screen reader and the keyboard, the trap field is never reached or announced.

## 6. Retention and clean-up (FR-023a, FR-016a)

1. Set retention 30 days on a form with older submissions (seed with
   `tests/fixtures/forms/seed-old-submissions.sql` on a dev database). The panel warns how
   many will be deleted.
2. Publish; run the sweep (restart, or wait for the hourly run). Expected: older submissions
   gone, a `retention_purge` log row with the count. Spam older than 30 days is gone too.

## 7. Accessibility and output checks (SC-008, SC-009)

- axe in `FormGoldenTests` and the admin specs passes.
- Manual screen-reader pass (NVDA + Firefox, VoiceOver + Safari) on the published form with
  and without scripts: labels, required, errors, summary focus, success announcement.
  (Manual until the browser smoke test of #25, exception E-5.)
- `FormRenderPerformanceTests`: a page with two forms stays within 012's budgets.
- The page CSP has `form-action 'self'` and `connect-src 'self'` only on pages with a form.

## 8. Upgrade (FR-041)

`UpgradeFrom024Tests`: on a 024 database snapshot, the migration adds the `forms` schema,
existing pages render the same bytes and CSP, and nothing else changes.
