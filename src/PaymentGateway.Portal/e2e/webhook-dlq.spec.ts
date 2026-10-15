import { test, expect } from '@playwright/test';

test.describe('F-PORTAL-05: Webhook DLQ Delivery Logs & Manual Replay Console', () => {
  const mockDlqEntries = [
    {
      id: 'dlq_001_exhausted',
      merchantId: 'merch_acme',
      eventId: 'evt_pay_failed_101',
      paymentId: 'pay_order_101',
      destinationUrl: 'https://api.acme-shop.com/webhooks/payments',
      deliveryAttempts: 5,
      lastStatusCode: 504,
      lastErrorMessage: 'Gateway Timeout: connection timed out after 5000ms',
      payloadJson: '{"eventId":"evt_pay_failed_101","paymentId":"pay_order_101","status":"Settled","amount":15000}',
      status: 'exhausted',
      failedAt: '2026-10-14T09:00:00Z',
      replayedAt: null,
    },
    {
      id: 'dlq_002_replayed',
      merchantId: 'merch_acme',
      eventId: 'evt_pay_settled_102',
      paymentId: 'pay_order_102',
      destinationUrl: 'https://api.acme-shop.com/webhooks/payments',
      deliveryAttempts: 5,
      lastStatusCode: 500,
      lastErrorMessage: 'Internal Server Error on recipient endpoint',
      payloadJson: '{"eventId":"evt_pay_settled_102","paymentId":"pay_order_102","status":"Settled","amount":2990}',
      status: 'replayed',
      failedAt: '2026-10-13T16:00:00Z',
      replayedAt: '2026-10-14T07:30:00Z',
      replayAttemptCount: 2,
      lastReplayStatusCode: 200,
    },
  ];

  test.beforeEach(async ({ page }) => {
    // Mock DLQ listing query
    await page.route('**/api/v1/merchants/*/webhooks/dlq*', async (route) => {
      const url = new URL(route.request().url());
      const statusParam = url.searchParams.get('status');

      let filtered = mockDlqEntries;
      if (statusParam && statusParam !== 'all') {
        filtered = mockDlqEntries.filter((e) => e.status.toLowerCase() === statusParam.toLowerCase());
      }

      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(filtered),
      });
    });

    // Mock DLQ replay endpoint
    await page.route('**/api/v1/merchants/*/webhooks/dlq/*/replay', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          statusCode: 200,
          latencyMs: 82,
          message: 'Webhook re-dispatched and accepted by recipient endpoint (HTTP 200).',
          replayedAt: new Date().toISOString(),
        }),
      });
    });
  });

  test('navigates to Webhook DLQ console and verifies metrics and table records', async ({ page }) => {
    await page.goto('/');

    // Navigate to Webhook DLQ tab
    await page.locator('button:has-text("Webhook DLQ")').click();

    // Verify header and metrics
    await expect(page.getByRole('heading', { name: 'Webhook Dead-Letter Queue (DLQ)' })).toBeVisible();
    await expect(page.getByText('Total DLQ Entries')).toBeVisible();
    await expect(page.getByText('Exhausted Failures')).toBeVisible();
    await expect(page.getByText('Replayed & Recovered')).toBeVisible();

    // Verify DLQ table items
    await expect(page.getByText('evt_pay_failed_101')).toBeVisible();
    await expect(page.getByText('evt_pay_settled_102')).toBeVisible();
    await expect(page.getByText('HTTP 504')).toBeVisible();
    await expect(page.getByText('5x').first()).toBeVisible();
    await expect(page.getByText('2 replays')).toBeVisible();
  });

  test('filters DLQ records by status and searches by Event ID', async ({ page }) => {
    await page.goto('/');
    await page.locator('button:has-text("Webhook DLQ")').click();

    // Filter by status: Exhausted
    await page.getByRole('button', { name: 'exhausted' }).click();
    await expect(page.getByText('evt_pay_failed_101')).toBeVisible();

    // Search query
    const searchInput = page.locator('input[placeholder*="Search by event ID"]');
    await searchInput.fill('failed_101');
    await expect(page.getByText('evt_pay_failed_101')).toBeVisible();

    await searchInput.fill('nonexistent_event');
    await expect(page.getByText('No Records Match Your Filter')).toBeVisible();
  });

  test('opens details modal with formatted JSON payload and triggers manual replay', async ({ page }) => {
    await page.goto('/');
    await page.locator('button:has-text("Webhook DLQ")').click();

    // Click "Details" button on exhausted record
    await page.locator('table button:has-text("Details")').first().click();

    // Verify details modal content
    await expect(page.getByRole('heading', { name: 'Webhook Delivery Diagnostics' })).toBeVisible();
    await expect(page.locator('.fixed p:has-text("Gateway Timeout")')).toBeVisible();
    await expect(page.getByText('Dispatched JSON Payload')).toBeVisible();
    await expect(page.getByText('"amount": 15000')).toBeVisible();

    // Trigger replay from inside modal
    await page.locator('.fixed button:has-text("Replay Webhook Now")').click();

    // Verify toast notification appears
    await expect(page.getByText('Webhook re-dispatched and accepted by recipient endpoint')).toBeVisible();

    // Close modal
    await page.locator('.fixed button:has-text("Close")').click();
    await expect(page.getByRole('heading', { name: 'Webhook Delivery Diagnostics' })).not.toBeVisible();
  });

  test('can access DLQ from sub-tab inside Webhooks console', async ({ page }) => {
    await page.goto('/');

    // Click Webhooks tab
    await page.locator('button:has-text("Webhooks")').click();
    await expect(page.getByText('Merchant Webhooks & Diagnostics')).toBeVisible();

    // Click "Dead-Letter Queue (DLQ)" sub-tab
    await page.locator('button:has-text("Dead-Letter Queue (DLQ)")').click();
    await expect(page.getByRole('heading', { name: 'Webhook Dead-Letter Queue (DLQ)' })).toBeVisible();
    await expect(page.getByText('evt_pay_failed_101')).toBeVisible();
  });
});
