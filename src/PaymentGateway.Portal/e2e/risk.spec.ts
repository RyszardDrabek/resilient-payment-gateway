import { test, expect } from '@playwright/test';

test.describe('F-PORTAL-03: Compliance Risk Flags Review & Disposition Console', () => {
  test.beforeEach(async ({ page }) => {
    // Mock risk flags query
    await page.route('**/api/v1/risk/flags*', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          items: [
            {
              id: 'flag_test_101',
              paymentId: 'pay_suspicious_001',
              partyId: 'merch_acme',
              eventId: 'evt_risk_101',
              reason: 'Velocity spike exceeds 5.0x rolling median',
              status: 'Open',
              disposition: null,
              createdAt: new Date().toISOString(),
              dispositionedAt: null,
              reviewerNotes: null,
            },
          ],
          page: 1,
          pageSize: 50,
          totalCount: 1,
        }),
      });
    });

    // Mock payment details
    await page.route('**/api/v1/payments/pay_suspicious_001', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          paymentId: 'pay_suspicious_001',
          partyId: 'merch_acme',
          amount: 250000,
          currency: 'EUR',
          settlementChannel: 'ADYEN',
          state: 'Authorized',
          channelReference: 'psp_ref_suspicious',
          declineReason: null,
          createdAt: new Date().toISOString(),
          capturedAmount: 0,
          remainingAuthorizedAmount: 250000,
          refundedAmount: 0,
          remainingRefundableAmount: 0,
        }),
      });
    });

    // Mock risk verdict
    await page.route('**/api/v1/risk/verdicts/*', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          id: 'verdict_101',
          eventId: 'evt_risk_101',
          paymentId: 'pay_suspicious_001',
          partyId: 'merch_acme',
          status: 'Flagged',
          reason: 'Velocity spike exceeds 5.0x rolling median',
          flagId: 'flag_test_101',
          scoredAt: new Date().toISOString(),
        }),
      });
    });

    // Mock disposition submission
    await page.route('**/api/v1/risk/flags/flag_test_101/disposition', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          id: 'flag_test_101',
          paymentId: 'pay_suspicious_001',
          partyId: 'merch_acme',
          eventId: 'evt_risk_101',
          reason: 'Velocity spike exceeds 5.0x rolling median',
          status: 'Dispositioned',
          disposition: 'confirmed',
          createdAt: new Date().toISOString(),
          dispositionedAt: new Date().toISOString(),
          reviewerNotes: 'Verified fraudulent card usage pattern.',
        }),
      });
    });
  });

  test('enforces role restriction for merchant and allows compliance auditor to review and disposition flag', async ({ page }) => {
    await page.goto('/');

    // 1. Switch active profile to Merchant
    const roleSelect = page.locator('header select');
    await roleSelect.selectOption('merchant');

    // 2. Click Risk tab -> should see access restriction guard (AC-4)
    await page.locator('button:has-text("Risk")').click();
    await expect(page.getByText('Compliance Access Restricted')).toBeVisible();

    // 3. Click "Switch to Compliance Auditor (Risk)" in the restriction guard
    await page.locator('button:has-text("Switch to Compliance Auditor (Risk)")').click();
    await expect(page.getByText('Compliance & Risk Console')).toBeVisible();

    // 4. Inspect queue and case details (AC-1, AC-2)
    await expect(page.getByRole('heading', { name: 'Review Queue' })).toBeVisible();
    await expect(page.getByText('pay_suspicious_001').first()).toBeVisible();
    await expect(page.getByText('High').first()).toBeVisible();
    await expect(page.getByText('Velocity spike exceeds 5.0x rolling median').first()).toBeVisible();
    await expect(page.getByText('2500.00 EUR')).toBeVisible();

    // 5. Click "Confirm Fraud" button -> opens DispositionModal (AC-3)
    await page.locator('button:has-text("Confirm Fraud")').first().click();
    await expect(page.getByText('Confirm Fraudulent Transaction')).toBeVisible();

    // 6. Fill reviewer notes and submit
    await page.locator('textarea').fill('Verified fraudulent card usage pattern.');
    await page.locator('button:has-text("Confirm Fraud")').last().click();

    // 7. Verify item is removed from queue and success notification appears (AC-3)
    await expect(page.getByText('has been successfully recorded as confirmed fraud')).toBeVisible();
    await expect(page.getByText('No open flags found')).toBeVisible();
  });

  test('dispositions case as false positive and removes it from queue', async ({ page }) => {
    await page.goto('/');

    // 1. Switch to Risk role and open Risk tab
    const roleSelect = page.locator('header select');
    await roleSelect.selectOption('risk');
    await page.locator('button:has-text("Risk")').click();

    await expect(page.getByRole('heading', { name: 'Review Queue' })).toBeVisible();

    // 2. Click False Positive button
    await page.locator('button:has-text("False Positive")').click();
    await expect(page.getByText('Mark as False Positive')).toBeVisible();

    // 3. Enter notes and confirm
    await page.locator('textarea').fill('Verified legitimate customer high-volume purchase.');
    await page.locator('button:has-text("Approve as False Positive")').click();

    // 4. Verify queue is cleared and notification shows false positive
    await expect(page.getByText('has been successfully recorded as false positive')).toBeVisible();
    await expect(page.getByText('No open flags found')).toBeVisible();
  });
});
