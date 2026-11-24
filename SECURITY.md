# Security policy

## Supported versions

Security fixes are released for the latest minor release of the current major version, as
a patch release. Upgrading to the latest patch of your release line is always supported
(see the operator documentation's upgrade guide, `docs/operators/upgrade.md`).

## Reporting a vulnerability

Please report vulnerabilities privately, never in a public issue, discussion or pull request:

1. Open the repository on GitHub, choose **Security → Report a vulnerability**, and describe
   the problem there. Only the maintainers can read the report.
2. Include the affected release (the first line QuickBuilder logs at startup), what an
   attacker can do, and the steps to reproduce it. Leave out real passwords, keys, personal
   data and site content; use made-up values.

## What to expect

- An acknowledgement within 7 days.
- An assessment, and a fix or mitigation plan, as soon as the problem is understood. Fixes
  ship as a patch release whose notes credit the reporter, unless you prefer otherwise.
- Please give us a reasonable time to release a fix before disclosing the problem publicly.
