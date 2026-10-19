import { test, expect } from '@playwright/test';

test.describe('Operator Cross-Console Navigation & Operational Scenarios', () => {
  test.beforeEach(async ({ page }) => {
    // Mock Unresolved Payments
    await page.route('**/api/v1/payments/unresolved*', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          items: [],
          page: 1,
          pageSize: 50,
          totalCount: 0,
          ageMinutes: 0,
          channel: null,
        }),
      });
    });

    // Mock Webhooks Subscription
    await page.route('**/api/v1/webhooks/merchants/subscription*', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          id: 'sub_test',
          partyId: 'merch_acme',
          endpointUrl: 'https://webhook.site/test',
          secretKey: 'whsec_test',
          isActive: true,
        }),
      });
    });

    // Mock Risk Flags
    await page.route('**/api/v1/risk/flags*', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          items: [],
          page: 1,
          pageSize: 50,
          totalCount: 0,
        }),
      });
    });

    // Mock Merchant API Keys
    await page.route('**/api/v1/merchants/*/api-keys*', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([]),
      });
    });

    // Mock Webhook DLQ
    await page.route('**/api/v1/merchants/*/webhooks/dlq*', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([]),
      });
    });

    // Mock Observability
    await page.route('**/api/v1/ops/observability', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          timestamp: new Date().toISOString(),
          api: {
            status: 'Healthy',
            serviceName: 'PaymentGateway.Api',
            version: '1.0.0',
            environment: 'Production',
            uptime: '02:00:00',
          },
          database: {
            status: 'Connected',
            paymentDbConnected: true,
            riskDbConnected: true,
            responseTimeMs: 1.2,
          },
          messageBroker: {
            status: 'Connected',
            provider: 'RabbitMQ',
            host: 'rabbitmq:5672',
          },
          monitoring: {
            prometheusStatus: 'Reachable',
            alertmanagerStatus: 'Reachable',
          },
          activeAlerts: [],
          latencyMetrics: {
            acquirerP95LatencySeconds: 0.25,
            outboxDispatchP95LatencySeconds: 0.04,
            declineRatePercent: 1.2,
            totalTransactions: 500,
            totalDeclines: 6,
            acquirerSlaCompliant: true,
            declineRateCompliant: true,
          },
          queues: {
            outbox: {
              pendingCount: 0,
              dispatchedCount: 500,
              failedCount: 0,
              oldestPendingAgeSeconds: null,
            },
            webhookDlq: {
              exhaustedCount: 0,
              replayedCount: 0,
              totalCount: 0,
            },
            unresolvedPayments: {
              totalUnresolvedCount: 0,
            },
          },
        }),
      });
    });
  });

  test('seamlessly transitions across all six operations consoles via navigation rail', async ({ page }) => {
    await page.goto('/');

    // 1. Initial State: Unresolved Payments console
    await expect(page.getByRole('heading', { name: 'Unresolved Payments Console' })).toBeVisible();
    await expect(page.locator('button:has-text("Unresolved Payments")')).toHaveClass(/bg-blue-600/);

    // 2. Switch to Merchant Webhooks
    await page.locator('button:has-text("Merchant Webhooks")').click();
    await expect(page.getByRole('heading', { name: 'Merchant Webhooks & Diagnostics' })).toBeVisible();
    await expect(page.locator('button:has-text("Merchant Webhooks")')).toHaveClass(/bg-blue-600/);

    // 3. Switch to Risk Review Queue
    await page.locator('button:has-text("Risk Review Queue")').click();
    await expect(page.getByRole('heading', { name: 'Compliance & Risk Console' })).toBeVisible();
    await expect(page.locator('button:has-text("Risk Review Queue")')).toHaveClass(/bg-blue-600/);

    // 4. Switch to Merchant API Keys
    await page.locator('button:has-text("Merchant API Keys")').click();
    await expect(page.getByRole('heading', { name: 'Merchant API Keys' })).toBeVisible();
    await expect(page.locator('button:has-text("Merchant API Keys")')).toHaveClass(/bg-blue-600/);

    // 5. Switch to Webhook DLQ
    await page.locator('button:has-text("Webhook DLQ")').click();
    await expect(page.getByRole('heading', { name: 'Webhook Dead-Letter Queue (DLQ)' })).toBeVisible();
    await expect(page.locator('button:has-text("Webhook DLQ")')).toHaveClass(/bg-blue-600/);

    // 6. Switch to System Observability
    await page.locator('button:has-text("Observability")').click();
    await expect(page.getByRole('heading', { name: 'System Observability & Reliability' })).toBeVisible();
    await expect(page.locator('button:has-text("Observability")')).toHaveClass(/bg-blue-600/);

    // 7. Verify navigation badges exist in sidebar rail
    const nav = page.locator('aside nav');
    await expect(nav.getByText('F-02')).toBeVisible();
    await expect(nav.getByText('F-03')).toBeVisible();
    await expect(nav.getByText('F-04')).toBeVisible();
    await expect(nav.getByText('F-05')).toBeVisible();
    await expect(nav.getByText('OPS')).toBeVisible();
  });
});
