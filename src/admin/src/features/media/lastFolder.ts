// 013 MV-23: the folder the editor's picker opened last, per user and site, so choosing
// several images from one folder does not mean navigating to it each time. A convenience
// kept in this browser only: storage that is unavailable, full or blocked simply means the
// picker opens at the top level.

function key(userId: string, siteId: string): string {
  return `qb.media.lastFolder.${userId}.${siteId}`
}

/** The last folder, or null for the top level or when nothing could be read. */
export function get(userId: string, siteId: string): string | null {
  try {
    return localStorage.getItem(key(userId, siteId)) || null
  } catch {
    return null
  }
}

/** Remembers a folder; null forgets it (the top level). */
export function set(userId: string, siteId: string, folderId: string | null): void {
  try {
    if (folderId) {
      localStorage.setItem(key(userId, siteId), folderId)
    } else {
      localStorage.removeItem(key(userId, siteId))
    }
  } catch {
    // Not remembered this time; nothing else depends on it.
  }
}
