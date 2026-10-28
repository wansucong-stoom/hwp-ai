import nodeAssert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import { PassThrough, Writable } from 'node:stream';
import { createHash } from 'node:crypto';
import { deflateSync } from 'node:zlib';
import { mkdtemp, readFile, writeFile, readdir, rm, mkdir, utimes } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { loadStoomRuntime } from '../hwp-codex-host.mjs';
import { generateCodexImage, validateGeneratedPng, MAX_IMAGE_BYTES, IMAGE_PROFILE } from '../codex-image-host.mjs';
let checks = 0;
const assert = new Proxy(nodeAssert, { apply(target, receiver, args) { checks++; return Reflect.apply(target, receiver, args); }, get(target, key) { const value = Reflect.get(target, key); return typeof value === 'function' ? (...args) => { checks++; return Reflect.apply(value, target, args); } : value; } });
const index = process.argv.indexOf('--stoom-module');
if (index < 0 || !process.argv[index + 1]) throw new Error('Pass --stoom-module <absolute source>. Synthetic pipes only, never real Codex.');
const { runtime } = await loadStoomRuntime(process.argv[index + 1]);
const account = { type: 'chatgpt', email: 'synthetic-image@unit.invalid' };
const accountId = createHash('sha256').update('stoom-hwp-account-v1\0' + account.email).digest('hex');
const root = await mkdtemp(path.join(os.tmpdir(), 'hwp-image-tests-'));
const outputDirectory = path.join(root, 'output'); await mkdir(outputDirectory);
const codeHome = path.join(root, 'codex-home'); const generated = path.join(codeHome, 'generated_images'); await mkdir(generated, { recursive: true });
const crcTable = Array.from({ length: 256 }, (_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
const crc = data => { let c = 0xffffffff; for (const b of data) c = crcTable[(c ^ b) & 255] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
const chunk = (type, data) => { const result = Buffer.alloc(data.length + 12); result.writeUInt32BE(data.length); result.write(type, 4); data.copy(result, 8); result.writeUInt32BE(crc(result.subarray(4, -4)), result.length - 4); return result; };
// Clearly synthetic PNG fixtures: no model or image-generation call is involved.
function png(width = 12, height = 9, settings = {}) {
 const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(width); ihdr.writeUInt32BE(height, 4); ihdr[8] = settings.depth ?? 8; ihdr[9] = 6; ihdr[12] = settings.interlace ?? 0;
 const rows = Buffer.alloc((width * 4 + 1) * height, 80); for (let i = 0; i < height; i++) rows[i * (width * 4 + 1)] = settings.filter ?? 0;
 return Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]),chunk('IHDR', ihdr),chunk('IDAT', deflateSync(rows, { level: settings.level ?? 6 })),chunk('IEND',Buffer.alloc(0))]);
}
const imageBytes = png();
const deadline = async (promise, ms = 3000) => { let timer; try { return await Promise.race([promise, new Promise((_, reject) => { timer = setTimeout(() => reject(new Error('Synthetic deadline')), ms); })]); } finally { clearTimeout(timer); } };
function fixture(settings = {}) {
 const calls = [], launches = [], children = [], starts = [], finishes = [];
 let reads = 0;
 const spawnImpl = (file, args, options) => {
  launches.push({ file, args, options });
  const child = new EventEmitter(); child.pid = 999999; child.exitCode = null; child.killed = false;
  child.stdout = new PassThrough(); child.stderr = new PassThrough(); let buffer = '';
  const send = value => { if (!child.killed) child.stdout.write(JSON.stringify(value) + '\n'); };
  const notify = (method, params) => send({ method, params: { threadId: 'image-thread', ...params } });
  child.stdin = new Writable({ write(data, _, done) {
   buffer += data.toString(); let at;
   while ((at = buffer.indexOf('\n')) >= 0) {
    const request = JSON.parse(buffer.slice(0, at)); buffer = buffer.slice(at + 1); calls.push(request);
    if (!Object.hasOwn(request, 'id')) continue;
    queueMicrotask(async () => {
     if (child.killed || settings.hang === request.method) return;
     const reply = result => send({ id: request.id, result });
     if (settings.reject === request.method) return send({ id: request.id, error: { code: -32601, message: 'Synthetic error' } });
     switch (request.method) {
      case 'initialize': return reply({});
      case 'account/read': return reply({ account: settings.accounts ? settings.accounts[Math.min(reads++, settings.accounts.length - 1)] : settings.account === undefined ? account : settings.account });
      case 'modelProvider/capabilities/read': return reply({ imageGeneration: settings.capability !== false, webSearch: false });
      case 'model/list': return reply(settings.modelCycle ? { data: [], nextCursor: 'repeat' } : { data: [{ id: 'synthetic-terra', isDefault: true, supportedReasoningEfforts: [{ reasoningEffort: 'medium', description: 'Synthetic medium' }], defaultReasoningEffort: 'medium' }], nextCursor: null });
      case 'config/read': return reply({ config: { mcp_servers: { personal: { command: 'never-execute' } } } });
      case 'thread/start': return reply({ thread: { id: 'image-thread' }, activePermissionProfile: { id: settings.badProfile ? 'wrong' : IMAGE_PROFILE }, approvalPolicy: 'never' });
      case 'mcpServerStatus/list': return reply({ data: settings.mcp ? [{ enabled: true, tools: { forbidden: {} } }] : [], nextCursor: null });
      case 'turn/start': {
       reply({ turn: { id: 'image-turn', status: 'inProgress' } }); notify('turn/started', { turn: { id: 'image-turn' } });
       if (settings.turnHangs) return;
       if (settings.serverRequest) return send({ id: 'unexpected-request', method: 'item/commandExecution/requestApproval', params: { threadId: 'image-thread' } });
       if (settings.tool) return notify('item/started', { item: { id: 'bad', type: settings.tool } });
       if (settings.badJson) return child.stdout.write('not-json\n');
       if (settings.oversizeLine) return child.stdout.write('a'.repeat(12 * 1024 * 1024 + 1));
       if (settings.quota) return notify('error', { error: { codexErrorInfo: 'usageLimitExceeded' }, willRetry: true });
       const item = { id: 'image-item', type: 'imageGeneration', status: 'completed', result: (settings.bytes || imageBytes).toString('base64'), ...(settings.item || {}) };
       if (settings.writeSaved) { const target = settings.writeSaved; await writeFile(target, imageBytes); if (settings.old) await utimes(target, new Date(0), new Date(0)); item.savedPath = target; delete item.result; }
       if (settings.noImage) { notify('item/completed', { item: { id: 'answer', type: 'agentMessage', text: settings.noImageText || '이미지 대신 합성 설명' } }); }
       else {
        notify('item/started', { turnId: 'image-turn', item: { id: 'image-item', type: 'imageGeneration', status: 'inProgress' } });
        notify('item/completed', { turnId: 'image-turn', item });
        if (settings.duplicate) notify('item/completed', { item });
        if (settings.second) notify('item/completed', { item: { ...item, id: 'second-image' } });
        if (settings.foreignTool) notify('item/started', { threadId: 'foreign-thread', item: { id: 'foreign', type: 'commandExecution' } });
        if (settings.badTurn) notify('turn/completed', { turn: { id: 'other-turn', status: 'failed' } });
       }
       return notify('turn/completed', { turn: { id: 'image-turn', status: settings.failedTurn ? 'failed' : 'completed' } });
      }
      case 'turn/steer': return reply({ turnId: request.params.expectedTurnId });
      default: return send({ id: request.id, error: { code: -32601, message: 'Unknown synthetic RPC' } });
     }
    });
   }
   done();
  } });
  child.kill = () => { child.killed = true; child.exitCode = 0; queueMicrotask(() => child.emit('close', 0)); return true; }; children.push(child); return child;
 };
 const options = { runtime, accountId, model: 'synthetic-terra', reasoningEffort: 'auto', prompt: '합성 데이터 A와 B를 한국어 인포그래픽으로 그려 주세요.', outputDirectory, tempRoot: root, discover: async () => 'synthetic-codex.exe', imageTimeout: 300, requestTimeout: 1000,
  connectionOptions: { spawnImpl, killImpl: child => child.kill(), env: { CODEX_HOME: codeHome, OPENAI_API_KEY: 'do-not-forward' }, rpcTimeout: 200 },
  onTurnStarted: value => starts.push(value), onTurnFinished: value => finishes.push(value), ...settings.options };
 return { run: () => deadline(generateCodexImage(options)), options, calls, launches, children, starts, finishes };
}
try {
 assert.deepEqual(validateGeneratedPng(imageBytes), { width: 12, height: 9 });
 const good = fixture({ duplicate: true, foreignTool: true, badTurn: true }); const result = await good.run();
 assert.equal(path.isAbsolute(result.path), true); assert.equal(result.width, 12); assert.equal(result.height, 9); assert.equal(result.sha256, createHash('sha256').update(imageBytes).digest('hex'));
 assert.deepEqual(await readFile(result.path), imageBytes); assert.equal(Object.keys(result).sort().join(','), 'height,path,sha256,width');
 assert.equal(good.launches.length, 1); assert.equal(good.launches[0].options.shell, false); assert.equal(good.launches[0].options.windowsHide, true); assert.equal(good.launches[0].options.env.OPENAI_API_KEY, undefined);
 for (const flag of ['features.shell_tool=false','features.unified_exec=false','features.view_image=false','features.code_mode=false','features.browser_use=false','features.image_generation=true','features.omit_app_server_notification_media=false']) assert(good.launches[0].args.includes(flag));
 assert(runtime.APP_SERVER_ARGS.includes('features.code_mode_host=false'), 'Shared runtime must retain its original host-off setting');
 assert.equal(runtime.APP_SERVER_ARGS.includes('features.code_mode_host=true'), false);
 const effective = new Map(); for (let i=0;i<good.launches[0].args.length-1;i++) if(good.launches[0].args[i]==='-c'){const setting=good.launches[0].args[++i],at=setting.indexOf('=');effective.set(setting.slice(0,at),setting.slice(at+1));}
 assert.equal(effective.get('features.code_mode_host'),'true'); assert.equal(effective.get('features.code_mode'),'false');
 for(const feature of ['shell_tool','unified_exec','apps','plugins','hooks','memories','multi_agent','view_image','computer_use','browser_use']) assert.equal(effective.get('features.'+feature),'false');
 assert.equal(effective.get('features.image_generation'),'true'); assert.equal(effective.get('features.omit_app_server_notification_media'),'false');
 const thread = good.calls.find(item => item.method === 'thread/start').params; const turn = good.calls.find(item => item.method === 'turn/start').params;
 assert.equal(thread.ephemeral, true); assert.equal(thread.approvalPolicy, 'never'); assert.equal(thread.config.permissions[IMAGE_PROFILE].filesystem[thread.cwd], 'write'); assert.equal(thread.config.permissions[IMAGE_PROFILE].network.enabled, false);
 assert.deepEqual(thread.config.mcp_servers.personal, { enabled: false }); assert.match(thread.baseInstructions, /image_gen\.imagegen/); assert.equal(Object.hasOwn(turn, 'effort'), false); assert.equal(good.starts.length, 1); assert.equal(good.starts[0].turnId, 'image-turn'); assert(good.finishes.length >= 1); assert(good.children.every(child => child.killed));
 assert.equal((await readdir(root)).some(name => name.startsWith('stoom-hwp-image-job-')), false);
 const medium = fixture({ options: { reasoningEffort: 'medium' } }); await medium.run(); assert.equal(medium.calls.find(item => item.method === 'turn/start').params.effort, 'medium');
 const dataUri = fixture({ item: { result: 'data:image/png;base64,' + imageBytes.toString('base64') } }); await dataUri.run(); assert(dataUri.calls.some(item => item.method === 'turn/start'));
 const saved = fixture({ writeSaved: path.join(generated, 'synthetic.png') }); const savedResult = await saved.run(); assert.deepEqual(await readFile(savedResult.path), imageBytes);
 const largeBytes = png(1000, 800, { level: 0 }); assert(largeBytes.toString('base64').length > 2 * 1024 * 1024); const large = fixture({ bytes: largeBytes }); assert.equal((await large.run()).width, 1000);
 for (const [settings, pattern] of [
  [{ capability: false }, /지원하지/], [{ reject: 'modelProvider/capabilities/read' }, /기능 지원/], [{ account: { type: 'apiKey' } }, /ChatGPT/],
  [{ accounts: [account, { ...account, email: 'changed@unit.invalid' }] }, /계정이 변경/], [{ badProfile: true }, /전용 권한/], [{ mcp: true }, /MCP/],
  [{ options: { model: 'missing' } }, /모델/], [{ options: { reasoningEffort: 'high' } }, /추론 강도/], [{ modelCycle: true }, /페이지/],
  [{ tool: 'commandExecution' }, /도구 작업/], [{ tool: 'webSearch' }, /도구 작업/], [{ serverRequest: true }, /허용하지/],
  [{ noImage: true }, /실제 이미지/], [{ second: true }, /여러 이미지/], [{ failedTurn: true }, /정상 완료/], [{ quota: true }, /실패/],
  [{ item: { failure: { type: 'usageLimitExceeded', limitId: 'synthetic' } } }, /한도/], [{ item: { inputImages: [{ path: 'forbidden', sha256: 'none' }] } }, /새 이미지/],
  [{ item: { status: 'failed' } }, /정상 완료/], [{ item: { result: '', fileId: 'opaque-only' } }, /첨부 ID/], [{ item: { result: '!!!!' } }, /base64/],
  [{ writeSaved: path.join(root, 'outside.png') }, /출력 폴더 밖/], [{ writeSaved: path.join(generated, 'stale.png'), old: true }, /새 PNG/],
  [{ badJson: true }, /RPC/], [{ oversizeLine: true }, /허용 크기/], [{ turnHangs: true, options: { imageTimeout: 20 } }, /시간이 초과/]
 ]) {
  const f = fixture(settings); await assert.rejects(f.run(), pattern); assert(f.children.every(child => child.killed)); assert(f.calls.filter(item => item.method === 'turn/start').length <= 1, 'Never automatically retry inference');
  if (settings.capability === false || settings.reject || settings.account || settings.options?.reasoningEffort || settings.modelCycle) assert.equal(f.calls.filter(item => item.method === 'turn/start').length, 0);
 }
 for (const invalid of [Buffer.alloc(MAX_IMAGE_BYTES + 1), Buffer.from('fake'), Buffer.concat([imageBytes, Buffer.from([1])]), png(12,9,{depth:16}),png(12,9,{interlace:1}),png(12,9,{filter:5})]) assert.throws(() => validateGeneratedPng(invalid));
 const corrupt = Buffer.from(imageBytes); corrupt[corrupt.length - 1] ^= 1; assert.throws(() => validateGeneratedPng(corrupt), /체크섬/);
 const huge = Buffer.from(imageBytes); huge.writeUInt32BE(9000,16); huge.writeUInt32BE(9000,20); huge.writeUInt32BE(crc(huge.subarray(12,29)),29); assert.throws(() => validateGeneratedPng(huge), /16메가픽셀/);
 for (const invalid of [{ prompt: '' }, { prompt: 'x'.repeat(24001) }, { accountId: 'bad' }, { outputDirectory: 'relative' }, { reasoningEffort: ' medium ' }, { imageTimeout: 0 }]) { const f = fixture({ options: invalid }); await assert.rejects(f.run()); assert.equal(f.launches.length, 0); }
 const aborter = new AbortController(); aborter.abort(new Error('synthetic pre-cancel')); const pre = fixture({ options: { signal: aborter.signal } }); await assert.rejects(pre.run(), /pre-cancel/); assert.equal(pre.launches.length, 0);
 const controller = new AbortController(); const cancel = fixture({ turnHangs: true, options: { signal: controller.signal, onTurnStarted: () => controller.abort(new Error('synthetic live cancel')) } }); await assert.rejects(cancel.run(), /live cancel/); assert(cancel.children.every(child => child.killed));
 const steer = fixture({ options: { onTurnStarted: async state => { const reply = await state.client.request('turn/steer', { threadId: state.threadId, expectedTurnId: state.turnId, input: [{ type: 'text', text: '합성 추가 지시', text_elements: [] }] }); assert.equal(reply.turnId, state.turnId); } } }); await steer.run(); assert(steer.calls.some(item => item.method === 'turn/steer'));
 const diagnostics = []; const absent = fixture({ noImage: true, noImageText: '현재 이미지 생성 도구가 없어 실행할 수 없습니다. synthetic-image@unit.invalid https://unit.invalid/x', options: { onDiagnostic: value => diagnostics.push(value) } });
 await assert.rejects(absent.run(), /실제 이미지/); assert.equal(diagnostics.length, 1); assert.equal(diagnostics[0].toolUnavailable, true); assert.deepEqual(diagnostics[0].itemTypes, ['agentMessage']); assert.equal(diagnostics[0].imageItemCount, 0); assert.equal(JSON.stringify(diagnostics).includes(account.email), false); assert.equal(JSON.stringify(diagnostics).includes('https://unit.invalid'), false); assert.equal(JSON.stringify(diagnostics).includes(absent.options.prompt), false);
 const echoDiagnostics = []; const echo = fixture({ noImage: true, noImageText: 'Image generation tool unavailable: 합성 데이터 A와 B를 한국어 인포그래픽으로 그려 주세요.', options: { onDiagnostic: value => echoDiagnostics.push(value) } }); await assert.rejects(echo.run()); assert.equal(echoDiagnostics[0].agentMessageExcerpt.includes(echo.options.prompt), false);
 const gray = Buffer.from(imageBytes); gray[25] = 0; gray.writeUInt32BE(crc(gray.subarray(12,29)),29); assert.throws(() => validateGeneratedPng(gray), /RGB/);
 const genericDiagnostics = []; const generic = fixture({ noImage: true, options: { onDiagnostic: value => genericDiagnostics.push(value) } }); await assert.rejects(generic.run()); assert.equal(genericDiagnostics[0].toolUnavailable, false); assert.equal(Object.hasOwn(genericDiagnostics[0], 'agentMessageExcerpt'), false);
 const rejectedDiagnostics = []; const rejected = fixture({ tool:'commandExecution',options:{onDiagnostic:value=>rejectedDiagnostics.push(value)} }); await assert.rejects(rejected.run(),/도구 작업/); assert.deepEqual(rejectedDiagnostics,[{kind:'image.item.rejected',itemType:'commandExecution'}]);
 const throwingDiagnostic = fixture({ options: { onDiagnostic: () => { throw Error('diagnostic sink failure'); } } }); assert.equal((await throwingDiagnostic.run()).width, 12);
 assert.equal(JSON.stringify(good.calls.find(item => item.method === 'turn/start')).includes(account.email), false);
 console.log(`PASS ${checks} synthetic image-host assertions. No Codex process or inference was executed.`);
} finally {
 if (path.dirname(root) === os.tmpdir() && path.basename(root).startsWith('hwp-image-tests-')) await rm(root, { recursive: true, force: true });
}
