// Accessibility scans of every admin screen a test reaches (specs/025-e2e-harness, E2E-09;
// FR-023a). The admin's own floor is docs/admin-accessibility-baseline.md.

import AxeBuilder from '@axe-core/playwright'
import { expect, type Page, type TestInfo } from '@playwright/test'
import { appendFile, mkdir } from 'node:fs/promises'
import path from 'node:path'
import { RESULTS } from '../setup/installations.js'
import { exceptionKey, readExceptions, type ScreenName } from './exceptions.js'

const FAILING_IMPACTS = new Set(['critical', 'serious'])

export async function scan(page: Page, screen: ScreenName, testInfo: TestInfo): Promise<void> {
  const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze()
  const excepted = new Set((await readExceptions()).filter((e) => e.screen === screen).map((e) => e.rule))

  const failing = results.violations.filter((v) => FAILING_IMPACTS.has(v.impact ?? '') && !excepted.has(v.id))
  const matched = results.violations.filter((v) => excepted.has(v.id)).map((v) => exceptionKey({ rule: v.id, screen }))
  const reported = results.violations.filter((v) => !FAILING_IMPACTS.has(v.impact ?? ''))

  if (matched.length > 0) {
    // One line per matched exception; global teardown reads every worker's lines (FR-023b).
    const directory = path.join(RESULTS, 'a11y-matched')
    await mkdir(directory, { recursive: true })
    await appendFile(path.join(directory, `${testInfo.workerIndex}.jsonl`), matched.map((k) => `${JSON.stringify(k)}\n`).join(''))
  }

  // Always attached, so a report shows that each screen was scanned and how many rules held.
  await testInfo.attach(`a11y-${screen}.json`, {
    body: JSON.stringify({ url: page.url(), rulesPassed: results.passes.length, failing, excepted: matched, reported }, null, 2),
    contentType: 'application/json',
  })

  const summary = failing.map(
    (v) => `${v.id} (${v.impact}) on ${screen}: ${v.help}\n    ${v.nodes.map((n) => n.target.join(' ')).join('\n    ')}`,
  )
  expect(summary, `Critical or serious accessibility problems on ${screen} (see a11y-exceptions.json)`).toEqual([])
}
