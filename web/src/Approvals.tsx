import { useEffect, useState } from 'react'
import { api, ApiError, formatMoney, type Claim, type Identity } from './api'

type Props = { identity: Identity }

// Submitted claims from the manager's direct reports. The API decides which claims appear
// and who may decide them; this page only shows the result.
export function Approvals({ identity }: Props) {
  const [claims, setClaims] = useState<Claim[]>([])
  const [error, setError] = useState<string | null>(null)

  async function load() {
    setClaims(await api<Claim[]>(identity, '/approvals'))
  }

  useEffect(() => {
    let current = true
    api<Claim[]>(identity, '/approvals')
      .then((list) => current && setClaims(list))
      .catch((e: Error) => current && setError(e.message))
    return () => {
      current = false
    }
  }, [identity])

  async function decide(claim: Claim, decision: 'approve' | 'reject') {
    setError(null)
    try {
      await api(identity, `/claims/${claim.id}/${decision}`, { method: 'POST' })
    } catch (e) {
      setError(e instanceof ApiError ? `${e.status}: ${e.message}` : String(e))
    }
    await load()
  }

  return (
    <section>
      <h2>Waiting for my approval</h2>

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
                <button type="button" onClick={() => decide(claim, 'approve')}>
                  Approve
                </button>
                <button type="button" onClick={() => decide(claim, 'reject')}>
                  Reject
                </button>
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
