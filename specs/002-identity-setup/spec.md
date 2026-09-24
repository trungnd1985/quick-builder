# Feature Specification: Identity, First-Run Setup and Authorization

**Feature Branch**: `002-identity-setup`

**Created**: 2026-09-23

**Status**: Draft

**Issue**: #2

**Input**: User description: "Identity and first-run setup: admin user accounts, login, a setup wizard that creates the first administrator, and server-side authorization on all admin endpoints, including per-site permissions"

## Clarifications

### Session 2026-09-23

- Q: What must the person running setup prove before the wizard accepts a submission?
  → **A: a one-time setup code.** While the installation is in setup mode, the
  application generates a random setup code. It prints the code prominently in its
  console output at start-up, and the recovery command can also print it. The wizard
  refuses any submission without the current code. The code stops working the moment
  setup completes.
- Q: How does a new user get their first password, and how does someone who has lost
  theirs regain access? → **A: administrator-issued one-time links, with no email.** An
  administrator generates a single-use, expiring set-password link and passes it to
  the user through their own channel. The same mechanism handles resets. Users cannot
  reset their own password in this feature.
- Q: Is Administrator an installation-wide role or a per-site role? → **A:
  installation-wide.** Administrator covers the whole installation: users, sites and
  every site's content. Designer and Editor are granted per site. A role that gives
  full control of a single site is not part of this feature.
- Q: When someone keeps typing wrong passwords for an account, should that account be
  locked for everyone, or only blocked for the client doing the guessing? → A: No hard
  account lockout. Each account gets an increasing delay after repeated failures, and
  a client that keeps failing is blocked for 15 minutes. A correct password from
  another client still works once the delay has passed. *Refined during
  `/speckit-analyze` on 2026-09-24*: an attacker who times failures to each wait's
  expiry could keep refusing the owner, so a browser that has signed in to the account
  before (a known device) is exempt from the per-account wait (FR-015a).
- Q: Can an account's email address be changed after it is created, and if so, by
  whom? → A: Only an administrator can change it, and the new address must be unique.
  The change is recorded as a security event and ends all of that user's sessions.
- Q: How long should recorded security events be kept before they are deleted? → A:
  For a configurable period (default 365 days), after which a background job deletes
  them automatically.
- Q: Should an administrator be able to sign a user out of all their sessions without
  deactivating the account? → A: Yes. Administrators can end all sessions of any user,
  and every user can end all of their own other sessions. Both are recorded as
  security events.
- Q: How fast should sign-in be, and how many sign-ins at once must the reference
  server handle? → A: 95% of successful sign-ins finish within 1 second with up to 10
  happening at once on the reference server, and other admin requests keep their
  normal response times during that burst.

## Context

Feature 001 shipped an administrative surface with no authentication and no
authorization, and accepted that as a recorded exception to the "Secure by Default"
principle (exception E-1 in the 001 plan). It compensated by binding the reference
deployment to the loopback interface only. This feature closes that exception: once it
ships, no administrative operation is reachable without an authenticated user who is
authorized for it, and the first administrator is created through a setup wizard rather
than through default credentials.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Operator claims a fresh installation through the setup wizard (Priority: P1)

An operator starts a brand-new installation and opens the admin address. Instead of the
admin shell, they see a setup wizard. They enter the setup code that the application
printed in its console output, then the first administrator's name, email address and
password, submit, and are signed in as that administrator. From then on the
wizard is gone for good: nobody can run it again, and the installation has no default
account of any kind.

**Why this priority**: Nothing else in this feature can be used until at least one
account exists, and the constitution forbids default credentials. This is the smallest
slice that turns an open installation into an owned one.

**Independent Test**: On a fresh installation, open the admin address, complete the
wizard, and confirm that you are signed in as an administrator. Then confirm that the
wizard can no longer be reached or submitted, and that no other account exists.

**Acceptance Scenarios**:

1. **Given** an installation with no user accounts,
   **When** anyone opens any admin address,
   **Then** they are sent to the setup wizard, and every other administrative operation
   responds "setup required" without doing anything.
2. **Given** the setup wizard is open,
   **When** the operator enters the setup code from the application's console output,
   along with a name, a valid email address and a password that meets the password
   policy,
   **Then** the first administrator account is created, the operator is signed in, and
   the admin shell opens.
3. **Given** setup has been completed,
   **When** anyone opens the wizard address or submits a setup request,
   **Then** the request is refused and no account is created or changed.
4. **Given** two setup submissions arrive at the same moment on a fresh installation,
   **When** both are processed,
   **Then** exactly one succeeds, and the other is refused without creating a second
   account.
5. **Given** the setup wizard is open,
   **When** a submission arrives with a missing or wrong setup code,
   **Then** it is refused and no account is created. Repeated wrong codes are
   rate-limited, and each one is recorded as a security event.
6. **Given** an installation that ran feature 001 and already holds sites but no user
   accounts,
   **When** it is upgraded to this feature,
   **Then** it enters setup mode, every existing site and host name is preserved, and
   public requests to those sites keep being served while setup is pending.

---

### User Story 2 - Users sign in and out, and the admin surface refuses everyone else (Priority: P1)

An administrator returns to the installation, signs in with their email address and
password, works in the admin shell, and signs out. Anyone who is not signed in, whether
in a browser or calling operations directly, is refused by every administrative
operation. The refusal happens on the server, not by hiding buttons.

**Why this priority**: This story removes exception E-1. It shares P1 with the wizard
because an account that cannot sign in, or an admin surface that is still open, leaves
the installation just as exposed as before.

**Independent Test**: Call every administrative operation without being signed in and
confirm that each one refuses. Sign in, confirm the same operations now succeed for an
administrator, sign out, and confirm they refuse again.

**Acceptance Scenarios**:

1. **Given** a user with an active account,
   **When** they sign in with the correct email address and password,
   **Then** a session is started and they land in the admin shell.
2. **Given** a sign-in attempt with an unknown email address or a wrong password,
   **When** it is submitted,
   **Then** it is rejected with one generic message that does not reveal which of the
   two was wrong, and it takes the same observable time in both cases.
3. **Given** no signed-in session,
   **When** any administrative operation is called directly, bypassing the admin shell,
   **Then** it is refused as unauthenticated and performs no work.
4. **Given** a signed-in user,
   **When** they sign out,
   **Then** their session stops working immediately, including for requests replayed
   with the old session credential.
5. **Given** one client has made repeated failed sign-in attempts,
   **When** it passes the configured threshold,
   **Then** that client's further sign-in attempts are refused for the block period,
   even with a correct password, and the block is recorded as a security event.
6. **Given** an account has received repeated failed attempts from many clients,
   **When** its owner signs in with the correct password from a browser they have
   signed in from before,
   **Then** the sign-in succeeds immediately. From a new browser, it succeeds once the
   indicated wait has passed. A stranger's failed attempts can slow the real owner down
   on a new browser, but can never lock them out.
7. **Given** a signed-in session that has been idle longer than the configured idle
   limit, or that has reached its absolute lifetime,
   **When** the user makes a request,
   **Then** the request is refused, and the admin shell returns the user to the sign-in
   screen. After they sign in again, it takes them back to where they were.
8. **Given** a request that would change state and does not come from the admin
   application,
   **When** it arrives carrying a valid session credential, for example from a
   cross-site form or script,
   **Then** it is refused.

---

### User Story 3 - Administrators manage user accounts (Priority: P2)

An administrator adds a colleague to the installation, decides what they may do, and
later changes those permissions or deactivates the account when the colleague leaves.
Every change takes effect immediately, including for sessions that are already open.

**Why this priority**: An installation with one person in it works on its own, so this
story follows the P1 stories. Most real installations need it soon after, though.

**Independent Test**: As an administrator, create a second account, get the new user
signed in for the first time, change their permissions and confirm the change applies
to their open session, then deactivate the account and confirm that the open session
stops working at once.

**Acceptance Scenarios**:

1. **Given** a signed-in administrator,
   **When** they create an account with a name, a unique email address and the
   permissions it should hold,
   **Then** the account exists without a usable password, and the administrator is
   shown a one-time set-password link to pass to the new user. Opening the link lets
   the user choose a password that meets the policy, after which they can sign in.
2. **Given** a user who has forgotten their password,
   **When** an administrator issues a new set-password link for that account and the
   user uses it,
   **Then** the user sets a new password, all of their existing sessions end, and the
   old password stops working.
3. **Given** a set-password link that has already been used, has expired, or has been
   replaced by a newer link for the same account,
   **When** anyone opens it,
   **Then** it is refused, and it reveals nothing about the account.
4. **Given** an email address already used by another account, ignoring
   upper/lower-case differences,
   **When** an administrator tries to create an account with it,
   **Then** the attempt is rejected, and a message names the conflict.
5. **Given** a user with an open session,
   **When** an administrator changes that user's permissions,
   **Then** the user's next request is judged by the new permissions, and no sign-out
   is needed.
6. **Given** a user with an open session,
   **When** an administrator deactivates the account,
   **Then** the user's next request is refused, they cannot sign in again, and their
   past actions stay attributed to them.
7. **Given** exactly one active administrator,
   **When** anyone tries to deactivate that account or remove its administrator role,
   **Then** the attempt is rejected, so the installation can never be left without an
   administrator.
8. **Given** a signed-in user of any role,
   **When** they change their own password, supplying their current password,
   **Then** the password changes, and every other session belonging to them ends.
9. **Given** a user who is not an administrator,
   **When** they call any user-management operation, including changing their own
   email address,
   **Then** it is refused as forbidden.
10. **Given** a user with an open session,
    **When** an administrator changes that user's email address to one no other
    account uses,
    **Then** the user's sessions end, they sign in with the new address from then on,
    the old address no longer signs in, and the change is recorded as a security
    event.
11. **Given** a user with sessions open on several devices,
    **When** an administrator chooses "sign out everywhere" for that user,
    **Then** every one of those sessions is refused on its next request, and the
    account stays active, so the user can sign in again straight away.
12. **Given** a signed-in user with other sessions open elsewhere,
    **When** they choose to end all their other sessions,
    **Then** every other session is refused on its next request, and the session they
    used to do it keeps working.

---

### User Story 4 - Users see and act only on the sites they are permitted on (Priority: P2)

An administrator grants a colleague a role on one site only. When the colleague signs
in, that site is the only one they see. Anything they try against any other site behaves
as though that site does not exist. Administrators continue to see and manage every
site.

**Why this priority**: The constitution requires per-site permissions (Principle V), and
multi-site installations are a core product promise. The model has to exist before the
page, CMS and media modules arrive, because each of them will plug its operations into
it.

**Independent Test**: With two sites, grant a user a role on site A only. As that user,
list sites and confirm that only A appears. Then call a site-specific operation against
site B and confirm the response is the same as for a site that does not exist.

**Acceptance Scenarios**:

1. **Given** a user who holds a role on site A only,
   **When** they list sites,
   **Then** only site A is returned.
2. **Given** the same user,
   **When** they call any operation that names site B,
   **Then** the response is identical to the one for a site that does not exist, and
   reveals nothing about site B.
3. **Given** a Designer or Editor on site A, whose role does not include a particular
   capability, such as managing host names,
   **When** they call that operation on site A,
   **Then** it is refused as forbidden, because they can see the site but may not
   perform that action.
4. **Given** an administrator,
   **When** they list sites or act on any site,
   **Then** every site is visible and actionable, without a per-site grant.
5. **Given** a user who holds different roles on different sites,
   **When** they act on each site,
   **Then** each action is judged only by the role held on that site.
6. **Given** a site is created,
   **When** its permissions are inspected,
   **Then** only administrators can reach it until someone grants a site role.

In this story, Administrator is an installation-wide role, and Designer and Editor are
granted per site (see Clarifications).

---

### User Story 5 - Operator recovers when no administrator can sign in (Priority: P3)

The only administrator has forgotten their password, or has left the organisation. The
operator, who can run commands against the installation's container, uses a documented
command to regain administrator access. It needs no database tools and no code changes.

**Why this priority**: This happens rarely. When it does, the installation is otherwise
lost, because the product has no vendor-operated recovery and email may not be
configured.

**Independent Test**: Lock yourself out of every administrator account. Then use the
documented recovery command to restore administrator access, and confirm that the
recovery was recorded as a security event.

**Acceptance Scenarios**:

1. **Given** an installation where no administrator can sign in,
   **When** the operator runs the documented recovery command with access to the
   running installation,
   **Then** they receive a set-password link for the administrator account they name,
   use it to set a new password and sign in, without editing data by hand.
2. **Given** the recovery command has been used,
   **When** security events are reviewed,
   **Then** the recovery is recorded with its time and the affected account.
3. **Given** someone who can reach the installation only over the network,
   **When** they look for the recovery path,
   **Then** nothing is exposed over the network, and recovery requires command-line
   access to the installation.

---

### Edge Cases

- **Setup wizard left open on an exposed installation**: a stranger who reaches it
  cannot complete it without the setup code, which appears only in the console output
  and through the operator command.
- **Restart, or several instances, during setup mode**: the installation has exactly one
  setup code, which is stored and kept until setup completes. Every instance prints
  that same code at start-up, so restarting never makes a code the operator has already
  copied stop working.
- **Set-password link opened after the account is deactivated**: the link is refused.
- **Setup interrupted partway**, for example because the browser closes or the process
  restarts: either no account exists and setup is still available, or the account exists
  and setup is closed. A half-created state is never possible.
- **An installation upgraded from 001 while an old admin shell is open in a browser**:
  the next request from that shell is refused, and the refreshed shell shows the setup
  wizard. It does not show stale data or a blank screen.
- **Email address in different letter case at sign-in**: it signs in to the same account.
  Addresses are compared without regard to case.
- **Leading or trailing spaces in an email address**: they are trimmed. The password is
  never trimmed.
- **Very long passwords**: accepted up to a documented maximum length, and every
  character counts. Passwords are never silently truncated.
- **A user whose last site role is removed and who is not an administrator**: they can
  still sign in, change their own password and sign out, but they see no sites and are
  told that they have no access yet.
- **A site is deleted in a future feature**: grants on it must not leave dangling
  permissions. The grant model is set up so that a later feature can remove a site's
  grants cleanly.
- **Plain HTTP from another machine**: credentials are refused with a message saying
  HTTPS is required. A TLS proxy that isn't listed as trusted triggers this too, which
  makes the misconfiguration visible instead of silently weakening throttling.
- **Owner signs in from a new browser during an attack**: they may have to wait up to
  the per-account maximum (default 60 seconds) before a correct password is evaluated.
  From a browser they have used before, they are not delayed.
- **Two administrators edit the same user at the same moment**: display name and email
  changes are last-write-wins; grants are replaced as a whole, one change at a time.
  The last-administrator rule is never broken by a race.
- **Clock changes on the host**: session expiry stays correct and never extends a
  session indefinitely.
- **Many instances behind a load balancer**: sign-in, sign-out, deactivation, permission
  changes and throttling apply on every instance, not only on the one that handled the
  request.
- **Public site requests**: they are never asked for credentials and are unaffected by
  setup state, sign-in state or sign-in throttling.
- **Health endpoints**: they stay reachable without credentials, whatever the setup
  state.

## Requirements *(mandatory)*

### Functional Requirements

**First-run setup**

- **FR-001**: An installation with no user accounts MUST be in setup mode. In setup
  mode, every administrative operation except the setup wizard's own operations MUST
  refuse with a distinguishable "setup required" response, and the admin shell MUST
  show the setup wizard.
- **FR-002**: The setup wizard MUST collect the first administrator's display name,
  email address and password. On success it MUST create that account with the
  administrator role and sign the operator in.
- **FR-003**: Setup MUST be completable exactly once. After the first account exists,
  the setup operations MUST refuse every request. Concurrent submissions MUST result in
  exactly one account.
- **FR-004**: While in setup mode, the installation MUST hold one random, single-use
  setup code that is hard to guess. The code MUST be printed prominently in the
  application's console output at every start-up in setup mode, and it MUST be
  retrievable through the operator command in FR-028. The wizard MUST refuse any
  submission that does not carry the current code. Wrong codes MUST be rate-limited and
  recorded as security events. The code MUST stop working when setup completes, and it
  MUST NOT be exposed over the network in any other way.
- **FR-004a**: The setup code is the one deliberate exception to feature 001's rule that
  log output contains no secrets (001 FR-032). The exception is allowed only because
  the code exists solely before any account does and is useless once setup completes.
  No other credential may appear in output.
- **FR-005**: The product MUST NOT ship, create or document any default account or
  default password under any circumstances.
- **FR-006**: Setup mode MUST NOT affect public site requests or health endpoints. An
  installation upgraded from feature 001 MUST keep all existing sites and host names
  and enter setup mode.

**Accounts and credentials**

- **FR-007**: A user account MUST have a display name, a unique email address used as
  the sign-in identifier (compared without regard to letter case), a status (active or
  deactivated) and the role grants described in FR-020 to FR-026.
- **FR-008**: Passwords MUST be at least 12 characters and at most a documented maximum
  of no less than 128 characters. They MUST be rejected if they appear in a list of
  commonly used or breached passwords that ships with the product, so the check works
  offline. Composition rules such as "one digit, one symbol" MUST NOT be imposed.
- **FR-009**: Passwords MUST be stored only as salted, deliberately slow one-way hashes
  following current industry guidance. They MUST never be stored, logged, returned or
  displayed in recoverable form. The hashing cost MUST be set as high as current
  guidance recommends while still meeting the sign-in performance target in SC-013,
  and it MUST be configurable. Hashes stored at an older cost MUST be upgraded
  transparently at the user's next successful sign-in.
- **FR-010**: Administrators MUST be able to create accounts, change a user's display
  name, email address and role grants, and deactivate and reactivate accounts. Deleting
  accounts is out of scope: deactivation preserves attribution of past actions.
- **FR-010a**: Only administrators MAY change an account's email address. Users cannot
  change their own. The new address MUST satisfy the same uniqueness rule as at
  creation (FR-007). A change MUST end all of that user's sessions, invalidate any
  outstanding set-password link for the account, and be recorded as a security event
  that carries both the old and the new address.
- **FR-011**: A new account MUST be created without a usable password. Administrators
  MUST be able to issue a set-password link for any account. The link MUST be shown
  only to the administrator who issued it, for them to pass on through a channel of
  their choosing, and the product MUST NOT send it anywhere.
- **FR-011a**: A set-password link MUST be single-use and hard to guess. It MUST expire
  after a configurable period (default 24 hours), and issuing a new link for an account
  MUST invalidate any earlier link for that account. Opening an invalid link MUST
  reveal nothing about the account. The existing password stays valid until the link is
  used. Using the link MUST end all of that user's sessions.
- **FR-011b**: The server MUST NOT build set-password links from the incoming request's
  host. The admin shell builds each link from the address the administrator is using.
  The recovery command builds it from the configured public address when one is set,
  and otherwise prints the path for the operator to complete. Onboarding and reset MUST
  work with no outbound network access. Users MUST NOT be able to reset their own
  password without an administrator in this feature.
- **FR-012**: Every signed-in user MUST be able to change their own password by
  supplying the current one. Doing so MUST end all of that user's other sessions.
- **FR-012a**: Administrators MUST be able to end all sessions of any user without
  changing the account's status. Every signed-in user MUST be able to end all of their
  own sessions except the current one. Both actions MUST take effect under FR-017 and
  be recorded as security events.
- **FR-013**: The system MUST refuse any change that would leave the installation with
  no active administrator, including when changes run concurrently.

**Sign-in and sessions**

- **FR-014**: Users MUST sign in with email address and password. Failed attempts MUST
  receive one generic response that does not reveal whether the account exists, and
  the response time MUST not reveal it either.
- **FR-015**: Sign-in MUST be throttled in two independent ways, and both MUST apply
  across all instances:
  - **Per client**: after a configurable number of consecutive failures from one
    client address (default 5), every sign-in attempt from that client MUST be refused
    for a configurable block period (default 15 minutes), whichever account it
    targets. The block MUST be recorded as a security event.
  - **Per account**: after a configurable number of consecutive failures on one
    account (default 3), each further failure MUST double the minimum wait before the
    next attempt on that account is evaluated, up to a configurable maximum (default
    60 seconds). An attempt that arrives before the wait has passed MUST be refused with
    a retry hint and MUST NOT be checked against the password. A successful sign-in
    resets the wait.
- **FR-015a**: An account MUST NOT be hard-locked by failed sign-ins. A browser that
  has previously signed in successfully to an account is a **known device** for that
  account.
  - **Known devices**: a correct password from a known device, on a client that is not
    blocked, MUST NOT be subject to the per-account wait, so failed attempts elsewhere
    cannot keep the owner out. After a configurable number of consecutive failures
    from a known device (default 5), that device MUST lose its known status.
  - **Other browsers**: a correct password MUST succeed once the per-account wait has
    passed.
  - **Unknown accounts**: email addresses that match no account MUST be throttled
    exactly like real accounts, so throttling reveals nothing about which accounts
    exist.
- **FR-016**: A session MUST end after a configurable idle period (default 2 hours) and
  after a configurable absolute lifetime (default 12 hours), whichever comes first.
- **FR-017**: Signing out, "sign out everywhere", deactivation, a password change, an
  email address change and a change of permissions MUST take effect on the user's very next request, on every instance. A session
  credential MUST NOT keep working after it has been revoked.
- **FR-018**: Session credentials MUST NOT be readable by scripts running in the page.
  They MUST be sent only over secure connections when the installation is reached over
  HTTPS, and state-changing requests MUST be protected against cross-site request
  forgery. Sign-in, setup and set-password requests that arrive over plain HTTP from a
  non-loopback client MUST be refused unless the operator has explicitly allowed
  insecure HTTP, and that setting MUST produce a warning at every startup.
- **FR-019**: The admin shell MUST return an unauthenticated user to the sign-in screen,
  and after sign-in MUST return them to the admin route they originally requested,
  provided that route belongs to the admin shell. Redirects to other destinations MUST
  NOT be possible.

**Authorization and per-site permissions**

- **FR-020**: The system MUST support the roles Administrator, Designer and Editor, as
  required by the constitution. Administrator MUST be granted for the whole
  installation only. Designer and Editor MUST be granted per site, and one user MAY
  hold different roles on different sites. Where a user holds Administrator, any site
  grants they also hold add nothing.
- **FR-020a**: The grant model MUST record the scope of each grant (installation or
  a specific site) explicitly. A per-site administrative role can then be added in a
  later feature without changing how existing grants are stored or evaluated.
- **FR-021**: Administrators MUST be able to see and act on every site without a
  per-site grant, and MUST be the only role that can manage user accounts, create sites
  and grant site roles.
- **FR-022**: A non-administrator MUST be able to see only the sites on which they hold
  a role. An operation naming any other site MUST respond exactly as if that site did
  not exist.
- **FR-023**: Where a user can see a site but their role does not permit the requested
  action, the operation MUST refuse as forbidden. "Forbidden" (can see but may not act)
  and "not found" (may not see) MUST be distinct and applied consistently.
- **FR-024**: Authorization MUST be decided on the server for every administrative
  operation, from the signed-in user's current grants. Hiding or disabling elements in
  the admin shell MUST NOT be the means of enforcement.
- **FR-025**: Every administrative operation MUST declare the permission it requires.
  An automated check MUST fail the build if any administrative operation is reachable
  without such a declaration. The only operations reachable anonymously are sign-in,
  the setup wizard's operations while in setup mode, health endpoints and the public
  site surface, and these MUST be listed explicitly.
- **FR-026**: Permissions MUST be expressed as named capabilities that roles grant, for
  example "manage host names on a site", rather than as role-name checks scattered
  through operations. That way later modules can add capabilities and map them to
  existing roles without changing this feature's enforcement.
- **FR-027**: All existing `Sites` operations from feature 001 (create site, list sites,
  assign host names) MUST come under this authorization. Creating a site MUST require
  administrator. Listing MUST be filtered per FR-022. Host-name assignment MUST require
  a capability that only Administrator holds in this feature. Designers and Editors
  can see their permitted sites but cannot change them.

**Operator recovery**

- **FR-028**: The product MUST provide a documented command, run with command-line
  access to the installation, that restores administrator access when no administrator
  can sign in. For an existing administrator account named by email address, it issues
  a set-password link under FR-011a. In setup mode, it prints the current setup code.
  It MUST NOT be exposed over the network, MUST require no database tools, and every
  use MUST be recorded as a security event.

**Security events and privacy**

- **FR-029**: The system MUST durably record security events: setup completed,
  successful sign-in, failed sign-in, client blocked, sign-out, password changed, account
  created, email address changed, account deactivated or reactivated, role grant
  changed, wrong setup code,
  set-password link issued, set-password link used or refused, sessions ended by an
  administrator or by the user, and operator recovery used. Each record MUST carry the
  time, actor (if any), affected account (if one exists: for a failed sign-in against
  an unknown email address there is none), event type, outcome and client address. The
  email address attempted in a failed sign-in MUST NOT be stored.
- **FR-029a**: Security events MUST be kept for a configurable retention period
  (default 365 days) and then deleted automatically by a background job, with no
  operator action. Deletion MUST be idempotent and safe with several instances
  running. Events inside the retention period MUST NOT be altered or removed by any
  operation in this feature.
- **FR-030**: Application log output MUST refer to users by internal identifier only.
  Email addresses, names, passwords and session credentials MUST NOT appear in logs,
  consistent with feature 001's logging rules.

**Admin shell**

- **FR-031**: The admin shell MUST provide a setup wizard, a sign-in screen, a
  user-management area for administrators (list, create, edit grants,
  deactivate/reactivate, issue a set-password link with a copy action), a set-password
  screen reached from a link, a change-own-password screen, a "sign out everywhere"
  action (for administrators, on any user; for each user, on their own other sessions)
  and a sign-out action. Each MUST
  show usable in-progress and error states.
- **FR-032**: Every new screen MUST meet the translation-lookup and accessibility
  baselines established in feature 001: no text written into screens, operable by
  keyboard alone, visible focus, sufficient contrast and labelled form controls.

**Deployment and verification**

- **FR-033**: The note at the reference deployment's network binding that currently
  warns about an unauthenticated admin interface MUST be updated to describe the
  security posture after this feature ships.
- **FR-034**: The release smoke test MUST be extended to complete the setup wizard, sign
  in, and perform the existing site steps as that administrator. It MUST also confirm
  that an unauthenticated call to an administrative operation is refused.
- **FR-035**: A default installation MUST still make zero outbound network requests
  from start-up through setup, sign-in and site creation.

### Key Entities

- **User Account**: a person who can sign in to the administrative surface. It has a
  stable identifier, display name, email address (unique, case-insensitive), password
  hash, status (active/deactivated), and creation and last-sign-in times. An account is
  never deleted in this feature.
- **Role**: a named bundle of capabilities: Administrator, Designer or Editor. It is
  defined by the product, not by operators, in this feature.
- **Capability**: a named permission to perform one kind of administrative action,
  such as "manage users", "create sites" or "manage host names on a site". Roles map to
  capabilities, and operations declare the capability they require.
- **Role Grant**: the assignment of a role to a user, with an explicit scope. An
  Administrator grant covers the whole installation. A Designer or Editor grant
  references exactly one site by its identifier.
- **Session**: an authenticated sign-in belonging to one user, with a start time, a
  last-activity time and a revoked state. It can be ended by sign-out, expiry, password
  change, deactivation or administrator action.
- **Installation Setup State**: whether the installation has been claimed. It is
  recorded atomically with the creation of the first account and can only ever move
  from unclaimed to claimed. While unclaimed, it holds the single current setup code.
- **Known Device**: a browser that has signed in successfully to one account. It is
  identified by an opaque, long-lived credential held in that browser and exempts that
  browser's correct sign-ins from the per-account wait (FR-015a). It loses its status
  after repeated failures, and expires after a period without use.
- **Set-Password Link**: a single-use, expiring authorization to set the password of one
  account, issued by an administrator or by the recovery command. It has an issue time,
  an expiry, the issuer, and a used/replaced state. Only one link per account is live
  at a time.
- **Security Event**: a durable record of an authentication or authorization-relevant
  action, carrying time, actor, affected account, type, outcome and client address.
  It cannot be changed once written and is deleted automatically when the retention
  period passes.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator goes from the single documented start command on a clean
  machine to being signed in as the first administrator in under 5 minutes, and completes the setup
  wizard itself in under 2 minutes.
- **SC-002**: 100% of administrative operations refuse unauthenticated callers. This is
  verified by an automated test that enumerates every administrative operation rather
  than a hand-picked sample, and by a build check that fails if any operation lacks a
  permission declaration.
- **SC-003**: Across the automated test suite, a user holding a role on one site
  receives zero data belonging to any other site from any administrative operation.
  Every operation naming an unpermitted site responds exactly as it does for a
  non-existent site.
- **SC-004**: Revoking access takes effect on the user's very next request, 0 requests
  late, on every instance. The revocations covered are sign-out, "sign out everywhere",
  deactivation, password change, email address change and permission change.
- **SC-005**: After the configured threshold, 100% of further sign-in attempts from a
  blocked client are refused for the block period, including attempts with the correct
  password and attempts spread across instances. In the same test, while that account
  is under sustained failed attempts from other clients, the legitimate owner signs in
  successfully on the first attempt from a browser they have signed in from before,
  and within 60 seconds from a new browser that retries after the indicated wait.
- **SC-006**: Across 1,000 automated sign-in attempts, responses for unknown accounts
  and wrong passwords cannot be told apart by response content, and their median
  response times differ by less than 10%.
- **SC-007**: An operator locked out of every administrator account regains
  administrator access in under 5 minutes using only the documented recovery command.
- **SC-008**: An installation upgraded from feature 001 keeps 100% of its sites and host
  names, and serves its public sites without interruption throughout setup.
- **SC-009**: No email address, name, password or session credential appears in log
  output across the full test suite, verified by an automated check.
- **SC-010**: Every new admin journey (setup, sign-in, user management, password change,
  sign-out) is completable with the keyboard alone, and 100% of its visible text
  resolves through the translation lookup.
- **SC-011**: Across the automated test suite, zero setup attempts without the current
  setup code succeed, and zero attempts succeed with a used, expired or replaced
  set-password link.
- **SC-012**: In an automated test with the clock moved forward, 100% of security events
  older than the retention period are gone after the next cleanup run, and 100% of
  newer events remain unchanged.
- **SC-013**: On the reference environment (two cores, four gigabytes of memory), with
  10 sign-ins happening at once, 95% of successful sign-ins complete within 1 second.
  During that burst, the 95th-percentile response time of other admin requests is no
  more than twice its value on an idle server.

## Scope

### In Scope

- The setup wizard and setup mode, including the upgrade path from a feature-001
  installation.
- Local accounts with email address and password, the password policy, and secure
  password storage.
- Sign-in, sign-out, session expiry, revocation, throttling and client blocking.
- Administrator management of accounts and role grants, and changing one's own
  password.
- The roles Administrator, Designer and Editor, the capability model, and per-site
  role grants.
- Server-side authorization on every administrative operation, including the existing
  `Sites` operations, plus the build-time check for undeclared operations.
- The operator recovery command.
- Security event recording.
- The admin shell screens for all of the above.
- The smoke-test extension and the reference-deployment note update.

### Out of Scope

- Single sign-on, external identity providers and social sign-in.
- Multi-factor authentication.
- API keys or tokens for machine clients.
- Custom, operator-defined roles and editing the capability mapping in the UI.
- Deleting user accounts, and exporting or erasing personal data.
- A screen for browsing security events. They are recorded in this feature, and viewing
  them is left to a later specification.
- Self-registration of accounts by visitors, and any authentication of public site
  visitors.
- Capabilities for modules that do not exist yet (pages, CMS, media, editor). Those
  modules add their capabilities in their own specifications.

## Assumptions

- **Local accounts only**: identity is managed entirely inside the installation, which
  keeps the product offline-capable and dependency-free, as required by Principles II
  and IV.
- **Email is the sign-in identifier but is not verified**: delivering email is optional
  in this product, so ownership of an address is not proven in this feature.
- **Admin shell is the only client**: every administrative operation is called by the
  admin shell in a browser session. Machine clients are out of scope.
- **Designer and Editor have few actions today**: until the page, CMS and media modules
  exist, Designer and Editor can mainly see their permitted sites. Their full
  capabilities arrive with those modules, which map onto this feature's model.
- **Default session and throttling values** (2-hour idle limit, 12-hour absolute
  lifetime, client block after 5 failures for 15 minutes, account delay starting after
  3 failures and capped at 60 seconds) are industry-typical starting points. Operators can
  change them through configuration.
- **Operators have command-line access** to the container they run. This is how
  recovery works, and it matches the constitution's operator model.
- **The reference deployment's loopback binding stays as the default.** With
  authentication and the setup code in place, operators can widen it safely, and the
  note at the binding says so (FR-033). Operators are still expected to expose the
  installation through their own TLS-terminating reverse proxy.
- **Set-password links travel out-of-band**: the administrator is trusted to pass each
  link through a reasonably private channel. The short expiry and single use limit the
  damage if a link leaks.
- **The setup code in console output is acceptable**: anyone who can read the
  installation's console output already controls the installation, so showing a
  pre-setup code there grants nothing new (see FR-004a).

## Dependencies

- Feature 001 (foundation skeleton): the `Sites` module, the administrative/public
  surface split, the outbox, the logging rules, and the translation and accessibility
  baselines.
- The project constitution, in particular Principles II (setup wizard, no default
  credentials), V (per-site permissions) and IX (server-side, policy-based
  authorization, login rate limiting).
- GitHub issue #2, which this feature implements, which removes exception E-1 from the
  001 plan, and which the pull request closes.
