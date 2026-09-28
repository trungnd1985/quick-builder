// Counts canvas node renders, per node id, so a test can prove that an edit re-renders only
// the nodes on the path it changed (research ED-10, SC-005). Off unless a test switches it on.

const counts = new Map<string, number>()
let enabled = false

export function enableRenderProbe(): void {
  enabled = true
  counts.clear()
}

export function renderCount(id: string): void {
  if (enabled) counts.set(id, (counts.get(id) ?? 0) + 1)
}

export const renderCounts = (): ReadonlyMap<string, number> => counts
