import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import axe from 'axe-core'
import en from '../../src/i18n/en.json'
import ImportCheckView from '../../src/features/transfer/ImportCheckView.vue'
import TransferReport from '../../src/features/transfer/TransferReport.vue'
import type { CheckResult, ImportReport } from '../../src/api/transfers'
import { i18n } from '../editor/harness'

// 020 contracts/admin-transfer.md §3 (the check) and data-model §4 (the report).

const result = (extra: Partial<CheckResult> = {}): CheckResult => ({
  archive: { format: 1, release: '1.4.0', createdAtUtc: '2026-11-02T09:00:00Z', source: { siteId: 's-1', siteName: 'Acme', enabled: true }, sameSiteAsTarget: false },
  versions: [{ contract: 'page-schema', archiveVersion: 1, currentVersion: 2 }],
  counts: { page: 84, item: 1240 },
  problems: [
    { severity: 'error', code: 'transfer.document_invalid', subject: { kind: 'page', id: 'p-1', label: 'About' }, detail: 'A block is not allowed here.' },
    { severity: 'warning', code: 'transfer.media_missing', subject: { kind: 'media_file', id: 'f-1', label: 'logo.png' }, detail: 'The archive holds its details but not its content.' },
  ],
  hostNamesLeftOut: ['acme.example'],
  hostNamesKept: [],
  conflicts: [],
  schemaChanges: [],
  removals: [],
  returns: [],
  changes: [],
  pastSchedules: [{ subject: { kind: 'page', id: 'p-2', label: 'Team' }, atUtc: '2026-10-01T09:00:00Z', action: 'publish' }],
  allowedModes: ['new_site', 'merge'],
  totals: {},
  ...extra,
})

describe('ImportCheckView', () => {
  it('shows the archive, versions, errors, warnings, host names left out and past schedules under their own headings', async () => {
    const { container } = render(ImportCheckView, { props: { result: result(), mode: 'new_site' }, global: { plugins: [i18n] } })

    expect(container.textContent).toContain('Exported from “Acme” by release 1.4.0')
    expect(container.textContent).toContain('84 pages')
    expect(container.textContent).toContain('page-schema version 1 will be upgraded to 2')
    expect(screen.getByRole('heading', { name: 'One problem stops the import' })).toBeTruthy()
    expect(container.textContent).toContain('About: A block is not allowed here.')
    expect(screen.getByRole('heading', { name: 'One warning' })).toBeTruthy()
    expect(container.textContent).toContain('logo.png')
    expect(container.textContent).toContain('acme.example')
    expect(container.textContent).toContain('Team')
    expect((await axe.run(container)).violations).toEqual([])
  })

  it('says when the archive was exported from the target site', () => {
    const { container } = render(ImportCheckView, {
      props: { result: result({ archive: { ...result().archive, sameSiteAsTarget: true }, problems: [] }), mode: 'merge' },
      global: { plugins: [i18n] },
    })

    expect(container.textContent).toContain(en.transfer.check.sameSite)
    expect(container.textContent).not.toContain('acme.example')
  })
})

describe('TransferReport', () => {
  const report: ImportReport = {
    mode: 'new_site', startFresh: false, policy: null, siteId: 's-9', imported: { page: 4, item: 2 },
    resolved: [], leftOut: [{ subject: { kind: 'host_name', id: 'h-1', label: 'acme.example' }, reason: 'host_name_taken' }],
    broken: [], droppedSchedules: [{ subject: { kind: 'page', id: 'p-2', label: 'Team' }, atUtc: '2026-10-01T09:00:00Z' }],
    removed: {}, preRestoreTransferId: null, historicalAuthors: 2,
  }

  it('lists what was imported, left out and dropped, and links to the site', async () => {
    const router = createRouter({ history: createMemoryHistory(), routes: [{ path: '/sites/:siteId', name: 'site', component: { template: '<div />' } }, { path: '/', component: { template: '<div />' } }] })
    const { container } = render(TransferReport, { props: { report }, global: { plugins: [i18n, router] } })

    expect(container.textContent).toContain('Imported 4 pages, 2 items.')
    expect(container.textContent).toContain('acme.example — used by another site here')
    expect(container.textContent).toContain('Team')
    expect(container.textContent).toContain('2 authors of the archive are not users here')
    expect(screen.getByRole('link', { name: en.transfer.report.openSite }).getAttribute('href')).toBe('/sites/s-9')
    expect((await axe.run(container)).violations).toEqual([])
  })
})
