#!/usr/bin/env node

/**
 * Asset Audit Script
 * 
 * Automates the detection of graphical issues within the application.
 * Checks for broken links and missing assets in the codebase.
 */

import fs from 'fs';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Colors for console output
const colors = {
  reset: '\x1b[0m',
  red: '\x1b[31m',
  green: '\x1b[32m',
  yellow: '\x1b[33m',
  blue: '\x1b[34m',
  cyan: '\x1b[36m',
};

// Configuration
const config = {
  publicDir: path.resolve(__dirname, '../public'),
  srcDir: path.resolve(__dirname, '../src'),
  criticalAssets: [
    'favicon.ico',
    'favicon-16x16.png',
    'favicon-32x32.png',
    'logo256.png',
    'logo512.png',
    'vite.svg',
  ],
  assetExtensions: ['.png', '.jpg', '.jpeg', '.svg', '.gif', '.webp', '.ico'],
  sourceExtensions: ['.ts', '.tsx', '.js', '.jsx', '.css'],
};

/**
 * Checks if critical assets exist in the public directory
 */
function checkCriticalAssets() {
  console.log(`\n${colors.cyan}=== Checking Critical Assets ===${colors.reset}\n`);

  const missing = [];
  const found = [];

  for (const asset of config.criticalAssets) {
    const assetPath = path.join(config.publicDir, asset);
    if (fs.existsSync(assetPath)) {
      const stats = fs.statSync(assetPath);
      const sizeKB = (stats.size / 1024).toFixed(2);
      found.push({ asset, size: sizeKB });
      console.log(`${colors.green}✓${colors.reset} ${asset} (${sizeKB} KB)`);
    } else {
      missing.push(asset);
      console.log(`${colors.red}✗${colors.reset} Missing: ${asset}`);
    }
  }

  return { found, missing };
}

/**
 * Scans source files for asset references
 */
function scanSourceFiles() {
  console.log(`\n${colors.cyan}=== Scanning Source Files for Asset References ===${colors.reset}\n`);

  const references = new Set();
  const assetPattern = /(['"`])([^'"`]*\.(png|jpg|jpeg|svg|gif|webp|ico))\1/gi;

  function scanDirectory(dir) {
    const entries = fs.readdirSync(dir, { withFileTypes: true });

    for (const entry of entries) {
      const fullPath = path.join(dir, entry.name);

      if (entry.isDirectory()) {
        // Skip node_modules and other common directories
        if (!['node_modules', '.git', 'dist', 'build'].includes(entry.name)) {
          scanDirectory(fullPath);
        }
      } else if (config.sourceExtensions.some((ext) => entry.name.endsWith(ext))) {
        try {
          const content = fs.readFileSync(fullPath, 'utf-8');
          let match;

          while ((match = assetPattern.exec(content)) !== null) {
            const assetRef = match[2];
            // Normalize path - remove leading slash
            const normalized = assetRef.startsWith('/') ? assetRef.slice(1) : assetRef;
            references.add(normalized);
          }
        } catch (error) {
          console.warn(`${colors.yellow}⚠${colors.reset} Could not read file: ${fullPath}`);
        }
      }
    }
  }

  scanDirectory(config.srcDir);

  console.log(`Found ${colors.blue}${references.size}${colors.reset} asset references in source files\n`);

  return Array.from(references);
}

/**
 * Verifies that referenced assets exist
 */
function verifyAssetReferences(references) {
  console.log(`${colors.cyan}=== Verifying Asset References ===${colors.reset}\n`);

  const broken = [];
  const valid = [];

  for (const ref of references) {
    const assetPath = path.join(config.publicDir, ref);
    if (fs.existsSync(assetPath)) {
      valid.push(ref);
      console.log(`${colors.green}✓${colors.reset} ${ref}`);
    } else {
      broken.push(ref);
      console.log(`${colors.red}✗${colors.reset} Broken reference: ${ref}`);
    }
  }

  return { valid, broken };
}

/**
 * Lists all assets in the public directory
 */
function listPublicAssets() {
  console.log(`\n${colors.cyan}=== Public Directory Assets ===${colors.reset}\n`);

  const assets = [];

  function scanDirectory(dir, relativePath = '') {
    const entries = fs.readdirSync(dir, { withFileTypes: true });

    for (const entry of entries) {
      const fullPath = path.join(dir, entry.name);
      const relPath = path.join(relativePath, entry.name);

      if (entry.isDirectory()) {
        scanDirectory(fullPath, relPath);
      } else if (config.assetExtensions.some((ext) => entry.name.endsWith(ext))) {
        const stats = fs.statSync(fullPath);
        const sizeKB = (stats.size / 1024).toFixed(2);
        assets.push({ path: relPath, size: sizeKB });
        console.log(`  ${relPath} (${sizeKB} KB)`);
      }
    }
  }

  scanDirectory(config.publicDir);

  return assets;
}

/**
 * Generates audit report
 */
function generateReport(criticalCheck, references, verification, publicAssets) {
  console.log(`\n${colors.cyan}=== Audit Report ===${colors.reset}\n`);

  const hasIssues =
    criticalCheck.missing.length > 0 ||
    verification.broken.length > 0;

  console.log(`Critical Assets:`);
  console.log(`  ${colors.green}✓ Found: ${criticalCheck.found.length}${colors.reset}`);
  if (criticalCheck.missing.length > 0) {
    console.log(`  ${colors.red}✗ Missing: ${criticalCheck.missing.length}${colors.reset}`);
  }

  console.log(`\nAsset References:`);
  console.log(`  Total: ${references.length}`);
  console.log(`  ${colors.green}✓ Valid: ${verification.valid.length}${colors.reset}`);
  if (verification.broken.length > 0) {
    console.log(`  ${colors.red}✗ Broken: ${verification.broken.length}${colors.reset}`);
  }

  console.log(`\nPublic Directory:`);
  console.log(`  Total assets: ${publicAssets.length}`);

  if (hasIssues) {
    console.log(`\n${colors.red}⚠ Issues detected!${colors.reset}`);
    
    if (criticalCheck.missing.length > 0) {
      console.log(`\nMissing critical assets:`);
      criticalCheck.missing.forEach((asset) => {
        console.log(`  - ${asset}`);
      });
    }
    
    if (verification.broken.length > 0) {
      console.log(`\nBroken asset references:`);
      verification.broken.forEach((ref) => {
        console.log(`  - ${ref}`);
      });
    }

    return false;
  } else {
    console.log(`\n${colors.green}✓ All assets verified successfully!${colors.reset}`);
    return true;
  }
}

/**
 * Main execution
 */
function main() {
  console.log(`${colors.blue}╔═══════════════════════════════════════╗${colors.reset}`);
  console.log(`${colors.blue}║   Asset Audit Script                  ║${colors.reset}`);
  console.log(`${colors.blue}║   Aura Video Studio                   ║${colors.reset}`);
  console.log(`${colors.blue}╚═══════════════════════════════════════╝${colors.reset}`);

  try {
    // Check if directories exist
    if (!fs.existsSync(config.publicDir)) {
      console.error(`${colors.red}Error: Public directory not found: ${config.publicDir}${colors.reset}`);
      process.exit(1);
    }

    if (!fs.existsSync(config.srcDir)) {
      console.error(`${colors.red}Error: Source directory not found: ${config.srcDir}${colors.reset}`);
      process.exit(1);
    }

    // Run audit steps
    const criticalCheck = checkCriticalAssets();
    const references = scanSourceFiles();
    const verification = verifyAssetReferences(references);
    const publicAssets = listPublicAssets();

    // Generate report
    const success = generateReport(criticalCheck, references, verification, publicAssets);

    process.exit(success ? 0 : 1);
  } catch (error) {
    console.error(`\n${colors.red}Error during audit: ${error.message}${colors.reset}`);
    console.error(error.stack);
    process.exit(1);
  }
}

main();
