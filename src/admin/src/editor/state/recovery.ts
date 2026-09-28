// Recovery copies of unsaved changes (011 clarification Q5, FR-030, research ED-19).
//
// While a document has unsaved changes, a copy lives in the browser's IndexedDB, keyed by the
// user, the site and the document. If the tab closes or the browser crashes before autosave
// gets them to the server, the next visit to that document offers them back:
//   - Restore, when the saved draft is the one the copy was based on;
//   - Open read-only to copy blocks, when someone has saved since — never an overwrite.
// Nothing is restored without asking. A copy goes when a save covers it, when the member
// declines it, when they sign out, or when the document is gone or no longer theirs to edit.
//
// IndexedDB can be missing (a private window) or full: recovery then quietly does nothing,
// and the leave warning still applies.

import type { ContentDocument, ContentNode, DocumentKind } from '../schema/schema'

const DATABASE = 'qb-editor-recovery'
const STORE = 'copies'

export interface RecoveryKey {
  userId: string
  siteId: string
  kind: DocumentKind
  documentId: string
}

export interface RecoveryRecord extends RecoveryKey {
  document: ContentDocument
  /** The draft version the unsaved changes were made on. */
  baseVersion: number
  savedAtUtc?: string
}

export type RecoveryOffer = { kind: 'restore' | 'read-only'; record: RecoveryRecord }

const keyOf = (key: RecoveryKey): string => [key.userId, key.siteId, key.kind, key.documentId].join('/')

let opening: Promise<IDBDatabase | null> | null = null

function open(): Promise<IDBDatabase | null> {
  if (opening) return opening
  const attempt = new Promise<IDBDatabase | null>((resolve) => {
    try {
      if (typeof indexedDB === 'undefined') return resolve(null)
      const request = indexedDB.open(DATABASE, 1)
      request.onupgradeneeded = () => {
        const store = request.result.createObjectStore(STORE, { keyPath: 'key' })
        store.createIndex('userId', 'userId')
      }
      request.onsuccess = () => resolve(request.result)
      request.onerror = () => resolve(null)
      request.onblocked = () => resolve(null)
    } catch {
      resolve(null)
    }
  })
  // Only a database that opened is kept; after a failure the next call tries again.
  opening = attempt.then((database) => {
    if (!database) opening = null
    return database
  })
  return opening
}

/** Runs one request in a transaction; null whenever IndexedDB cannot be used. */
async function run<T>(mode: IDBTransactionMode, work: (store: IDBObjectStore) => IDBRequest<T> | void): Promise<T | null> {
  const database = await open()
  if (!database) return null
  return new Promise((resolve) => {
    try {
      const transaction = database.transaction(STORE, mode)
      const request = work(transaction.objectStore(STORE))
      transaction.oncomplete = () => resolve(request ? request.result : null)
      transaction.onerror = () => resolve(null)
      transaction.onabort = () => resolve(null)
    } catch {
      resolve(null)
    }
  })
}

export async function writeRecovery(record: RecoveryRecord): Promise<void> {
  await run('readwrite', (store) => {
    store.put({ ...record, key: keyOf(record), savedAtUtc: new Date().toISOString() })
  })
}

export async function findRecovery(key: RecoveryKey): Promise<RecoveryRecord | null> {
  const found = await run<RecoveryRecord & { key: string }>('readonly', (store) => store.get(keyOf(key)))
  return found ?? null
}

export async function removeRecovery(key: RecoveryKey): Promise<void> {
  await run('readwrite', (store) => {
    store.delete(keyOf(key))
  })
}

/** Removes every copy of one user: on sign-out, so the next person on this browser sees none. */
export async function clearRecoveryForUser(userId: string): Promise<void> {
  const keys = await run<IDBValidKey[]>('readonly', (store) => store.index('userId').getAllKeys(IDBKeyRange.only(userId)))
  if (!keys || keys.length === 0) return
  await run('readwrite', (store) => {
    for (const key of keys) store.delete(key)
  })
}

/**
 * What to offer for a document just opened at `loaded`: nothing if there is no copy or it
 * holds nothing new (and then the copy is dropped); Restore if the saved draft is still the one
 * the copy was based on; otherwise read-only copying.
 */
export async function recoveryOffer(key: RecoveryKey, loaded: { version: number; root: ContentNode }): Promise<RecoveryOffer | null> {
  const record = await findRecovery(key)
  if (!record) return null

  if (JSON.stringify(record.document.root) === JSON.stringify(loaded.root)) {
    await removeRecovery(key)
    return null
  }
  return { kind: record.baseVersion === loaded.version ? 'restore' : 'read-only', record }
}
