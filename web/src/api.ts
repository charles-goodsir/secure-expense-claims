// Development sign-in: the seeded users from the API's DevSeed, with the roles the stub
// sign-in should give them. Entra ID replaces this in Phase 3.
export type Identity = { id: string; name: string; roles: string[] }

export const devIdentities: Identity[] = [
  {
    id: '0199f0a4-0000-7000-8000-000000000001',
    name: 'Alice',
    roles: ['Employee'],
  },
  {
    id: '0199f0a4-0000-7000-8000-00000000000a',
    name: 'Manny',
    roles: ['Employee', 'Manager'],
  },
  {
    id: '0199f0a4-0000-7000-8000-00000000000f',
    name: 'Fiona',
    roles: ['Employee', 'Finance'],
  },
  {
    id: '0199f0a4-0000-7000-8000-0000000000ad',
    name: 'Adam',
    roles: ['Admin'],
  },
]

export type ClaimStatus =
  | 'Draft'
  | 'Submitted'
  | 'Approved'
  | 'Rejected'
  | 'Paid'

export type Claim = {
  id: string
  description: string
  amount: number
  currency: string
  status: ClaimStatus
  createdAt: string
}

// Problem details from the API, plus the HTTP status, so the page can show a useful message.
export class ApiError extends Error {
  readonly status: number

  constructor(status: number, title: string) {
    super(title)
    this.status = status
  }
}

export function devHeaders(identity: Identity): Record<string, string> {
  return { 'X-Dev-User': identity.id, 'X-Dev-Roles': identity.roles.join(',') }
}

export async function api<T>(
  identity: Identity,
  path: string,
  init: RequestInit = {},
): Promise<T> {
  // JSON bodies are sent as strings. A FormData upload must not get this header: the browser
  // sets multipart/form-data itself, with the boundary the API needs to read the parts.
  const json = typeof init.body === 'string'
  const response = await fetch(`/api${path}`, {
    ...init,
    headers: {
      ...(json ? { 'Content-Type': 'application/json' } : {}),
      ...devHeaders(identity),
      ...init.headers,
    },
  })

  if (!response.ok) {
    const problem = await response.json().catch(() => null)
    throw new ApiError(
      response.status,
      problem?.detail ?? problem?.title ?? response.statusText,
    )
  }
  return response.status === 204
    ? (undefined as T)
    : ((await response.json()) as T)
}
// Receipts need the sign-in headers, so a plain link won't work. This fetches the file and
// hands it to the browser as a download, using the file name the API chose.
export async function download(identity: Identity, path: string) {
  const response = await fetch(`/api${path}`, { headers: devHeaders(identity) })
  if (!response.ok) {
    throw new ApiError(response.status, response.statusText)
  }
  const disposition = response.headers.get('Content-Disposition') ?? ''
  const link = document.createElement('a')
  link.href = URL.createObjectURL(await response.blob())
  link.download = /filename=([^;]+)/.exec(disposition)?.[1] ?? 'receipt'
  link.click()
  // Give the browser a moment to start the download before the URL is released.
  setTimeout(() => URL.revokeObjectURL(link.href), 1000)
}

export function formatMoney(amount: number, currency: string): string {
  return new Intl.NumberFormat('en-NZ', { style: 'currency', currency }).format(
    amount,
  )
}
