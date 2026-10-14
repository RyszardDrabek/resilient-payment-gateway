import { test, expect } from '@playwright/test';

test.describe('F-PORTAL-04: Merchant API Keys Management Console', () => {
  const mockKeys = [
    {
      id: 'key_001_active',
      merchantId: 'merch_acme',
      name: 'Production Core Checkout',
      prefix: 'a1b2c3',
      scopes: ['payments:write', 'payments:read'],
      createdAt: '2026-10-14T08:00:00Z',
      revokedAt: null,
      isRevoked: false,
    },
    {
      id: 'key_002_revoked',
      merchantId: 'merch_acme',
      name: 'Old Reporting Worker',
      prefix: 'd4e5f6',
      scopes: ['payments:read'],
      createdAt: '2026-10-10T12:00:00Z',
      revokedAt: '2026-10-12T14:30:00Z',
      isRevoked: true,
    },
  ];

  test.beforeEach(async ({ page }) => {
    // Mock API Keys fetch
    await page.route('**/api/v1/merchants/*/api-keys', async (route) => {
      if (route.request().method() === 'GET') {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(mockKeys),
        });
      } else if (route.request().method() === 'POST') {
        const payload = JSON.parse(route.request().postData() || '{}');
        await route.fulfill({
          status: 201,
          contentType: 'application/json',
          body: JSON.stringify({
            id: 'key_003_new',
            merchantId: 'merch_acme',
            name: payload.name || 'New Service Key',
            prefix: '998877',
            rawKey: 'rpg_live_998877abcdef1234567890abcdef1234567890abcdef',
            scopes: payload.scopes || ['payments:write', 'payments:read'],
            createdAt: new Date().toISOString(),
          }),
        });
      } else {
        await route.continue();
      }
    });

    // Mock API Key revoke
    await page.route('**/api/v1/merchants/*/api-keys/*/revoke', async (route) => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          id: 'key_001_active',
          merchantId: 'merch_acme',
          name: 'Production Core Checkout',
          prefix: 'a1b2c3',
          scopes: ['payments:write', 'payments:read'],
          createdAt: '2026-10-14T08:00:00Z',
          revokedAt: new Date().toISOString(),
          isRevoked: true,
        }),
      });
    });
  });

  test('renders API keys console with metrics cards and key records', async ({ page }) => {
    await page.goto('/');

    // Navigate to Merchant API Keys tab
    await page.locator('button:has-text("Merchant API Keys")').click();

    // Verify header and metrics
    await expect(page.getByRole('heading', { name: 'Merchant API Keys' })).toBeVisible();
    await expect(page.getByText('Total Keys')).toBeVisible();
    await expect(page.getByText('Active Keys')).toBeVisible();
    await expect(page.getByText('Revoked Keys')).toBeVisible();

    // Verify key items
    await expect(page.getByText('Production Core Checkout')).toBeVisible();
    await expect(page.getByText('Old Reporting Worker')).toBeVisible();
    await expect(page.getByText('payments:write').first()).toBeVisible();
    await expect(page.getByText('payments:read').first()).toBeVisible();
  });

  test('generates new API key and displays raw secret in security modal', async ({ page }) => {
    await page.goto('/');
    await page.locator('button:has-text("Merchant API Keys")').click();

    // Click "Generate New Key" button
    await page.locator('button:has-text("Generate New Key")').click();
    await expect(page.getByRole('heading', { name: 'Generate New API Key' })).toBeVisible();

    // Fill form
    await page.locator('input[placeholder*="Backend Production Checkout"]').fill('Microservice Order Handler');

    // Submit generation
    await page.locator('button[type="submit"]:has-text("Generate Key")').click();

    // Verify KeyCreatedModal displays raw secret and warning
    await expect(page.getByRole('heading', { name: 'API Key Created Successfully' })).toBeVisible();
    await expect(page.getByText('Save this key now!')).toBeVisible();
    await expect(page.locator('input[value^="rpg_live_"]')).toBeVisible();

    // Close modal
    await page.locator('button:has-text("I Have Saved My Secret Key")').click();
    await expect(page.getByRole('heading', { name: 'API Key Created Successfully' })).not.toBeVisible();
  });

  test('filters keys by search query and revokes active key', async ({ page }) => {
    await page.goto('/');
    await page.locator('button:has-text("Merchant API Keys")').click();

    // Search query
    const searchInput = page.locator('input[placeholder*="Search by key name"]');
    await searchInput.fill('Production');
    await expect(page.getByText('Production Core Checkout')).toBeVisible();
    await expect(page.getByText('Old Reporting Worker')).not.toBeVisible();

    await searchInput.clear();
    await expect(page.getByText('Old Reporting Worker')).toBeVisible();

    // Click Revoke in table row (using exact button name or table selector)
    await page.locator('table button:has-text("Revoke")').first().click();
    await expect(page.getByRole('heading', { name: 'Revoke API Key' })).toBeVisible();
    await expect(page.getByText('This action cannot be undone')).toBeVisible();

    // Confirm revocation
    await page.locator('button:has-text("Revoke Key Immediately")').click();
    await expect(page.getByText('API key revoked successfully.')).toBeVisible();
  });
});
