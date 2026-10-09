import { useEffect, useState } from 'react'
import { api, devIdentities, type Identity } from './api'
import { accountName, entraEnabled, signOut } from './auth'
import { ClaimQueue } from './ClaimQueue'
import { MyClaims } from './MyClaims'
import { Admin } from './Admin'

const storageKey = 'dev-identity'

function savedIdentity(): Identity {
  const savedId = localStorage.getItem(storageKey)
  return (
    devIdentities.find((identity) => identity.id === savedId) ??
    devIdentities[0]
  )
}

export default function App() {
  const [identity, setIdentity] = useState<Identity | null>(
    entraEnabled ? null : savedIdentity,
  )
  // With Entra, the API says who the token belongs to and which roles it carries
  useEffect(() => {
    if (!entraEnabled) return
    void api<{ id: string; roles: string[] }>(null, '/me').then((me) =>
      setIdentity({ ...me, name: accountName() }),
    )
  }, [])

  function signInAs(id: string) {
    const next = devIdentities.find((candidate) => candidate.id === id)!
    localStorage.setItem(storageKey, next.id)
    setIdentity(next)
  }
  if (!identity) return <p className="page">Signing in…</p>

  return (
    <div className="page">
      <header>
        <h1>Expense claims</h1>
        {entraEnabled ? (
          <p className="identity">
            Signed in as {identity.name}{' '}
            <button onClick={() => void signOut()}>Sign out</button>
          </p>
        ) : (
          <label className="identity">
            Signed in as (development only)
            <select
              value={identity.id}
              onChange={(event) => signInAs(event.target.value)}
            >
              {devIdentities.map((candidate) => (
                <option key={candidate.id} value={candidate.id}>
                  {candidate.name}: {candidate.roles.join(', ')}
                </option>
              ))}
            </select>
          </label>
        )}
      </header>

      <main>
        {/* key forces a fresh component, and a fresh load, when the identity changes. */}
        {identity.roles.includes('Manager') && (
          <ClaimQueue
            key={`${identity.id}-approvals`}
            identity={identity}
            title="Waiting for my approval"
            listPath="/approvals"
            actions={['approve', 'reject']}
          />
        )}
        {identity.roles.includes('Finance') && (
          <ClaimQueue
            key={`${identity.id}-payments`}
            identity={identity}
            title="Approved, waiting for payment"
            listPath="/payments"
            actions={['pay']}
          />
        )}
        {identity.roles.includes('Employee') && (
          <MyClaims key={identity.id} identity={identity} />
        )}
        {identity.roles.includes('Admin') && (
          <Admin key={identity.id} identity={identity} />
        )}
      </main>
    </div>
  )
}
