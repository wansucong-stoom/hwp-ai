/** Explicit, synthetic live integration check. Never reads an HWP document. */
import { loadStoomRuntime, createHwpHandler } from './hwp-codex-host.mjs';
import { readFile } from 'node:fs/promises';
import path from 'node:path';

const configPath = process.argv[2];
if (!configPath || !path.isAbsolute(configPath)) {
  throw new Error('Usage: node bridge/smoke.mjs <absolute hwp-codex.json> [--generate]');
}
const config = JSON.parse((await readFile(configPath, 'utf8')).replace(/^\uFEFF/, ''));
const { runtime, source } = await loadStoomRuntime(config.StoomModulePath);
const handle = createHwpHandler({ runtime });
const status = await handle({ id: 'smoke-status', action: 'status' });
const report = {
  sourceSha256: source.sha256,
  ready: status.ready,
  modelCount: status.models.length,
  generationExecuted: false,
  userDocumentRead: false
};
if (process.argv.includes('--generate')) {
  if (!status.ready) throw new Error(status.message);
  const response = await handle({
    id: 'smoke-generate', action: 'generate', accountId: status.accountId,
    usageConsent: true, model: 'auto',
    prompt: 'This is a synthetic software integration test. Reply with exactly HWP_CODEX_OK and nothing else.'
  });
  report.generationExecuted = true;
  report.model = response.model;
  report.expected = 'HWP_CODEX_OK';
  report.actual = response.text.trim();
  report.passed = report.actual === report.expected;
  if (!report.passed) process.exitCode = 1;
}
console.log(JSON.stringify(report));
