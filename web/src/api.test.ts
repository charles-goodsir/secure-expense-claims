import { afterEach, describe, expect, it, vi } from 'vitest'
import { api, ApiError, devIdentities, formatMoney } from './api'

const alice = devIdentities[0]

afterEach(() => vi.unstubAllGlobals())

describe('api', () => {
  it('sends the dev sign-in headers to the proxied path', async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(new Response('[]', { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)

    await api(alice, '/claims')

    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/claims')
    expect(init.headers).toMatchObject({
      'X-Dev-User': alice.id,
      'X-Dev-Roles': 'Employee',
    })
  })

  it('turns an error response into an ApiError with the status and title', async () => {
    const problem = JSON.stringify({ title: 'Conflict', status: 409 })
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(new Response(problem, { status: 409 })),
    )

    const error = await api(alice, '/claims/x/submit', {
      method: 'POST',
    }).catch((e) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect(error).toMatchObject({ status: 409, message: 'Conflict' })
  })

  it('returns undefined for 204 No Content', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(new Response(null, { status: 204 })),
    )

    expect(
      await api(alice, '/admin/users/x/manager', { method: 'PUT' }),
    ).toBeUndefined()
  })
})

describe('formatMoney', () => {
  it('formats NZD with two decimals', () => {
    expect(formatMoney(42.5, 'NZD')).toBe('$42.50')
  })
})
