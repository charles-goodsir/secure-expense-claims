import { useEffect, useState, type ChangeEvent } from 'react'
import { api, ApiError, download, type Identity } from './api'

type Receipt = {
  id: string
  contentType: string
  sizeBytes: number
  originalFileName: string
}

type Props = { identity: Identity; claimId: string; canUpload: boolean }

// The receipts on one claim, with a download button for each and, on drafts, an upload input.
// ponytail: one request per claim row, fine at dev scale; return receipts with the claim list if it grows.
export function Receipts({ identity, claimId, canUpload }: Props) {
  const [receipts, setReceipts] = useState<Receipt[]>([])
  const [error, setError] = useState<string | null>(null)
  const path = `/claims/${claimId}/receipts`

  async function load() {
    setReceipts(await api<Receipt[]>(identity, path))
  }

  useEffect(() => {
    let current = true
    api<Receipt[]>(identity, path)
      .then((list) => current && setReceipts(list))
      .catch((e: Error) => current && setError(e.message))
    return () => {
      current = false
    }
  }, [identity, path])

  async function run(action: () => Promise<unknown>) {
    setError(null)
    try {
      await action()
    } catch (e) {
      setError(e instanceof ApiError ? `${e.status}: ${e.message}` : String(e))
    }
    await load()
  }

  function upload(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    // Clears the input so picking the same file again still fires a change.
    event.target.value = ''
    if (!file) {
      return
    }
    const body = new FormData()
    body.append('file', file)
    void run(() => api(identity, path, { method: 'POST', body }))
  }

  return (
    <div className="receipts">
      {receipts.map((receipt) => (
        <button
          key={receipt.id}
          type="button"
          className="link"
          onClick={() => run(() => download(identity, `${path}/${receipt.id}`))}
        >
          {receipt.originalFileName}
        </button>
      ))}
      {canUpload && (
        <label className="upload">
          Add receipt
          <input
            type="file"
            accept="application/pdf,image/png,image/jpeg"
            onChange={upload}
          />
        </label>
      )}
      {error && (
        <span role="alert" className="error">
          {error}
        </span>
      )}
    </div>
  )
}
