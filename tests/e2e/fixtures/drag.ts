// Dragging with the pointer (specs/025-e2e-harness, E2E-10; FR-009). The editor uses native
// HTML5 drag and drop (011 ED-02), which Playwright drives in Chromium with dragTo. If a
// handler needs a DataTransfer payload the synthetic drag did not carry, the fallback
// dispatches the drag events with one shared DataTransfer, as a browser would.

import type { Locator, Page } from '@playwright/test'

export async function dragBlock(page: Page, source: Locator, target: Locator, dropped: () => Promise<void>): Promise<void> {
  await source.dragTo(target)
  try {
    await dropped()
    return
  } catch {
    // Fall through to the dispatched drag.
  }

  const transfer = await page.evaluateHandle(() => new DataTransfer())
  await source.dispatchEvent('dragstart', { dataTransfer: transfer })
  await target.dispatchEvent('dragenter', { dataTransfer: transfer })
  await target.dispatchEvent('dragover', { dataTransfer: transfer })
  await target.dispatchEvent('drop', { dataTransfer: transfer })
  await source.dispatchEvent('dragend', { dataTransfer: transfer })
  await dropped()
}
