// @vitest-environment jsdom
import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import App from './App'
import { devIdentities } from './api'

afterEach(() => {
  cleanup()
  vi.unstubAllGlobals()
  localStorage.clear()
})

// The sections each seeded user should see. Lint, build and the API tests all passed once
// with the My Claims page deleted from App.tsx; this catches that.
const expected: Record<string, string[]> = {
  Alice: ['My claims'],
  Manny: ['Waiting for my approval', 'My claims'],
  Fiona: ['Approved, waiting for payment', 'My claims'],
  Adam: ['Users', 'Audit log'],
}

it.each(devIdentities)('shows $name the right sections', async (identity) => {
  // A new empty list for every request: a Response body can only be read once.
  vi.stubGlobal(
    'fetch',
    vi.fn(async () => new Response('[]')),
  )
  localStorage.setItem('dev-identity', identity.id)

  render(<App />)

  const headings = screen
    .queryAllByRole('heading', { level: 2 })
    .map((heading) => heading.textContent)
  expect(headings).toEqual(expected[identity.name])
})
