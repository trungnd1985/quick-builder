import { afterEach, describe, expect, it, vi } from 'vitest'
import { waitFor } from '@testing-library/vue'
import { useEditorStore } from '../../src/editor/state/editorStore'
import { defaultTokens, type TokenSet } from '../../src/editor/tokens/defaultTokens'
import { fontStackIds } from '../../src/editor/tokens/fontStacks'
import { readJson, pageExamples } from './contractFiles'
import { join } from 'node:path'
import { mountEditor, stubServer } from './harness'

// 011 SC-008, FR-035, research ED-06, ED-17: opening and using the editor reaches nothing
// but the installation. Every block type is drawn — an embed with a real YouTube address,
// every font stack — and nothing in the canvas may point anywhere else.

const ORIGIN = 'http://localhost:3000'

function outsideReferences(root: Element): string[] {
  const found: string[] = []
  const outside = (value: string | null) => {
    if (!value) return false
    try {
      return new URL(value, ORIGIN).origin !== new URL(ORIGIN).origin
    } catch {
      return false
    }
  }

  for (const element of root.querySelectorAll('*')) {
    for (const attribute of ['src', 'href', 'srcset', 'poster', 'data', 'action']) {
      const value = element.getAttribute(attribute)
      if (outside(value)) found.push(`${element.tagName.toLowerCase()}[${attribute}=${value}]`)
    }
    const style = element.getAttribute('style') ?? ''
    for (const match of style.matchAll(/url\(\s*['"]?([^'")]+)/g)) {
      if (outside(match[1])) found.push(`${element.tagName.toLowerCase()} style url(${match[1]})`)
    }
  }
  return found
}

describe('no third-party requests (SC-008)', () => {
  afterEach(() => vi.unstubAllGlobals())

  it.each(fontStackIds)('draws every block type with the %s font and reaches nothing outside the installation', async (font) => {
    stubServer(readJson(join(pageExamples, 'valid', 'every-block.json')))
    const { container } = await mountEditor()
    const store = useEditorStore()
    const tokens: TokenSet = { ...defaultTokens, typography: { ...defaultTokens.typography, headingFont: font, bodyFont: font } }
    store.setTokens(tokens)

    const canvas = container.querySelector('.qb-canvas')!
    await waitFor(() => expect(canvas.querySelector('[data-node-type="embed"]')).not.toBeNull())

    expect(outsideReferences(canvas)).toEqual([])
    expect(canvas.querySelector('iframe, script, object, embed, link[rel="stylesheet"]')).toBeNull()
    // The embed shows a placeholder that names the provider, not the player.
    expect(canvas.querySelector('[data-node-type="embed"]')!.textContent).toContain('YouTube')
    // Fonts are stacks of local names only.
    expect((canvas as HTMLElement).style.getPropertyValue('--qb-font-body')).not.toMatch(/url\(|https?:/)
  })

  it('only ever calls the installation itself', async () => {
    const server = stubServer(readJson(join(pageExamples, 'valid', 'every-block.json')))
    await mountEditor()

    expect(server.calls.length).toBeGreaterThan(0)
    for (const call of server.calls) expect(call.url.startsWith('/api/v1/'), call.url).toBe(true)
  })
})
