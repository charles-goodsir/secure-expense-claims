import {
  InteractionRequiredAuthError,
  PublicClientApplication,
  type AccountInfo,
} from '@azure/msal-browser'

// Set at build time (VITE_ variables are public: they end up in the bundle). Without a
// client ID the app uses the development sign-in picker instead.
const clientId = import.meta.env.VITE_ENTRA_CLIENT_ID
const tenantId = import.meta.env.VITE_ENTRA_TENANT_ID
const scopes = [import.meta.env.VITE_API_SCOPE]

export const entraEnabled = Boolean(clientId)

const msal = entraEnabled
  ? new PublicClientApplication({
      auth: {
        clientId,
        authority: `https://login.microsoftonline.com/${tenantId}`,
        redirectUri: window.location.origin,
      },
      // Tokens last for this tab only, not across browser restarts.
      cache: { cacheLocation: 'sessionStorage' },
    })
  : null

let account: AccountInfo | null = null

// Finishes a sign-in redirect if this page load is one, otherwise sends the browser to
// Entra. Returns false when the page is about to leave for the sign-in page.
export async function signIn(): Promise<boolean> {
  await msal!.initialize()
  const redirect = await msal!.handleRedirectPromise()
  account = redirect?.account ?? msal!.getAllAccounts()[0] ?? null
  if (account) return true
  await msal!.loginRedirect({ scopes })
  return false
}

export function accountName(): string {
  return account?.name ?? account?.username ?? ''
}

// MSAL returns a cached access token, or quietly renews it. If Entra needs the user
// again (a session expired, say), it redirects to sign in.
export async function bearerHeader(): Promise<Record<string, string>> {
  try {
    const { accessToken } = await msal!.acquireTokenSilent({
      scopes,
      account: account!,
    })
    return { Authorization: `Bearer ${accessToken}` }
  } catch (error) {
    if (error instanceof InteractionRequiredAuthError) {
      await msal!.acquireTokenRedirect({ scopes })
    }
    throw error
  }
}

export function signOut() {
  return msal!.logoutRedirect()
}
