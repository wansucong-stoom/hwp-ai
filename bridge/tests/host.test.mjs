import nodeAssert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import { PassThrough, Writable } from 'node:stream';
import { mkdtemp, readdir, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {
  loadStoomRuntime, createHwpHandler, startJsonlHost, JsonlDecoder,
  PROFILE, INPUT_LIMIT, OUTPUT_LIMIT
} from '../hwp-codex-host.mjs';

let checks = 0;
const assert = new Proxy(nodeAssert, {
  apply(target, receiver, args) { checks++; return Reflect.apply(target, receiver, args); },
  get(target, key) {
    const value = Reflect.get(target, key);
    return typeof value === 'function' ? (...args) => { checks++; return Reflect.apply(value, target, args); } : value;
  }
});

const option = process.argv.indexOf('--stoom-module');
if (option === -1 || !process.argv[option + 1]) throw new Error('Pass --stoom-module <absolute source path>. Tests never execute Codex.');
const { runtime, source } = await loadStoomRuntime(process.argv[option + 1]);
assert.match(source.sha256, /^[a-f0-9]{64}$/);
const syntheticA = { type: 'chatgpt', email: 'synthetic-a@unit.invalid' };
const syntheticB = { type: 'chatgpt', email: 'synthetic-b@unit.invalid' };
const tick = () => new Promise(resolve => setImmediate(resolve));
const deadline = async (promise, ms = 2000) => {
  let timer;
  try { return await Promise.race([promise, new Promise((_, reject) => { timer = setTimeout(() => reject(new Error('Synthetic test timeout')), ms); })]); }
  finally { clearTimeout(timer); }
};

const testRoot = await mkdtemp(path.join(os.tmpdir(), 'hwp-bridge-test-'));
function fixture(settings = {}) {
  const calls = [], children = [], launches = [];
  let accountReads = 0;
  const spawnImpl = (executable, args, options) => {
    launches.push({ executable, args, options });
    const child = new EventEmitter();
    child.pid = 999999; child.exitCode = null; child.killed = false;
    child.stdout = new PassThrough(); child.stderr = new PassThrough();
    let buffer = '';
    const send = value => { if (!child.killed) child.stdout.write(JSON.stringify(value) + '\n'); };
    const notify = (method, params) => send({ method, params });
    child.stdin = new Writable({ write(chunk, _, done) {
      buffer += chunk.toString();
      let index;
      while ((index = buffer.indexOf('\n')) !== -1) {
        const request = JSON.parse(buffer.slice(0, index)); buffer = buffer.slice(index + 1);
        calls.push(request);
        if (!Object.hasOwn(request, 'id')) continue;
        queueMicrotask(() => {
          if (child.killed || settings.hang === request.method) return;
          const reply = result => send({ id: request.id, result });
          if (settings.reject === request.method) return send({ id: request.id, error: { code: -32601, message: 'Synthetic RPC error' } });
          switch (request.method) {
            case 'initialize': return reply({});
            case 'account/read': return reply({ account: settings.accounts ? settings.accounts[Math.min(accountReads++, settings.accounts.length - 1)] : settings.account === undefined ? syntheticA : settings.account });
            case 'model/list':
              if (settings.modelCycle) return reply({ data: [], nextCursor: 'repeated' });
              return reply(request.params.cursor
                ? { data: [{ id: 'synthetic-default', displayName: 'Synthetic default', isDefault: true }, { id: 'synthetic-hidden', hidden: true }], nextCursor: null }
                : { data: [{ id: 'synthetic-first', displayName: 'Synthetic first' }], nextCursor: 'page-2' });
            case 'account/rateLimits/read': return reply({ rateLimits: { primary: { usedPercent: 12, windowDurationMins: 300, resetsAt: 1900000000 } } });
            case 'config/read': return reply({ config: { mcp_servers: { synthetic_personal: { command: 'never-execute' } } } });
            case 'thread/start': return reply({ thread: { id: 'synthetic-thread' }, activePermissionProfile: { id: settings.badProfile ? 'wrong-profile' : request.params.config.default_permissions }, approvalPolicy: 'never' });
            case 'mcpServerStatus/list': return reply({ data: settings.mcp ? [{ runtimeStatus: 'ready', tools: { forbidden: {} } }] : [], nextCursor: null });
            case 'turn/start':
              reply({ turn: { id: 'synthetic-turn', status: 'inProgress' } });
              if (settings.turnHangs) return;
              if (settings.serverRequest) return send({ id: 1000, method: 'item/commandExecution/requestApproval', params: { threadId: 'synthetic-thread' } });
              if (settings.tool) return notify('item/started', { threadId: 'synthetic-thread', item: { type: 'commandExecution', command: 'never-execute' } });
              if (settings.quota) return notify('error', { threadId: 'synthetic-thread', error: { codexErrorInfo: 'usageLimitExceeded' }, willRetry: true });
              notify('thread/tokenUsage/updated', { threadId: 'synthetic-thread', tokenUsage: { last: { inputTokens: 10, outputTokens: 20, totalTokens: 30 } } });
              notify('account/rateLimits/updated', { rateLimits: { primary: { usedPercent: 13 } } });
              if (settings.phases) {
                notify('item/completed', { threadId: 'synthetic-thread', item: { id: 'synthetic-commentary', type: 'agentMessage', phase: 'commentary', text: '진행 중 안내는 문서에 삽입하지 않습니다.' } });
                notify('item/completed', { threadId: 'synthetic-thread', item: { id: 'synthetic-partial', type: 'agentMessage', phase: 'partial_answer', text: '중간 초안' } });
                notify('item/completed', { threadId: 'synthetic-thread', item: { id: 'synthetic-final', type: 'agentMessage', phase: 'final_answer', text: '최종 합성 문장입니다.' } });
                return notify('turn/completed', { threadId: 'synthetic-thread', turn: { status: 'completed' } });
              }
              notify('item/completed', { threadId: 'synthetic-thread', item: { id: 'synthetic-item', type: 'agentMessage', text: settings.empty ? '' : '합성 문장을 다듬었습니다.' } });
              // A repeated completion must not duplicate inserted text.
              notify('item/completed', { threadId: 'synthetic-thread', item: { id: 'synthetic-item', type: 'agentMessage', text: settings.empty ? '' : '합성 문장을 다듬었습니다.' } });
              return notify('turn/completed', { threadId: 'synthetic-thread', turn: { status: 'completed' } });
            default: return send({ id: request.id, error: { code: -32601, message: 'Unknown synthetic method' } });
          }
        });
      }
      done();
    } });
    child.kill = () => { child.killed = true; child.exitCode = 0; queueMicrotask(() => child.emit('close', 0)); return true; };
    children.push(child);
    return child;
  };
  const handler = createHwpHandler({
    runtime, discover: async () => 'synthetic-codex.exe', tempRoot: testRoot,
    connectionOptions: { spawnImpl, killImpl: child => child.kill(), rpcTimeout: 150,
      env: { OPENAI_API_KEY: 'synthetic-never-forward', CODEX_ACCESS_TOKEN: 'synthetic-never-forward', CODEX_HOME: 'synthetic-account-home', HOME: 'synthetic-home' } },
    turnTimeout: 200, requestTimeout: 1000, statusTimeout: 1000, ...settings.handlerOptions
  });
  return { handler, calls, children, launches };
}
const generation = accountId => ({ id: 'generate', action: 'generate', accountId, usageConsent: true, prompt: '다음 합성 문장을 다듬어 주세요: 합성 입력입니다.', model: 'auto' });

try {
  // The real STOOM RPC/process class is exercised with mocked child-process pipes.
  const good = fixture();
  const status = await deadline(good.handler({ id: 'status', action: 'status' }));
  assert.equal(status.ready, true);
  assert.match(status.accountId, /^[a-f0-9]{64}$/);
  assert.equal(JSON.stringify(status).includes(syntheticA.email), false);
  assert.deepEqual(status.models.map(item => item.id), ['synthetic-first', 'synthetic-default']);
  assert.equal(status.limits.primary.usedPercent, 12);
  assert(!good.calls.some(item => item.method === 'thread/start' || item.method === 'turn/start'), 'Status must not create an inference thread');
  assert(good.children.every(child => child.killed), 'Status closes its owned process');
  assert.equal(good.launches[0].options.shell, false);
  assert.equal(good.launches[0].options.windowsHide, true);
  assert.equal(good.launches[0].options.env.OPENAI_API_KEY, undefined);
  assert.equal(good.launches[0].options.env.CODEX_ACCESS_TOKEN, undefined);
  assert.equal(good.launches[0].options.env.CODEX_HOME, 'synthetic-account-home');
  assert.equal(good.calls.find(item => item.method === 'initialize').params.clientInfo.name, 'stoom_hwp_assistant');
  assert.deepEqual(await readdir(testRoot), [], 'Status does not create temporary document directories');

  const result = await deadline(good.handler(generation(status.accountId)));
  assert.equal(result.text, '합성 문장을 다듬었습니다.');
  assert.equal(result.model, 'synthetic-default');
  assert.equal(result.usage.totalTokens, 30);
  assert.equal(result.limits.primary.usedPercent, 13);
  const thread = good.calls.find(item => item.method === 'thread/start').params;
  assert.equal(thread.ephemeral, true);
  assert.equal(thread.approvalPolicy, 'never');
  assert.equal(thread.serviceName, 'stoom_hwp_assistant');
  assert(thread.baseInstructions.includes('writing, rewriting, summarization'));
  assert.equal(thread.config.default_permissions, PROFILE);
  assert.equal(thread.config.permissions[PROFILE].network.enabled, false);
  assert(Object.values(thread.config.permissions[PROFILE].filesystem).every(value => value === 'read'));
  assert.equal(thread.config.mcp_servers.synthetic_personal.enabled, false);
  assert.equal(good.calls.filter(item => item.method === 'turn/start').length, 1);
  assert.deepEqual(await readdir(testRoot), [], 'Generation cleans only its allocated temporary directory');

  const phased = fixture({ phases: true });
  const phasedResult = await deadline(phased.handler(generation(status.accountId)));
  assert.equal(phasedResult.text, '최종 합성 문장입니다.');
  assert(!phasedResult.text.includes('안내') && !phasedResult.text.includes('초안'), 'Only the declared final answer is returned when phases are present');

  for (const account of [null, { type: 'apiKey' }, { type: 'chatgpt' }]) {
    const blocked = fixture({ account });
    const probe = await deadline(blocked.handler({ id: 'status', action: 'status' }));
    assert.equal(probe.ready, false);
    assert(!blocked.calls.some(item => item.method === 'model/list' || item.method === 'turn/start'));
    assert(blocked.children.every(child => child.killed));
  }
  const missing = fixture({ handlerOptions: { discover: async () => null } });
  assert.equal((await missing.handler({ id: 'status', action: 'status' })).ready, false);
  assert.equal(missing.launches.length, 0);

  await assert.rejects(good.handler({ ...generation(status.accountId), usageConsent: false }), /구독 사용/);
  await assert.rejects(good.handler({ ...generation(status.accountId), prompt: 'a'.repeat(160001) }), /160,000/);
  await assert.rejects(good.handler({ ...generation(status.accountId), accountId: '0'.repeat(64) }), /계정이 변경/);
  await assert.rejects(good.handler({ ...generation(status.accountId), model: 'not-available' }), /모델 목록/);
  assert.equal(good.calls.filter(item => item.method === 'turn/start').length, 1, 'Invalid requests cannot infer');

  const changing = fixture({ accounts: [syntheticA, syntheticB] });
  await assert.rejects(changing.handler(generation(status.accountId)), /계정이 변경/);
  assert(!changing.calls.some(item => item.method === 'turn/start'));
  for (const settings of [{ badProfile: true }, { mcp: true }]) {
    const blocked = fixture(settings);
    await assert.rejects(blocked.handler(generation(status.accountId)), /권한|MCP/);
    assert(!blocked.calls.some(item => item.method === 'turn/start'));
    assert(blocked.children.every(child => child.killed));
  }
  const cycle = fixture({ modelCycle: true });
  await assert.rejects(cycle.handler({ id: 'status', action: 'status' }), /페이지/);
  for (const settings of [{ tool: true }, { serverRequest: true }, { quota: true }, { empty: true }, { turnHangs: true }]) {
    const failed = fixture(settings);
    await assert.rejects(deadline(failed.handler(generation(status.accountId))), /도구|한도|결과|초과/);
    assert.equal(failed.calls.filter(item => item.method === 'turn/start').length, 1, 'Failed inference never retries');
    assert(failed.children.every(child => child.killed));
  }

  const aborter = fixture({ hang: 'account/read' });
  const controller = new AbortController();
  const pending = aborter.handler({ id: 'status', action: 'status' }, controller.signal);
  await tick(); controller.abort();
  await assert.rejects(deadline(pending), /취소|abort/i);
  assert(aborter.children.every(child => child.killed));

  const beforeStart = fixture();
  const alreadyAborted = new AbortController(); alreadyAborted.abort();
  await assert.rejects(beforeStart.handler({ id: 'aborted', action: 'status' }, alreadyAborted.signal), /취소|abort/i);
  assert.equal(beforeStart.launches.length, 0, 'An already cancelled request cannot launch Codex');
  await assert.rejects(beforeStart.handler({ id: 'bad-action', action: 'run-command' }), /지원하지 않는/);
  assert.equal(beforeStart.launches.length, 0, 'Unknown actions cannot launch Codex');

  const duringTurn = fixture({ turnHangs: true });
  const turnController = new AbortController();
  const turnPromise = duringTurn.handler(generation(status.accountId), turnController.signal);
  while (!duringTurn.calls.some(item => item.method === 'turn/start')) await tick();
  turnController.abort();
  await assert.rejects(deadline(turnPromise), /취소|abort/i);
  assert.equal(duringTurn.calls.filter(item => item.method === 'turn/start').length, 1);
  assert(duringTurn.children.every(child => child.killed), 'Cancelling an active turn terminates its owned process');

  // UTF-8 may split anywhere; JSONL messages may arrive together.
  const decoded = [];
  const decoder = new JsonlDecoder(value => decoded.push(value));
  const frames = Buffer.from(JSON.stringify({ id: 1, action: 'status', text: '한글' }) + '\n' + JSON.stringify({ id: 2, action: 'status' }) + '\n');
  for (let index = 0; index < frames.length; index++) decoder.push(frames.subarray(index, index + 1));
  assert.equal(decoded.length, 2); assert.equal(decoded[0].text, '한글'); assert.equal(decoder.incomplete, false);
  assert.throws(() => new JsonlDecoder(() => {}).push(Buffer.alloc(INPUT_LIMIT + 1)), /크기/);
  assert.throws(() => new JsonlDecoder(() => {}).push(Buffer.from([0xc0, 0xaf, 10])), /UTF-8/);

  const input = new PassThrough(), output = new PassThrough(), received = [];
  let responseBuffer = '';
  output.on('data', chunk => {
    responseBuffer += chunk.toString();
    let end;
    while ((end = responseBuffer.indexOf('\n')) !== -1) { received.push(JSON.parse(responseBuffer.slice(0, end))); responseBuffer = responseBuffer.slice(end + 1); }
  });
  const hanging = fixture({ hang: 'account/read' });
  const service = startJsonlHost({ input, output, handle: hanging.handler });
  input.write(JSON.stringify({ id: 'active', action: 'status' }) + '\n');
  input.write(JSON.stringify({ id: 'queued', action: 'status' }) + '\n');
  await tick();
  input.write(JSON.stringify({ id: 'cancel-queued', action: 'cancel', targetId: 'queued' }) + '\n');
  input.write(JSON.stringify({ id: 'cancel-active', action: 'cancel', targetId: 'active' }) + '\n');
  await deadline(service.idle);
  assert(received.some(item => item.id === 'cancel-active' && item.data.cancelled));
  assert(received.some(item => item.id === 'cancel-queued' && item.data.cancelled));
  assert(received.some(item => item.id === 'active' && !item.ok));
  assert(received.some(item => item.id === 'queued' && !item.ok));
  assert.equal(hanging.launches.length, 1, 'Queued cancellation must not start a child');
  assert(hanging.children.every(child => child.killed));
  input.write(JSON.stringify({ id: 'cancel-finished', action: 'cancel', targetId: 'active' }) + '\n');
  assert(received.some(item => item.id === 'cancel-finished' && item.data.cancelled === false));
  service.close();

  const eofInput = new PassThrough(), eofOutput = new PassThrough();
  const eofFixture = fixture({ hang: 'account/read' });
  const eofService = startJsonlHost({ input: eofInput, output: eofOutput, handle: eofFixture.handler });
  eofInput.write(JSON.stringify({ id: 'eof', action: 'status' }) + '\n');
  await tick(); eofInput.end();
  await deadline(eofService.idle);
  assert.equal(eofService.closed, true);
  assert(eofFixture.children.every(child => child.killed), 'Parent disconnect terminates owned Codex child');

  const bigInput = new PassThrough(), bigOutput = new PassThrough(); let bigLine = '';
  bigOutput.on('data', chunk => { bigLine += chunk.toString(); });
  const bigService = startJsonlHost({ input: bigInput, output: bigOutput, handle: async () => ({ text: 'x'.repeat(OUTPUT_LIMIT) }) });
  bigInput.write('{"id":"big","action":"status"}\n');
  await deadline(bigService.idle);
  assert.equal(JSON.parse(bigLine).ok, false);
  assert(Buffer.byteLength(bigLine) < 1024);
  bigService.close();

  const duplicateInput = new PassThrough(), duplicateOutput = new PassThrough();
  const duplicateFixture = fixture({ hang: 'account/read' });
  const duplicateService = startJsonlHost({ input: duplicateInput, output: duplicateOutput, handle: duplicateFixture.handler });
  const duplicateResponses = [];
  duplicateOutput.on('data', chunk => duplicateResponses.push(...chunk.toString().trim().split('\n').map(line => JSON.parse(line))));
  duplicateInput.write('{"id":"duplicate","action":"status"}\n');
  await tick();
  duplicateInput.write('{"id":"duplicate","action":"status"}\n');
  await deadline(duplicateService.idle);
  assert.equal(duplicateService.closed, true);
  assert(duplicateResponses.some(item => item.id === 'duplicate' && !item.ok && /중복/.test(item.error)));
  assert(duplicateFixture.children.every(child => child.killed));

  const boundedInput = new PassThrough(), boundedOutput = new PassThrough(), boundedResponses = [];
  boundedOutput.on('data', chunk => boundedResponses.push(...chunk.toString().trim().split('\n').map(line => JSON.parse(line))));
  const waitingFixture = fixture({ hang: 'account/read' });
  const boundedService = startJsonlHost({ input: boundedInput, output: boundedOutput, handle: waitingFixture.handler });
  boundedInput.write(Array.from({ length: 9 }, (_, index) => JSON.stringify({ id: index, action: 'status' })).join('\n') + '\n');
  assert(boundedResponses.some(item => item.id === 8 && !item.ok && /대기/.test(item.error)));
  boundedService.close(); await deadline(boundedService.idle);
  assert.equal(waitingFixture.launches.length, 0, 'Closing before the queue runs must not launch any child');

  const malformedInput = new PassThrough(), malformedOutput = new PassThrough(); let malformedLine = '';
  malformedOutput.on('data', chunk => { malformedLine += chunk.toString(); });
  const malformedService = startJsonlHost({ input: malformedInput, output: malformedOutput, handle: async () => { throw new Error('Must not run'); } });
  malformedInput.write('not-json\n');
  assert.equal(malformedService.closed, true);
  assert.equal(JSON.parse(malformedLine).id, null);
  assert.equal(JSON.parse(malformedLine).ok, false);

  assert.deepEqual(await readdir(testRoot), []);
} finally {
  const resolved = path.resolve(testRoot);
  assert.equal(path.dirname(resolved), path.resolve(os.tmpdir()));
  assert(path.basename(resolved).startsWith('hwp-bridge-test-'));
  await rm(resolved, { recursive: true, force: true });
}
console.log(`PASS: ${checks} assertions; HWP bridge with real STOOM RPC class and synthetic child pipes; no Codex process or inference executed. Source SHA-256: ${source.sha256}`);
