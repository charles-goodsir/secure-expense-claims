import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { entraEnabled, signIn } from './auth.ts'

// With Entra, nothing renders until the user is signed in.
if (!entraEnabled || (await signIn())) {
  createRoot(document.getElementById('root')!).render(
    <StrictMode>
      <App />
    </StrictMode>,
  )
}
