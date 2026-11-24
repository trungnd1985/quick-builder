# Contract: Docs Build and Publishing

**Feature**: 026 · **Requirements**: FR-036 – FR-041 · **Research**: OD-08, OD-09, OD-11, OD-14

## 1. Build

`npm ci && npm run build -- --base /<repo>/<major>.<minor>/` in `docs/operators/`, with
environment `QB_DOCS_RELEASE=<exact version>` (shown in the header as "Built from 0.3.2") and
`QB_DOCS_LINE=<major>.<minor>`. Output: `docs/operators/.vitepress/dist/`.

The build fails on: a dead internal link (VitePress default, `ignoreDeadLinks` not set), a
sidebar entry without a page, a Markdown parse error.

Built site contents never include: analytics, external fonts, external scripts, or calls to
third parties (search index is local). A test greps the built HTML for `<script src="http`
and `<link href="http` and fails on any hit.

## 2. Where each step runs

| Workflow | Job / step | Does |
|---|---|---|
| `ci.yml` (every PR, push to main) | job `docs` | `qb-docs lint`, `qb-docs settings check`, build with `base /<repo>/dev/` (not published). |
| `docs-procedures.yml` (path-filtered PR; `workflow_call`) | matrix `procedure` | runnable-snippets.md §4. |
| `release.yml` | `prepare` | `qb-docs upgrade-notes generate --through <version>` must leave the tree unchanged; `settings check`; `lint`; build with the release's base; upload artifact `docs-site` (90 days). Any failure stops the run before `build`. |
| `release.yml` | `procedures` (needs `manifest`) | calls `docs-procedures.yml` with the digest. Listed in `candidate.needs` and the failure-banner loop. |
| `promote.yml` | after "Publish the release" | step "Publish the documentation" (§3). Pre-releases skip it. |

## 3. Publish step (`promote.yml`)

Inputs: tag, `version`, `highest`, `prerelease` (already computed by the workflow).

1. Skip when `prerelease == true`.
2. Download `docs-site` from the tag's successful Release run (`gh run download`); if absent,
   check out the tag and rebuild (§1).
3. Check out `gh-pages` of `${DOCS_PAGES_REPOSITORY:-$GITHUB_REPOSITORY}` (create it as an
   orphan branch with `.nojekyll` on first use), authenticating with `DOCS_DEPLOY_KEY` when
   set, else `GITHUB_TOKEN`.
4. Replace `/<major>.<minor>/` with the build. Touch nothing else except:
   - `versions.json`: upsert `{ line, release, published }`, sort by line descending;
   - `index.html`: rewrite the redirect target only when `highest == true`.
5. Commit `docs: publish <version>` and push. A push conflict retries once after rebase
   (two promotes of different lines are serialised by the existing `concurrency` group per
   tag, not across tags).
6. If the target has no Pages site enabled or the push is refused, fail the step with:
   `Documentation for <version> was not published: set DOCS_PAGES_REPOSITORY (and DOCS_DEPLOY_KEY) to a repository with GitHub Pages enabled. The release itself is published.`

FR-038 holds because the step runs only after the release is published, and the release's
own run already proved the docs build and checks.

## 4. Version picker contract

`/versions.json` (site root, outside any line):

```json
[
  { "line": "0.3", "release": "0.3.2", "published": "2027-01-10" },
  { "line": "0.2", "release": "0.2.1", "published": "2026-12-15" }
]
```

The theme fetches `../versions.json` relative to its own base; when unreachable (reading a
downloaded copy offline) the picker is hidden and the page still shows "Built from".

## 5. Links that depend on the published site

| Place | Before | After |
|---|---|---|
| `InstallationGate.Guide` (startup refusal log) | `…/blob/main/docs/operations.md` | `{QbDocsBaseUrl}{major.minor}/upgrade#when-quickbuilder-refuses-to-start` |
| `docs/releases/TEMPLATE.md` header | `…/blob/main/docs/operations.md#upgrading-and-rolling-back` | `{{docs}}/upgrade` (new template variable filled by `qb-release notes`) |
| Release-notes config key links | `docs/configuration.md` | `{{docs}}/reference/configuration#<anchor>` |
| `docker-compose.yml` header comment, `README.md` | `docs/operations.md` | site URL and `docs/operators/` path |

`QbDocsBaseUrl` lives in `Directory.Build.props` next to `QbUpgradeFloor` and is written to
the Host's assembly metadata; `qb-release` reads the same property.
