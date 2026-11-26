# Contract: `tests/e2e/a11y-exceptions.json`

The list of known critical and serious accessibility violations that do not fail a test
(FR-023a, FR-023b).

```json
{
  "exceptions": [
    {
      "rule": "color-contrast",
      "screen": "editor",
      "reason": "The canvas outline colour is 2.8:1 against white; tracked for the token update.",
      "issue": 123
    }
  ]
}
```

## Rules

1. Each entry has `rule` (an axe rule id), `screen` (a `ScreenName`) and a non-empty
   `reason`. `issue` is optional.
2. The pair (`rule`, `screen`) is unique. An entry covers every node that breaks that rule on
   that screen.
3. If the file breaks rule 1 or 2, global setup fails before any test runs.
4. When `QB_E2E_CHECK_EXCEPTIONS=1`, every entry must match at least once. An entry that
   never matched fails the run in global teardown, naming the entry, so it gets removed.

   Both CI jobs set the variable, because they always run the whole suite. A developer sets
   it only for a full local run. Without it, the check is skipped, because a filtered run
   does not visit every screen.
5. Moderate and minor violations are never listed. They are reported, never failed.
6. The file starts with the violations the first full run finds that are not fixed in this
   feature's PR. Each one gets a reason.
