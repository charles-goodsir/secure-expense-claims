import { useEffect, useState } from 'react'
import { api, ApiError, formatMoney, type Claim, type Identity } from './api'

type Props = {
  identity: Identity
  title: string
  // The list endpoint, such as '/approvals'. The API decides which claims it returns.
  listPath: string
  // One button per action. Each posts to /claims/{id}/{action}.
  actions: string[]
}

// A list of claims waiting on the signed-in user, with a button for each action they can take.
// The API decides which claims appear and who may act on them; this page only shows the result.
export function ClaimQueue({ identity, title, listPath, actions }: Props) {
  const [claims, setClaims] = useState<Claim[]>([])
  const [error, setError] = useState<string | null>(null)

  async function load() {
    setClaims(await api<Claim[]>(identity, listPath))
  }

  useEffect(() => {
    let current = true
    api<Claim[]>(identity, listPath)
      .then((list) => current && setClaims(list))
      .catch((e: Error) => current && setError(e.message))
    return () => {
      current = false
    }
  }, [identity, listPath])

  async function act(claim: Claim, action: string) {
    setError(null)
    try {
      await api(identity, `/claims/${claim.id}/${action}`, { method: 'POST' })
    } catch (e) {
      setError(e instanceof ApiError ? `${e.status}: ${e.message}` : String(e))
    }
    await load()
  }

  return (
    <section>
      <h2>{title}</h2>

      {error && (
        <p role="alert" className="error">
          {error}
        </p>
      )}

      <table>
        <thead>
          <tr>
            <th>Description</th>
            <th>Amount</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {claims.map((claim) => (
            <tr key={claim.id}>
              <td>{claim.description}</td>
              <td>{formatMoney(claim.amount, claim.currency)}</td>
              <td className="actions">
                {actions.map((action) => (
                  <button
                    key={action}
                    type="button"
                    onClick={() => act(claim, action)}
                  >
                    {action[0].toUpperCase() + action.slice(1)}
                  </button>
                ))}
              </td>
            </tr>
          ))}
          {claims.length === 0 && (
            <tr>
              <td colSpan={3}>Nothing waiting.</td>
            </tr>
          )}
        </tbody>
      </table>
    </section>
  )
}
