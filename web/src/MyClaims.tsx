import { useEffect, useState, type FormEvent } from 'react'
import { api, ApiError, formatMoney, type Claim, type Identity } from './api'

type Props = { identity: Identity }

export function MyClaims({ identity }: Props) {
  const [claims, setClaims] = useState<Claim[]>([])
  const [editing, setEditing] = useState<Claim | null>(null)
  const [description, setDescription] = useState('')
  const [amount, setAmount] = useState('')
  const [error, setError] = useState<string | null>(null)

  async function load() {
    setClaims(await api<Claim[]>(identity, '/claims'))
  }

  // Loads the list when the page opens. If the identity changes before the response arrives,
  // the stale response is ignored.
  useEffect(() => {
    let current = true
    api<Claim[]>(identity, '/claims')
      .then((list) => current && setClaims(list))
      .catch((e: Error) => current && setError(e.message))
    return () => {
      current = false
    }
  }, [identity])

  function startEditing(claim: Claim) {
    setEditing(claim)
    setDescription(claim.description)
    setAmount(String(claim.amount))
  }

  function resetForm() {
    setEditing(null)
    setDescription('')
    setAmount('')
  }

  // Runs one API call, shows its error if it fails, and reloads the list either way.
  async function run(action: () => Promise<unknown>) {
    setError(null)
    try {
      await action()
    } catch (e) {
      setError(e instanceof ApiError ? `${e.status}: ${e.message}` : String(e))
    }
    await load()
  }

  function save(event: FormEvent) {
    event.preventDefault()
    const body = JSON.stringify({ description, amount: Number(amount) })
    void run(async () => {
      if (editing) {
        await api(identity, `/claims/${editing.id}`, { method: 'PUT', body })
      } else {
        await api(identity, '/claims', { method: 'POST', body })
      }
      resetForm()
    })
  }

  function submit(claim: Claim) {
    void run(() =>
      api(identity, `/claims/${claim.id}/submit`, { method: 'POST' }),
    )
  }

  return (
    <section>
      <h2>My claims</h2>

      <form onSubmit={save} className="claim-form">
        <input
          aria-label="Description"
          placeholder="What was it for?"
          value={description}
          onChange={(event) => setDescription(event.target.value)}
          required
          maxLength={500}
        />
        <input
          aria-label="Amount"
          placeholder="Amount"
          type="number"
          step="0.01"
          min="0.01"
          value={amount}
          onChange={(event) => setAmount(event.target.value)}
          required
        />
        <button type="submit">{editing ? 'Save changes' : 'Add claim'}</button>
        {editing && (
          <button type="button" onClick={resetForm}>
            Cancel
          </button>
        )}
      </form>

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
            <th>Status</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {claims.map((claim) => (
            <tr key={claim.id}>
              <td>{claim.description}</td>
              <td>{formatMoney(claim.amount, claim.currency)}</td>
              <td>
                <span className={`status status-${claim.status.toLowerCase()}`}>
                  {claim.status}
                </span>
              </td>
              <td className="actions">
                {claim.status === 'Draft' && (
                  <>
                    <button type="button" onClick={() => startEditing(claim)}>
                      Edit
                    </button>
                    <button type="button" onClick={() => submit(claim)}>
                      Submit
                    </button>
                  </>
                )}
              </td>
            </tr>
          ))}
          {claims.length === 0 && (
            <tr>
              <td colSpan={4}>No claims yet.</td>
            </tr>
          )}
        </tbody>
      </table>
    </section>
  )
}
