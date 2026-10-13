import { UserRole } from '../types/auth';

const DEV_SECRET = 'DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!';
const ISSUER = 'payment-gateway';
const AUDIENCE = 'payment-gateway';

function base64UrlEncode(buffer: ArrayBuffer | Uint8Array | string): string {
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

export async function createDevJwt(
  role: UserRole,
  subject?: string,
  secret: string = DEV_SECRET
): Promise<string> {
  const sub = subject ?? (role === 'ops' ? 'ops-user-1' : role === 'risk' ? 'risk-officer-1' : 'merch_acme');
  
  const header = {
    alg: 'HS256',
    typ: 'JWT',
  };

  const now = Math.floor(Date.now() / 1000);
  const payload = {
    sub,
    role,
    iss: ISSUER,
    aud: AUDIENCE,
    exp: now + 86400 * 7, // 7 days
    nbf: now - 60,
    iat: now,
  };

  const headerB64 = base64UrlEncode(JSON.stringify(header));
  const payloadB64 = base64UrlEncode(JSON.stringify(payload));
  const dataToSign = `${headerB64}.${payloadB64}`;

  const encoder = new TextEncoder();
  const keyData = encoder.encode(secret);

  const cryptoObj = globalThis.crypto;
  if (!cryptoObj?.subtle) {
    // In environments without Web Crypto subtle, return unsigned demo token
    return `${dataToSign}.dummy_sig`;
  }

  const key = await cryptoObj.subtle.importKey(
    'raw',
    keyData,
    { name: 'HMAC', hash: 'SHA-256' },
    false,
    ['sign']
  );

  const signature = await cryptoObj.subtle.sign(
    'HMAC',
    key,
    encoder.encode(dataToSign)
  );

  const signatureB64 = base64UrlEncode(signature);
  return `${dataToSign}.${signatureB64}`;
}
