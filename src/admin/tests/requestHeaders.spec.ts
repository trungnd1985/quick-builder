import { afterEach, describe, expect, it, vi } from 'vitest'
import { request, RICH_TEXT_HEADER } from '../src/api/request'

describe('request', () => {
  afterEach(() => vi.unstubAllGlobals())

  it.each(['GET', 'PUT'])('says on every %s which rich text formats this admin keeps (035 research RT-13)', async (method) => {
    const fetch = vi.fn().mockResolvedValue(new Response('{}', { status: 200, headers: { 'content-type': 'application/json' } }))
    vi.stubGlobal('fetch', fetch)

    await request('/anything', { method })

    expect((fetch.mock.calls[0]![1] as RequestInit).headers).toMatchObject({ [RICH_TEXT_HEADER]: '2' })
  })
})
