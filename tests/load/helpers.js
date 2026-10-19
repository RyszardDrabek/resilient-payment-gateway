import crypto from 'k6/crypto';
import encoding from 'k6/encoding';

export const GATEWAY_URL = __ENV.GATEWAY_URL || 'http://localhost:5080';
export const JWT_SECRET = __ENV.JWT_SECRET || 'DEV_ONLY_CHANGE_ME_32CHARS_MINIMUM!!';

/**
 * Encodes string to base64url format
 */
export function base64Url(str) {
  return encoding.b64encode(str, 'rawurl');
}

/**
 * Generates an HMAC-SHA256 signed JWT token for payment gateway authorization
 */
export function generateJwt(role = 'merchant', sub = 'merch_acme', secret = JWT_SECRET) {
  const header = JSON.stringify({ alg: 'HS256', typ: 'JWT' });
  const now = Math.floor(Date.now() / 1000);
  const payload = JSON.stringify({
    sub: sub,
    role: role,
    iss: 'payment-gateway',
    aud: 'payment-gateway',
    exp: now + 3600,
    nbf: now - 60,
    iat: now,
  });

  const headerB64 = base64Url(header);
  const payloadB64 = base64Url(payload);
  const message = `${headerB64}.${payloadB64}`;
  const signature = crypto.hmac('sha256', secret, message, 'rawurl');
  return `${message}.${signature}`;
}

/**
 * Generates a pseudo-random UUID v4 string for Idempotency-Key
 */
export function generateIdempotencyKey(prefix = 'k6') {
  const randomPart = crypto.hexEncode(crypto.randomBytes(16));
  return `${prefix}-${Date.now()}-${randomPart.substring(0, 12)}`;
}

/**
 * Builds standard payment authorization payload
 */
export function buildPaymentPayload(overrides = {}) {
  const currencies = ['EUR', 'USD', 'PLN'];
  const randomCurrency = currencies[Math.floor(Math.random() * currencies.length)];
  const randomAmount = Math.floor(Math.random() * 50000) + 1000; // 10.00 to 500.00 in minor units

  return Object.assign({
    partyId: 'merch_acme',
    amount: randomAmount,
    currency: randomCurrency,
    settlementChannel: 'mocked-adyen',
  }, overrides);
}

/**
 * Standard HTTP headers for authenticated gateway requests
 */
export function authHeaders(token, idempotencyKey = null) {
  const headers = {
    'Content-Type': 'application/json',
    'Accept': 'application/json',
    'Authorization': `Bearer ${token}`,
  };

  if (idempotencyKey) {
    headers['Idempotency-Key'] = idempotencyKey;
  }

  return headers;
}
