import { test, expect } from '@playwright/test';

test.describe('F-PORTAL-01: Operator Dashboard & Unresolved Payments Console', () => {
  test.beforeEach(async ({ page }) => {
    // Mock the unresolved payments query endpoint
    await page.route('**/api/v1/payments/unresolved*', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/problem+json, application/json',
        body: JSON.stringify({
          items: [
            {
              paymentId: 'pay_unresolved_001',
              state: 'Pending',
              settlementChannel: 'ADYEN',
              channelReference: 'psp_ref_test_001',
              createdAt: new Date(Date.now() - 45 * 60000).toISOString(),
              durationElapsedMinutes: 45,
            },
            {
              paymentId: 'pay_unresolved_002',
              state: 'Authorized',
              settlementChannel: 'WEB3',
              channelReference: '0xabc123',
              createdAt: new Date(Date.now() - 15 * 60000).toISOString(),
              durationElapsedMinutes: 15,
            },
          ],
          page: 1,
          pageSize: 50,
          totalCount: 2,
          ageMinutes: 0,
          channel: null,
        }),
      });
    });

    // Mock payment details endpoint
    await page.route('**/api/v1/payments/pay_unresolved_001', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          paymentId: 'pay_unresolved_001',
          partyId: 'merch_acme',
          amount: 15000,
          currency: 'EUR',
          settlementChannel: 'ADYEN',
          state: 'Pending',
          channelReference: 'psp_ref_test_001',
          declineReason: null,
          createdAt: new Date(Date.now() - 45 * 60000).toISOString(),
          capturedAmount: 0,
          remainingAuthorizedAmount: 15000,
          refundedAmount: 0,
          remainingRefundableAmount: 0,
        }),
      });
    });
  });

  test('renders metric cards, unresolved table, and allows inspecting payment details', async ({ page }) => {
    await page.goto('/');

    // 1. Verify title and navigation tab
    await expect(page.getByText('Unresolved Payments Console')).toBeVisible();
    await expect(page.locator('button:has-text("Unresolved Payments")')).toBeVisible();

    // 2. Verify metric cards
    await expect(page.getByText('Unresolved Total')).toBeVisible();
    await expect(page.locator('text=2').first()).toBeVisible();
    await expect(page.getByText('Oldest In-Flight')).toBeVisible();

    // 3. Verify table contents
    await expect(page.getByText('pay_unresolved_001')).toBeVisible();
    await expect(page.getByText('pay_unresolved_002')).toBeVisible();

    // 4. Open payment detail modal
    const inspectButton = page.locator('button:has-text("Inspect")').first();
    await inspectButton.click();

    // 5. Verify modal content
    await expect(page.getByText('Payment Details')).toBeVisible();
    await expect(page.getByText('150.00 EUR').first()).toBeVisible();
    await expect(page.getByText('psp_ref_test_001').first()).toBeVisible();

    // 6. Close modal
    const closeButton = page.locator('button[aria-label="Close Modal"]');
    await closeButton.click();
    await expect(page.getByText('Payment Details')).not.toBeVisible();
  });

  test('filters unresolved payments by settlement channel', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByText('Unresolved Payments Console')).toBeVisible();

    // Select Web3 in Channel filter dropdown
    const channelSelect = page.locator('main select').first();
    await channelSelect.selectOption('web3-anvil');

    // Filter value should update
    await expect(channelSelect).toHaveValue('web3-anvil');
  });
});
