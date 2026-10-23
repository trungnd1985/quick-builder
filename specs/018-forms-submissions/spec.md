# Feature Specification: Forms and Submissions

**Feature Branch**: `018-forms-submissions`

**Created**: 2026-10-21

**Status**: Draft

**Issue**: #18

**Input**: User description: "#18 Forms and submissions. Problem / goal: Most sites need to collect enquiries. A designer can place a form block with fields on a page, and an editor can read the submissions and be notified by email when one arrives. In: form block with configurable fields and validation, server-side validation and spam protection (honeypot plus rate limiting), submission storage per site, submission list with search and CSV export, email notification via SMTP, success/error states and redirect after submit. Out: payment fields, third-party CRM integrations, multi-step forms."

## Context

Features 009 to 024 give each site a page tree, a visual editor with content blocks, shared
components, publishing with server-side rendering and exact cache refreshes, collections,
canonical addresses and menus. Everything a visitor sees is read-only: a site has no way to
hear back from its visitors. Today a site that wants enquiries must link to an email address
or embed a third-party form service, which leaks visitor data to another company and breaks
when that service changes.

This feature adds:

- a **Form block** that a member places on a page or component and fills with fields
  (text, email, phone, choices, consent and others), each with its own validation rules;
- **submission handling** on the server: every value is checked again on the server, and
  spam is held back by a hidden trap field and a limit on how often one visitor may submit;
- **submission storage** per site and per form, read in the admin through a list with search,
  filters, a detail view and CSV export;
- **email notification** to chosen recipients when a submission arrives, sent through the
  installation's SMTP server;
- **success and error states** on the published page, and an optional redirect to a thank-you
  page after a successful submission.

Constitution Principle IV applies because email goes through SMTP behind an abstraction and
the product must work when no SMTP server is configured. Principle V applies because forms and
submissions belong to one site. Principle VI applies because the Form block is defined once
like every other block. Principle VII applies because visitors can only submit to forms that
are published. Principle VIII applies because published pages stay cached and the form works
without scripts. Principle IX applies because submissions are personal data from anonymous
visitors: they are validated, encoded wherever they are shown, kept out of logs, and visible
only to members of the site.

## Clarifications

### Session 2026-10-21

- Q: Who should be allowed to read, export and delete a site's form submissions? → **A: every
  role on the site can read, mark read and export; only Designers and Administrators can
  delete** (FR-025, FR-028).
- Q: Should submissions be deleted automatically after a set time, or kept until someone
  deletes them? → **A: a retention setting per form (30, 90, 180 or 365 days), off by
  default**; when set, older submissions of that form are deleted automatically each day
  (FR-003, FR-023, FR-023a).
- Q: Should a form be defined inside its block on a page, or managed once per site in a
  "Forms" list that blocks then point at? → **A: inside the Form block**, published with its
  page or component; a form is reused on several pages by placing it in a shared component
  (FR-002, FR-004).
- Q: Should notification emails contain the submitted values, or only say that a submission
  arrived and link to it in the admin? → **A: a per-form setting: include values (the
  default) or link only**; a link-only email carries no visitor data and no reply address
  (FR-032, FR-033).
- Q: When a submission fills in the hidden trap field, should it be thrown away, or kept in a
  separate "Spam" list that members can check and restore from? → **A: kept in a Spam list**
  without notification and deleted automatically after 30 days; any member can restore one to
  the normal list, and Designers and Administrators can delete spam (FR-016, FR-016a).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Building a contact form on a page (Priority: P1)

A Designer opens the Contact page in the editor and adds a **Form** block. They name the form
"Contact enquiry" and add fields: "Name" (single-line text, required), "Email" (email,
required), "Phone" (phone, optional), "Topic" (dropdown: Sales, Support, Other), "Message"
(multi-line text, required, at most 2,000 characters) and "I agree to be contacted" (consent
checkbox, required). They reorder the fields, set the submit button's label to "Send", write
the success message "Thanks, we will reply within one working day", and publish the page.

**Why this priority**: Without a form there is nothing to submit, store or notify about.

**Independent Test**: Create a form with one field of every type and every validation rule.
Reload the editor and confirm the form is stored exactly. Publish the page and confirm the
published page shows every field with its label, help text, required marker and choices, in
order, with and without scripts.

**Acceptance Scenarios**:

1. **Given** a page or component draft,
   **When** a member who may edit it adds a Form block,
   **Then** the block appears with a name, one email field and a submit button, ready to edit.
2. **Given** a Form block,
   **When** the member adds, edits, reorders or removes fields,
   **Then** each field keeps its type, label, help text, placeholder, required setting and
   type-specific rules, and the canvas shows the form as visitors will see it.
3. **Given** a form with an invalid setup (no fields, two fields with the same name, a choice
   field without options, a minimum above the maximum),
   **When** the member saves,
   **Then** the editor names each problem, and publishing the page is refused until it is fixed.
4. **Given** a published page with a form,
   **When** a visitor requests it,
   **Then** the form is part of the server-rendered page, every field has a visible label tied
   to its input, and required fields are marked visibly and for assistive technology.

---

### User Story 2 - A visitor submits the form and gets clear feedback (Priority: P1)

A visitor fills in the Contact form, forgets the message and types "jane@" as their email.
The form points at both problems next to the fields and in a summary at the top, keeps
everything they typed, and moves focus to the summary. They fix the fields and submit again.
The form is replaced by the success message. On another site, the form is set to redirect to
a "Thank you" page instead.

**Why this priority**: Collecting the enquiry is the point of the feature; a visitor who is
not told what went wrong gives up.

**Independent Test**: For every field type and rule, submit valid values, invalid values and
crafted requests that skip the page's own checks (missing fields, extra fields, values not in
the choice list, oversized values). Repeat with scripts on and off. Confirm that only valid
submissions are stored, that each refusal names every failing field, and that the success
message or redirect follows a valid submission.

**Acceptance Scenarios**:

1. **Given** a published form,
   **When** a visitor submits values that pass every rule,
   **Then** the submission is stored, and the visitor sees the form's success message in place
   of the form or is sent to the form's redirect target.
2. **Given** a published form,
   **When** a visitor submits a value that breaks a rule,
   **Then** nothing is stored, each failing field shows its own message, a summary lists all
   failing fields with links to them, focus moves to the summary, and every entered value is
   kept.
3. **Given** a request that bypasses the page (sent directly, with scripts off, or edited by
   hand),
   **When** it reaches the server,
   **Then** the server applies every rule of the published form again and refuses the
   submission as in scenario 2 if any rule fails; fields that the form does not define are
   ignored and never stored.
4. **Given** a visitor whose browser runs no scripts,
   **When** they submit the form,
   **Then** they get the same outcome as with scripts: the page shown again with errors and
   their values, or the success message, or the redirect.
5. **Given** a problem on the server that prevents storing the submission,
   **When** the visitor submits,
   **Then** they see the form's error message, their values are kept so they can try again,
   and no partial submission is stored.

---

### User Story 3 - An editor reads, searches and exports submissions (Priority: P1)

An Editor opens **Submissions** for the site. They see the newest submissions first, with the
form name, the page it came from, the time, a short preview and a "new" marker for unread
ones. They filter by "Contact enquiry" and the last 30 days, search for "hosting", open a
submission to read every value, and mark it as handled. A Designer deletes an obvious test
entry. At the end of the month the Editor exports the filtered list to CSV and opens it in a
spreadsheet.

**Why this priority**: Submissions nobody can read have no value. With User Stories 1 and 2
this is the smallest useful feature, even before email notifications.

**Independent Test**: Store submissions on two forms of two sites, including values with
commas, quotes, line breaks, non-Latin text and values that start with `=`, `+`, `-` or `@`.
Filter, search, open, mark and delete as each role. Export and open the file in a spreadsheet
application. Confirm columns, values and that no member sees another site's submissions.

**Acceptance Scenarios**:

1. **Given** a member with any role on a site,
   **When** they open Submissions,
   **Then** they see that site's submissions, newest first, each with its form, source page,
   time received, read state and the first values of the submission.
2. **Given** the submission list,
   **When** the member filters by form, by date range or by read state, or types search words,
   **Then** the list shows only submissions that match all filters and whose values contain
   the words, regardless of letter case.
3. **Given** a submission,
   **When** the member opens it,
   **Then** they see every value under the field label it was submitted with, the form, the
   page, the time, and the state of its email notification, and the submission is marked read.
4. **Given** a filtered list,
   **When** the member exports it,
   **Then** they receive a CSV file with one row per submission, a header row of field labels,
   columns for time, form and page, and the values of every field that appears in the
   exported submissions, in a form spreadsheet applications open correctly.
5. **Given** one or more submissions,
   **When** a Designer or Administrator of the site deletes them after confirming,
   **Then** they are removed permanently, and the deletion is recorded with who did it and
   how many were deleted.
6. **Given** a member with the Editor role,
   **When** they try to delete a submission by any means,
   **Then** the request is refused, and the admin does not offer deletion to them.
7. **Given** a member of another site only,
   **When** they request this site's submissions or export by any means,
   **Then** the request is refused.

---

### User Story 4 - Email notification when a submission arrives (Priority: P2)

The Designer sets the Contact form to notify "sales@client.example" and
"office@client.example" with the subject "New enquiry from the website". When a visitor
submits, both recipients receive an email listing the submitted values and a link to the
submission in the admin. Replying to the email answers the visitor, because the form uses
the visitor's email field as the reply address. One day the mail server is down: the
submission is still stored, the email is retried, and the submission shows that its
notification failed after the last attempt.

**Why this priority**: Notification makes the feature useful day to day, but submissions are
already safe and readable without it, so it comes after User Stories 1 to 3.

**Independent Test**: With a test mail server, submit forms with zero, one and ten recipients,
with and without a reply-address field, and with values containing line breaks and
header-like text. Then stop the mail server, submit, and start it again. Confirm the emails'
recipients, subject, reply address and content, the retries, and the notification state
shown on each submission.

**Acceptance Scenarios**:

1. **Given** a published form with notification recipients, values included (the default) and
   a configured SMTP server,
   **When** a submission is stored,
   **Then** each recipient receives one email with the form name, the source page, the time,
   every submitted value under its label, and a link to the submission in the admin.
2. **Given** a form with "include values in notifications" turned off,
   **When** a submission is stored,
   **Then** each recipient's email names the form, the page and the time and links to the
   submission, and contains no submitted value and no reply address from the visitor.
3. **Given** a form whose notification includes values and uses a field of type email as its
   reply address,
   **When** the submitted value is valid,
   **Then** the email's reply address is that value; otherwise the email has no reply address
   from the visitor.
4. **Given** the mail server cannot be reached,
   **When** a submission is stored,
   **Then** the visitor still sees success, the email is retried, and the submission shows
   the notification as pending, sent or failed.
5. **Given** an installation with no SMTP server configured,
   **When** a member sets notification recipients on a form,
   **Then** the editor warns that notifications will not be sent until an operator configures
   email, and submissions are still stored and shown as "notification not sent".
6. **Given** an Administrator,
   **When** they send a test email from the admin,
   **Then** it is sent to their own address, and the admin shows success or the mail server's
   error.

---

### User Story 5 - Spam and abuse are kept out (Priority: P2)

A bot fills in every field it finds, including a field visitors never see, and posts the form
hundreds of times a minute. Its first submissions land in the site's Spam list, where nobody
is notified, and the rest are stopped by the rate limit; the bot gets no hint that it was
caught. A real visitor whose password manager filled the hidden field finds their enquiry in
Spam, and an Editor restores it. A real visitor who double-clicks the submit button creates
one submission, not two.

**Why this priority**: A public form without spam protection fills the list with junk and
floods recipients' inboxes within days. It builds on User Story 2.

**Independent Test**: Submit with the trap field filled, at a rate above the limit from one
address, from many addresses at a normal rate, and twice with the same submission in quick
succession. Restore a submission from Spam and let another pass 30 days. Confirm what is
stored, where, what is notified and what each sender sees.

**Acceptance Scenarios**:

1. **Given** a published form,
   **When** a submission that passes the form's rules arrives with the hidden trap field
   filled,
   **Then** it is stored in the site's Spam list, not notified, and the sender receives the
   same response as a successful submission.
2. **Given** a submission in the Spam list,
   **When** a member of the site restores it,
   **Then** it moves to the normal submission list as unread, and its notification is sent
   as for a new submission.
3. **Given** a submission that has been in the Spam list for 30 days,
   **When** the daily clean-up runs,
   **Then** it is deleted permanently.
4. **Given** one visitor address has sent more submissions to a site than the limit allows in
   the current time window,
   **When** it submits again,
   **Then** nothing is stored, and the visitor sees a message asking them to try again later.
5. **Given** a visitor who submits the same form twice within a few seconds (for example by
   double-clicking),
   **When** the second request arrives,
   **Then** only one submission is stored and notified, and both requests show success.
6. **Given** a hidden trap field,
   **When** a person using assistive technology or the keyboard fills in the form,
   **Then** the trap field is never announced or reachable.

---

### Edge Cases

- **A field is removed or renamed after submissions were stored**: old submissions keep their
  values with the label they were submitted under. Exports include a column for every field
  found in the exported submissions, removed fields included.
- **The form block is deleted or its page is unpublished or deleted**: stored submissions
  stay, listed under the form's last name and marked "form no longer published"; new
  submissions to it are refused with the form's error message.
- **A page draft changes the form but is not published yet**: visitors keep submitting to the
  published form, and the server validates against the published fields only.
- **A retention period is shortened or turned on for a form with old submissions**: once the
  page is published, the next daily run deletes every submission older than the new period.
  The editor warns how many existing submissions that will delete before the member saves.
- **A Form block is copied or duplicated**: the copy is a new form with its own submissions;
  submissions never move between forms.
- **The same form appears on several pages through a component**: it is one form; each
  submission records the page it was sent from.
- **A visitor leaves the page open across a publish that changes the form**: the submission is
  checked against the form as now published; values for removed fields are dropped, and new
  required fields that are missing are reported as errors.
- **Values containing script-like text, angle brackets, quotes or line breaks**: stored as
  typed, shown as text in the admin and in emails, and never able to add email headers or
  break out of an element or attribute.
- **CSV values that start with `=`, `+`, `-`, `@`, tab or carriage return**: exported so that
  spreadsheet applications show them as text and never run them as formulas.
- **A visitor's email value in an email field with spaces or uppercase letters**: surrounding
  spaces are removed; letter case is kept.
- **A redirect target page is unpublished or deleted**: the success message is shown instead,
  and the editor marks the form's redirect as broken. Publishing a page whose form redirects to
  a page that is not live is refused, naming the target.
- **A redirect to an external address**: only `https` and `http` addresses are allowed
  (011 FR-015).
- **Many visitors behind one shared address** (an office, a mobile network): the rate limit
  is generous enough for normal use; operators can raise it.
- **Recipients change while a notification is being retried**: later attempts go to the
  recipients as now published.
- **A notification recipient address is invalid**: the editor refuses it when saving the form.
- **A very large submission list** (100,000 submissions on a site): the list, search and
  export still work within the limits in the Success Criteria.
- **An export started while new submissions arrive**: the file contains the submissions that
  matched when the export started.
- **A submission arrives for a site that is disabled**: it is refused as every request to a
  disabled site is (009).
- **The submit button is pressed while a previous submission is in progress**: the button is
  disabled with scripts; without scripts, the duplicate rule (User Story 5, scenario 3)
  applies.

## Requirements *(mandatory)*

### Functional Requirements

**Form block**

- **FR-001**: A Form block type MUST be added to the editor's palette, defined once with its
  properties, defaults, editor controls and renderer like the other block types (constitution
  Principle VI). Adding it MUST NOT change other block types. It MUST be documented in the page
  schema contract and the shared schema package, with a schema version.
- **FR-002**: Members who may edit a page or component draft (011) MUST be able to add,
  configure, move and remove Form blocks in it. A Form block's changes reach visitors only when
  its page or component is published (constitution Principle VII).
- **FR-003**: A Form block MUST have: a form name (required, at most 100 characters, used in
  the admin and in notifications), an ordered list of fields, the submit button's label, a
  success message, an error message, an optional redirect target, notification recipients,
  a notification subject, whether notifications include values (FR-032), an optional
  reply-address field and a retention period (FR-023a).
  Messages and labels have sensible defaults.
- **FR-004**: A form MUST be defined entirely by its Form block; there is no separate list of
  forms to manage. Each form MUST have a stable identity that survives renames, page moves and
  field changes, so its submissions stay together. A copied Form block MUST get a new identity.
  The same form is shown on several pages only by placing its block in a shared component.
  *(Clarified 2026-10-21.)*
- **FR-005**: A form MUST offer these field types: single-line text, multi-line text, email,
  phone, number, date, dropdown (one choice), radio buttons (one choice), checkboxes (several
  choices), single checkbox, and consent checkbox (a single checkbox with a statement that
  may contain a link to a page).
- **FR-006**: Each field MUST have a label (required, at most 200 characters), an optional
  help text and placeholder, a required setting, and a stable identifier that never changes
  once the field exists. Stored values are keyed by that identifier (constitution
  Principle VI).
- **FR-007**: Fields MUST support these rules: required; minimum and maximum length for text
  fields (single-line at most 500 characters, multi-line at most 10,000); valid address for
  email; digits, spaces and the characters `+ ( ) -` only for phone; minimum, maximum and
  whole-number-only for number; earliest and latest date for date; options list (1 to 100
  options, each at most 200 characters) for choice fields, with minimum and maximum selections
  for checkboxes. A field MAY have its own error message; otherwise a default message naming
  the field and the rule is used.
- **FR-008**: A form MUST have between 1 and 50 fields, and field labels MUST be unique within
  the form regardless of letter case. Saving the draft MUST show every setup problem;
  publishing MUST be refused, naming the form and the problem, while any remains.

**Rendering and visitor experience**

- **FR-009**: On the site, the Form block MUST be rendered on the server as a standard HTML
  form that works without scripts. Every input MUST have a visible label tied to it, required
  fields MUST be marked visibly and for assistive technology, and help text MUST be announced
  with its field. The form MUST use the site's design tokens.
- **FR-010**: Scripts MAY check rules in the browser before sending and submit without a page
  reload, as progressive enhancement only. They MUST add at most 15 KB to a published page
  (constitution Principle VIII), and the outcome MUST be the same as without scripts.
- **FR-011**: When a submission is refused because of field rules, the visitor MUST see a
  message next to each failing field, a summary at the top listing every failing field with a
  link to it, and every value they entered. Focus MUST move to the summary, and the field
  messages MUST be announced to assistive technology.
- **FR-012**: After a successful submission, the visitor MUST either see the form's success
  message in place of the form, announced to assistive technology, or be sent to the form's
  redirect target: a page of the same site (by identifier, so it follows moves) or an external
  `https`/`http` address. Reloading after success MUST NOT submit again.
- **FR-013**: When the submission cannot be accepted for a reason other than field rules
  (the form is no longer published, a server problem), the visitor MUST see the form's error
  message with their values kept. When the rate limit is reached, the visitor MUST see a
  message asking them to try again later.
- **FR-014**: Published pages that contain a form MUST stay cacheable like other pages
  (012 FR-031). A response that shows a visitor's values or errors MUST NOT be cached or shown
  to anyone else.

**Server-side validation and spam protection**

- **FR-015**: The server MUST accept submissions only for forms that are part of a live page
  of the site the request is addressed to, and MUST validate every value against the published
  form's rules, whatever the browser checked. Values for fields the form does not define MUST
  be dropped. A request larger than 256 KB MUST be refused.
- **FR-016**: Every form MUST include a hidden trap field that people do not see, reach or
  hear. A submission with the trap field filled MUST get the same response as a successful
  one and MUST NOT be notified. If it passes the form's rules, it MUST be stored in the site's
  Spam list (FR-016a); otherwise it MUST be dropped. *(Clarified 2026-10-21.)*
- **FR-016a**: The Spam list MUST be separate from the submission list: spam is not counted as
  unread, not shown in the normal list, search or export, and not notified. Members with any
  role on the site MUST be able to view spam and restore a spam submission, which moves it to
  the normal list as unread and sends its notification as for a new submission. Designers and
  Administrators MUST be able to delete spam. Spam submissions MUST be deleted automatically
  30 days after they were received, whatever the form's retention setting. Restores and
  deletions are recorded as in FR-030. *(Clarified 2026-10-21.)*
- **FR-017**: Submissions MUST be limited per visitor address and site. By default at most 5
  submissions per minute and 30 per hour from one address to one site are accepted; operators
  MUST be able to change both limits in configuration. Requests over the limit are not stored.
- **FR-018**: A second submission of the same values to the same form within 10 seconds MUST
  NOT create a second submission or notification. Visitors are not told apart, so that no
  visitor data is stored (FR-021).
- **FR-019**: The number of submissions caught by the trap field (kept as spam or dropped) and
  refused by the rate limit MUST be counted per site and form and shown to members in
  Submissions, without storing the refused or dropped values.

**Submission storage**

- **FR-020**: Every accepted submission MUST be stored with its site, form, the page it was
  sent from, the time received, its values keyed by field identifier together with each
  field's label and type at submission time, its read state, its notification state and
  whether it is spam (FR-016a).
- **FR-021**: The visitor's network address, browser details and other data not entered in
  the form MUST NOT be stored with the submission. Data needed for the rate limit MUST be kept
  only for the length of its time window.
- **FR-022**: Submission values MUST NOT appear in logs, error reports or audit records
  (constitution Principle IX). Logs refer to submissions and forms by identifier.
- **FR-023**: Submissions MUST be kept until a Designer or Administrator deletes them, the
  form's retention period ends (FR-023a), the 30-day spam period ends (FR-016a), or the site
  is deleted.
- **FR-023a**: Each form MUST have a retention setting: off (the default) or 30, 90, 180 or
  365 days. It is set and published with the Form block (FR-002). When it is on, the built-in
  background jobs MUST delete, at least once a day, every submission of that form received
  longer ago than the period, using the form's last published setting even after the form is
  no longer published. Each run that deletes submissions MUST be recorded with the form and
  how many were deleted, without the values. The submission list and the editor MUST show the
  form's retention period. *(Clarified 2026-10-21.)*
- **FR-024**: Submissions MUST belong to exactly one site, MUST be separated by site as every
  site-owned record (constitution Principle V), and MUST be covered by the site isolation
  suite.

**Submission list, search and export**

- **FR-025**: Members with any role on a site (Administrator, Designer or Editor) MUST be able
  to list, open, search, filter, mark read or unread and export that site's submissions. Only
  Designers and Administrators of the site MUST be able to delete them. Members without a role
  on the site MUST be refused on every endpoint. *(Clarified 2026-10-21.)*
- **FR-026**: The list MUST show submissions newest first, paged, with form name, source
  page, time received, read state, notification state and a preview of the first values. The
  admin navigation MUST show the number of unread submissions for the current site.
- **FR-027**: The list MUST be filterable by form, by date range and by read state, and
  searchable by words found in any submitted value, ignoring letter case. Filters and search
  combine.
- **FR-028**: Designers and Administrators MUST be able to delete one submission or a
  selection of submissions, after confirming. Deletion is permanent.
- **FR-029**: Members MUST be able to export the current filtered list as a CSV file encoded
  as UTF-8 that spreadsheet applications open with the right characters, with a header row,
  columns for time received, form and page, and one column per field label found in the
  exported submissions. Values starting with `=`, `+`, `-`, `@`, tab or carriage return MUST
  be written so they are treated as text, never as formulas.
- **FR-030**: Viewing a submission marks it read. Exports and deletions MUST be recorded with
  who did them, when, which filters were used and how many submissions they covered, without
  the submitted values.

**Email notification**

- **FR-031**: A form MAY have 0 to 10 notification recipients, each a valid email address,
  and a subject of at most 200 characters (default: "New submission: " followed by the form
  name). Setting recipients follows FR-002.
- **FR-032**: For each accepted submission on a form with recipients, the product MUST send
  one email to the recipients containing the form name, the source page, the time and a link
  to the submission in the admin. Each form MUST have a setting "include values in
  notifications", on by default: when on, the email also lists every value under its label;
  when off, the email contains no submitted value. Values MUST be written as plain text, and no
  value may add or change email headers. *(Clarified 2026-10-21.)*
- **FR-033**: When a form includes values in notifications and names one of its email fields
  as the reply-address field, and the submitted value is a valid address, the notification's
  reply address MUST be that value. A link-only notification MUST NOT carry a reply address
  from the visitor, and the editor MUST disable the reply-address field while values are off.
- **FR-034**: Notifications MUST be sent after the submission is stored, in the background,
  so that a slow or unavailable mail server never delays or fails the visitor's submission.
  Failed sends MUST be retried at least 5 times over at least 1 hour, and sending MUST happen
  at most once per submission even with several instances.
- **FR-035**: Each submission MUST show its notification state: not needed (no recipients),
  not sent (email not configured), pending, sent, failed with the mail server's reason, or
  none (spam, which is never notified unless restored).
- **FR-036**: The SMTP server, its port, connection security, sign-in and the sender address
  MUST be set by the operator in configuration for the whole installation, with the password
  taken from a secret, never stored in the database (constitution Principles II, IV, IX). With
  no SMTP configuration, the product MUST start and work, with notifications off. A present
  but invalid configuration MUST fail at startup with a clear message.
- **FR-037**: Administrators MUST be able to send a test email to their own address from the
  admin and see the result. The editor MUST warn on any form with recipients while email is
  not configured.

**Contracts, upgrade and portability**

- **FR-038**: Every submission, list, export and test-email operation MUST check the
  member's role on the site on the server (constitution Principle IX), and every refusal MUST
  state a machine-readable reason.
- **FR-039**: Publishing a page or component MUST be refused, naming the form and the target,
  when a Form block on it redirects to a page that is not live.
- **FR-040**: Export and import of a site (constitution Principle III) MUST include forms as
  part of the pages and components that hold them, with redirect targets mapped to the
  imported pages. Submissions are not part of a site export. Building export and import is
  the obligation of #20.
- **FR-041**: The upgrade from the previous release MUST add submission storage without
  changing existing pages, components or published versions.

### Key Entities

- **Form**: defined by a Form block in a page or component and published with it. It has a
  stable identity, a name, an ordered list of fields, messages, an optional redirect target,
  notification settings and a retention period. It belongs to one site.
- **Form Field**: belongs to one form. It has a stable identifier, a type, a label, help text,
  a placeholder, a required setting, type-specific rules and, for choice fields, options.
- **Submission**: belongs to one site and one form. It holds the page it came from, the time
  received, the values keyed by field identifier with each field's label and type at
  submission time, a read state, a notification state and whether it is spam.
- **Notification Delivery**: belongs to one submission. It records the attempts and the
  outcome (pending, sent, failed with reason). Recipients are taken from the form's last
  published settings at each attempt.
- **Spam Counter**: per site and form, the number of submissions caught by the trap field and
  refused by the rate limit, by day. It holds no submitted values.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A Designer who has used the page editor can build and publish a six-field
  contact form with validation and a notification in under 5 minutes, without help.
- **SC-002**: A visitor can complete and send a six-field form in under 1 minute, and sees
  the success message or the redirect within 2 seconds of pressing submit on the reference
  environment.
- **SC-003**: Across a test matrix of every field type and rule, with requests sent directly to
  the server, 100% of submissions that break a rule are refused and 100% of valid ones are
  stored with exactly the submitted values.
- **SC-004**: 100% of submissions with the trap field filled are kept out of the normal list
  and never notified, 100% of submissions above the rate limit are neither stored nor
  notified, and a person submitting at a normal pace is never limited. A trapped submission
  can be found and restored from Spam for 30 days.
- **SC-005**: When the mail server is reachable, 95% of notification emails are handed to it
  within 1 minute of the submission; when it is unreachable, no submission is lost and every
  notification ends as sent or failed with a reason.
- **SC-006**: On a site with 100,000 submissions, the list, filters and search return results
  in under 2 seconds, and an export of 10,000 submissions completes in under 30 seconds.
- **SC-007**: Exported files open in common spreadsheet applications with correct columns and
  characters, and no value in the test matrix is run as a formula.
- **SC-008**: 100% of Form block outputs in the golden-file test encode every label, message
  and value for its context, and every form passes the accessibility baseline of 011 with
  scripts on and off.
- **SC-009**: Pages with forms stay within 012's render budgets on a cache miss and a cache
  hit.

## Scope

### In Scope

- A Form block with eleven field types, per-field rules and messages.
- Server-side validation of every submission against the published form.
- Spam protection with a hidden trap field, a Spam list with restore, a per-address rate
  limit and duplicate suppression.
- Submission storage per site and form, with a list, filters, search, read state, deletion and
  CSV export.
- Email notification through SMTP, with retries, delivery state and a test email.
- Success and error states on the page, and a redirect after a successful submission.

### Out of Scope

- Payment fields and payments of any kind.
- Integrations with CRMs, mailing lists, webhooks or other third-party services.
- Multi-step forms, conditional fields and calculated fields.
- File upload fields.
- Confirmation emails to the visitor.
- CAPTCHA or other challenges that need a third-party service.
- Reusable forms managed outside pages (a form library) and form templates.
- Per-site or per-form SMTP servers.
- Replying to visitors from the admin.

## Assumptions

- **Roles** follow 010 to 024: Editors may edit page drafts and place blocks (011
  clarification), so they can also add and configure Form blocks; the issue's "designer
  places, editor reads" describes the usual split, not a permission rule.
- **Who reads submissions**: any member of the site, including Editors, because the issue
  names Editors as the readers. Permanent deletion is kept to Designers and Administrators
  (clarified 2026-10-21). Submissions contain personal data, so they are limited to members of
  that site and never shown across sites.
- **Forms live in pages**: a form is defined by its block, like other content, and published
  with its page or component. A shared form appears on many pages by placing it in a component.
- **SMTP is installation-wide**: one mail server per installation, set by the operator, as
  for any other infrastructure (constitution Principle IV). Recipients and subjects are set
  per form.
- **Retention**: submissions are kept until deleted, unless the form has a retention period
  (clarified 2026-10-21). It is off by default so that nothing is deleted without someone
  choosing it.
- **Privacy by default**: no visitor network address or browser details are stored with a
  submission.
- **Spam protection** uses only built-in measures, so the product keeps working offline
  (constitution Principle II).
- **Limits** (50 fields per form, 10 recipients, 5 per minute and 30 per hour per address)
  are defaults that can be raised later.
- **Upgrade**: existing sites have no forms, and existing pages are not changed.

## Dependencies

- Feature 001 (foundation skeleton): the admin shell, its navigation and background jobs.
- Feature 002 (identity and setup): members, roles and Administrator email addresses.
- Feature 009 (sites and host name resolution): resolving the site of a submission, the base
  URL, site isolation and the isolation suite.
- Feature 010 (pages, page tree and shared components): page identifiers, components and the
  page schema contract.
- Feature 011 (editor core): block definitions, the palette, the property panel, link rules
  and the accessibility baseline.
- Feature 012 (publishing and rendering): publish refusals, caching, render budgets and
  design tokens.
- The project constitution, in particular Principles II (works offline, fail-fast
  configuration), IV (SMTP behind an abstraction), V (site isolation), VI (block definitions
  and stable identifiers), VII (published data only), VIII (caching and script budget), IX
  (encoding, authorization, no personal data in logs) and X (golden-file tests for blocks).
- GitHub issue #18, which this feature implements and which the pull request closes.
