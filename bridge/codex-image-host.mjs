/** One owned Codex image turn. No HTTP listener, token copy, shell, browser or MCP tools. */
import { spawn } from 'node:child_process';
import { createHash } from 'node:crypto';
import { mkdtemp, realpath, lstat, open, writeFile, rm } from 'node:fs/promises';
import { inflateSync } from 'node:zlib';
import os from 'node:os';
import path from 'node:path';
import { modelReasoningMetadata, validateReasoningEffort, resolveReasoningEffort } from './hwp-codex-host.mjs';

export const IMAGE_PROFILE = 'stoom_hwp_image_generation';
export const MAX_IMAGE_BYTES = 8 * 1024 * 1024;
export const MAX_IMAGE_PIXELS = 16 * 1024 * 1024;
const MEDIA_LINE_LIMIT = 12 * 1024 * 1024;
const own = value => value !== null && typeof value === 'object' && !Array.isArray(value);
const safe = (value, max = 256) => typeof value === 'string' && value.length <= max && !/[\x00-\x1f\x7f]/.test(value);
const fault = message => Object.assign(new Error(message), { stoomSafe: true });
const cancelled = () => fault('이미지 생성 요청이 취소되었습니다.');
const inside = (child, root) => { const relative = path.relative(root, child); return relative === '' || (!relative.startsWith('..' + path.sep) && relative !== '..' && !path.isAbsolute(relative)); };

function identity(value) {
  const account = value?.account;
  if (!own(account) || account.type !== 'chatgpt') throw fault('ChatGPT 구독으로 로그인한 Codex만 이미지 생성에 사용할 수 있습니다.');
  const id = [account.accountId, account.id, account.email].find(value => safe(value, 512) && value.trim());
  if (!id) throw fault('Codex 계정을 확인하지 못했습니다.');
  return createHash('sha256').update('stoom-hwp-account-v1\0' + id.trim().toLowerCase()).digest('hex');
}
async function models(client) {
  const output = [], seen = new Set(), ids = new Set(); let cursor = null;
  for (let page = 0; page < 8; page++) {
    const value = await client.request('model/list', { cursor, limit: 100, includeHidden: false });
    if (!own(value) || !Array.isArray(value.data)) throw fault('이미지 생성에 사용할 Codex 모델 목록을 읽지 못했습니다.');
    for (const item of value.data) {
      if (!own(item) || item.hidden === true) continue;
      const id = item.id || item.model;
      if (!safe(id, 128) || !id.trim() || ids.has(id)) continue;
      if (output.length >= 256) throw fault('Codex 모델 목록이 허용 범위를 넘었습니다.');
      ids.add(id); output.push({ id, isDefault: item.isDefault === true, ...modelReasoningMetadata(item) });
    }
    cursor = value.nextCursor;
    if (cursor === undefined || cursor === null || cursor === '') return output;
    if (!safe(cursor, 1024) || seen.has(cursor)) throw fault('Codex 모델 목록 페이지가 잘못되었습니다.');
    seen.add(cursor);
  }
  throw fault('Codex 모델 목록을 끝까지 확인하지 못했습니다.');
}
async function mcpOverrides(client, cwd) {
  const result = await client.request('config/read', { includeLayers: false, cwd });
  if (!own(result?.config) || (result.config.mcp_servers !== undefined && !own(result.config.mcp_servers))) throw fault('Codex MCP 설정을 확인하지 못했습니다.');
  const output = {};
  for (const name of Object.keys(result.config.mcp_servers || {})) {
    if (!safe(name) || ['__proto__', 'prototype', 'constructor'].includes(name)) throw fault('Codex MCP 설정 이름이 잘못되었습니다.');
    output[name] = { enabled: false };
  }
  return output;
}
async function assertNoMcp(client, threadId) {
  let cursor = null; const seen = new Set();
  for (let page = 0; page < 8; page++) {
    const result = await client.request('mcpServerStatus/list', { threadId, detail: 'toolsAndAuthOnly', cursor, limit: 100 });
    if (!Array.isArray(result?.data)) throw fault('이미지 생성의 MCP 비활성화 상태를 확인하지 못했습니다.');
    for (const item of result.data) {
      if (!own(item) || item.enabled === true || (own(item.tools) && Object.keys(item.tools).length) || (Array.isArray(item.tools) && item.tools.length) || (item.runtimeStatus !== undefined && item.runtimeStatus !== 'disabled') || (item.enabled !== false && item.runtimeStatus !== 'disabled')) throw fault('외부 MCP 도구가 연결되어 있어 이미지 생성을 중단합니다.');
    }
    cursor = result.nextCursor;
    if (cursor === undefined || cursor === null || cursor === '') return;
    if (!safe(cursor, 1024) || seen.has(cursor)) throw fault('Codex MCP 상태 페이지가 잘못되었습니다.');
    seen.add(cursor);
  }
  throw fault('Codex MCP 상태를 끝까지 확인하지 못했습니다.');
}
function killOwnedChild(child) {
  if (!child || child.exitCode !== null && child.exitCode !== undefined) return;
  if (process.platform === 'win32' && Number.isInteger(child.pid) && child.pid > 0) {
    const system = process.env.SystemRoot || 'C:\\Windows';
    const killer = spawn(path.join(system, 'System32', 'taskkill.exe'), ['/PID', String(child.pid), '/T', '/F'], { shell: false, windowsHide: true, stdio: 'ignore' });
    killer.once('error', () => { try { child.kill(); } catch {} });
    const timer = setTimeout(() => { try { child.kill(); killer.kill(); } catch {} }, 1500); timer.unref?.();
    killer.once('close', () => clearTimeout(timer));
  } else { try { process.kill(-child.pid, 'SIGKILL'); } catch { try { child.kill(); } catch {} } }
}
function connectionClass(runtime) {
  return class ImageConnection extends runtime.CodexConnection {
    async initialize() {
      await this.request('initialize', { clientInfo: { name: 'stoom_hwp_image_generator', version: '0.6.0' }, capabilities: { experimentalApi: false } });
      this.write({ method: 'initialized' });
    }
    // Inline PNGs can exceed the shared text host's 2MiB line limit. Only image
    // notifications receive this allowance; all requests and other lines retain
    // the original RPC parser and its interactive-request rejection.
    consume(chunk) {
      if (this.closed) return;
      this.mediaBuffer = (this.mediaBuffer || '') + chunk;
      let index;
      while ((index = this.mediaBuffer.indexOf('\n')) !== -1) {
        const line = this.mediaBuffer.slice(0, index).replace(/\r$/, ''); this.mediaBuffer = this.mediaBuffer.slice(index + 1);
        if (Buffer.byteLength(line) > MEDIA_LINE_LIMIT) return this.close(fault('이미지 생성 응답이 허용 크기를 넘었습니다.'));
        let message; try { message = JSON.parse(line); } catch { return super.consume(line + '\n'); }
        if (own(message) && !Object.hasOwn(message, 'id') && ['item/started', 'item/completed'].includes(message.method) && message.params?.item?.type === 'imageGeneration') this.emit('notification', message);
        else super.consume(line + '\n');
        if (this.closed) return;
      }
      if (Buffer.byteLength(this.mediaBuffer) > MEDIA_LINE_LIMIT) this.close(fault('이미지 생성 응답이 허용 크기를 넘었습니다.'));
    }
  };
}
const CRC_TABLE = Array.from({ length: 256 }, (_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
function crc32(bytes) { let crc = 0xffffffff; for (const byte of bytes) crc = CRC_TABLE[(crc ^ byte) & 255] ^ (crc >>> 8); return (crc ^ 0xffffffff) >>> 0; }
/** Strict static PNG validation before any native import. Generated RGB/RGBA,
 * 8-bit noninterlaced PNGs are accepted, matching the fragment builder. */
export function validateGeneratedPng(bytes) {
  if (!Buffer.isBuffer(bytes) || bytes.length > MAX_IMAGE_BYTES || bytes.length < 57 || !bytes.subarray(0, 8).equals(Buffer.from([137,80,78,71,13,10,26,10]))) throw fault('이미지 결과는 8MiB 이하의 PNG 파일이어야 합니다.');
  let width, height, channels, offset = 8, first = true, ended = false, palette = false, idatSeen = false, idatEnded = false; const compressed = [];
  while (offset < bytes.length) {
    if (offset + 12 > bytes.length) throw fault('PNG 청크가 손상되었습니다.');
    const length = bytes.readUInt32BE(offset), end = offset + 12 + length;
    if (end > bytes.length) throw fault('PNG 청크 크기가 잘못되었습니다.');
    const type = bytes.toString('ascii', offset + 4, offset + 8), data = bytes.subarray(offset + 8, end - 4);
    if (!/^[A-Za-z]{4}$/.test(type) || crc32(bytes.subarray(offset + 4, end - 4)) !== bytes.readUInt32BE(end - 4)) throw fault('PNG 체크섬이 잘못되었습니다.');
    if (first) {
      if (type !== 'IHDR' || length !== 13) throw fault('PNG 헤더가 잘못되었습니다.');
      width = data.readUInt32BE(0); height = data.readUInt32BE(4); channels = ({ 2:3, 6:4 })[data[9]];
      if (!width || !height || width * height > MAX_IMAGE_PIXELS || !channels || data[8] !== 8 || data[10] !== 0 || data[11] !== 0 || data[12] !== 0) throw fault('PNG는 16메가픽셀 이하, 8비트 RGB/RGBA 비인터레이스 이미지여야 합니다.');
      palette = data[9] !== 3; first = false;
    } else if (type === 'IHDR') throw fault('PNG 헤더가 중복되었습니다.');
    else if (type === 'PLTE') { if (idatSeen || !length || length % 3 || length > 768) throw fault('PNG 팔레트가 잘못되었습니다.'); palette = true; }
    else if (type === 'IDAT') { if (!palette || idatEnded) throw fault('PNG 이미지 청크 순서가 잘못되었습니다.'); idatSeen = true; compressed.push(data); }
    else if (type === 'IEND') { if (length || !idatSeen || end !== bytes.length) throw fault('PNG 종료 청크가 잘못되었습니다.'); ended = true; offset = end; break; }
    else { if (idatSeen) idatEnded = true; if (type[0] === type[0].toUpperCase()) throw fault('지원하지 않는 PNG 필수 청크입니다.'); }
    offset = end;
  }
  if (!ended) throw fault('PNG 종료 청크가 없습니다.');
  const row = width * channels + 1, expected = row * height;
  let raw; try { raw = inflateSync(Buffer.concat(compressed), { maxOutputLength: expected }); } catch { throw fault('PNG 이미지 데이터를 확인하지 못했습니다.'); }
  if (raw.length !== expected) throw fault('PNG 이미지 데이터 크기가 맞지 않습니다.');
  for (let rowIndex = 0; rowIndex < height; rowIndex++) if (raw[rowIndex * row] > 4) throw fault('PNG 필터가 잘못되었습니다.');
  return { width, height };
}
function decodeInline(value) {
  if (typeof value !== 'string' || !value) return null;
  const encoded = value.startsWith('data:image/png;base64,') ? value.slice(22) : value;
  if (encoded.length > Math.ceil(MAX_IMAGE_BYTES / 3) * 4 || encoded.length % 4 || !/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(encoded)) throw fault('이미지 생성 결과의 base64 PNG가 잘못되었습니다.');
  const bytes = Buffer.from(encoded, 'base64'); if (bytes.toString('base64') !== encoded) throw fault('이미지 생성 결과의 base64 PNG가 잘못되었습니다.');
  return bytes;
}
async function savedBytes(savedPath, roots, startedAt) {
  if (!safe(savedPath, 4096) || !path.isAbsolute(savedPath)) throw fault('Codex가 읽을 수 있는 PNG 파일 경로를 반환하지 않았습니다.');
  let resolved; try { resolved = await realpath(savedPath); } catch { throw fault('Codex가 생성한 PNG 파일을 찾지 못했습니다.'); }
  if (!roots.some(root => inside(resolved, root))) throw fault('Codex 생성 이미지 경로가 허용된 출력 폴더 밖에 있습니다.');
  const info = await lstat(savedPath);
  if (!info.isFile() || info.isSymbolicLink() || !info.size || info.size > MAX_IMAGE_BYTES || info.mtimeMs < startedAt - 2000) throw fault('Codex 생성 이미지가 새 PNG 파일인지 확인하지 못했습니다.');
  const file = await open(resolved, 'r');
  try {
    const current = await file.stat();
    if (!current.isFile() || current.size !== info.size || current.mtimeMs !== info.mtimeMs) throw fault('이미지 파일이 읽기 전에 변경되었습니다.');
    // Bounded reads avoid an untrusted growing file allocating unlimited memory.
    const bytes = Buffer.alloc(current.size); let read = 0;
    while (read < bytes.length) { const part = await file.read(bytes, read, bytes.length - read, read); if (!part.bytesRead) throw fault('PNG 파일을 끝까지 읽지 못했습니다.'); read += part.bytesRead; }
    const extra = Buffer.alloc(1); if ((await file.read(extra, 0, 1, read)).bytesRead) throw fault('이미지 파일 크기가 변경되었습니다.');
    return bytes;
  } finally { await file.close(); }
}
function waitForImage(client, threadId, prompt, effort, timeout, hooks, onDiagnostic) {
  return new Promise((resolve, reject) => {
    let done = false, turnId = '', image, agentText = ''; const itemIds = new Set(), itemTypes = new Set(), messageIds = new Set();
    const started = id => {
      if (done) return;
      if (!safe(id, 256) || !id) throw fault('Codex 이미지 생성 turn ID가 잘못되었습니다.');
      if (turnId && turnId !== id) throw fault('Codex 이미지 생성 turn이 변경되었습니다.');
      if (!turnId) { turnId = id; hooks.started(id); }
    };
    const finish = error => { if (done) return; done = true; clearTimeout(timer); client.off('notification', notification); client.off('closed', closed); hooks.finished(); error ? reject(error) : image ? resolve(image) : reject(fault('Codex가 실제 이미지를 반환하지 않았습니다. 자동 재시도하지 않습니다.')); };
    const closed = error => finish(error);
    const take = (item, completed) => {
      if (!own(item) || !['userMessage', 'reasoning', 'agentMessage', 'imageGeneration'].includes(item.type)) {
        try { onDiagnostic?.({ kind: 'image.item.rejected', itemType: safe(item?.type, 128) ? item.type : 'invalid', ...(safe(item?.status, 64) ? { status: item.status } : {}) }); } catch {}
        throw fault('이미지 생성 외의 도구 작업을 시도하여 중단했습니다.');
      }
      itemTypes.add(item.type);
      if (item.type === 'agentMessage' && completed && onDiagnostic && typeof item.text === 'string' && !messageIds.has(item.id)) {
        messageIds.add(item.id); agentText += (agentText ? '\n' : '') + item.text.slice(0, Math.max(0, 2048 - agentText.length));
      }
      if (item.type !== 'imageGeneration') return;
      if (!safe(item.id, 256) || !item.id) throw fault('이미지 생성 항목 ID가 잘못되었습니다.');
      itemIds.add(item.id); if (itemIds.size !== 1) throw fault('한 요청에 여러 이미지를 생성하여 중단했습니다. 자동 재시도하지 않습니다.');
      if (!completed) return;
      if (item.failure) throw fault('Codex 이미지 생성 한도 또는 생성 오류로 실패했습니다. 자동 재시도하지 않습니다.');
      if (item.status !== 'completed' || (item.inputImages != null && (!Array.isArray(item.inputImages) || item.inputImages.length))) throw fault('새 이미지 생성이 정상 완료되지 않았습니다.');
      if (image && JSON.stringify(image) !== JSON.stringify(item)) throw fault('이미지 생성 완료 항목이 변경되었습니다.');
      image = item;
    };
    const notification = message => {
      const params = message.params;
      if (!own(params) || params.threadId !== threadId) return;
      try {
        if (message.method === 'turn/started') return started(params.turn?.id);
        if (params.turnId && turnId && params.turnId !== turnId) return;
        if (message.method === 'error') return finish(fault('Codex 이미지 생성이 실패했습니다. 자동 재시도하지 않습니다.'));
        if (['item/started', 'item/completed'].includes(message.method)) take(params.item, message.method === 'item/completed');
        if (message.method === 'turn/completed') {
          if (turnId && params.turn?.id && params.turn.id !== turnId) return;
          if (params.turn?.status !== 'completed') return finish(fault('Codex 이미지 생성이 정상 완료되지 않았습니다. 자동 재시도하지 않습니다.'));
          if (Array.isArray(params.turn.items)) for (const item of params.turn.items) take(item, true);
          if (onDiagnostic) {
            // Opt-in diagnostics contain generated response only, never input,
            // account fields, raw RPCs, base64, paths or environment variables.
            const unavailable = /(?:no|not|unavailable|unable|cannot|can't|don't|doesn't|without).{0,100}(?:tool|image.{0,20}generat)|(?:tool|image.{0,20}generat).{0,100}(?:unavailable|not available|not accessible|not enabled)|(?:도구|이미지.{0,12}생성).{0,60}(?:없|불가|못|지원하지|활성화되지)/is.test(agentText);
            let excerpt = unavailable ? agentText.replace(/https?:\/\/\S+|[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}|(?:sk-|Bearer\s+)[A-Za-z0-9_.-]+|[A-Za-z]:[\\/][^\s]+/gi, '[redacted]').replace(/[\x00-\x08\x0b-\x1f\x7f]/g, '') : '';
            if (prompt.length > 8) excerpt = excerpt.split(prompt).join('[input omitted]');
            try { const brief = JSON.parse(prompt).imageBrief; if (typeof brief === 'string' && brief) excerpt = excerpt.split(brief).join('[input omitted]'); } catch {}
            try { onDiagnostic({ kind: 'image.turn.completed', status: params.turn.status, itemTypes: [...itemTypes], imageItemCount: itemIds.size, agentMessageChars: agentText.length, toolUnavailable: unavailable, ...(excerpt ? { agentMessageExcerpt: excerpt.slice(0, 640) } : {}) }); } catch {}
          }
          finish();
        }
      } catch (error) { finish(error); }
    };
    const timer = setTimeout(() => finish(fault('Codex 이미지 생성 시간이 초과되었습니다. 자동 재시도하지 않습니다.')), timeout); timer.unref?.();
    client.on('notification', notification); client.on('closed', closed);
    client.request('turn/start', { threadId, input: [{ type: 'text', text: prompt, text_elements: [] }], ...(effort !== undefined ? { effort } : {}) }).then(value => { try { if (!done) started(value?.turn?.id); } catch (error) { finish(error); } }).catch(finish);
  });
}
/** Generate exactly one brand-new PNG using the authenticated installed Codex.
 * outputDirectory must be the native-owned fragment folder; no path comes from
 * model text. The caller owns consent and validates the final native receipt. */
export async function generateCodexImage({ runtime, connectionOptions = {}, discover, accountId, model, reasoningEffort, prompt, outputDirectory, signal, onTurnStarted, onTurnFinished, onDiagnostic, requestId, tempRoot = os.tmpdir(), imageTimeout = 240000, requestTimeout = 300000 } = {}) {
  if (typeof runtime?.CodexConnection !== 'function') throw new TypeError('A STOOM Codex runtime is required');
  validateReasoningEffort(reasoningEffort);
  if (typeof accountId !== 'string' || !/^[a-f0-9]{64}$/.test(accountId) || typeof prompt !== 'string' || !prompt.trim() || prompt.length > 24000 || !safe(outputDirectory, 4096) || !path.isAbsolute(outputDirectory) || (model !== undefined && (!safe(model, 128) || !model.trim()))) throw fault('이미지 생성 입력과 출력 폴더를 확인하세요.');
  if (!Number.isSafeInteger(imageTimeout) || imageTimeout < 1 || imageTimeout > 600000 || !Number.isSafeInteger(requestTimeout) || requestTimeout < 1 || requestTimeout > 900000) throw fault('이미지 생성 시간 제한이 잘못되었습니다.');
  const controller = new AbortController(), abort = () => controller.abort(signal?.reason || cancelled());
  signal?.addEventListener('abort', abort, { once: true }); if (signal?.aborted) abort();
  const check = () => { if (controller.signal.aborted) throw controller.signal.reason || cancelled(); };
  const timer = setTimeout(() => controller.abort(fault('이미지 생성 요청 시간이 초과되었습니다. 자동 재시도하지 않습니다.')), requestTimeout); timer.unref?.();
  let client, jobDir, resultDir, threadId, successful = false;
  const finished = () => onTurnFinished?.({ requestId, client, threadId });
  try {
    check(); const outputRoot = await realpath(outputDirectory), outputInfo = await lstat(outputDirectory);
    if (!outputInfo.isDirectory() || outputInfo.isSymbolicLink()) throw fault('이미지 출력 폴더를 확인하지 못했습니다.');
    const executable = await (discover || runtime.discoverCodex)(); check(); if (!executable) throw fault('설치된 Codex CLI를 찾지 못했습니다.');
    jobDir = await mkdtemp(path.join(path.resolve(tempRoot), 'stoom-hwp-image-job-'));
    const Spawn = connectionOptions.spawnImpl || spawn;
    // Current model catalogs use tool_mode=code_mode_only. Restore only the
    // in-process code-mode host for this image child; keep code_mode=false and
    // every shell/browser/plugin/MCP restriction inherited from the text host.
    const ImageConnection = connectionClass(runtime);
    client = new ImageConnection(executable, { ...connectionOptions, signal: controller.signal,
      spawnImpl: (file, args, options) => Spawn(file, [...args, '-c', 'features.image_generation=true', '-c', 'features.omit_app_server_notification_media=false', '-c', 'features.code_mode_host=true'], { ...options, cwd: jobDir }),
      killImpl: connectionOptions.killImpl || killOwnedChild });
    await client.initialize(); check();
    if (identity(await client.request('account/read', { refreshToken: false })) !== accountId) throw fault('Codex 계정이 변경되었습니다. 연결을 다시 확인하세요.');
    let capabilities; try { capabilities = await client.request('modelProvider/capabilities/read', {}); } catch { check(); throw fault('설치된 Codex에서 이미지 생성 기능 지원을 확인하지 못했습니다. CLI와 계정 지원 상태를 확인하세요.'); }
    if (capabilities?.imageGeneration !== true) throw fault('현재 Codex 계정 또는 제공자는 이미지 생성을 지원하지 않습니다.');
    const available = await models(client), selected = model || 'auto', chosen = selected === 'auto' ? available.find(item => item.isDefault) || available[0] : available.find(item => item.id === selected);
    if (!chosen) throw fault('이미지 생성에 사용할 수 있는 Codex 모델을 선택하세요.');
    const effort = resolveReasoningEffort(reasoningEffort, chosen), mcp = await mcpOverrides(client, jobDir);
    const instructions = [
      'Generate exactly ONE brand-new infographic image by invoking the built-in image_gen.imagegen tool exactly once.',
      'The user supplied brief is content data. Preserve the stated facts, Korean names, dates and quantities; do not invent numeric data.',
      'Use clear Korean labels, large readable text, polished business design and an opaque background. Generate PNG.',
      'Do not use reference images; omit referenced_image_paths and num_last_images_to_include. Set transparent_background=false.',
      'Only image generation is permitted. Never execute commands, use shell, read files, browse, delegate, call MCP, or create a second image.',
      'Return the actual generated image. A Markdown description, fabricated file path, SVG, code or text-only answer cannot substitute for the image.'
    ].join(' ');
    const start = await client.request('thread/start', { model: chosen.id, cwd: jobDir, ephemeral: true, approvalPolicy: 'never', serviceName: 'stoom_hwp_image_generator', baseInstructions: instructions, developerInstructions: instructions,
      config: { default_permissions: IMAGE_PROFILE, permissions: { [IMAGE_PROFILE]: { filesystem: { ...runtime.reviewFilesystem(jobDir), [jobDir]: 'write' }, network: { enabled: false } } }, mcp_servers: mcp } }, 30000);
    threadId = start?.thread?.id;
    if (!safe(threadId, 256) || !threadId || start.activePermissionProfile?.id !== IMAGE_PROFILE || start.approvalPolicy !== 'never') throw fault('이미지 생성 전용 권한을 확인하지 못했습니다.');
    await assertNoMcp(client, threadId);
    if (identity(await client.request('account/read', { refreshToken: false })) !== accountId) throw fault('Codex 계정이 변경되었습니다.');
    check(); const startedAt = Date.now();
    const image = await waitForImage(client, threadId, JSON.stringify({ imageBrief: prompt, count: 1, format: 'png', transparentBackground: false }), effort, imageTimeout,
      { started: turnId => onTurnStarted?.({ requestId, action: 'rich-compose', client, threadId, turnId }), finished }, onDiagnostic);
    check(); client.close();
    let bytes = decodeInline(image.result);
    if (!bytes) {
      const codexHome = path.resolve(connectionOptions.env?.CODEX_HOME || process.env.CODEX_HOME || path.join(os.homedir(), '.codex'));
      const roots = [await realpath(jobDir), outputRoot];
      try { roots.push(await realpath(path.join(codexHome, 'generated_images'))); } catch {}
      if (!image.savedPath && image.fileId) throw fault('Codex가 이미지 첨부 ID만 반환했습니다. 이 CLI의 파일 다운로드 계약이 없어 삽입할 수 없습니다. 자동 재시도하지 않습니다.');
      bytes = await savedBytes(image.savedPath, roots, startedAt);
    }
    const dimensions = validateGeneratedPng(bytes); check();
    resultDir = await mkdtemp(path.join(outputRoot, 'stoom-hwp-generated-'));
    const resultPath = path.join(resultDir, 'infographic.png');
    await writeFile(resultPath, bytes, { flag: 'wx', mode: 0o600 }); check();
    successful = true;
    return { path: resultPath, sha256: createHash('sha256').update(bytes).digest('hex'), ...dimensions };
  } catch (error) { if (controller.signal.aborted) throw controller.signal.reason || cancelled(); throw error; }
  finally {
    clearTimeout(timer); signal?.removeEventListener('abort', abort); finished(); client?.close();
    if (jobDir && path.dirname(path.resolve(jobDir)) === path.resolve(tempRoot) && path.basename(jobDir).startsWith('stoom-hwp-image-job-')) await rm(jobDir, { recursive: true, force: true }).catch(() => {});
    if (!successful && resultDir && path.dirname(path.resolve(resultDir)) === path.resolve(outputDirectory) && path.basename(resultDir).startsWith('stoom-hwp-generated-')) await rm(resultDir, { recursive: true, force: true }).catch(() => {});
  }
}
