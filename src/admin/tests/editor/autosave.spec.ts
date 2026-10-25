import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { ApiError } from '../../src/api/request'
import { setProp } from '../../src/editor/ops'
import { createAutosave } from '../../src/editor/state/autosave'
import { useEditorStore } from '../../src/editor/state/editorStore'
import type { ContentNode } from '../../src/editor/schema/schema'

// 011 FR-026 to FR-029, research ED-13: the autosave state machine.

const refused = (status: number, errorCode: string, extra: Record<string, unknown> = {}) =>
  new ApiError('refused', status, { title: 'x', status, errorCode, ...extra })

function setup(save: (document: unknown, version: number) => Promise<{ version: number }>) {
  const store = useEditorStore()
  const root: ContentNode = {
    id: 'root', type: 'root', props: {}, children: [
      { id: 's', type: 'section', props: {}, children: [{ id: 'h', type: 'heading', props: { text: 'Hi' }, children: [] }] },
    ],
  }
  store.root = root
  store.savedRoot = root
  store.baseVersion = 3
  const autosave = createAutosave(store, save)
  const change = (text: string) => store.apply(setProp(store.root!, 'h', 'text', text))
  return { store, autosave, change }
}

describe('autosave', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.useFakeTimers()
  })
  afterEach(() => vi.useRealTimers())

  it('waits two quiet seconds, then saves once against the loaded version', async () => {
    const save = vi.fn(async () => ({ version: 4 }))
    const { store, autosave, change } = setup(save)

    change('A')
    await vi.advanceTimersByTimeAsync(1500)
    change('AB')
    await vi.advanceTimersByTimeAsync(1500)
    expect(save).not.toHaveBeenCalled()
    expect(autosave.state.value).toBe('dirty')

    await vi.advanceTimersByTimeAsync(600)
    expect(save).toHaveBeenCalledTimes(1)
    expect(save.mock.calls[0][1]).toBe(3)
    expect(autosave.state.value).toBe('saved')
    expect(store.baseVersion).toBe(4)
  })

  it('saves a bound block with the binding format version, instead of stopping (016 rule 13)', async () => {
    const save = vi.fn(async () => ({ version: 4 }))
    const { store, autosave } = setup(save)

    store.apply(setProp(store.root!, 'h', 'bind', { text: { value: { field: 'f1' } } }))
    await vi.advanceTimersByTimeAsync(2100)

    expect(autosave.state.value).toBe('saved')
    expect(save).toHaveBeenCalledTimes(1)
    expect(save.mock.calls[0][0]).toMatchObject({ schemaVersion: 1, bindingVersion: 1 })
  })

  it('saves a document without bindings without a binding format version', async () => {
    const save = vi.fn(async () => ({ version: 4 }))
    const { change } = setup(save)

    change('A')
    await vi.advanceTimersByTimeAsync(2100)

    expect(save.mock.calls[0][0]).not.toHaveProperty('bindingVersion')
  })

  it('keeps one save in flight and saves again after it for changes made meanwhile', async () => {
    let finish: (value: { version: number }) => void = () => undefined
    const save = vi.fn(() => new Promise<{ version: number }>((resolve) => (finish = resolve)))
    const { autosave, change } = setup(save)

    change('A')
    await vi.advanceTimersByTimeAsync(2000)
    expect(autosave.state.value).toBe('saving')
    change('AB')
    await vi.advanceTimersByTimeAsync(2000)
    expect(save).toHaveBeenCalledTimes(1)

    finish({ version: 4 })
    await vi.advanceTimersByTimeAsync(2100)
    expect(save).toHaveBeenCalledTimes(2)
    expect(save.mock.calls[1][1]).toBe(4)
  })

  it('retries a network failure with growing waits, and Retry now skips the wait (FR-027)', async () => {
    const save = vi.fn()
      .mockRejectedValueOnce(new TypeError('Failed to fetch'))
      .mockRejectedValueOnce(refused(503, 'unavailable'))
      .mockResolvedValue({ version: 4 })
    const { autosave, change } = setup(save)

    change('A')
    await vi.advanceTimersByTimeAsync(2000)
    expect(autosave.state.value).toBe('retrying')
    await vi.advanceTimersByTimeAsync(1900)
    expect(save).toHaveBeenCalledTimes(1)
    await vi.advanceTimersByTimeAsync(200)
    expect(save).toHaveBeenCalledTimes(2)
    expect(autosave.state.value).toBe('retrying')

    // The second wait is 4 s; Retry now does not wait for it.
    await autosave.retryNow()
    expect(save).toHaveBeenCalledTimes(3)
    expect(autosave.state.value).toBe('saved')
  })

  it.each([
    [409, 'pages.out_of_date', 'conflict'],
    [409, 'pages.admin_outdated', 'outdated'],
    [403, 'identity.forbidden', 'forbidden'],
    [404, 'pages.not_found', 'gone'],
  ])('stops on %i %s and makes the editor read-only (FR-028, FR-029)', async (status, code, state) => {
    const save = vi.fn().mockRejectedValue(refused(status, code))
    const { store, autosave, change } = setup(save)

    change('A')
    await vi.advanceTimersByTimeAsync(2000)

    expect(autosave.state.value).toBe(state)
    expect(store.readOnly?.reason).toBe(state)
    change('AB')
    await vi.advanceTimersByTimeAsync(60_000)
    expect(save).toHaveBeenCalledTimes(1)
  })

  it('stops on an invalid document with the rule and where', async () => {
    const save = vi.fn().mockRejectedValue(refused(400, 'pages.invalid_document', { rule: 'props_invalid', pointer: '/root/children/0/children/0/props/text' }))
    const { store, autosave, change } = setup(save)

    change('A')
    await vi.advanceTimersByTimeAsync(2000)

    expect(autosave.state.value).toBe('invalid')
    expect(store.readOnly).toEqual({ reason: 'invalid', rule: 'props_invalid', pointer: '/root/children/0/children/0/props/text' })
  })

  it('sends nothing when the pre-check fails', async () => {
    const save = vi.fn(async () => ({ version: 4 }))
    const { store, autosave } = setup(save)

    // Something the operations would never produce: a second node with the same id.
    store.apply({ ok: true, root: { ...store.root!, children: [...store.root!.children, store.root!.children[0]] }, selection: null })
    await vi.advanceTimersByTimeAsync(2000)

    expect(save).not.toHaveBeenCalled()
    expect(autosave.state.value).toBe('invalid')
  })

  it('is saved again when undo brings back the saved document', async () => {
    const save = vi.fn(async () => ({ version: 4 }))
    const { store, autosave, change } = setup(save)
    const saved = store.root!

    change('A')
    expect(autosave.state.value).toBe('dirty')
    store.restore({ root: saved, selection: null })

    expect(autosave.state.value).toBe('saved')
    await vi.advanceTimersByTimeAsync(5000)
    expect(save).not.toHaveBeenCalled()
  })

  it('starts over after Reload latest', async () => {
    const save = vi.fn().mockRejectedValueOnce(refused(409, 'pages.out_of_date')).mockResolvedValue({ version: 9 })
    const { store, autosave, change } = setup(save)
    change('A')
    await vi.advanceTimersByTimeAsync(2000)
    expect(autosave.state.value).toBe('conflict')

    store.replaceWithDraft(store.root!, 8)
    autosave.reset()
    expect(autosave.state.value).toBe('saved')

    change('B')
    await vi.advanceTimersByTimeAsync(2000)
    expect(save.mock.calls.at(-1)![1]).toBe(8)
    expect(autosave.state.value).toBe('saved')
  })

  it('never reports its own quick saves as a conflict: each one is based on the last (023 FR-004)', async () => {
    let version = 3
    const save = vi.fn(async (_document: unknown, based: number) => {
      if (based !== version) throw refused(409, 'pages.out_of_date')
      return { version: ++version }
    })
    const { autosave, change } = setup(save)

    for (const text of ['A', 'AB', 'ABC', 'ABCD']) {
      change(text)
      await vi.advanceTimersByTimeAsync(2100)
    }

    expect(save).toHaveBeenCalledTimes(4)
    expect(save.mock.calls.map((call) => call[1])).toEqual([3, 4, 5, 6])
    expect(autosave.state.value).toBe('saved')
    expect(autosave.conflict.value).toBeNull()
  })

  it('keeps who changed the draft and when, for the conflict dialog (023 FR-003)', async () => {
    const changedBy = { id: 'u-ben', displayName: 'Ben Hale' }
    const save = vi.fn().mockRejectedValue(refused(409, 'pages.out_of_date', { changedBy, changedAtUtc: '2026-10-25T10:42:00Z', currentVersion: 12 }))
    const { autosave, change } = setup(save)

    change('A')
    await vi.advanceTimersByTimeAsync(2100)

    expect(autosave.state.value).toBe('conflict')
    expect(autosave.conflict.value).toEqual({ changedBy, changedAtUtc: '2026-10-25T10:42:00Z', changeKind: 'member' })

    autosave.reset()
    expect(autosave.conflict.value).toBeNull()
  })

  it('stops in the locked state when another session holds the lock, and never sends again (023 research CE-07)', async () => {
    const store = useEditorStore()
    const root: ContentNode = { id: 'root', type: 'root', props: {}, children: [{ id: 's', type: 'section', props: {}, children: [{ id: 'h', type: 'heading', props: { text: 'Hi' }, children: [] }] }] }
    store.root = root
    store.savedRoot = root
    store.baseVersion = 3
    const save = vi.fn().mockRejectedValue(refused(409, 'editing.lock_held'))
    const onLockLost = vi.fn()
    const autosave = createAutosave(store, save, { onLockLost })

    store.apply(setProp(store.root!, 'h', 'text', 'A'))
    await vi.advanceTimersByTimeAsync(2100)

    expect(autosave.state.value).toBe('locked')
    expect(onLockLost).toHaveBeenCalledTimes(1)
    store.apply(setProp(store.root!, 'h', 'text', 'AB'))
    await vi.advanceTimersByTimeAsync(10_000)
    expect(save).toHaveBeenCalledTimes(1)
  })

  it('holds a save back until the lock is held, and saves nothing when it is refused (023 research CE-04)', async () => {
    const store = useEditorStore()
    const root: ContentNode = { id: 'root', type: 'root', props: {}, children: [{ id: 's', type: 'section', props: {}, children: [{ id: 'h', type: 'heading', props: { text: 'Hi' }, children: [] }] }] }
    store.root = root
    store.savedRoot = root
    store.baseVersion = 3
    const save = vi.fn(async () => ({ version: 4 }))
    const beforeSave = vi.fn().mockResolvedValueOnce(false).mockResolvedValue(true)
    const autosave = createAutosave(store, save, { beforeSave })

    store.apply(setProp(store.root!, 'h', 'text', 'A'))
    await vi.advanceTimersByTimeAsync(2100)
    expect(save).not.toHaveBeenCalled()
    expect(autosave.state.value).toBe('dirty')

    await autosave.saveNow()
    expect(save).toHaveBeenCalledTimes(1)
  })
})
