# Contract: Release Notes, Upgrade Notes and Fragments

**Feature**: 019 · **Requirements**: FR-032 – FR-035 · **Research**: RL-16

## 1. The template (`docs/releases/TEMPLATE.md`)

Every release's notes have exactly these sections, in this order. `qb-release notes` fills
them; a section that would be empty says **None.** (FR-033).

```markdown
# QuickBuilder {version}

Released {date} · [Upgrade guide](…/docs/operations.md#upgrading-and-rolling-back)

## Highlights
{bodies of fragments, added and changed first, one bullet each}

## Changes
### Added
### Changed
### Fixed
### Security
{one bullet per fragment of that kind: body's first sentence (#issue)}

## Upgrade notes
### Breaking changes            {fragments with breaking: true — only in a new major}
### Manual steps                {fragment `manual` entries}
### Configuration               {added / changed / removed keys, each linked to docs/configuration.md}
### Database migrations         {derived: per module, migration names added since {previous}}
### Content schema versions     {derived: contracts whose version was raised, from → to}
### Background work after upgrading   {fragment `background` entries}
### Caches                      {fragment `caches` entries}
### Visible changes             {every allow-list entry: reason and pages (snapshot.md §3)}

## Supported upgrade path
Upgrade directly from any {major}.x release{, or from {floor} (the last {major-1}.x minor)}.
Tested in this release from: {upgrade sources}.
Rolling back: restore the backup you took before upgrading, then start {previous}.

## Images
| Platform | Reference |
| linux/amd64, linux/arm64 | `ghcr.io/trungnd1985/quickbuilder:{version}` (`sha256:{index}`) |

## Known issues
{fragment-free: the maintainer edits the draft before approving; "None." by default}
```

"Database migrations" and "Content schema versions" are derived by the tool from git and are
never taken from fragments, so they cannot be forgotten.

## 2. Fragment format (`docs/releases/unreleased/<issue>-<slug>.md`)

```markdown
---
issue: 19
kind: added            # added | changed | fixed | security
breaking: false
config:
  added: [Releases:Foo]          # optional; flow lists of exact keys only
  changed: []
  removed: []
background: []                   # optional; flow list of quoted sentences
caches: ["Every cached page renders once more: the block stylesheet changes."]
manual: []
---
Operators can see the running release in Settings → About this installation.
```

- Front matter keys are exactly those above; lists are YAML flow lists on one line; strings
  with commas are double-quoted. Anything else is a parse error naming file and line.
- Body: 1–5 sentences of Markdown, no headings.
- File name prefix must equal `issue`.

## 3. Assembly rules (`qb-release notes`)

The tool fails (exit 1), naming the file or section, when:

1. any fragment does not parse (§2);
2. any fragment has `breaking: true` and the version is not a new major (`X.0.0`, X ≥ 1)
   (FR-034, FR-001a);
3. the keys added/removed in `docs/configuration.md` tables between `{previous}` and `HEAD`
   differ from the union of fragments' `config.added` / `config.removed`;
4. there are migrations or raised schema versions since `{previous}` and no fragment at all;
5. the release is a patch and the patch check would fail (FR-034a, contracts/release-pipeline.md §5);
6. a required section of §1 is missing from the template file itself;
7. a `*-visible-changes.json` file does not parse, has no `reason`, or no `pages`.

`{previous}` is the previous available release tag, or `pre-0.1.0-baseline` for 0.1.0.

## 4. PR-time check (`qb-release fragment check`)

A pull request must add at least one fragment when it adds a migration file, changes
`docs/configuration.md`, changes a file under `docs/contracts/` other than examples, or changes
`docker-compose.yml`. Adding the label `no-release-note` does **not** bypass it (labels are
not visible to the tool); a change that truly needs no note gets a `kind: changed` fragment
saying so in one line, which is cheap and reviewable.

## 5. The first release

0.1.0's notes are assembled from one fragment per feature already merged (001–018, 024,
035), written in this feature from each plan's "Operator impact" and the existing
"Upgrading from feature 0NN" sections, plus 019's own. Its "Supported upgrade path" reads:
"First release. Installations running a pre-release build from `main` upgrade by replacing
the image."
