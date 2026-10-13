import { test, expect } from '@playwright/test';

test.describe('F-PORTAL-02: Merchant Webhook Self-Service & Event Simulator', () => {
  test.beforeEach(async ({ page }) => {
    // Mock webhook subscription endpoint
    await page.route('**/api/v1/webhooks/merchants/subscription*', async (route) => {
      if (route.request().method() === 'GET') {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            id: 'sub_test_001',
            partyId: 'merch_acme',
            endpointUrl: 'https://webhook.site/mock-endpoint',
            secretKey: 'whsec_secret_1234567890abcdef',
            isActive: true,
          }),
        });
      } else if (route.request().method() === 'POST' && route.request().url().includes('rotate-secret')) {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            id: 'sub_test_001',
            partyId: 'merch_acme',
            endpointUrl: 'https://webhook.site/mock-endpoint',
            secretKey: 'whsec_secret_9999999999abcdef',
            isActive: true,
          }),
        });
      } else {
        await route.continue();
      }
    });

    // Mock test dispatch endpoint
    await page.route('**/api/v1/webhooks/merchants/test-dispatch*', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          success: true,
          statusCode: 200,
          latencyMs: 45,
          endpointUrl: 'https://webhook.site/mock-endpoint',
          eventId: 'evt_sim_9999',
          timestamp: new Date().toISOString(),
          signature: 'sha256_mock_sig_123',
          requestHeaders: {
            'Content-Type': 'application/json',
            'X-Signature-SHA256': 'sha256_mock_sig_123',
          },
          requestPayload: '{"eventId":"evt_sim_9999","type":"payment.settled"}',
          responseBody: '{"received":true}',
          errorMessage: null,
        }),
      });
    });
  });

  test('views webhook configuration, reveals secret key, and executes test simulation', async ({ page }) => {
    await page.goto('/');

    // 1. Switch to Webhooks tab
    await page.locator('button:has-text("Webhooks")').click();
    await expect(page.getByText('Merchant Webhooks & Diagnostics')).toBeVisible();

    // 2. Verify endpoint URL is preloaded
    const endpointInput = page.locator('input[type="url"]');
    await expect(endpointInput).toHaveValue('https://webhook.site/mock-endpoint');

    // 3. Test revealing secret key
    await page.locator('button:has-text("Show")').click();
    await expect(page.getByText('whsec_secret_1234567890abcdef')).toBeVisible();

    // 4. Test Event Simulator dispatch
    const sendButton = page.locator('button:has-text("Dispatch Test Event")');
    await expect(sendButton).toBeEnabled();
    await sendButton.click();

    // 5. Inspect simulator diagnostic results
    await expect(page.getByText('Last Dispatch Diagnostics')).toBeVisible();
    await expect(page.getByText('200').first()).toBeVisible();
    await expect(page.getByText('45 ms')).toBeVisible();
    await expect(page.getByText('sha256_mock_sig_123')).toBeVisible();
  });
});
