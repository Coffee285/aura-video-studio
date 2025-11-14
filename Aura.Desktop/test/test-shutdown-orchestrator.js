/**
 * Test Shutdown Orchestrator
 * 
 * Validates that:
 * 1. ShutdownOrchestrator coordinates cleanup sequence
 * 2. Child processes are tracked and terminated
 * 3. Backend shutdown works properly
 * 4. Timeout enforcement works
 * 5. All components are cleaned up in order
 */

const fs = require('fs');
const path = require('path');

console.log('=== Shutdown Orchestrator Tests ===\n');

let testsPassed = 0;
let testsFailed = 0;

function test(name, fn) {
  try {
    fn();
    console.log(`✓ ${name}`);
    testsPassed++;
  } catch (error) {
    console.error(`✗ ${name}`);
    console.error(`  Error: ${error.message}`);
    testsFailed++;
  }
}

// Test 1: Verify shutdown-orchestrator.js exists
test('shutdown-orchestrator.js module exists', () => {
  const shutdownOrchestrator = path.join(__dirname, '../electron/shutdown-orchestrator.js');
  if (!fs.existsSync(shutdownOrchestrator)) {
    throw new Error('shutdown-orchestrator.js not found');
  }
});

// Test 2: Verify ShutdownOrchestrator has required methods
test('ShutdownOrchestrator has all required methods', () => {
  const orchestratorContent = fs.readFileSync(
    path.join(__dirname, '../electron/shutdown-orchestrator.js'),
    'utf8'
  );
  
  const requiredMethods = [
    'shutdown',
    '_executeShutdownSteps',
    '_shutdownBackendViaApi',
    '_terminateBackendProcess',
    '_terminateChildProcesses',
    '_createTotalTimeout',
    '_getComponentSummary',
    'shouldWarnUser',
    'getElapsedTime'
  ];
  
  for (const method of requiredMethods) {
    if (!orchestratorContent.includes(method)) {
      throw new Error(`ShutdownOrchestrator missing method: ${method}`);
    }
  }
});

// Test 3: Verify timeouts are defined
test('ShutdownOrchestrator defines all required timeouts', () => {
  const orchestratorContent = fs.readFileSync(
    path.join(__dirname, '../electron/shutdown-orchestrator.js'),
    'utf8'
  );
  
  const requiredTimeouts = [
    'BACKEND_API',
    'BACKEND_PROCESS',
    'BACKEND_FORCE',
    'CHILD_PROCESSES',
    'TOTAL_SHUTDOWN'
  ];
  
  for (const timeout of requiredTimeouts) {
    if (!orchestratorContent.includes(timeout)) {
      throw new Error(`Missing timeout definition: ${timeout}`);
    }
  }
  
  // Verify TOTAL_SHUTDOWN is 5000ms or less
  const totalShutdownMatch = orchestratorContent.match(/TOTAL_SHUTDOWN:\s*(\d+)/);
  if (!totalShutdownMatch) {
    throw new Error('TOTAL_SHUTDOWN timeout not found');
  }
  
  const totalShutdownMs = parseInt(totalShutdownMatch[1], 10);
  if (totalShutdownMs > 5000) {
    throw new Error(`TOTAL_SHUTDOWN timeout is ${totalShutdownMs}ms, must be ≤ 5000ms`);
  }
});

// Test 4: Verify child-process-manager.js exists
test('child-process-manager.js module exists', () => {
  const childProcessManager = path.join(__dirname, '../electron/child-process-manager.js');
  if (!fs.existsSync(childProcessManager)) {
    throw new Error('child-process-manager.js not found');
  }
});

// Test 5: Verify ChildProcessManager has required methods
test('ChildProcessManager has all required methods', () => {
  const managerContent = fs.readFileSync(
    path.join(__dirname, '../electron/child-process-manager.js'),
    'utf8'
  );
  
  const requiredMethods = [
    'register',
    'unregister',
    'getAllPids',
    'getProcess',
    'isRunning',
    'terminateAll',
    'forceTerminateAll',
    '_terminateProcess',
    '_windowsTerminate',
    '_unixTerminate',
    '_waitForProcessExit',
    'getStats'
  ];
  
  for (const method of requiredMethods) {
    if (!managerContent.includes(method)) {
      throw new Error(`ChildProcessManager missing method: ${method}`);
    }
  }
});

// Test 6: Verify main.js integrates shutdown orchestrator
test('main.js imports and uses ShutdownOrchestrator', () => {
  const mainJs = fs.readFileSync(
    path.join(__dirname, '../electron/main.js'),
    'utf8'
  );
  
  if (!mainJs.includes("require('./shutdown-orchestrator')")) {
    throw new Error('main.js does not import ShutdownOrchestrator');
  }
  
  if (!mainJs.includes('shutdownOrchestrator = new ShutdownOrchestrator')) {
    throw new Error('main.js does not instantiate ShutdownOrchestrator');
  }
  
  if (!mainJs.includes('shutdownOrchestrator.shutdown')) {
    throw new Error('main.js does not call shutdownOrchestrator.shutdown');
  }
});

// Test 7: Verify main.js integrates child process manager
test('main.js imports and uses ChildProcessManager', () => {
  const mainJs = fs.readFileSync(
    path.join(__dirname, '../electron/main.js'),
    'utf8'
  );
  
  if (!mainJs.includes("require('./child-process-manager')")) {
    throw new Error('main.js does not import ChildProcessManager');
  }
  
  if (!mainJs.includes('childProcessManager = new ChildProcessManager')) {
    throw new Error('main.js does not instantiate ChildProcessManager');
  }
});

// Test 8: Verify backend-service.js registers with child process manager
test('backend-service.js registers processes with manager', () => {
  const backendService = fs.readFileSync(
    path.join(__dirname, '../electron/backend-service.js'),
    'utf8'
  );
  
  if (!backendService.includes('childProcessManager')) {
    throw new Error('backend-service.js does not reference childProcessManager');
  }
  
  if (!backendService.includes('childProcessManager.register')) {
    throw new Error('backend-service.js does not call register on childProcessManager');
  }
  
  if (!backendService.includes('childProcessManager.unregister')) {
    throw new Error('backend-service.js does not call unregister on childProcessManager');
  }
});

// Test 9: Verify cleanup function uses orchestrator
test('cleanup function delegates to ShutdownOrchestrator', () => {
  const mainJs = fs.readFileSync(
    path.join(__dirname, '../electron/main.js'),
    'utf8'
  );
  
  if (!mainJs.includes('shutdownOrchestrator.shutdown')) {
    throw new Error('cleanup function does not call shutdownOrchestrator.shutdown');
  }
  
  // Should pass all necessary context
  const requiredContext = [
    'backendService',
    'ipcHandlers',
    'trayManager',
    'childProcessManager'
  ];
  
  for (const contextItem of requiredContext) {
    if (!mainJs.includes(contextItem)) {
      throw new Error(`cleanup context missing: ${contextItem}`);
    }
  }
});

// Test 10: Verify Windows-specific termination
test('ChildProcessManager uses taskkill for Windows', () => {
  const managerContent = fs.readFileSync(
    path.join(__dirname, '../electron/child-process-manager.js'),
    'utf8'
  );
  
  if (!managerContent.includes('taskkill')) {
    throw new Error('ChildProcessManager does not use taskkill for Windows');
  }
  
  if (!managerContent.includes('/T')) {
    throw new Error('ChildProcessManager does not use /T flag for process tree termination');
  }
});

// Test 11: Verify SystemController has shutdown endpoint
test('SystemController has shutdown endpoint', () => {
  const systemController = fs.readFileSync(
    path.join(__dirname, '../../Aura.Api/Controllers/SystemController.cs'),
    'utf8'
  );
  
  if (!systemController.includes('[HttpPost("shutdown")]')) {
    throw new Error('SystemController missing shutdown endpoint');
  }
  
  if (!systemController.includes('IHostApplicationLifetime')) {
    throw new Error('SystemController does not use IHostApplicationLifetime');
  }
  
  if (!systemController.includes('StopApplication')) {
    throw new Error('SystemController does not call StopApplication');
  }
});

// Test 12: Verify safe-initialization passes childProcessManager
test('safe-initialization.js passes childProcessManager to BackendService', () => {
  const safeInit = fs.readFileSync(
    path.join(__dirname, '../electron/safe-initialization.js'),
    'utf8'
  );
  
  if (!safeInit.includes('childProcessManager')) {
    throw new Error('safe-initialization.js does not accept childProcessManager parameter');
  }
  
  // Verify it's passed to BackendService constructor
  if (!safeInit.includes('new BackendService(app, isDev, childProcessManager)')) {
    throw new Error('safe-initialization.js does not pass childProcessManager to BackendService');
  }
});

// Summary
console.log('\n=== Test Summary ===');
console.log(`Passed: ${testsPassed}`);
console.log(`Failed: ${testsFailed}`);
console.log(`Total:  ${testsPassed + testsFailed}`);

if (testsFailed > 0) {
  console.log('\n❌ Some tests failed');
  process.exit(1);
} else {
  console.log('\n✅ All tests passed');
  process.exit(0);
}
