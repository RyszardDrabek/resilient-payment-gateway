import { test, expect } from '@playwright/test';

test.describe('F-PORTAL-07: Operator System Observability Console', () => {
  const mockObservabilitySummary = {
    timestamp: new Date().toISOString(),
    api: {
      status: 'Healthy',
      serviceName: 'PaymentGateway.Api',
      version: '1.0.0',
      environment: 'Production',
      uptime: '04:12:30',
    },
    database: {
      status: 'Connected',
      paymentDbConnected: true,
      riskDbConnected: true,
      responseTimeMs: 1.45,
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
    activeAlerts: [
      {
        fingerprint: 'alert-crit-001',
        alertName: 'HighPaymentDeclineRate',
        severity: 'critical',
        state: 'active',
        summary: 'High Payment Decline Rate (> 5%)',
        description: 'Payment decline rate is 6.5% over the last 5 minutes.',
        startsAt: new Date(Date.now() - 10 * 60000).toISOString(),
        labels: {
          alertname: 'HighPaymentDeclineRate',
          severity: 'critical',
          job: 'payment-gateway-api',
        },
        annotations: {
          summary: 'High Payment Decline Rate (> 5%)',
        },
      },
      {
        fingerprint: 'alert-warn-002',
        alertName: 'AcquirerSettlementLatencySlaBreach',
        severity: 'warning',
        state: 'active',
        summary: 'Acquirer Settlement Latency SLA Breach (p95 > 2s)',
        description: 'Acquirer settlement channel p95 latency is 2.45s.',
        startsAt: new Date(Date.now() - 5 * 60000).toISOString(),
        labels: {
          alertname: 'AcquirerSettlementLatencySlaBreach',
          severity: 'warning',
          channel: 'ADYEN',
        },
        annotations: {
          summary: 'Acquirer Settlement Latency SLA Breach (p95 > 2s)',
        },
      },
    ],
    latencyMetrics: {
      acquirerP95LatencySeconds: 0.32,
      outboxDispatchP95LatencySeconds: 0.045,
      declineRatePercent: 1.85,
      totalTransactions: 1200,
      totalDeclines: 22,
      acquirerSlaCompliant: true,
      declineRateCompliant: true,
    },
    queues: {
      outbox: {
        pendingCount: 3,
        dispatchedCount: 1197,
        failedCount: 0,
        oldestPendingAgeSeconds: 1.2,
      },
      webhookDlq: {
        exhaustedCount: 4,
        replayedCount: 2,
        totalCount: 6,
      },
      unresolvedPayments: {
        totalUnresolvedCount: 2,
      },
    },
  };

  test.beforeEach(async ({ page }) => {
    // Mock observability endpoint
    await page.route('**/api/v1/ops/observability', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(mockObservabilitySummary),
      });
    });

    // Mock unresolved payments endpoint in case of deep-link
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

    // Mock webhook DLQ endpoint in case of deep-link
    await page.route('**/api/v1/merchants/*/webhooks/dlq*', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([]),
      });
    });
  });

  test('navigates to Observability console and renders core service statuses', async ({ page }) => {
    await page.goto('/');

    // Click Observability in navigation rail
    await page.locator('button:has-text("Observability")').click();

    // Verify main header
    await expect(page.getByRole('heading', { name: 'System Observability & Reliability' })).toBeVisible();
    await expect(page.getByText('ATTENTION REQUIRED')).toBeVisible();

    // Verify service health cards
    await expect(page.getByText('PaymentGateway.Api')).toBeVisible();
    await expect(page.getByText('PostgreSQL Cluster')).toBeVisible();
    await expect(page.getByText('RabbitMQ', { exact: true }).first()).toBeVisible();
    await expect(page.getByText('Prometheus & Alerts')).toBeVisible();

    // Verify database latency is rendered
    await expect(page.getByText('1.45 ms')).toBeVisible();
  });

  test('displays Alertmanager active alerts and filters by severity pills', async ({ page }) => {
    await page.goto('/');
    await page.locator('button:has-text("Observability")').click();

    // Verify active alerts header and counts
    await expect(page.getByRole('heading', { name: 'Alertmanager Active Alerts' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'All (2)' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Critical (1)' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Warning (1)' })).toBeVisible();

    // Both alerts should initially be visible
    await expect(page.getByText('HighPaymentDeclineRate', { exact: true })).toBeVisible();
    await expect(page.getByText('AcquirerSettlementLatencySlaBreach', { exact: true })).toBeVisible();

    // Filter to Critical only
    await page.getByRole('button', { name: 'Critical (1)' }).click();
    await expect(page.getByText('HighPaymentDeclineRate', { exact: true })).toBeVisible();
    await expect(page.getByText('AcquirerSettlementLatencySlaBreach', { exact: true })).not.toBeVisible();

    // Filter to Warning only
    await page.getByRole('button', { name: 'Warning (1)' }).click();
    await expect(page.getByText('HighPaymentDeclineRate', { exact: true })).not.toBeVisible();
    await expect(page.getByText('AcquirerSettlementLatencySlaBreach', { exact: true })).toBeVisible();

    // Restore All filter
    await page.getByRole('button', { name: 'All (2)' }).click();
    await expect(page.getByText('HighPaymentDeclineRate', { exact: true })).toBeVisible();
    await expect(page.getByText('AcquirerSettlementLatencySlaBreach', { exact: true })).toBeVisible();
  });

  test('displays latency percentiles against SLA targets and decline rate', async ({ page }) => {
    await page.goto('/');
    await page.locator('button:has-text("Observability")').click();

    // Verify section header
    await expect(page.getByText('Latency SLA Percentiles & Throughput')).toBeVisible();

    // Acquirer latency SLA card
    await expect(page.getByText('Acquirer Channel p95 Latency')).toBeVisible();
    await expect(page.getByText('0.320s')).toBeVisible();
    await expect(page.getByText('SLA Compliant')).toBeVisible();

    // Outbox dispatch duration card
    await expect(page.getByText('Transactional Outbox p95')).toBeVisible();
    await expect(page.getByText('45 ms', { exact: true })).toBeVisible();

    // Payment decline rate card
    await expect(page.getByText('Payment Decline Rate', { exact: true })).toBeVisible();
    await expect(page.getByText('1.85%')).toBeVisible();
    await expect(page.getByText('22 / 1200')).toBeVisible();
  });

  test('displays transactional queue telemetry and navigates via deep-links to DLQ and Unresolved consoles', async ({ page }) => {
    await page.goto('/');
    await page.locator('button:has-text("Observability")').click();

    // Verify queue telemetry counts
    await expect(page.getByText('Transactional Outbox', { exact: true })).toBeVisible();
    await expect(page.getByText('1197')).toBeVisible(); // Dispatched
    await expect(page.getByText('1.2s')).toBeVisible(); // Oldest lag

    // Click deep-link to Webhook DLQ
    await page.getByRole('button', { name: 'Manage Webhook DLQ' }).click();

    // Should transition to Webhook DLQ console
    await expect(page.getByRole('heading', { name: 'Webhook Dead-Letter Queue (DLQ)' })).toBeVisible();

    // Return to Observability console
    await page.locator('button:has-text("Observability")').click();
    await expect(page.getByRole('heading', { name: 'System Observability & Reliability' })).toBeVisible();

    // Click deep-link to Unresolved payments console
    await page.getByRole('button', { name: 'View Unresolved Console' }).click();

    // Should transition to Unresolved Payments console
    await expect(page.getByRole('heading', { name: 'Unresolved Payments Console' })).toBeVisible();
  });

  test('interacts with auto-refresh and manual refresh button', async ({ page }) => {
    let fetchCount = 0;
    await page.route('**/api/v1/ops/observability', async (route) => {
      fetchCount++;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(mockObservabilitySummary),
      });
    });

    await page.goto('/');
    await page.locator('button:has-text("Observability")').click();

    expect(fetchCount).toBeGreaterThanOrEqual(1);
    const initialCount = fetchCount;

    // Trigger manual refresh
    await page.getByRole('button', { name: 'Refresh' }).click();
    await expect.poll(() => fetchCount).toBeGreaterThan(initialCount);

    // Verify external dashboard links are rendered
    await expect(page.getByText('Grafana (Cockpits)')).toBeVisible();
    await expect(page.getByText('Prometheus (Meters)')).toBeVisible();
    await expect(page.getByText('Alertmanager (Alerts)')).toBeVisible();
    await expect(page.getByText('Jaeger (Distributed Traces)')).toBeVisible();
  });
});
