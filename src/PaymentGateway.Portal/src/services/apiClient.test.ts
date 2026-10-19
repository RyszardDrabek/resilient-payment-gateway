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

  it('fetches open risk flags with pagination params', async () => {
    const originalFetch = globalThis.fetch;
    try {
      let capturedUrl = '';
      let capturedInit: RequestInit | undefined;

      globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
        capturedUrl = input.toString();
        capturedInit = init;
        return new Response(JSON.stringify({
          items: [{
            id: 'flag_101',
            paymentId: 'pay_101',
            partyId: 'party_acme',
            eventId: 'evt_101',
            reason: 'High velocity spike',
            status: 'Open',
            disposition: null,
            createdAt: '2026-10-13T10:00:00Z',
            dispositionedAt: null,
            reviewerNotes: null
          }],
          page: 1,
          pageSize: 20,
          totalCount: 1
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }) as typeof fetch;

      const { fetchOpenRiskFlags } = await import('./apiClient');
      const result = await fetchOpenRiskFlags('risk-token', 1, 20);

      expect(capturedUrl).toContain('/api/v1/risk/flags?page=1&pageSize=20');
      expect((capturedInit?.headers as Record<string, string>)?.Authorization).toBe('Bearer risk-token');
      expect(result.items.length).toBe(1);
      expect(result.items[0].id).toBe('flag_101');
      expect(result.totalCount).toBe(1);
    } finally {
      globalThis.fetch = originalFetch;
    }
  });

  it('fetches risk verdict by identifier', async () => {
    const originalFetch = globalThis.fetch;
    try {
      let capturedUrl = '';
      globalThis.fetch = (async (input: RequestInfo | URL) => {
        capturedUrl = input.toString();
        return new Response(JSON.stringify({
          id: 'v_101',
          eventId: 'evt_101',
          paymentId: 'pay_101',
          partyId: 'party_acme',
          status: 'Flagged',
          reason: 'Amount exceeds threshold',
          flagId: 'flag_101',
          scoredAt: '2026-10-13T10:00:00Z'
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }) as typeof fetch;

      const { fetchRiskVerdict } = await import('./apiClient');
      const result = await fetchRiskVerdict('risk-token', 'v_101');

      expect(capturedUrl).toContain('/api/v1/risk/verdicts/v_101');
      expect(result.status).toBe('Flagged');
      expect(result.flagId).toBe('flag_101');
    } finally {
      globalThis.fetch = originalFetch;
    }
  });

  it('submits risk flag disposition and parses result', async () => {
    const originalFetch = globalThis.fetch;
    try {
      let capturedUrl = '';
      let capturedMethod = '';
      let capturedBody = '';

      globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
        capturedUrl = input.toString();
        capturedMethod = init?.method ?? '';
        capturedBody = init?.body?.toString() ?? '';
        return new Response(JSON.stringify({
          id: 'flag_101',
          paymentId: 'pay_101',
          partyId: 'party_acme',
          eventId: 'evt_101',
          reason: 'High velocity spike',
          status: 'Dispositioned',
          disposition: 'confirmed',
          createdAt: '2026-10-13T10:00:00Z',
          dispositionedAt: '2026-10-13T11:00:00Z',
          reviewerNotes: 'Confirmed fraudulent pattern'
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }) as typeof fetch;

      const { submitRiskDisposition } = await import('./apiClient');
      const result = await submitRiskDisposition('risk-token', 'flag_101', {
        disposition: 'confirmed',
        notes: 'Confirmed fraudulent pattern'
      });

      expect(capturedMethod).toBe('POST');
      expect(capturedUrl).toContain('/api/v1/risk/flags/flag_101/disposition');
      expect(JSON.parse(capturedBody)).toEqual({
        disposition: 'confirmed',
        notes: 'Confirmed fraudulent pattern'
      });
      expect(result.status).toBe('Dispositioned');
      expect(result.disposition).toBe('confirmed');
      expect(result.reviewerNotes).toBe('Confirmed fraudulent pattern');
    } finally {
      globalThis.fetch = originalFetch;
    }
  });

  it('fetches merchant API keys and parses collection', async () => {
    const originalFetch = globalThis.fetch;
    try {
      let capturedUrl = '';
      let capturedHeaders: Record<string, string> = {};

      globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
        capturedUrl = input.toString();
        capturedHeaders = (init?.headers ?? {}) as Record<string, string>;
        return new Response(JSON.stringify([
          {
            id: 'key_1',
            merchantId: 'merch_acme',
            name: 'Backend Live',
            prefix: 'rpg_live_ab12cd',
            scopes: ['payments:write', 'payments:read'],
            createdAt: '2026-10-14T10:00:00Z',
            isRevoked: false,
            revokedAt: null
          }
        ]), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }) as typeof fetch;

      const { fetchMerchantApiKeys } = await import('./apiClient');
      const keys = await fetchMerchantApiKeys('auth-token', 'merch_acme');

      expect(capturedUrl).toContain('/api/v1/merchants/merch_acme/api-keys');
      expect(capturedHeaders.Authorization).toBe('Bearer auth-token');
      expect(keys.length).toBe(1);
      expect(keys[0].name).toBe('Backend Live');
      expect(keys[0].prefix).toBe('rpg_live_ab12cd');
      expect(keys[0].scopes).toContain('payments:write');
    } finally {
      globalThis.fetch = originalFetch;
    }
  });

  it('creates a new merchant API key with scopes and receives raw key', async () => {
    const originalFetch = globalThis.fetch;
    try {
      let capturedUrl = '';
      let capturedMethod = '';
      let capturedBody = '';

      globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
        capturedUrl = input.toString();
        capturedMethod = init?.method ?? '';
        capturedBody = init?.body?.toString() ?? '';
        return new Response(JSON.stringify({
          id: 'key_new',
          merchantId: 'merch_acme',
          name: 'Microservice Key',
          prefix: 'rpg_live_123456',
          scopes: ['payments:write'],
          createdAt: '2026-10-14T11:00:00Z',
          rawKey: 'rpg_live_123456_supersecretkeyrawvalue'
        }), { status: 201, headers: { 'Content-Type': 'application/json' } });
      }) as typeof fetch;

      const { createMerchantApiKey } = await import('./apiClient');
      const result = await createMerchantApiKey('auth-token', 'merch_acme', {
        name: 'Microservice Key',
        scopes: ['payments:write'],
        isLive: true
      });

      expect(capturedMethod).toBe('POST');
      expect(capturedUrl).toContain('/api/v1/merchants/merch_acme/api-keys');
      expect(JSON.parse(capturedBody)).toEqual({
        name: 'Microservice Key',
        scopes: ['payments:write'],
        isLive: true
      });
      expect(result.rawKey).toBe('rpg_live_123456_supersecretkeyrawvalue');
      expect(result.prefix).toBe('rpg_live_123456');
    } finally {
      globalThis.fetch = originalFetch;
    }
  });

  it('revokes an API key via POST /revoke', async () => {
    const originalFetch = globalThis.fetch;
    try {
      let capturedUrl = '';
      let capturedMethod = '';

      globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
        capturedUrl = input.toString();
        capturedMethod = init?.method ?? '';
        return new Response(JSON.stringify({ message: 'API key revoked successfully.' }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' }
        });
      }) as typeof fetch;

      const { revokeMerchantApiKey } = await import('./apiClient');
      await revokeMerchantApiKey('auth-token', 'merch_acme', 'key_to_revoke');

      expect(capturedMethod).toBe('POST');
      expect(capturedUrl).toContain('/api/v1/merchants/merch_acme/api-keys/key_to_revoke/revoke');
    } finally {
      globalThis.fetch = originalFetch;
    }
  });

  it('fetches merchant webhook DLQ records with status filter and pagination', async () => {
    const originalFetch = globalThis.fetch;
    try {
      let capturedUrl = '';
      let capturedHeaders: Record<string, string> = {};

      globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
        capturedUrl = input.toString();
        capturedHeaders = (init?.headers ?? {}) as Record<string, string>;
        return new Response(JSON.stringify([
          {
            id: 'dlq_1',
            eventId: 'evt_dlq_test',
            paymentId: 'pay_dlq_test',
            merchantId: 'merch_acme',
            destinationUrl: 'https://webhook.site/test',
            payloadJson: '{"amount":5000}',
            deliveryAttempts: 5,
            lastErrorMessage: 'HTTP 504 Gateway Timeout',
            lastStatusCode: 504,
            status: 'exhausted',
            failedAt: '2026-10-14T09:00:00Z',
            replayedAt: null
          }
        ]), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }) as typeof fetch;

      const { fetchMerchantWebhookDlq } = await import('./apiClient');
      const records = await fetchMerchantWebhookDlq('auth-token', 'merch_acme', 'exhausted', 1, 25);

      expect(capturedUrl).toContain('/api/v1/merchants/merch_acme/webhooks/dlq?status=exhausted&page=1&pageSize=25');
      expect(capturedHeaders.Authorization).toBe('Bearer auth-token');
      expect(records.length).toBe(1);
      expect(records[0].eventId).toBe('evt_dlq_test');
      expect(records[0].lastStatusCode).toBe(504);
      expect(records[0].status).toBe('exhausted');
    } finally {
      globalThis.fetch = originalFetch;
    }
  });

  it('triggers manual replay of DLQ entry and parses replay result', async () => {
    const originalFetch = globalThis.fetch;
    try {
      let capturedUrl = '';
      let capturedMethod = '';

      globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
        capturedUrl = input.toString();
        capturedMethod = init?.method ?? '';
        return new Response(JSON.stringify({
          success: true,
          message: 'Webhook replayed successfully.',
          statusCode: 200,
          replayedAt: '2026-10-14T11:00:00Z'
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }) as typeof fetch;

      const { replayMerchantWebhookDlqEntry } = await import('./apiClient');
      const result = await replayMerchantWebhookDlqEntry('auth-token', 'merch_acme', 'dlq_entry_456');

      expect(capturedMethod).toBe('POST');
      expect(capturedUrl).toContain('/api/v1/merchants/merch_acme/webhooks/dlq/dlq_entry_456/replay');
      expect(result.success).toBe(true);
      expect(result.statusCode).toBe(200);
      expect(result.replayedAt).toBe('2026-10-14T11:00:00Z');
    } finally {
      globalThis.fetch = originalFetch;
    }
  });

  it('fetches system observability summary with active alerts and telemetry metrics', async () => {
    const originalFetch = globalThis.fetch;
    try {
      let capturedUrl = '';
      let capturedHeaders: Record<string, string> = {};

      globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
        capturedUrl = input.toString();
        capturedHeaders = (init?.headers ?? {}) as Record<string, string>;
        return new Response(JSON.stringify({
          timestamp: '2026-10-19T08:00:00Z',
          api: {
            status: 'Healthy',
            serviceName: 'PaymentGateway.Api',
            version: '1.0.0',
            environment: 'Production',
            uptime: '02:45:10'
          },
          database: {
            status: 'Connected',
            paymentDbConnected: true,
            riskDbConnected: true,
            responseTimeMs: 1.85
          },
          messageBroker: {
            status: 'InMemory',
            provider: 'In-Memory',
            host: 'localhost (in-process)'
          },
          monitoring: {
            prometheusStatus: 'Reachable',
            alertmanagerStatus: 'Reachable'
          },
          activeAlerts: [
            {
              fingerprint: 'alert-1',
              alertName: 'HighPaymentDeclineRate',
              severity: 'critical',
              state: 'active',
              summary: 'High Payment Decline Rate (> 5%)',
              description: 'Payment decline rate is 6.2% over last 5m.',
              startsAt: '2026-10-19T07:55:00Z',
              labels: { alertname: 'HighPaymentDeclineRate', severity: 'critical' },
              annotations: { summary: 'High Payment Decline Rate (> 5%)' }
            }
          ],
          latencyMetrics: {
            acquirerP95LatencySeconds: 0.35,
            outboxDispatchP95LatencySeconds: 0.05,
            declineRatePercent: 1.4,
            totalTransactions: 500,
            totalDeclines: 7,
            acquirerSlaCompliant: true,
            declineRateCompliant: true
          },
          queues: {
            outbox: {
              pendingCount: 0,
              dispatchedCount: 450,
              failedCount: 0,
              oldestPendingAgeSeconds: null
            },
            webhookDlq: {
              exhaustedCount: 2,
              replayedCount: 1,
              totalCount: 3
            },
            unresolvedPayments: {
              totalUnresolvedCount: 1
            }
          }
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }) as typeof fetch;

      const { fetchObservabilitySummary } = await import('./apiClient');
      const summary = await fetchObservabilitySummary('ops-jwt-token');

      expect(capturedUrl).toContain('/api/v1/ops/observability');
      expect(capturedHeaders.Authorization).toBe('Bearer ops-jwt-token');
      expect(summary.api.status).toBe('Healthy');
      expect(summary.database.paymentDbConnected).toBe(true);
      expect(summary.activeAlerts.length).toBe(1);
      expect(summary.activeAlerts[0].alertName).toBe('HighPaymentDeclineRate');
      expect(summary.latencyMetrics.acquirerSlaCompliant).toBe(true);
      expect(summary.queues.webhookDlq.exhaustedCount).toBe(2);
    } finally {
      globalThis.fetch = originalFetch;
    }
  });

  it('posts payment request with idempotency key in createTestPayment', async () => {
    const originalFetch = globalThis.fetch;
    try {
      let capturedUrl = '';
      let capturedHeaders: Record<string, string> = {};
      let capturedBody: string = '';

      globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
        capturedUrl = input.toString();
        capturedHeaders = (init?.headers ?? {}) as Record<string, string>;
        capturedBody = init?.body?.toString() ?? '';
        return new Response(JSON.stringify({
          paymentId: 'pay_test_3ds_123',
          partyId: 'cardholder_challenge_0002',
          amount: 5000,
          currency: 'EUR',
          settlementChannel: 'ADYEN',
          state: 'RequiresAction',
          channelReference: 'adyen_auth_123',
          declineReason: null,
          createdAt: new Date().toISOString(),
          capturedAmount: 0,
          remainingAuthorizedAmount: 0,
          refundedAmount: 0,
          remainingRefundableAmount: 0,
          action: {
            actionType: 'threeDS2',
            targetUrl: 'https://checkoutshopper-test.adyen.com/checkoutshopper/threeDS2.shtml',
            token: 'token_123',
            expiresAt: new Date(Date.now() + 900000).toISOString()
          }
        }), { status: 201, headers: { 'Content-Type': 'application/json' } });
      }) as typeof fetch;

      const { createTestPayment } = await import('./apiClient');
      const result = await createTestPayment('test-jwt-token', {
        partyId: 'cardholder_challenge_0002',
        amount: 5000,
        currency: 'EUR',
        settlementChannel: 'ADYEN',
        idempotencyKey: 'idem_test_3ds'
      });

      expect(capturedUrl).toContain('/payments');
      expect(capturedHeaders.Authorization).toBe('Bearer test-jwt-token');
      expect(capturedHeaders['Idempotency-Key']).toBe('idem_test_3ds');
      expect(JSON.parse(capturedBody).partyId).toBe('cardholder_challenge_0002');
      expect(result.state).toBe('RequiresAction');
      expect(result.action?.actionType).toBe('threeDS2');
    } finally {
      globalThis.fetch = originalFetch;
    }
  });

  it('posts 3DS result to /payments/{id}/3ds-result', async () => {
    const originalFetch = globalThis.fetch;
    try {
      let capturedUrl = '';
      let capturedHeaders: Record<string, string> = {};
      let capturedBody: string = '';

      globalThis.fetch = (async (input: RequestInfo | URL, init?: RequestInit) => {
        capturedUrl = input.toString();
        capturedHeaders = (init?.headers ?? {}) as Record<string, string>;
        capturedBody = init?.body?.toString() ?? '';
        return new Response(JSON.stringify({
          paymentId: 'pay_test_3ds_123',
          partyId: 'cardholder_challenge_0002',
          amount: 5000,
          currency: 'EUR',
          settlementChannel: 'ADYEN',
          state: 'Authorized',
          channelReference: 'adyen_3ds_auth_456',
          declineReason: null,
          createdAt: new Date().toISOString(),
          capturedAmount: 0,
          remainingAuthorizedAmount: 5000,
          refundedAmount: 0,
          remainingRefundableAmount: 0,
          action: null
        }), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }) as typeof fetch;

      const { submitThreeDsResult } = await import('./apiClient');
      const result = await submitThreeDsResult('test-jwt-token', 'pay_test_3ds_123', {
        threeDsResult: 'completed',
        details: { threeDSResult: 'success' }
      });

      expect(capturedUrl).toContain('/payments/pay_test_3ds_123/3ds-result');
      expect(capturedHeaders.Authorization).toBe('Bearer test-jwt-token');
      expect(JSON.parse(capturedBody).threeDsResult).toBe('completed');
      expect(result.state).toBe('Authorized');
    } finally {
      globalThis.fetch = originalFetch;
    }
  });

  it('fetchPaymentMethods sends proper GET request and extracts items', async () => {
    const originalFetch = globalThis.fetch;
    try {
      let capturedUrl = '';
      globalThis.fetch = (async (input: RequestInfo | URL) => {
        capturedUrl = input.toString();
        return new Response(JSON.stringify([
          {
            id: 'pm_1',
            partyId: 'merch_test',
            settlementChannel: 'ADYEN',
            cardBrand: 'visa',
            maskedPan: '**** 1111',
            expiryMonth: 12,
            expiryYear: 2030,
            status: 'Active',
            createdAt: '2026-10-19T10:00:00Z'
          }
        ]), { status: 200, headers: { 'Content-Type': 'application/json' } });
      }) as typeof fetch;

      const { fetchPaymentMethods } = await import('./apiClient');
      const methods = await fetchPaymentMethods('tok_1', 'merch_test');

      expect(capturedUrl).toContain('/api/v1/payment-methods?partyId=merch_test');
      expect(methods).toHaveLength(1);
      expect(methods[0].id).toBe('pm_1');
      expect(methods[0].status).toBe('Active');
    } finally {
      globalThis.fetch = originalFetch;
    }
  });

  it('createSubscription sends proper POST body and returns subscription item', async () => {
    const originalFetch = globalThis.fetch;
    try {
      let capturedBody = '';
      globalThis.fetch = (async (_input: RequestInfo | URL, init?: RequestInit) => {
        capturedBody = init?.body as string;
        return new Response(JSON.stringify({
          id: 'sub_1',
          partyId: 'merch_test',
          paymentMethodId: 'pm_1',
          amount: 2500,
          currency: 'EUR',
          interval: 'Monthly',
          state: 'Active',
          nextBillingAt: '2026-11-19T10:00:00Z',
          failureCount: 0,
          createdAt: '2026-10-19T10:00:00Z'
        }), { status: 201, headers: { 'Content-Type': 'application/json' } });
      }) as typeof fetch;

      const { createSubscription } = await import('./apiClient');
      const sub = await createSubscription('tok_1', {
        partyId: 'merch_test',
        paymentMethodId: 'pm_1',
        amount: 2500,
        currency: 'EUR',
        interval: 'Monthly'
      });

      expect(JSON.parse(capturedBody).amount).toBe(2500);
      expect(sub.id).toBe('sub_1');
      expect(sub.state).toBe('Active');
    } finally {
      globalThis.fetch = originalFetch;
    }
  });
});



