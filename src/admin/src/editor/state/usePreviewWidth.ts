// The canvas preview width (011 US5, FR-036, research ED-09), remembered per member in this
// browser. Storage may be unavailable (a private window): the width then starts at desktop
// and is simply not remembered.

import { ref, watch, type Ref } from 'vue'

export const PREVIEW_WIDTHS = { desktop: 1280, tablet: 768, mobile: 375 } as const

export type PreviewWidth = keyof typeof PREVIEW_WIDTHS

const isWidth = (value: unknown): value is PreviewWidth => typeof value === 'string' && value in PREVIEW_WIDTHS

export function usePreviewWidth(userId: string): Ref<PreviewWidth> {
  const key = `qb.editor.previewWidth.${userId}`
  let initial: PreviewWidth = 'desktop'
  try {
    const stored = localStorage.getItem(key)
    if (isWidth(stored)) initial = stored
  } catch {
    // Storage unavailable: start at desktop.
  }

  const width = ref<PreviewWidth>(initial)
  watch(width, (next) => {
    try {
      localStorage.setItem(key, next)
    } catch {
      // Not remembered this time.
    }
  })
  return width
}
