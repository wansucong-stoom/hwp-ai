import fs from 'node:fs/promises';
import path from 'node:path';
import { createHash } from 'node:crypto';
import { inflateSync } from 'node:zlib';

const MAX_IMAGE_BYTES = 8 * 1024 * 1024;
const MAX_PIXELS = 16 * 1024 * 1024;
const TEMPLATE_URL = new URL('./templates/hwpx-fragment/base.json', import.meta.url);
const XML = '<?xml version="1.0" encoding="UTF-8"?>';
const NS = 'xmlns:hp="http://www.hancom.co.kr/hwpml/2011/paragraph" xmlns:hs="http://www.hancom.co.kr/hwpml/2011/section" xmlns:hc="http://www.hancom.co.kr/hwpml/2011/core"';
const digest = bytes => createHash('sha256').update(bytes).digest('hex');
const escapeXml = text => text.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&apos;');
function fail(message) { throw new Error(`HWPX fragment: ${message}`); }
function object(value, allowed, label) {
  if (!value || typeof value !== 'object' || Array.isArray(value) || ![Object.prototype, null].includes(Object.getPrototypeOf(value))) fail(`${label} must be an object`);
  if (Object.keys(value).some(key => !allowed.includes(key))) fail(`${label} has unsupported fields`);
}
function text(value, label, allowEmpty = true) {
  if (typeof value !== 'string' || value.length > 12000 || (!allowEmpty && !value.trim()) || /[\u0000-\u001f\u007f-\u009f\u2028\u2029]/u.test(value)) fail(`${label} must be plain single-paragraph text`);
  // Reject unpaired UTF-16 surrogates, noncharacters and XML-invalid scalars.
  for (const scalar of value) {
    const n = scalar.codePointAt(0);
    if ((n >= 0xd800 && n <= 0xdfff) || (n >= 0xfdd0 && n <= 0xfdef) || (n & 0xffff) === 0xfffe || (n & 0xffff) === 0xffff) fail(`${label} has invalid Unicode`);
  }
  return value;
}
function integer(value, min, max, label) {
  if (!Number.isSafeInteger(value) || value < min || value > max) fail(`${label} is outside allowed range`);
  return value;
}
function samePath(a, b) { return process.platform === 'win32' ? a.toLowerCase() === b.toLowerCase() : a === b; }
function within(root, candidate) {
  const relative = path.relative(root, candidate);
  return relative !== '' && !relative.startsWith(`..${path.sep}`) && relative !== '..' && !path.isAbsolute(relative);
}
async function ownedDirectory(directory) {
  if (typeof directory !== 'string' || !path.isAbsolute(directory) || /^[/\\]{2}/.test(directory)) fail('outputDir must be a local absolute request directory');
  const resolved = path.resolve(directory), real = await fs.realpath(resolved);
  if (!samePath(resolved, real) || !(await fs.lstat(resolved)).isDirectory()) fail('outputDir must be a real directory without symlink ancestors');
  return real;
}

const CRC_TABLE = Uint32Array.from({length: 256}, (_, i) => {
  let c = i;
  for (let n = 0; n < 8; n++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  return c >>> 0;
});
export function crc32(bytes) {
  let crc = 0xffffffff;
  for (const byte of bytes) crc = CRC_TABLE[(crc ^ byte) & 255] ^ (crc >>> 8);
  return (crc ^ 0xffffffff) >>> 0;
}

/** Fully bounds the PNG container and inflates RGB/RGBA scanlines, never opens URLs. */
export function inspectPng(bytes) {
  if (!Buffer.isBuffer(bytes) || bytes.length > MAX_IMAGE_BYTES || bytes.length < 57 || !bytes.subarray(0, 8).equals(Buffer.from([137,80,78,71,13,10,26,10]))) fail('invalid or oversized PNG');
  let offset = 8, width, height, channels, sawHeader = false, sawEnd = false, sawData = false, endedData = false;
  const data = [];
  while (offset < bytes.length) {
    if (offset + 12 > bytes.length) fail('truncated PNG chunk');
    const size = bytes.readUInt32BE(offset), end = offset + size + 12;
    if (end > bytes.length || size > MAX_IMAGE_BYTES) fail('PNG chunk bounds');
    const kindBytes = bytes.subarray(offset + 4, offset + 8), kind = kindBytes.toString('ascii');
    if (!/^[A-Za-z]{4}$/.test(kind) || crc32(bytes.subarray(offset + 4, end - 4)) !== bytes.readUInt32BE(end - 4)) fail('PNG chunk CRC/type');
    const chunk = bytes.subarray(offset + 8, end - 4);
    if (!sawHeader && kind !== 'IHDR') fail('PNG IHDR must be first');
    if (kind === 'IHDR') {
      if (sawHeader || size !== 13) fail('duplicate/invalid PNG IHDR');
      sawHeader = true; width = chunk.readUInt32BE(0); height = chunk.readUInt32BE(4);
      if (!width || !height || width * height > MAX_PIXELS) fail('PNG pixel limit');
      if (chunk[8] !== 8 || ![2, 6].includes(chunk[9]) || chunk[10] || chunk[11] || chunk[12]) fail('only non-interlaced 8-bit RGB/RGBA PNG is supported');
      channels = chunk[9] === 6 ? 4 : 3;
    } else if (kind === 'IDAT') {
      if (endedData) fail('non-consecutive PNG IDAT');
      sawData = true; data.push(chunk);
    } else {
      if (sawData) endedData = true;
      if (['acTL', 'fcTL', 'fdAT'].includes(kind)) fail('animated PNG is unsupported');
      if (kind === 'IEND') { if (size || !sawData) fail('invalid PNG end'); sawEnd = true; offset = end; break; }
      if (kind[0] === kind[0].toUpperCase() && kind !== 'PLTE') fail('unknown PNG critical chunk');
      if (kind === 'PLTE' && (sawData || !size || size % 3 || size > 768)) fail('invalid PNG palette');
    }
    offset = end;
  }
  if (!sawEnd || offset !== bytes.length) fail('PNG missing end or trailing data');
  const stride = width * channels, expectedBytes = (stride + 1) * height;
  let raster;
  try { raster = inflateSync(Buffer.concat(data), {maxOutputLength: expectedBytes}); }
  catch { fail('invalid PNG compressed raster'); }
  if (raster.length !== expectedBytes) fail('PNG raster length mismatch');
  for (let y = 0; y < height; y++) if (raster[y * (stride + 1)] > 4) fail('invalid PNG scanline filter');
  return {width, height, sha256: digest(bytes)};
}

export function validateFragmentBlocks(blocks) {
  if (!Array.isArray(blocks) || blocks.length < 1 || blocks.length > 64) fail('blocks must contain 1..64 items');
  let textLength = 0, tableCount = 0, imageCount = 0;
  const countText = (s, label, empty = true) => { const t = text(s, label, empty); textLength += t.length; if (textLength > 24000) fail('total text exceeds 24000'); return t; };
  return blocks.map((block, index) => {
    if (!block || typeof block !== 'object') fail(`block ${index} is invalid`);
    if (block.type === 'paragraph') {
      object(block, ['type', 'role', 'text'], 'paragraph');
      if (!['title', 'heading', 'body', 'bullet'].includes(block.role)) fail('unsupported paragraph role');
      const value = countText(block.text, 'paragraph text');
      if (block.role === 'bullet') { textLength += 2; if (textLength > 24000) fail('total text exceeds 24000'); }
      return {type: 'paragraph', role: block.role, text: value};
    }
    if (block.type === 'table') {
      object(block, ['type', 'headers', 'rows'], 'table');
      if (++tableCount > 3 || !Array.isArray(block.headers) || block.headers.length < 1 || block.headers.length > 8 || !Array.isArray(block.rows) || block.rows.length < 1 || block.rows.length > 20) fail('table size/count limit');
      const headers = block.headers.map(s => countText(s, 'table header'));
      const rows = block.rows.map(row => {
        if (!Array.isArray(row) || row.length !== headers.length) fail('table must be rectangular');
        return row.map(s => countText(s, 'table cell'));
      });
      return {type: 'table', headers, rows};
    }
    if (block.type === 'image') {
      object(block, ['type', 'path', 'sha256', 'width', 'height', 'caption'], 'image');
      if (++imageCount > 2 || typeof block.path !== 'string' || !path.isAbsolute(block.path) || /^[/\\]{2}/.test(block.path) || !/\.png$/i.test(block.path) || typeof block.sha256 !== 'string' || !/^[0-9a-f]{64}$/i.test(block.sha256)) fail('invalid image receipt');
      const width = integer(block.width, 1, MAX_PIXELS, 'image pixel width'), height = integer(block.height, 1, MAX_PIXELS, 'image pixel height');
      if (width * height > MAX_PIXELS) fail('PNG pixel limit');
      return {type: 'image', path: block.path, sha256: block.sha256.toLowerCase(), width, height, caption: countText(block.caption ?? '', 'image caption')};
    }
    fail('unsupported block type (infographic must first resolve to a validated local image)');
  });
}

function storedZip(entries) {
  const locals = [], central = []; let offset = 0;
  for (const [name, value] of entries) {
    if (!/^[A-Za-z0-9_./-]+$/.test(name) || name.includes('..') || name.startsWith('/')) fail('invalid ZIP part name');
    const bytes = Buffer.isBuffer(value) ? value : Buffer.from(value, 'utf8'), filename = Buffer.from(name), crc = crc32(bytes);
    const local = Buffer.alloc(30); local.writeUInt32LE(0x04034b50, 0); local.writeUInt16LE(20, 4); local.writeUInt16LE(0x800, 6); local.writeUInt16LE(0x21, 12);
    local.writeUInt32LE(crc, 14); local.writeUInt32LE(bytes.length, 18); local.writeUInt32LE(bytes.length, 22); local.writeUInt16LE(filename.length, 26);
    locals.push(local, filename, bytes);
    const item = Buffer.alloc(46); item.writeUInt32LE(0x02014b50, 0); item.writeUInt16LE(20, 4); item.writeUInt16LE(20, 6); item.writeUInt16LE(0x800, 8); item.writeUInt16LE(0x21, 14);
    item.writeUInt32LE(crc, 16); item.writeUInt32LE(bytes.length, 20); item.writeUInt32LE(bytes.length, 24); item.writeUInt16LE(filename.length, 28); item.writeUInt32LE(offset, 42);
    central.push(item, filename); offset += local.length + filename.length + bytes.length;
  }
  const directory = Buffer.concat(central), end = Buffer.alloc(22); end.writeUInt32LE(0x06054b50, 0); end.writeUInt16LE(entries.length, 8); end.writeUInt16LE(entries.length, 10); end.writeUInt32LE(directory.length, 12); end.writeUInt32LE(offset, 16);
  return Buffer.concat([...locals, directory, end]);
}
function validateMemoDefinition(value) {
  if (value === null || value === undefined) return null;
  object(value, ['id','width','lineColor','fillColor','activeColor'], 'memoDefinition');
  if (value.id !== 1 || !Number.isInteger(value.width) || value.width < 1 || value.width > 100000 || !['lineColor','fillColor','activeColor'].every(k => typeof value[k] === 'string' && /^#[0-9A-F]{6}$/.test(value[k]))) fail('unsupported original memo definition');
  return value;
}
function headerXml(template,memo) {
  let header = template['Contents/header.xml'];
  const base = header.match(/<hh:charPr id="0"[\s\S]*?<\/hh:charPr>/)?.[0];
  if (!base) fail('cached header is invalid');
  const shapes = [[7,2000,'#183153'],[8,1400,'#215A85'],[9,900,'#555555'],[10,1000,'#FFFFFF']].map(([id,height,color]) => base.replace('id="0"', `id="${id}"`).replace(/height="\d+"/, `height="${height}"`).replace('textColor="#000000"', `textColor="${color}"`).replace('<hh:underline ', (id === 9 ? '' : '<hh:bold/>') + '<hh:underline ')).join('');
  if (!header.includes('<hh:charProperties itemCnt="7">') || !header.includes('<hh:borderFills itemCnt="2">')) fail('unexpected cached Hancom-style header');
  header = header.replace('<hh:charProperties itemCnt="7">', '<hh:charProperties itemCnt="11">').replace('</hh:charProperties>', shapes + '</hh:charProperties>');
  const border = (id, fill) => `<hh:borderFill id="${id}" threeD="0" shadow="0" centerLine="NONE" breakCellSeparateLine="0"><hh:slash type="NONE" Crooked="0" isCounter="0"/><hh:backSlash type="NONE" Crooked="0" isCounter="0"/>${['left','right','top','bottom'].map(side => `<hh:${side}Border type="SOLID" width="0.12 mm" color="#B7C9D9"/>`).join('')}<hh:diagonal type="NONE" width="0.1 mm" color="#B7C9D9"/><hc:fillBrush><hc:winBrush faceColor="${fill}" hatchColor="#FFFFFF" alpha="0"/></hc:fillBrush></hh:borderFill>`;
  header = header.replace('<hh:borderFills itemCnt="2">','<hh:borderFills itemCnt="4">').replace('</hh:borderFills>', border(3,'#215A85') + border(4,'#F2F6FA') + '</hh:borderFills>');
  if(memo){
    if(header.includes('<hh:memoProperties')||!header.includes('</hh:refList>'))fail('cached memo header is invalid');
    header=header.replace('</hh:refList>',`<hh:memoProperties itemCnt="1"><hh:memoPr id="1" width="${memo.width}" lineWidth="1" lineType="SOLID" lineColor="${memo.lineColor}" fillColor="${memo.fillColor}" activeColor="${memo.activeColor}" memoType="NOMAL"/></hh:memoProperties></hh:refList>`);
  }
  return header;
}

/** Build a new insertion fragment. This function never opens or modifies a user document. */
export async function buildHwpxFragment({blocks, outputDir, fragmentDirectory, contentWidthHwpUnit = 42520, memoDefinition = null} = {}) {
  if (outputDir && fragmentDirectory && outputDir !== fragmentDirectory) fail('conflicting output directories');
  const validated = validateFragmentBlocks(blocks), root = await ownedDirectory(outputDir || fragmentDirectory);
  const width = integer(contentWidthHwpUnit, 7200, 200000, 'contentWidthHwpUnit');
  const template = JSON.parse(await fs.readFile(TEMPLATE_URL, 'utf8')), memo = validateMemoDefinition(memoDefinition);
  const entries = [['mimetype', 'application/hwp+zip'], ['version.xml',template['version.xml']], ['META-INF/container.xml',template['META-INF/container.xml']], ['META-INF/container.rdf',template['META-INF/container.rdf']], ['META-INF/manifest.xml',template['META-INF/manifest.xml']], ['settings.xml',template['settings.xml']], ['Contents/header.xml',headerXml(template,memo)]];
  const expected = {paragraphTexts: [], tables: [], images: []}, paragraphs = [], imageManifest = [];
  let nextId = 1000000000, first = true;
  const sectionProperties = template.sectionProperties.replace(/(<hp:pagePr\b[^>]*\bwidth=")\d+"/, `$1${width + 17008}"`).replace('memoShapeIDRef="0"',memo?'memoShapeIDRef="1"':'memoShapeIDRef="0"') + template.columnProperties;
  const paragraph = (value, charPr = 0, content = '', body = true) => {
    const section = body && first ? sectionProperties : ''; if (body) first = false;
    const xml = `<hp:p id="${nextId++}" paraPrIDRef="0" styleIDRef="0" pageBreak="0" columnBreak="0" merged="0"><hp:run charPrIDRef="${charPr}">${section}${content}<hp:t>${escapeXml(value)}</hp:t></hp:run></hp:p>`;
    if (body) { paragraphs.push(xml); expected.paragraphTexts.push(value); }
    return xml;
  };
  const pos = affect => `<hp:pos treatAsChar="1" affectLSpacing="${affect}" flowWithText="1" allowOverlap="0" holdAnchorAndSO="0" vertRelTo="PARA" horzRelTo="COLUMN" vertAlign="TOP" horzAlign="LEFT" vertOffset="0" horzOffset="0"/>`;
  const margins = '<hp:outMargin left="0" right="0" top="0" bottom="0"/>';
  let imageCount = 0;
  for (const block of validated) {
    if (block.type === 'paragraph') {
      paragraph(block.role === 'bullet' ? `• ${block.text}` : block.text, block.role === 'title' ? 7 : block.role === 'heading' ? 8 : 0);
    } else if (block.type === 'table') {
      const cells = [block.headers, ...block.rows], columns = block.headers.length, widths = Array.from({length: columns}, (_, i) => Math.floor(width / columns) + (i < width % columns ? 1 : 0));
      const rowHeight = row => Math.max(2400, ...row.map((s, c) => (Math.ceil([...s].reduce((sum,ch)=>sum+(ch.codePointAt(0)>255?1:0.55),0) / Math.max(1, (widths[c]-840)/1100)) || 1) * 1485 + 800));
      const heights = cells.map(rowHeight), height = heights.reduce((a,b)=>a+b,0), id = nextId++;
      const rows = cells.map((row, r) => '<hp:tr>' + row.map((s,c) => `<hp:tc name="" header="${r===0?1:0}" hasMargin="1" protect="0" editable="0" dirty="1" borderFillIDRef="${r===0?3:r%2?2:4}"><hp:subList id="" textDirection="HORIZONTAL" lineWrap="BREAK" vertAlign="CENTER" linkListIDRef="0" linkListNextIDRef="0" textWidth="0" textHeight="0" hasTextRef="0" hasNumRef="0">${paragraph(s,r===0?10:0,'',false)}</hp:subList><hp:cellAddr colAddr="${c}" rowAddr="${r}"/><hp:cellSpan colSpan="1" rowSpan="1"/><hp:cellSz width="${widths[c]}" height="${heights[r]}"/><hp:cellMargin left="420" right="420" top="400" bottom="400"/></hp:tc>`).join('') + '</hp:tr>').join('');
      paragraph('', 0, `<hp:tbl id="${id}" zOrder="0" numberingType="TABLE" textWrap="TOP_AND_BOTTOM" textFlow="BOTH_SIDES" lock="0" dropcapstyle="None" pageBreak="CELL" repeatHeader="1" rowCnt="${cells.length}" colCnt="${columns}" cellSpacing="0" borderFillIDRef="1" noAdjust="0"><hp:sz width="${width}" widthRelTo="ABSOLUTE" height="${height}" heightRelTo="ABSOLUTE" protect="0"/>${pos(0)}${margins}<hp:inMargin left="0" right="0" top="0" bottom="0"/>${rows}</hp:tbl>`);
      expected.tables.push({rows: cells.length, columns, cells});
    } else {
      const source = path.resolve(block.path), relative = path.relative(root,source);
      if (!within(root,source) || relative.split(path.sep).some(s=>s.includes(':')) || !samePath(source,await fs.realpath(source))) fail('image must be inside owned outputDir without symlinks');
      const stat = await fs.lstat(source); if (!stat.isFile() || stat.size > MAX_IMAGE_BYTES) fail('image must be a bounded regular PNG file');
      const bytes = await fs.readFile(source), info = inspectPng(bytes);
      if (info.sha256 !== block.sha256 || info.width !== block.width || info.height !== block.height) fail('image bytes do not match verified receipt');
      const scale = Math.min(1, width / (info.width * 75), 36000 / (info.height * 75));
      const w = Math.max(1, Math.round(info.width * 75 * scale)), h = Math.max(1, Math.round(info.height * 75 * scale));
      const imageId = `image${++imageCount}`, id = nextId++;
      const matrix = '<hc:transMatrix e1="1" e2="0" e3="0" e4="0" e5="1" e6="0"/><hc:scaMatrix e1="1" e2="0" e3="0" e4="0" e5="1" e6="0"/><hc:rotMatrix e1="1" e2="0" e3="0" e4="0" e5="1" e6="0"/>';
      const image = `<hp:pic id="${id}" instid="${id}" zOrder="0" numberingType="PICTURE" textWrap="SQUARE" textFlow="BOTH_SIDES" lock="0" dropcapstyle="None" href="" groupLevel="0" reverse="0"><hp:offset x="0" y="0"/><hp:orgSz width="${w}" height="${h}"/><hp:curSz width="${w}" height="${h}"/><hp:flip horizontal="0" vertical="0"/><hp:rotationInfo angle="0" centerX="${Math.floor(w/2)}" centerY="${Math.floor(h/2)}" rotateimage="1"/><hp:renderingInfo>${matrix}</hp:renderingInfo><hp:imgRect><hc:pt0 x="0" y="0"/><hc:pt1 x="${w}" y="0"/><hc:pt2 x="${w}" y="${h}"/><hc:pt3 x="0" y="${h}"/></hp:imgRect><hp:imgClip left="0" right="${w}" top="0" bottom="${h}"/><hp:inMargin left="0" right="0" top="0" bottom="0"/><hp:imgDim dimwidth="${w}" dimheight="${h}"/><hc:img binaryItemIDRef="${imageId}" bright="0" contrast="0" effect="REAL_PIC" alpha="0"/><hp:effects/><hp:sz width="${w}" widthRelTo="ABSOLUTE" height="${h}" heightRelTo="ABSOLUTE" protect="0"/>${pos(1)}${margins}<hp:shapeComment/></hp:pic>`;
      paragraph('', 0, image); if (block.caption) paragraph(block.caption,9);
      entries.push([`BinData/${imageId}.png`, bytes]); expected.images.push({sha256: info.sha256, width:w, height:h});
      imageManifest.push({id:imageId,sha256:info.sha256,pixelWidth:info.width,pixelHeight:info.height,widthHwpUnit:w,heightHwpUnit:h});
    }
  }
  paragraph(''); // Explicit final empty paragraph prevents trailing object anchors merging into existing body text.
  entries.push(['Contents/section0.xml', `${XML}<hs:sec ${NS}>${paragraphs.join('')}</hs:sec>`]);
  const imageItems = imageManifest.map(i=>`<opf:item id="${i.id}" href="BinData/${i.id}.png" media-type="image/png" isEmbeded="1"/>`).join('');
  if (!template['Contents/content.hpf'].includes('</opf:manifest>')) fail('cached package manifest is invalid');
  entries.push(['Contents/content.hpf',template['Contents/content.hpf'].replace('</opf:manifest>',imageItems+'</opf:manifest>')]);
  entries.push(['Preview/PrvText.txt', expected.paragraphTexts.join('\r\n')]);
  const bytes = storedZip(entries), fragmentPath = path.join(root,'fragment.hwpx');
  await fs.writeFile(fragmentPath, bytes, {flag:'wx'});
  return {fragmentPath, fragmentSha256:digest(bytes), expected};
}


