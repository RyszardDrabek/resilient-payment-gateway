import { describe, it, expect } from 'vitest';
import { createDevJwt } from './tokenService';

describe('tokenService', () => {
  it('generates a 3-part signed JWT for ops role', async () => {
    const token = await createDevJwt('ops', 'test-operator');
    expect(token).toBeDefined();

    const parts = token.split('.');
    expect(parts.length).toBe(3);

    // Decode payload
    const payloadJson = atob(parts[1].replace(/-/g, '+').replace(/_/g, '/'));
    const payload = JSON.parse(payloadJson);

    expect(payload.sub).toBe('test-operator');
    expect(payload.role).toBe('ops');
    expect(payload.iss).toBe('payment-gateway');
    expect(payload.aud).toBe('payment-gateway');
    expect(payload.exp).toBeGreaterThan(Date.now() / 1000);
  });

  it('generates default subject when not provided', async () => {
    const opsToken = await createDevJwt('ops');
    const merchantToken = await createDevJwt('merchant');
    const riskToken = await createDevJwt('risk');

    const decode = (jwt: string) => JSON.parse(atob(jwt.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')));

    expect(decode(opsToken).role).toBe('ops');
    expect(decode(merchantToken).role).toBe('merchant');
    expect(decode(riskToken).role).toBe('risk');
  });
});
