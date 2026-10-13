import { describe, it, expect } from 'vitest';
import { ApiError } from './apiClient';

describe('apiClient ApiError', () => {
  it('correctly captures HTTP status code and RFC 7807 problem details', () => {
    const error = new ApiError(403, 'The caller does not have the required role to access this resource.', {
      type: 'urn:rpg:problem:forbidden',
      title: 'Forbidden',
      status: 403,
      detail: 'The caller does not have the required role to access this resource.',
      correlationId: 'test-corr-123',
    });

    expect(error.status).toBe(403);
    expect(error.message).toBe('The caller does not have the required role to access this resource.');
    expect(error.problem?.correlationId).toBe('test-corr-123');
    expect(error.problem?.type).toBe('urn:rpg:problem:forbidden');
  });

  it('serializes request payloads and headers properly', async () => {
    const originalFetch = globalThis.fetch;
    try {
      let capturedUrl = '';
      let capturedInit: RequestInit | undefined;

      globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
        capturedUrl = input.toString();
        capturedInit = init;
        return new Response(JSON.stringify({
          id: 'sub-1',
          partyId: 'merch_acme',
          endpointUrl: 'https://example.com/hook',
          secretKey: 'whsec_test',
          isActive: true
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }) as typeof fetch;

      const { fetchWebhookSubscription } = await import('./apiClient');
      const result = await fetchWebhookSubscription('test-token', 'merch_acme');

      expect(capturedUrl).toContain('/api/v1/webhooks/merchants/subscription?partyId=merch_acme');
      expect((capturedInit?.headers as Record<string, string>)?.Authorization).toBe('Bearer test-token');
      expect(result.partyId).toBe('merch_acme');
      expect(result.endpointUrl).toBe('https://example.com/hook');
    } finally {
      globalThis.fetch = originalFetch;
    }
  });

  it('sends PUT payload when updating webhook subscription', async () => {
    const originalFetch = globalThis.fetch;
    try {
      let capturedMethod = '';
      let capturedBody = '';

      globalThis.fetch = (async (_input: RequestInfo | URL, init?: RequestInit) => {
        capturedMethod = init?.method ?? '';
        capturedBody = init?.body?.toString() ?? '';
        return new Response(JSON.stringify({
          id: 'sub-1',
          partyId: 'merch_acme',
          endpointUrl: 'https://new.example.com/hook',
          secretKey: 'whsec_test',
          isActive: true
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }) as typeof fetch;

      const { updateWebhookSubscription } = await import('./apiClient');
      const result = await updateWebhookSubscription('token-123', {
        endpointUrl: 'https://new.example.com/hook',
        isActive: true
      }, 'merch_acme');

      expect(capturedMethod).toBe('PUT');
      expect(JSON.parse(capturedBody)).toEqual({
        endpointUrl: 'https://new.example.com/hook',
        isActive: true
      });
      expect(result.endpointUrl).toBe('https://new.example.com/hook');
    } finally {
      globalThis.fetch = originalFetch;
    }
  });

  it('triggers POST to rotate-secret endpoint', async () => {
    const originalFetch = globalThis.fetch;
    try {
      let capturedUrl = '';
      let capturedMethod = '';

      globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
        capturedUrl = input.toString();
        capturedMethod = init?.method ?? '';
        return new Response(JSON.stringify({
          id: 'sub-1',
          partyId: 'merch_acme',
          endpointUrl: 'https://example.com/hook',
          secretKey: 'whsec_rotated_999',
          isActive: true
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }) as typeof fetch;

      const { rotateWebhookSecret } = await import('./apiClient');
      const result = await rotateWebhookSecret('token-123', 'merch_acme');

      expect(capturedMethod).toBe('POST');
      expect(capturedUrl).toContain('/api/v1/webhooks/merchants/subscription/rotate-secret?partyId=merch_acme');
      expect(result.secretKey).toBe('whsec_rotated_999');
    } finally {
      globalThis.fetch = originalFetch;
    }
  });

  it('invokes test-dispatch and parses diagnostic results', async () => {
    const originalFetch = globalThis.fetch;
    try {
      let capturedUrl = '';
      let capturedBody = '';

      globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
        capturedUrl = input.toString();
        capturedBody = init?.body?.toString() ?? '';
        return new Response(JSON.stringify({
          success: true,
          statusCode: 200,
          latencyMs: 76,
          endpointUrl: 'https://example.com/hook',
          eventId: 'evt_sim_123',
          timestamp: '1773400000',
          signature: 'abcd1234efgh5678',
          requestHeaders: { 'X-Signature-SHA256': 'abcd1234efgh5678' },
          requestPayload: '{"eventId":"evt_sim_123"}',
          responseBody: '{"ok":true}',
          errorMessage: null
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }) as typeof fetch;

      const { dispatchTestWebhook } = await import('./apiClient');
      const result = await dispatchTestWebhook('token-123', {
        endpointUrl: 'https://example.com/hook',
        eventType: 'payment.settled'
      }, 'merch_acme');

      expect(capturedUrl).toContain('/api/v1/webhooks/merchants/test-dispatch?partyId=merch_acme');
      expect(JSON.parse(capturedBody)).toEqual({
        endpointUrl: 'https://example.com/hook',
        eventType: 'payment.settled'
      });
      expect(result.success).toBe(true);
      expect(result.statusCode).toBe(200);
      expect(result.latencyMs).toBe(76);
      expect(result.signature).toBe('abcd1234efgh5678');
    } finally {
      globalThis.fetch = originalFetch;
    }
  });
});

