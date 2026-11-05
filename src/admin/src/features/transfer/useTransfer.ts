// Follows one transfer while its screen is open (020 contracts/admin-transfer.md §1,
// research ST-22). Polls every 2 seconds until the transfer reaches a final state, or a state
// that waits for the Administrator (`awaiting_confirmation`). A transfer keeps running on the
// server when the screen closes; reopening the screen picks it up again by id.

import { onBeforeUnmount, shallowRef } from 'vue'
import { FINAL_STATES, transfersApi, type Transfer } from '../../api/transfers'

export const TRANSFER_POLL_MS = 2_000

/** States the poll stops at: nothing changes until someone acts. */
const RESTING = new Set<Transfer['state']>(['uploading', 'uploaded', 'awaiting_confirmation'])

export function isFinished(transfer: Transfer | null): boolean {
  return transfer !== null && FINAL_STATES.has(transfer.state)
}

export function useTransfer() {
  const transfer = shallowRef<Transfer | null>(null)
  const error = shallowRef<unknown>(null)
  let timer: ReturnType<typeof setTimeout> | null = null
  let stopped = false

  function stop(): void {
    stopped = true
    if (timer) clearTimeout(timer)
    timer = null
  }

  async function poll(id: string): Promise<void> {
    try {
      const next = await transfersApi.get(id)
      transfer.value = next
      error.value = null
      if (FINAL_STATES.has(next.state) || RESTING.has(next.state)) {
        stop()
        return
      }
    } catch (failure) {
      // A missed poll is retried; the transfer is unaffected.
      error.value = failure
    }

    if (!stopped) timer = setTimeout(() => void poll(id), TRANSFER_POLL_MS)
  }

  /** Starts following a transfer, from the state the caller already has, if any. */
  function follow(next: Transfer | string): void {
    stop()
    stopped = false
    if (typeof next === 'string') {
      void poll(next)
      return
    }

    transfer.value = next
    if (!FINAL_STATES.has(next.state) && !RESTING.has(next.state)) {
      timer = setTimeout(() => void poll(next.id), TRANSFER_POLL_MS)
    }
  }

  async function cancel(): Promise<void> {
    if (!transfer.value) return
    transfer.value = await transfersApi.cancel(transfer.value.id)
    follow(transfer.value)
  }

  onBeforeUnmount(stop)

  return { transfer, error, follow, stop, cancel }
}
