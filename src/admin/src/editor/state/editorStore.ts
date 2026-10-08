// One editing session: one open document (011 data-model §5, research ED-10).
//
// The document is an immutable tree in a shallowRef. Every change is an operation from
// ../ops, applied through `apply`, which is the one place a change is committed — so undo,
// autosave and the recovery copy all see every change, and nothing else can slip past them.

import { defineStore } from 'pinia'
import { computed, ref, shallowRef } from 'vue'
import { editorApi } from '../../api/editor'
import { pagesApi } from '../../api/pages'
import { publishingApi } from '../../api/publishing'
import { ApiError } from '../../api/request'
import { validateDocument } from '../schema/documentValidator'
import { CURRENT_SCHEMA_VERSION } from '../schema/limits'
import type { ContentDocument, ContentNode, DocumentKind } from '../schema/schema'
import { defaultTokens, type TokenSet } from '../tokens/defaultTokens'
import type { OpRefusal, OpResult } from '../ops'
import { locate } from '../ops/tree'

/** Why the document cannot be changed right now (FR-028, FR-029, ED-19). */
/** `version`: a published version, opened from the history (012 FR-013). */
export type ReadOnlyReason = 'conflict' | 'forbidden' | 'gone' | 'invalid' | 'outdated' | 'recovery' | 'version'

export interface ReadOnlyState {
  reason: ReadOnlyReason
  /** For `invalid`: the failed rule and where. */
  rule?: string
  pointer?: string
  /** For `version`: which one, and when it was published. */
  versionNumber?: number
  publishedAtUtc?: string
}

/** A moment of the session: what undo restores (ED-12). */
export interface Snapshot {
  root: ContentNode
  selection: string | null
}

export type LoadState = 'idle' | 'loading' | 'ready' | 'not-found' | 'forbidden' | 'error'

/** Listeners the store tells about committed changes: history and autosave. */
export interface ChangeListener {
  /** Called with the state before a change, and a key when it may merge with the last one. */
  (before: Snapshot, coalesceKey: string | undefined): void
}

export const useEditorStore = defineStore('editor', () => {
  const siteId = ref('')
  const kind = ref<DocumentKind>('page')
  const documentId = ref('')
  const root = shallowRef<ContentNode | null>(null)
  const baseVersion = ref(0)
  const savedRoot = shallowRef<ContentNode | null>(null)
  const selection = ref<string | null>(null)
  const tokens = shallowRef<TokenSet>(defaultTokens)
  const readOnly = ref<ReadOnlyState | null>(null)
  const loadState = ref<LoadState>('idle')
  /** The last refusal of an operation, for the status line; cleared by the next change. */
  const lastRefusal = ref<OpRefusal | null>(null)

  const listeners = new Set<ChangeListener>()

  const document = computed<ContentDocument | null>(() =>
    root.value ? { schemaVersion: CURRENT_SCHEMA_VERSION, root: root.value } : null,
  )

  const selectedNode = computed(() => (root.value && selection.value ? locate(root.value, selection.value)?.node ?? null : null))

  /** Whether the document differs from the draft last loaded or saved. */
  const dirty = computed(() => root.value !== savedRoot.value)

  function onChange(listener: ChangeListener): () => void {
    listeners.add(listener)
    return () => listeners.delete(listener)
  }

  /**
   * Commits a successful operation, or reports why it was refused (FR-021, FR-042). An
   * operation that returns the same root changed nothing and records nothing.
   */
  function apply(result: OpResult, options: { coalesce?: string } = {}): OpRefusal | null {
    if (readOnly.value) {
      lastRefusal.value = { code: 'read_only' }
      return lastRefusal.value
    }
    if (!result.ok) {
      lastRefusal.value = result.refusal
      return result.refusal
    }

    lastRefusal.value = null
    if (result.root === root.value) {
      selection.value = result.selection
      return null
    }

    const before: Snapshot = { root: root.value!, selection: selection.value }
    root.value = result.root
    selection.value = result.selection
    for (const listener of listeners) listener(before, options.coalesce)
    return null
  }

  /** Puts a snapshot back without recording it: undo, redo and restore use this. */
  function restore(snapshot: Snapshot): void {
    root.value = snapshot.root
    selection.value = snapshot.selection && locate(snapshot.root, snapshot.selection) ? snapshot.selection : null
  }

  function select(id: string | null): void {
    selection.value = id && root.value && locate(root.value, id) ? id : null
  }

  /** Records that the draft now stored on the server is `saved`, at `version`. */
  function markSaved(saved: ContentNode, version: number): void {
    savedRoot.value = saved
    baseVersion.value = version
  }

  function setReadOnly(state: ReadOnlyState | null): void {
    readOnly.value = state
  }

  /**
   * Opens a document: its draft and the site's tokens, fetched together. A draft that no
   * longer validates opens read-only with the reason, so no save can make it worse (spec
   * Edge Cases; analysis C2).
   */
  async function load(site: string, documentKind: DocumentKind, id: string): Promise<void> {
    siteId.value = site
    kind.value = documentKind
    documentId.value = id
    loadState.value = 'loading'
    readOnly.value = null
    selection.value = null
    lastRefusal.value = null

    try {
      const [draft, designTokens] = await Promise.all([
        documentKind === 'page' ? pagesApi.pageDraft(site, id) : pagesApi.componentDraft(site, id),
        editorApi.designTokens(site),
      ])

      const loaded = draft.document as unknown as ContentDocument
      root.value = loaded.root
      savedRoot.value = loaded.root
      baseVersion.value = draft.version
      tokens.value = designTokens.document

      const check = validateDocument(draft.document, documentKind)
      if (!check.valid) readOnly.value = { reason: 'invalid', rule: check.rule, pointer: check.pointer }

      loadState.value = 'ready'
    } catch (error) {
      root.value = null
      savedRoot.value = null
      loadState.value = !(error instanceof ApiError) ? 'error' : error.status === 404 ? 'not-found' : error.status === 403 ? 'forbidden' : 'error'
    }
  }

  /**
   * Opens a retained published version read-only (012 FR-013). Nothing is saved from here: a
   * version comes back through Roll back or Copy to draft in the history.
   */
  async function loadVersion(site: string, documentKind: DocumentKind, id: string, number: number): Promise<void> {
    siteId.value = site
    kind.value = documentKind
    documentId.value = id
    loadState.value = 'loading'
    selection.value = null
    lastRefusal.value = null

    try {
      const [version, designTokens] = await Promise.all([
        publishingApi.version(site, documentKind, id, number),
        editorApi.designTokens(site),
      ])

      const loaded = version.document as unknown as ContentDocument
      root.value = loaded.root
      savedRoot.value = loaded.root
      baseVersion.value = 0
      tokens.value = designTokens.document
      readOnly.value = { reason: 'version', versionNumber: version.number, publishedAtUtc: version.publishedAtUtc }
      loadState.value = 'ready'
    } catch (error) {
      root.value = null
      savedRoot.value = null
      readOnly.value = null
      loadState.value = !(error instanceof ApiError) ? 'error' : error.status === 404 ? 'not-found' : error.status === 403 ? 'forbidden' : 'error'
    }
  }

  /** Replaces the whole document with a fresh draft from the server (Reload latest, ED-15). */
  function replaceWithDraft(loaded: ContentNode, version: number): void {
    root.value = loaded
    savedRoot.value = loaded
    baseVersion.value = version
    selection.value = null
    readOnly.value = null
  }

  function setTokens(next: TokenSet): void {
    tokens.value = next
  }

  return {
    siteId,
    kind,
    documentId,
    root,
    baseVersion,
    savedRoot,
    selection,
    tokens,
    readOnly,
    loadState,
    lastRefusal,
    document,
    selectedNode,
    dirty,
    onChange,
    apply,
    restore,
    select,
    markSaved,
    setReadOnly,
    loadVersion,
    load,
    replaceWithDraft,
    setTokens,
  }
})
