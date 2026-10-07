import { useEffect, useState } from 'react'
import { api, ApiError, type ClaimStatus, type Identity } from './api'

type User = {
  id: string
  displayName: string
  email: string
  managerId: string | null
}

type AuditEntry = {
  id: number
  at: string
  actorId: string
  action: string
  claimId: string | null
  fromStatus: ClaimStatus | null
  toStatus: ClaimStatus | null
  detail: string | null
}

type Props = { identity: Identity }

// Reporting lines and the audit log. An admin can't approve or pay anything; the API has no
// workflow permissions for the Admin role (threat E4).
export function Admin({ identity }: Props) {
  const [users, setUsers] = useState<User[]>([])
  const [audit, setAudit] = useState<AuditEntry[]>([])
  const [error, setError] = useState<string | null>(null)

  async function load() {
    const [userList, auditList] = await Promise.all([
      api<User[]>(identity, '/admin/users'),
      api<AuditEntry[]>(identity, '/admin/audit'),
    ])
    setUsers(userList)
    setAudit(auditList)
  }

  useEffect(() => {
    let current = true
    Promise.all([
      api<User[]>(identity, '/admin/users'),
      api<AuditEntry[]>(identity, '/admin/audit'),
    ])
      .then(([userList, auditList]) => {
        if (current) {
          setUsers(userList)
          setAudit(auditList)
        }
      })
      .catch((e: Error) => current && setError(e.message))
    return () => {
      current = false
    }
  }, [identity])

  async function setManager(user: User, managerId: string) {
    setError(null)
    try {
      await api(identity, `/admin/users/${user.id}/manager`, {
        method: 'PUT',
        body: JSON.stringify({ managerId }),
      })
    } catch (e) {
      setError(e instanceof ApiError ? `${e.status}: ${e.message}` : String(e))
    }
    await load()
  }

  const nameOf = (id: string | null) =>
    users.find((user) => user.id === id)?.displayName ?? id ?? ''

  return (
    <>
      <section>
        <h2>Users</h2>

        {error && (
          <p role="alert" className="error">
            {error}
          </p>
        )}

        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th>Email</th>
              <th>Manager</th>
            </tr>
          </thead>
          <tbody>
            {users.map((user) => (
              <tr key={user.id}>
                <td>{user.displayName}</td>
                <td>{user.email}</td>
                <td>
                  <select
                    aria-label={`Manager for ${user.displayName}`}
                    value={user.managerId ?? ''}
                    onChange={(event) => setManager(user, event.target.value)}
                  >
                    {/* The API can set a manager but not remove one. */}
                    {user.managerId === null && (
                      <option value="" disabled>
                        None
                      </option>
                    )}
                    {users
                      .filter((candidate) => candidate.id !== user.id)
                      .map((candidate) => (
                        <option key={candidate.id} value={candidate.id}>
                          {candidate.displayName}
                        </option>
                      ))}
                  </select>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      <section>
        <h2>Audit log</h2>
        <table>
          <thead>
            <tr>
              <th>When</th>
              <th>Who</th>
              <th>Action</th>
              <th>Change</th>
            </tr>
          </thead>
          <tbody>
            {audit.map((entry) => (
              <tr key={entry.id}>
                <td>{new Date(entry.at).toLocaleString('en-NZ')}</td>
                <td>{nameOf(entry.actorId)}</td>
                <td>{entry.action}</td>
                <td>
                  {entry.toStatus
                    ? `${entry.fromStatus} → ${entry.toStatus}`
                    : entry.detail}
                </td>
              </tr>
            ))}
            {audit.length === 0 && (
              <tr>
                <td colSpan={4}>No audit entries yet.</td>
              </tr>
            )}
          </tbody>
        </table>
      </section>
    </>
  )
}
