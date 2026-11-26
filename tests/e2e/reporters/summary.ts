// The run's summary on the GitHub job page (specs/025-e2e-harness, E2E-11; FR-019): passed,
// failed, flaky and skipped counts, and the flaky tests by name, so a test that passed only
// on its retry is visible without downloading anything.

import type { FullResult, Reporter, Suite, TestCase } from '@playwright/test/reporter'
import { appendFile } from 'node:fs/promises'

const isInstallationProject = (test: TestCase) => /-(setup|teardown)$/.test(test.parent.project()?.name ?? '')

export default class SummaryReporter implements Reporter {
  private root?: Suite

  onBegin(_config: unknown, suite: Suite): void {
    this.root = suite
  }

  async onEnd(result: FullResult): Promise<void> {
    const file = process.env.GITHUB_STEP_SUMMARY
    if (!file || !this.root) {
      return
    }

    const tests = this.root.allTests().filter((t) => !isInstallationProject(t))
    const count = (outcome: ReturnType<TestCase['outcome']>) => tests.filter((t) => t.outcome() === outcome)
    const flaky = count('flaky')

    const lines = [
      `### Browser suite: ${result.status}`,
      '',
      '| Passed | Failed | Flaky | Skipped |',
      '|---|---|---|---|',
      `| ${count('expected').length} | ${count('unexpected').length} | ${flaky.length} | ${count('skipped').length} |`,
      '',
      ...(flaky.length > 0
        ? ['Passed only on retry (fix the cause, do not raise retries):', '', ...flaky.map((t) => `- ${t.titlePath().slice(1).join(' › ')}`), '']
        : []),
    ]
    await appendFile(file, `${lines.join('\n')}\n`)
  }
}
