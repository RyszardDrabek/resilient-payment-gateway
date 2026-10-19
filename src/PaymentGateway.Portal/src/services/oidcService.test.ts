import { describe, it, expect, beforeEach } from 'vitest';
import {
  base64UrlEncode,
  generateRandomString,
  generateCodeChallenge,
  parseJwtPayload,
  extractAuthUser,
  saveSession,
  getSavedSession,
  clearSession,
  getStorage,
  OidcSession,
} from './oidcService';

describe('oidcService', () => {
  beforeEach(() => {
    getStorage().clear();
  });

  describe('encoding & crypto utilities', () => {
    it('correctly encodes strings and byte arrays to base64url', () => {
      const encodedStr = base64UrlEncode('hello world');
      expect(encodedStr).toBe('aGVsbG8gd29ybGQ');

      const bytes = new Uint8Array([255, 254, 253]);
      const encodedBytes = base64UrlEncode(bytes);
      expect(encodedBytes).not.toContain('+');
      expect(encodedBytes).not.toContain('/');
      expect(encodedBytes).not.toContain('=');
    });

    it('generates random strings with expected length', () => {
      const str1 = generateRandomString(32);
      const str2 = generateRandomString(32);
      expect(str1).toBeDefined();
      expect(str2).toBeDefined();
      expect(str1).not.toBe(str2);
    });

    it('generates valid code challenge via SHA-256', async () => {
      const verifier = 'test_code_verifier_12345678901234567890';
      const challenge = await generateCodeChallenge(verifier);
      expect(challenge).toBeDefined();
      expect(challenge.length).toBeGreaterThan(10);
      expect(challenge).not.toContain('+');
      expect(challenge).not.toContain('/');
    });
  });

  describe('JWT payload parsing and role extraction', () => {
    it('parses valid JWT payload JSON', () => {
      const payload = { sub: 'user_1', iss: 'test_idp', custom: 42 };
      const token = `header.${btoa(JSON.stringify(payload))}.sig`;
      const parsed = parseJwtPayload(token);
      expect(parsed.sub).toBe('user_1');
      expect(parsed.iss).toBe('test_idp');
      expect(parsed.custom).toBe(42);
    });

    it('extracts ops role correctly from JWT role claim', () => {
      const payload = {
        sub: 'ops-user-123',
        preferred_username: 'ops_specialist',
        role: 'ops',
        email: 'ops@gateway.io',
      };
      const token = `header.${btoa(JSON.stringify(payload))}.sig`;

      const user = extractAuthUser(token);
      expect(user.role).toBe('ops');
      expect(user.subject).toBe('ops-user-123');
      expect(user.displayName).toBe('ops_specialist');
      expect(user.email).toBe('ops@gateway.io');
    });

    it('extracts merchant role and merchantId from token claims', () => {
      const payload = {
        sub: 'merchant-456',
        preferred_username: 'acme_admin',
        role: ['merchant', 'user'],
        merchant_id: 'merch_enterprise_99',
      };
      const token = `header.${btoa(JSON.stringify(payload))}.sig`;

      const user = extractAuthUser(token);
      expect(user.role).toBe('merchant');
      expect(user.merchantId).toBe('merch_enterprise_99');
      expect(user.displayName).toBe('acme_admin');
    });

    it('extracts risk role from Keycloak realm_access structure', () => {
      const payload = {
        sub: 'risk-789',
        preferred_username: 'risk_auditor',
        realm_access: {
          roles: ['offline_access', 'risk', 'uma_authorization'],
        },
      };
      const token = `header.${btoa(JSON.stringify(payload))}.sig`;

      const user = extractAuthUser(token);
      expect(user.role).toBe('risk');
      expect(user.subject).toBe('risk-789');
    });

    it('gracefully handles malformed JWT tokens', () => {
      const user = extractAuthUser('invalid_token', 'fallback_sub');
      expect(user.subject).toBe('fallback_sub');
      expect(user.role).toBe('ops');
    });
  });

  describe('session management in sessionStorage', () => {
    it('saves and retrieves active session before expiration', () => {
      const testSession: OidcSession = {
        accessToken: 'mock_access_token',
        idToken: 'mock_id_token',
        expiresAt: Date.now() + 60000,
        user: {
          role: 'ops',
          subject: 'test-user',
          displayName: 'Test User',
        },
      };

      saveSession(testSession);
      const retrieved = getSavedSession();
      expect(retrieved).not.toBeNull();
      expect(retrieved?.accessToken).toBe('mock_access_token');
      expect(retrieved?.user.displayName).toBe('Test User');
    });

    it('returns null and purges expired session', () => {
      const expiredSession: OidcSession = {
        accessToken: 'expired_token',
        expiresAt: Date.now() - 1000,
        user: {
          role: 'merchant',
          subject: 'expired-user',
          displayName: 'Expired',
        },
      };

      saveSession(expiredSession);
      const retrieved = getSavedSession();
      expect(retrieved).toBeNull();
    });

    it('clears session on clearSession', () => {
      const activeSession: OidcSession = {
        accessToken: 'active_token',
        expiresAt: Date.now() + 60000,
        user: {
          role: 'risk',
          subject: 'risk-user',
          displayName: 'Risk',
        },
      };

      saveSession(activeSession);
      clearSession();
      expect(getSavedSession()).toBeNull();
    });
  });
});
