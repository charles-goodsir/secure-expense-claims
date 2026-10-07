import { useState } from 'react'
import { devIdentities, type Identity } from './api'
import { MyClaims } from './MyClaims'

const storageKey = 'dev-identity'

function savedIdentity(): Identity {
  const savedId = localStorage.getItem(storageKey)
  return (
    devIdentities.find((identity) => identity.id === savedId) ??
    devIdentities[0]
  )
}

export default function App() {
  const [identity, setIdentity] = useState(savedIdentity)

  function signInAs(id: string) {
    const next = devIdentities.find((candidate) => candidate.id === id)!
    localStorage.setItem(storageKey, next.id)
    setIdentity(next)
  }

  return (
    <div className="page">
      <header>
        <h1>Expense claims</h1>
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
      </header>

      <main>
        {identity.roles.includes('Employee') ? (
          // key forces a fresh component, and a fresh load, when the identity changes.
          <MyClaims key={identity.id} identity={identity} />
        ) : (
          <p>
            {identity.name} has no employee claims. Admin pages arrive in the
            next step.
          </p>
        )}
      </main>
    </div>
  )
}
