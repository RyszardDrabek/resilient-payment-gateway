import { test, expect } from '@playwright/test';

test.describe('3DS2 & SCA Challenge Simulator Console', () => {
  test.beforeEach(async ({ page }) => {
    // Default mocks for background initializations
    await page.route('**/api/v1/payments/unresolved*', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ items: [], page: 1, pageSize: 50, totalCount: 0, ageMinutes: 0, channel: null }),
      });
    });

    // Mock initial 3DS2 Challenge payment
    await page.route('**/payments', async (route) => {
      if (route.request().method() === 'POST') {
        const body = route.request().postDataJSON();
        const isChallenge = body?.partyId?.includes('0002') || body?.partyId?.includes('challenge');

        if (isChallenge) {
          await route.fulfill({
            status: 201,
            contentType: 'application/json',
            body: JSON.stringify({
              paymentId: 'pay_3ds_sim_1001',
              partyId: body.partyId,
              amount: body.amount || 4500,
              currency: body.currency || 'EUR',
              settlementChannel: 'ADYEN',
              state: 'RequiresAction',
              channelReference: 'adyen_auth_1001',
              declineReason: null,
              createdAt: new Date().toISOString(),
              capturedAmount: 0,
              remainingAuthorizedAmount: 0,
              refundedAmount: 0,
              remainingRefundableAmount: 0,
              action: {
                actionType: 'threeDS2',
                targetUrl: 'https://checkoutshopper-test.adyen.com/checkoutshopper/threeDS2.shtml',
                token: 'mock_3ds_payload_token_xyz',
                expiresAt: new Date(Date.now() + 900000).toISOString(),
              },
            }),
          });
          return;
        }

        // Frictionless or other
        await route.fulfill({
          status: 201,
          contentType: 'application/json',
          body: JSON.stringify({
            paymentId: 'pay_3ds_sim_1002',
            partyId: body.partyId,
            amount: body.amount || 4500,
            currency: body.currency || 'EUR',
            settlementChannel: 'ADYEN',
            state: 'Authorized',
            channelReference: 'adyen_auth_1002',
            declineReason: null,
            createdAt: new Date().toISOString(),
            capturedAmount: 0,
            remainingAuthorizedAmount: body.amount || 4500,
            refundedAmount: 0,
            remainingRefundableAmount: 0,
            action: null,
          }),
        });
        return;
      }

      await route.continue();
    });

    // Mock 3DS challenge result submission
    await page.route('**/payments/*/3ds-result', async (route) => {
      if (route.request().method() === 'POST') {
        const body = route.request().postDataJSON();
        const approved = body?.threeDsResult === 'completed';

        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            paymentId: 'pay_3ds_sim_1001',
            partyId: 'cardholder_challenge_0002',
            amount: 4500,
            currency: 'EUR',
            settlementChannel: 'ADYEN',
            state: approved ? 'Authorized' : 'Declined',
            channelReference: approved ? 'adyen_3ds_auth_final' : 'adyen_auth_1001',
            declineReason: approved ? null : 'ThreeDsRefused',
            createdAt: new Date().toISOString(),
            capturedAmount: 0,
            remainingAuthorizedAmount: approved ? 4500 : 0,
            refundedAmount: 0,
            remainingRefundableAmount: 0,
            action: null,
          }),
        });
        return;
      }

      await route.continue();
    });

    await page.goto('/');
  });

  test('navigates to 3DS2 Simulator and verifies header and presets', async ({ page }) => {
    // Click 3DS2 Simulator tab
    await page.getByRole('button', { name: '3DS2 Simulator' }).click();

    // Verify view header
    await expect(page.locator('h1')).toContainText('3DS2 & SCA Challenge Simulator');
    await expect(page.locator('text=ADR-019 / F-PORTAL-07')).toBeVisible();

    // Verify preset buttons exist
    await expect(page.getByRole('button', { name: '3DS2 Challenge Flow' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Frictionless Flow' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Direct Acquirer Decline' })).toBeVisible();
  });

  test('simulates 3DS2 Challenge Flow and completes approval interactively', async ({ page }) => {
    await page.getByRole('button', { name: '3DS2 Simulator' }).click();

    // Click quick scenario preset for 3DS2 Challenge
    await page.getByRole('button', { name: '3DS2 Challenge Flow' }).click();

    // Verify RequiresAction state and action details
    await expect(page.locator('text=RequiresAction (3DS2 Challenge)')).toBeVisible();
    await expect(page.locator('text=Target URL:')).toBeVisible();
    await expect(page.getByText('mock_3ds_payload_token_xyz', { exact: true })).toBeVisible();

    // Verify interactive resolution controls
    const approveBtn = page.getByRole('button', { name: 'Complete Challenge (Approve)' });
    const rejectBtn = page.getByRole('button', { name: 'Fail Challenge (Reject)' });
    await expect(approveBtn).toBeVisible();
    await expect(rejectBtn).toBeVisible();

    // Click Approve
    await approveBtn.click();

    // Verify transition to Authorized
    await expect(page.locator('text=Payment Successfully Authorized')).toBeVisible();
    await expect(page.getByText('adyen_3ds_auth_final', { exact: true })).toBeVisible();
  });

  test('simulates 3DS2 Challenge Flow and rejects challenge interactively', async ({ page }) => {
    await page.getByRole('button', { name: '3DS2 Simulator' }).click();

    // Click quick scenario preset for 3DS2 Challenge
    await page.getByRole('button', { name: '3DS2 Challenge Flow' }).click();
    await expect(page.locator('text=RequiresAction (3DS2 Challenge)')).toBeVisible();

    // Click Reject
    const rejectBtn = page.getByRole('button', { name: 'Fail Challenge (Reject)' });
    await rejectBtn.click();

    // Verify transition to Declined
    await expect(page.locator('text=Payment Declined')).toBeVisible();
    await expect(page.getByText('ThreeDsRefused', { exact: true })).toBeVisible();
  });
});
