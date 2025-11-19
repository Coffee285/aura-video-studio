/**
 * E2E Tests for Wizard FFmpeg Hardening (PR Requirements)
 *
 * Tests the specific scenarios outlined in the wizard hardening PR:
 * 1. Fresh environment with backend running, FFmpeg not installed
 * 2. Backend stopped when entering Step 2
 * 3. Backend running with FFmpeg already installed
 * 4. Clean desktop and rebuild (fresh state)
 */

import { test, expect, type Page } from '@playwright/test';

// Helper to clear wizard state
async function clearWizardState(page: Page) {
  await page.evaluate(() => {
    // Clear all wizard-related localStorage keys
    localStorage.removeItem('hasSeenOnboarding');
    localStorage.removeItem('hasCompletedFirstRun');
    localStorage.removeItem('wizard-state');
    localStorage.removeItem('wizard-progress');

    // Clear circuit breaker state
    Object.keys(localStorage).forEach((key) => {
      if (key.startsWith('circuit-breaker-')) {
        localStorage.removeItem(key);
      }
    });
  });
}

// Helper to simulate backend responses
async function mockBackendRunning(page: Page) {
  // Mock system status as incomplete (setup required)
  await page.route('**/api/setup/system-status', (route) => {
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        isComplete: false,
        ffmpegPath: null,
        outputDirectory: '/home/runner/AuraVideoStudio/Output',
      }),
    });
  });

  // Mock wizard status check
  await page.route('**/api/setup/wizard/status', (route) => {
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        canResume: false,
        currentStep: 0,
      }),
    });
  });

  // Mock backend health
  await page.route('**/api/system/health/status', (route) => {
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        status: 'Healthy',
      }),
    });
  });
}

async function mockFFmpegNotInstalled(page: Page) {
  await page.route('**/api/system/ffmpeg/status', (route) => {
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        installed: false,
        valid: false,
        version: null,
        path: null,
        source: 'none',
        mode: 'none',
        error: 'FFmpeg not found in system PATH or common locations',
        lastValidatedAt: null,
        lastValidationResult: 'not-found',
        errorCode: 'FFMPEG_NOT_FOUND',
        errorMessage: 'FFmpeg executable not found',
        attemptedPaths: [
          '/usr/bin/ffmpeg',
          '/usr/local/bin/ffmpeg',
          'C:\\Program Files\\ffmpeg\\bin\\ffmpeg.exe',
        ],
        versionMeetsRequirement: false,
        minimumVersion: '4.0.0',
        hardwareAcceleration: {
          nvencSupported: false,
          amfSupported: false,
          quickSyncSupported: false,
          videoToolboxSupported: false,
          availableEncoders: [],
        },
        correlationId: 'test-not-installed',
      }),
    });
  });
}

async function mockFFmpegInstalled(page: Page) {
  await page.route('**/api/system/ffmpeg/status', (route) => {
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        installed: true,
        valid: true,
        version: '6.0',
        path: '/usr/bin/ffmpeg',
        source: 'PATH',
        mode: 'system',
        error: null,
        lastValidatedAt: new Date().toISOString(),
        lastValidationResult: 'ok',
        errorCode: null,
        errorMessage: null,
        attemptedPaths: ['/usr/bin/ffmpeg'],
        versionMeetsRequirement: true,
        minimumVersion: '4.0.0',
        hardwareAcceleration: {
          nvencSupported: false,
          amfSupported: false,
          quickSyncSupported: false,
          videoToolboxSupported: false,
          availableEncoders: ['libx264', 'libx265'],
        },
        correlationId: 'test-installed',
      }),
    });
  });
}

async function mockFFmpegInstallSuccess(page: Page) {
  await page.route('**/api/ffmpeg/install', (route) => {
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        success: true,
        message: 'FFmpeg installed successfully',
        version: '6.0',
        path: '/home/runner/.aura/ffmpeg/ffmpeg',
        installedAt: new Date().toISOString(),
        mode: 'local',
        correlationId: 'test-install-success',
      }),
    });
  });
}

async function mockFFmpegRescanSuccess(page: Page) {
  await page.route('**/api/ffmpeg/rescan', (route) => {
    route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        success: true,
        installed: true,
        valid: true,
        version: '6.0',
        path: '/usr/bin/ffmpeg',
        source: 'PATH',
        error: null,
        message: 'FFmpeg found in system PATH',
        correlationId: 'test-rescan-success',
      }),
    });
  });
}

test.describe('Wizard FFmpeg Hardening - Backend Running, FFmpeg Not Installed', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await clearWizardState(page);
    await mockBackendRunning(page);
    await mockFFmpegNotInstalled(page);
  });

  test('Test 1: Fresh environment, backend running, FFmpeg not installed', async ({ page }) => {
    // Navigate to wizard
    await page.goto('/setup');

    // Wait for welcome step
    await expect(page.getByText(/Welcome to Aura Video Studio/i)).toBeVisible({ timeout: 10000 });

    // Click Get Started or Next to proceed to Step 1 (FFmpeg Check)
    const getStartedButton = page.getByRole('button', { name: /get started|next/i }).first();
    await getStartedButton.click();

    // Should be on Step 2 (internally step 1): FFmpeg Check
    await expect(page.getByText(/Check for Existing FFmpeg/i)).toBeVisible({ timeout: 5000 });

    // FFmpegDependencyCard should show "Not Ready"
    await expect(page.getByText('Not Ready')).toBeVisible();

    // Should NOT show "Backend unreachable" error (backend is mocked as running)
    await expect(page.getByText(/Backend unreachable/i)).not.toBeVisible();
    await expect(page.getByText(/Backend Server Not Running/i)).not.toBeVisible();

    // Proceed to Step 3 (FFmpeg Install)
    await page.getByRole('button', { name: /next/i }).click();

    // Should see install options
    await expect(page.getByText(/Install or Configure FFmpeg/i)).toBeVisible({ timeout: 5000 });

    // Mock successful installation
    await mockFFmpegInstallSuccess(page);
    await mockFFmpegInstalled(page); // After install, status should return installed

    // Click "Install Managed FFmpeg"
    const installButton = page.getByRole('button', { name: /Install Managed FFmpeg/i });
    await expect(installButton).toBeVisible();
    await installButton.click();

    // Wait for installation progress
    await expect(page.getByText(/Installing.../i)).toBeVisible({ timeout: 2000 });

    // After installation, badge should change to "Ready"
    await expect(page.getByText('Ready')).toBeVisible({ timeout: 15000 });

    // Green checkmark icon should be visible
    await expect(page.locator('[aria-label*="Checkmark"], svg[class*="Checkmark"]')).toBeVisible();

    // Next button should be enabled
    const nextButton = page.getByRole('button', { name: /next/i }).last();
    await expect(nextButton).toBeEnabled();

    // Complete wizard (click through remaining steps)
    await nextButton.click();

    // Should NOT see "Failed to save progress" error
    await expect(page.getByText(/Failed to save progress/i)).not.toBeVisible();
  });
});

test.describe('Wizard FFmpeg Hardening - Backend Stopped', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await clearWizardState(page);
  });

  test('Test 2: Backend stopped when entering Step 2', async ({ page }) => {
    // Initially mock backend as running for first step
    await mockBackendRunning(page);
    await mockFFmpegNotInstalled(page);

    await page.goto('/setup');

    // Wait for welcome step
    await expect(page.getByText(/Welcome to Aura Video Studio/i)).toBeVisible({ timeout: 10000 });

    // Click to proceed
    await page
      .getByRole('button', { name: /get started|next/i })
      .first()
      .click();

    // Now simulate backend stopping - all API calls fail
    await page.unroute('**/api/**');
    await page.route('**/api/**', (route) => {
      route.abort('failed'); // Simulate network failure
    });

    // Try to proceed to FFmpeg Install step
    await page.getByRole('button', { name: /next/i }).click();

    // Wait a moment for API calls to fail
    await page.waitForTimeout(2000);

    // BackendStatusBanner should appear with clear message
    const backendBanner = page
      .locator('text=/Backend Server Not Running/i')
      .or(page.locator('text=/Backend unreachable/i'));
    await expect(backendBanner.first()).toBeVisible({ timeout: 5000 });

    // Banner should have instructions
    await expect(
      page
        .getByText(/Please start the backend server/i)
        .or(page.getByText(/ensure the Aura backend is running/i))
    ).toBeVisible();

    // Retry button should be visible
    await expect(page.getByRole('button', { name: /retry/i })).toBeVisible();

    // FFmpegDependencyCard should also show specific error
    await expect(
      page.getByText(/Backend unreachable/i).or(page.getByText(/No response from backend/i))
    ).toBeVisible();

    // Now mock backend coming back online
    await page.unroute('**/api/**');
    await mockBackendRunning(page);
    await mockFFmpegNotInstalled(page);

    // Click Retry
    await page.getByRole('button', { name: /retry/i }).first().click();

    // Banner should disappear
    await expect(backendBanner.first()).not.toBeVisible({ timeout: 5000 });

    // Re-scan button should work
    await mockFFmpegRescanSuccess(page);
    await mockFFmpegInstalled(page);

    const rescanButton = page.getByRole('button', { name: /re-scan/i });
    if (await rescanButton.isVisible()) {
      await rescanButton.click();

      // Status should update to Ready if FFmpeg is found
      await expect(page.getByText('Ready')).toBeVisible({ timeout: 10000 });
    }
  });
});

test.describe('Wizard FFmpeg Hardening - FFmpeg Already Installed', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await clearWizardState(page);
    await mockBackendRunning(page);
    await mockFFmpegInstalled(page); // FFmpeg is already installed
  });

  test('Test 3: Backend running with FFmpeg already installed', async ({ page }) => {
    await page.goto('/setup');

    // Wait for welcome step
    await expect(page.getByText(/Welcome to Aura Video Studio/i)).toBeVisible({ timeout: 10000 });

    // Proceed to FFmpeg Check
    await page
      .getByRole('button', { name: /get started|next/i })
      .first()
      .click();

    // FFmpegDependencyCard should show "Ready" badge immediately
    await expect(page.getByText('Ready')).toBeVisible({ timeout: 10000 });

    // Should show detected path
    await expect(page.getByText(/\/usr\/bin\/ffmpeg/i)).toBeVisible();

    // Should show version
    await expect(page.getByText(/6\.0/)).toBeVisible();

    // Next button should be enabled immediately
    const nextButton = page.getByRole('button', { name: /next/i }).last();
    await expect(nextButton).toBeEnabled();

    // Can proceed through wizard without installing again
    await nextButton.click();
    await expect(page.getByText(/Install or Configure FFmpeg/i)).toBeVisible({ timeout: 5000 });
  });
});

test.describe('Wizard FFmpeg Hardening - Clean State', () => {
  test('Test 4: Clean desktop and rebuild - fresh wizard state', async ({ page }) => {
    // Simulate clean-desktop.ps1 script - all state cleared
    await page.goto('/');
    await clearWizardState(page);

    // Also clear any backend state by mocking fresh responses
    await mockBackendRunning(page);
    await mockFFmpegNotInstalled(page);

    await page.goto('/setup');

    // Wizard should start from Step 1 (Welcome)
    await expect(page.getByText(/Welcome to Aura Video Studio/i)).toBeVisible({ timeout: 10000 });

    // Should show Step 1 of 6 indicator
    await expect(page.getByText(/Step 1 of 6/i)).toBeVisible();

    // No stale progress from previous session
    await expect(page.getByText(/resume/i)).not.toBeVisible();
    await expect(page.getByText(/incomplete setup/i)).not.toBeVisible();

    // FFmpeg detection should behave same as fresh install
    await page
      .getByRole('button', { name: /get started|next/i })
      .first()
      .click();

    // Should show FFmpeg check with no cached state
    await expect(page.getByText(/Check for Existing FFmpeg/i)).toBeVisible({ timeout: 5000 });
    await expect(page.getByText('Not Ready')).toBeVisible();
  });
});

test.describe('Wizard FFmpeg Hardening - Error Message Quality', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
    await clearWizardState(page);
    await mockBackendRunning(page);
  });

  test('Error messages distinguish backend offline vs FFmpeg not found', async ({ page }) => {
    // Start with FFmpeg not found (but backend reachable)
    await mockFFmpegNotInstalled(page);

    await page.goto('/setup');
    await expect(page.getByText(/Welcome to Aura Video Studio/i)).toBeVisible({ timeout: 10000 });
    await page
      .getByRole('button', { name: /get started|next/i })
      .first()
      .click();

    // Should show "Not Ready" but NOT "Backend unreachable"
    await expect(page.getByText('Not Ready')).toBeVisible({ timeout: 5000 });
    await expect(page.getByText(/Backend unreachable/i)).not.toBeVisible();
    await expect(page.getByText(/Backend Server Not Running/i)).not.toBeVisible();

    // Error should specifically mention FFmpeg not found
    await page
      .getByRole('button', { name: /show details/i })
      .first()
      .click();
    await expect(page.getByText(/FFmpeg not found|not installed/i)).toBeVisible();
  });

  test('Auto-save errors show specific causes', async ({ page }) => {
    // Mock save progress to fail with specific error
    await page.route('**/api/setup/wizard/save-progress', (route) => {
      route.abort('failed');
    });

    await mockFFmpegNotInstalled(page);

    await page.goto('/setup');
    await expect(page.getByText(/Welcome to Aura Video Studio/i)).toBeVisible({ timeout: 10000 });

    // Navigate to step that triggers auto-save
    await page
      .getByRole('button', { name: /get started|next/i })
      .first()
      .click();

    // Wait for auto-save to attempt
    await page.waitForTimeout(2000);

    // Should NOT show generic "Failed to save progress"
    // Should show specific message like "Backend unreachable - progress saved locally only"
    const autoSaveIndicator = page
      .locator('[class*="AutoSave"], [data-testid*="autosave"]')
      .first();
    if (await autoSaveIndicator.isVisible()) {
      const text = await autoSaveIndicator.textContent();
      expect(text).toMatch(/saved locally|backend unreachable|progress saved/i);
      expect(text).not.toMatch(/^Failed to save progress$/); // Generic message should not appear
    }
  });
});
