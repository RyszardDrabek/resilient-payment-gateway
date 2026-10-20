import { AuthUser, UserRole } from '../types/auth';

export interface OidcSession {
  accessToken: string;
  idToken?: string;
  refreshToken?: string;
  expiresAt: number; // epoch ms
  user: AuthUser;
}

const STORAGE_KEY = 'rpg_oidc_session';
const VERIFIER_KEY = 'rpg_oidc_verifier';
const STATE_KEY = 'rpg_oidc_state';

const memoryStorage = new Map<string, string>();

export function getStorage() {
  if (typeof window !== 'undefined' && window.sessionStorage) {
    return window.sessionStorage;
  }
  if (typeof sessionStorage !== 'undefined') {
    return sessionStorage;
  }
  return {
    getItem: (key: string) => memoryStorage.get(key) ?? null,
    setItem: (key: string, value: string) => memoryStorage.set(key, value),
    removeItem: (key: string) => memoryStorage.delete(key),
    clear: () => memoryStorage.clear(),
  };
}

export function getOidcConfig() {
  const authority = import.meta.env.VITE_OIDC_AUTHORITY || '';
  const clientId = import.meta.env.VITE_OIDC_CLIENT_ID || 'payment-gateway-portal';
  const redirectUri = typeof window !== 'undefined' ? `${window.location.origin}/` : 'http://localhost:3000/';
  const scope = 'openid profile email';

  return {
    authority,
    clientId,
    redirectUri,
    scope,
    isConfigured: Boolean(authority && authority.trim().length > 0),
  };
}

export function base64UrlEncode(buffer: ArrayBuffer | Uint8Array | string): string {
  let bytes: Uint8Array;
  if (typeof buffer === 'string') {
    bytes = new TextEncoder().encode(buffer);
  } else if (buffer instanceof Uint8Array) {
    bytes = buffer;
  } else {
    bytes = new Uint8Array(buffer);
  }

  let binary = '';
  for (let i = 0; i < bytes.byteLength; i++) {
    binary += String.fromCharCode(bytes[i]);
  }
  return btoa(binary)
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '');
}

export function generateRandomString(length = 48): string {
  const array = new Uint8Array(length);
  if (typeof crypto !== 'undefined' && crypto.getRandomValues) {
    crypto.getRandomValues(array);
  } else {
    for (let i = 0; i < length; i++) {
      array[i] = Math.floor(Math.random() * 256);
    }
  }
  return base64UrlEncode(array);
}

export async function generateCodeChallenge(verifier: string): Promise<string> {
  const encoder = new TextEncoder();
  const data = encoder.encode(verifier);
  if (typeof crypto !== 'undefined' && crypto.subtle) {
    const digest = await crypto.subtle.digest('SHA-256', data);
    return base64UrlEncode(digest);
  }
  return base64UrlEncode(verifier);
}

export function parseJwtPayload(token: string): Record<string, unknown> {
  try {
    const parts = token.split('.');
    if (parts.length < 2) return {};
    const base64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
    const json = decodeURIComponent(
      atob(base64)
        .split('')
        .map((c) => '%' + ('00' + c.charCodeAt(0).toString(16)).slice(-2))
        .join('')
    );
    return JSON.parse(json);
  } catch {
    return {};
  }
}

export function extractAuthUser(token: string, fallbackSub = 'user'): AuthUser {
  const payload = parseJwtPayload(token);
  const sub = (payload.sub as string) || fallbackSub;

  let role: UserRole = 'ops';
  const roleClaim = payload.role || payload.roles;
  const realmAccess = payload.realm_access as { roles?: string[] } | undefined;
  const allRoles: string[] = [];

  if (Array.isArray(roleClaim)) {
    allRoles.push(...roleClaim.map(String));
  } else if (typeof roleClaim === 'string') {
    allRoles.push(roleClaim);
  }

  if (Array.isArray(realmAccess?.roles)) {
    allRoles.push(...realmAccess.roles.map(String));
  }

  const normalized = allRoles.map((r) => r.toLowerCase());
  if (normalized.includes('merchant')) {
    role = 'merchant';
  } else if (normalized.includes('risk')) {
    role = 'risk';
  } else if (normalized.includes('ops')) {
    role = 'ops';
  }

  const displayName =
    (payload.preferred_username as string) ||
    (payload.name as string) ||
    (payload.email as string) ||
    sub;

  const merchantId = (payload.merchant_id as string) || (role === 'merchant' ? 'merch_acme' : undefined);

  return {
    role,
    subject: sub,
    displayName,
    merchantId,
    email: (payload.email as string) || undefined,
  };
}

export async function initiateLogin(): Promise<void> {
  const config = getOidcConfig();
  if (!config.isConfigured) return;

  const verifier = generateRandomString(48);
  const challenge = await generateCodeChallenge(verifier);
  const state = generateRandomString(24);

  const storage = getStorage();
  storage.setItem(VERIFIER_KEY, verifier);
  storage.setItem(STATE_KEY, state);

  const authUrl = new URL(`${config.authority}/protocol/openid-connect/auth`);
  authUrl.searchParams.set('client_id', config.clientId);
  authUrl.searchParams.set('response_type', 'code');
  authUrl.searchParams.set('scope', config.scope);
  authUrl.searchParams.set('redirect_uri', config.redirectUri);
  authUrl.searchParams.set('state', state);
  authUrl.searchParams.set('code_challenge', challenge);
  authUrl.searchParams.set('code_challenge_method', 'S256');

  window.location.href = authUrl.toString();
}

export async function handleOidcCallback(): Promise<OidcSession | null> {
  const config = getOidcConfig();
  if (!config.isConfigured || typeof window === 'undefined') return null;

  const urlParams = new URLSearchParams(window.location.search);
  const code = urlParams.get('code');
  const returnedState = urlParams.get('state');

  if (!code) return null;

  const storage = getStorage();
  const savedState = storage.getItem(STATE_KEY);
  const verifier = storage.getItem(VERIFIER_KEY);

  if (!savedState || !verifier || savedState !== returnedState) {
    console.error('Invalid OIDC state parameter.');
    return null;
  }

  window.history.replaceState({}, document.title, window.location.pathname);

  const tokenUrl = `${config.authority}/protocol/openid-connect/token`;
  const body = new URLSearchParams({
    grant_type: 'authorization_code',
    client_id: config.clientId,
    code,
    redirect_uri: config.redirectUri,
    code_verifier: verifier,
  });

  const response = await fetch(tokenUrl, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/x-www-form-urlencoded',
    },
    body: body.toString(),
  });

  if (!response.ok) {
    const errorText = await response.text();
    console.error('Token exchange failed:', errorText);
    return null;
  }

  const tokenData = await response.json();
  const accessToken = tokenData.access_token;
  const idToken = tokenData.id_token;
  const refreshToken = tokenData.refresh_token;
  const expiresIn = tokenData.expires_in || 300;
  const expiresAt = Date.now() + expiresIn * 1000;

  const user = extractAuthUser(accessToken, 'keycloak_user');

  const session: OidcSession = {
    accessToken,
    idToken,
    refreshToken,
    expiresAt,
    user,
  };

  saveSession(session);
  storage.removeItem(VERIFIER_KEY);
  storage.removeItem(STATE_KEY);

  return session;
}

export function saveSession(session: OidcSession): void {
  try {
    getStorage().setItem(STORAGE_KEY, JSON.stringify(session));
  } catch (e) {
    console.error('Failed to save session:', e);
  }
}

export function getSavedSession(): OidcSession | null {
  try {
    const storage = getStorage();
    const raw = storage.getItem(STORAGE_KEY);
    if (!raw) return null;
    const session: OidcSession = JSON.parse(raw);
    if (session.expiresAt && session.expiresAt > Date.now()) {
      return session;
    }
    storage.removeItem(STORAGE_KEY);
    return null;
  } catch {
    return null;
  }
}

export function clearSession(): void {
  try {
    const storage = getStorage();
    storage.removeItem(STORAGE_KEY);
    storage.removeItem(VERIFIER_KEY);
    storage.removeItem(STATE_KEY);
  } catch (e) {
    console.error('Failed to clear session:', e);
  }
}

export function initiateLogout(): void {
  const config = getOidcConfig();
  const session = getSavedSession();
  clearSession();

  if (config.isConfigured && session?.idToken) {
    const logoutUrl = new URL(`${config.authority}/protocol/openid-connect/logout`);
    logoutUrl.searchParams.set('id_token_hint', session.idToken);
    logoutUrl.searchParams.set('post_logout_redirect_uri', config.redirectUri);
    window.location.href = logoutUrl.toString();
  } else {
    window.location.href = window.location.pathname;
  }
}
