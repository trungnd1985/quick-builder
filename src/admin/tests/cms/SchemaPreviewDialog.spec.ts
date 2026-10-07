import { describe, expect, it } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/vue'
import SchemaPreviewDialog from '../../src/features/cms/SchemaPreviewDialog.vue'
import type { Preview } from '../../src/api/cms'
import { en, i18n, violations } from './harness'

// 014 FR-023, FR-036: the preview lists what each change does, warns when values are lost,
// and its button says what it will do.

const change = (extra: Partial<Preview['changes'][number]>): Preview['changes'][number] => ({
  kind: 'typeChanged', rule: 'lossy', fieldId: 'f2', key: 'minutes', name: 'Minutes', setting: null, optionId: null,
  from: 'text', to: 'number', counts: {}, examples: [], fillInAllowed: false, ...extra,
})

function renderDialog(preview: Preview, saving = false) {
  return render(SchemaPreviewDialog, { props: { preview, saving }, global: { plugins: [i18n] } })
}

describe('SchemaPreviewDialog', () => {
  it('describes each change with its counts and examples, and warns that losses cannot be undone', async () => {
    const { container, emitted } = renderDialog({
      revision: 2,
      itemCount: 4,
      requiresConfirmation: true,
      digest: 'd'.repeat(64),
      changes: [
        change({ counts: { converted: 1, partlyLost: 1, cleared: 2 }, examples: [{ itemId: 'i', title: null, outcome: 'cleared' }] }),
        change({ kind: 'fieldRenamed', rule: 'free' }),
      ],
    })

    expect(screen.getByText('Change Minutes from Text to Number')).toBeTruthy()
    expect(screen.getByText('2 cleared')).toBeTruthy()
    expect(screen.getByText('1 partly lost')).toBeTruthy()
    expect(screen.getByText(new RegExp(en.cms.preview.untitled))).toBeTruthy()
    expect(screen.getByText(en.cms.preview.undo)).toBeTruthy()
    // Free changes are not listed.
    expect(screen.queryByText('Change Minutes')).toBeNull()
    expect(await violations(container)).toEqual([])

    await fireEvent.click(screen.getByRole('button', { name: en.cms.preview.confirm }))
    expect(emitted().confirm).toHaveLength(1)
  })

  it('saves without a warning when nothing is lost, and mentions UTC for dates', () => {
    renderDialog({
      revision: 2,
      itemCount: 3,
      requiresConfirmation: false,
      digest: 'd'.repeat(64),
      changes: [change({ kind: 'settingsChanged', rule: 'lossless', setting: 'withTime', from: 'false', to: 'true', counts: { converted: 3 } })],
    })

    expect(screen.queryByText(en.cms.preview.undo)).toBeNull()
    expect(screen.getByText(en.cms.preview.utc)).toBeTruthy()
    expect(screen.getByRole('button', { name: en.cms.preview.save })).toBeTruthy()
  })

  it('cancels with Escape and shows progress while saving', async () => {
    const { emitted } = renderDialog({ revision: 2, itemCount: 1, requiresConfirmation: true, digest: 'd'.repeat(64), changes: [change({ counts: { cleared: 1 } })] }, true)

    expect(screen.getByRole('button', { name: en.cms.preview.saving })).toHaveProperty('disabled', true)
    await fireEvent.keyDown(screen.getByRole('alertdialog'), { key: 'Escape' })
    expect(emitted().cancel).toHaveLength(1)
  })
})
