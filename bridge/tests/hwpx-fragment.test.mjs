import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { deflateSync } from 'node:zlib';
import { buildHwpxFragment, validateFragmentBlocks, inspectPng, crc32 } from '../hwpx-fragment.mjs';

const root = fileURLToPath(new URL('../../investigation/', import.meta.url));
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
async function directory(t) {
  await fs.mkdir(root,{recursive:true}); const d = await fs.mkdtemp(path.join(root,'fragment-test-'));
  t.after(async()=>{ assert.ok(d.startsWith(root)); await fs.rm(d,{recursive:true,force:true}); }); return d;
}
function chunk(type,bytes=Buffer.alloc(0)) {
  const kind=Buffer.from(type), head=Buffer.alloc(4), tail=Buffer.alloc(4); head.writeUInt32BE(bytes.length); tail.writeUInt32BE(crc32(Buffer.concat([kind,bytes])));
  return Buffer.concat([head,kind,bytes,tail]);
}
function png(width=2,height=2,{filter=0,rasterBytes,ihdrWidth=width}={}) {
  const h=Buffer.alloc(13); h.writeUInt32BE(ihdrWidth);h.writeUInt32BE(height,4);h[8]=8;h[9]=6;
  const raster=rasterBytes || Buffer.alloc((width*4+1)*height,255); if(!rasterBytes) for(let y=0;y<height;y++)raster[y*(width*4+1)]=filter;
  return Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]),chunk('IHDR',h),chunk('IDAT',deflateSync(raster)),chunk('IEND')]);
}
async function asset(dir,bytes=png()) { const filename=path.join(dir,'asset.png'); await fs.writeFile(filename,bytes); const info=inspectPng(bytes); return {type:'image',path:filename,...info,caption:'그림 설명'}; }
const paragraph=(text='본문',role='body')=>({type:'paragraph',role,text});
// Independent ZIP reader checks stored local and central directory metadata, names, CRCs and first mimetype.
function unzip(bytes) {
  const end=bytes.length-22;assert.equal(bytes.readUInt32LE(end),0x06054b50); const count=bytes.readUInt16LE(end+10), entries=new Map();let offset=0;
  while(bytes.readUInt32LE(offset)===0x04034b50){assert.equal(bytes.readUInt16LE(offset+8),0);const size=bytes.readUInt32LE(offset+18),nameLength=bytes.readUInt16LE(offset+26),extra=bytes.readUInt16LE(offset+28),name=bytes.subarray(offset+30,offset+30+nameLength).toString(),start=offset+30+nameLength+extra,data=bytes.subarray(start,start+size);assert.equal(crc32(data),bytes.readUInt32LE(offset+14));assert.ok(!entries.has(name));entries.set(name,data);offset=start+size;}
  assert.equal(offset,bytes.readUInt32LE(end+16));assert.equal(entries.size,count);assert.equal([...entries.keys()][0],'mimetype');assert.equal(entries.get('mimetype').toString(),'application/hwp+zip');return entries;
}
async function readReceipt(receipt){const bytes=await fs.readFile(receipt.fragmentPath);assert.equal(sha(bytes),receipt.fragmentSha256);return unzip(bytes);}

test('Hancom-style package scaffolding retains version, metadata and complete section settings',async t=>{
 const d=await directory(t),r=await buildHwpxFragment({outputDir:d,contentWidthHwpUnit:30001,blocks:[paragraph('제목','title')]});const z=await readReceipt(r);
 const version=z.get('version.xml').toString(),header=z.get('Contents/header.xml').toString(),section=z.get('Contents/section0.xml').toString(),manifest=z.get('Contents/content.hpf').toString();
 assert.match(version,/xmlns:hv="http:\/\/www\.hancom\.co\.kr\/hwpml\/2011\/version"/);assert.match(version,/tagetApplication="WORDPROCESSOR"/);
 for(const name of ['META-INF/container.rdf','META-INF/manifest.xml','settings.xml'])assert.ok(z.has(name));
 assert.match(manifest,/id="settings" href="settings\.xml"/);assert.match(header,/<hh:charProperties itemCnt="11">/);assert.match(header,/<hh:borderFills itemCnt="4">/);
 for(const name of ['grid','startNum','visibility','lineNumberShape','footNotePr','endNotePr','pageBorderFill'])assert.match(section,new RegExp('<hp:'+name+'\\b'));
 assert.match(section,/<hp:pagePr\b[^>]*width="47009"/);assert.match(section,/<hp:run charPrIDRef="7">/);
});
test('a supported native memo definition is preserved in the header and section',async t=>{
 const d=await directory(t),memo={id:1,width:15591,lineColor:'#B6D7AE',fillColor:'#F0FFE9',activeColor:'#CFF1C7'};
 const r=await buildHwpxFragment({outputDir:d,memoDefinition:memo,blocks:[paragraph('본문')]});const z=await readReceipt(r),h=z.get('Contents/header.xml').toString(),s=z.get('Contents/section0.xml').toString();
 assert.match(h,/<hh:memoProperties itemCnt="1">/);assert.match(h,/id="1" width="15591" lineWidth="1" lineType="SOLID" lineColor="#B6D7AE" fillColor="#F0FFE9" activeColor="#CFF1C7" memoType="NOMAL"/);assert.match(s,/memoShapeIDRef="1"/);
 const invalid=await directory(t);await assert.rejects(buildHwpxFragment({outputDir:invalid,memoDefinition:{...memo,fillColor:'<unsafe>'},blocks:[paragraph()]}),/memo/);await assert.rejects(fs.stat(path.join(invalid,'fragment.hwpx')),{code:'ENOENT'});
});

test('paragraph content, Unicode, escaping and deterministic terminal paragraph',async t=>{
 const d=await directory(t),r=await buildHwpxFragment({outputDir:d,blocks:[paragraph('제목','title'),paragraph('A<&"\'😀'),paragraph('첫째','bullet')]});const z=await readReceipt(r),s=z.get('Contents/section0.xml').toString();
 assert.deepEqual(r.expected.paragraphTexts,['제목','A<&"\'😀','• 첫째','']);assert.match(s,/A&lt;&amp;&quot;&apos;😀/);assert.equal((s.match(/<hp:secPr /g)||[]).length,1);assert.doesNotMatch(s,/linesegarray|DOCTYPE|<hp:tbl/);
});
test('table is native editable rectangular cells with header and empty-cell preservation',async t=>{
 const d=await directory(t),cells=[['구분','내용'],['A',''],['B','<확인>']],r=await buildHwpxFragment({outputDir:d,contentWidthHwpUnit:30001,blocks:[{type:'table',headers:cells[0],rows:cells.slice(1)}]});const s=(await readReceipt(r)).get('Contents/section0.xml').toString();
 assert.deepEqual(r.expected.tables,[{rows:3,columns:2,cells}]);assert.deepEqual(r.expected.paragraphTexts,['','']);assert.equal((s.match(/<hp:tc /g)||[]).length,6);assert.match(s,/rowCnt="3" colCnt="2"/);assert.match(s,/affectLSpacing="0"/);assert.match(s,/cellSz width="15001"/);assert.match(s,/cellSz width="15000"/);const ids=[...s.matchAll(/<hp:p id="(\d+)"/g)].map(m=>m[1]);assert.equal(new Set(ids).size,ids.length);
});
test('PNG is embedded byte-for-byte, linked in manifest, aspect fitted and caption ordered',async t=>{
 const d=await directory(t),image=await asset(d,png(8,4)),r=await buildHwpxFragment({outputDir:d,contentWidthHwpUnit:7200,blocks:[paragraph('앞'),image,paragraph('뒤')]});const z=await readReceipt(r),s=z.get('Contents/section0.xml').toString();
 assert.equal(sha(z.get('BinData/image1.png')),image.sha256);assert.match(z.get('Contents/content.hpf').toString(),/id="image1" href="BinData\/image1.png" media-type="image\/png" isEmbeded="1"/);assert.match(s,/binaryItemIDRef="image1"/);assert.deepEqual(r.expected.paragraphTexts,['앞','','그림 설명','뒤','']);assert.deepEqual(r.expected.images,[{sha256:image.sha256,width:600,height:300}]);assert.match(s,/affectLSpacing="1"/);
});
test('large image display size fits body width without distorting aspect ratio',async t=>{
 const d=await directory(t),image=await asset(d,png(1000,500)),r=await buildHwpxFragment({outputDir:d,contentWidthHwpUnit:30000,blocks:[image]});assert.deepEqual(r.expected.images,[{sha256:image.sha256,width:30000,height:15000}]);
});
test('PNG decoder rejects bad CRC, truncation, trailing data and unsupported filters',()=>{
 const bad=Buffer.from(png());bad[bad.length-1]^=1;assert.throws(()=>inspectPng(bad),/CRC/);assert.throws(()=>inspectPng(png().subarray(0,-2)),/truncated|bounds/);assert.throws(()=>inspectPng(Buffer.concat([png(),Buffer.from('x')])),/trailing/);assert.throws(()=>inspectPng(png(2,2,{filter:5})),/filter/);
});
test('PNG bounded decoder rejects oversized geometry and compressed raster length/bombs',()=>{
 assert.throws(()=>inspectPng(png(2,2,{ihdrWidth:20000000})),/pixel/);assert.throws(()=>inspectPng(png(2,2,{rasterBytes:Buffer.alloc(100000)})),/raster/);assert.throws(()=>inspectPng(png(2,2,{rasterBytes:Buffer.alloc(2)})),/raster/);
});
test('image hash or dimension changes fail before fragment is written',async t=>{
 const d=await directory(t),image=await asset(d);await assert.rejects(buildHwpxFragment({outputDir:d,blocks:[{...image,sha256:'0'.repeat(64)}]}),/receipt/);await assert.rejects(buildHwpxFragment({outputDir:d,blocks:[{...image,width:3}]}),/receipt/);await assert.rejects(fs.stat(path.join(d,'fragment.hwpx')),{code:'ENOENT'});
});
test('external file and file URL cannot cross request asset boundary',async t=>{
 const d=await directory(t),outside=await directory(t),image=await asset(outside);await assert.rejects(buildHwpxFragment({outputDir:d,blocks:[image]}),/owned outputDir/);assert.throws(()=>validateFragmentBlocks([{...image,path:'https://example.com/image.png'}]),/receipt/);
});
test('unknown XML, path, role and unresolved infographic fields are rejected',()=>{
 assert.throws(()=>validateFragmentBlocks([{...paragraph(),xml:'<raw/>'}]),/unsupported/);assert.throws(()=>validateFragmentBlocks([paragraph('x','script')]),/role/);assert.throws(()=>validateFragmentBlocks([{type:'infographic',prompt:'draw'}]),/unsupported/);assert.doesNotThrow(()=>validateFragmentBlocks([paragraph('<!DOCTYPE')])); // Plain text is escaped by the builder.
});
test('XML-like plain text remains escaped data',async t=>{
 const d=await directory(t),r=await buildHwpxFragment({outputDir:d,blocks:[paragraph('<!DOCTYPE a [<!ENTITY x SYSTEM "file:///secret">]>')]}),s=(await readReceipt(r)).get('Contents/section0.xml').toString();assert.doesNotMatch(s,/<!DOCTYPE/);assert.match(s,/&lt;!DOCTYPE/);
});
test('invalid scalar/control text and cell paragraph breaks are rejected',()=>{
 for(const bad of ['\u0000','a\nb','\t','\ud800','\ufffe']) assert.throws(()=>validateFragmentBlocks([paragraph(bad)]),/text|Unicode/);assert.doesNotThrow(()=>validateFragmentBlocks([paragraph('한글😀&')]));assert.throws(()=>validateFragmentBlocks([{type:'table',headers:['A'],rows:[['a\nb']]}]),/single-paragraph/);
});
test('total text, blocks, table counts and row dimensions are bounded',()=>{
 assert.throws(()=>validateFragmentBlocks([paragraph('x'.repeat(12000)),paragraph('y'.repeat(12000)),paragraph('z')]),/24000/);assert.throws(()=>validateFragmentBlocks(Array.from({length:65},()=>paragraph())),/64/);const table={type:'table',headers:['A'],rows:[['']]};assert.throws(()=>validateFragmentBlocks([table,table,table,table]),/limit/);assert.throws(()=>validateFragmentBlocks([{...table,rows:[['','']]}]),/rectangular/);assert.throws(()=>validateFragmentBlocks([{...table,rows:Array.from({length:21},()=>[''])}]),/limit/);
});
test('table upper bounds remain valid and images are limited to two',()=>{
 assert.doesNotThrow(()=>validateFragmentBlocks([{type:'table',headers:Array(8).fill(''),rows:Array.from({length:20},()=>Array(8).fill(''))}]));const image={type:'image',path:path.resolve(root,'asset.png'),sha256:'1'.repeat(64),width:1,height:1,caption:''};assert.throws(()=>validateFragmentBlocks([image,image,image]),/receipt/);
});
test('existing fragment is never overwritten',async t=>{
 const d=await directory(t),file=path.join(d,'fragment.hwpx');await fs.writeFile(file,'keep');await assert.rejects(buildHwpxFragment({outputDir:d,blocks:[paragraph()]}),{code:'EEXIST'});assert.equal(await fs.readFile(file,'utf8'),'keep');
});
test('host geometry must be a bounded integer and directories must be real',async t=>{
 const d=await directory(t);await assert.rejects(buildHwpxFragment({outputDir:d,contentWidthHwpUnit:0,blocks:[paragraph()]}),/contentWidth/);await assert.rejects(buildHwpxFragment({outputDir:'relative',blocks:[paragraph()]}),/absolute/);await assert.rejects(buildHwpxFragment({outputDir:d,fragmentDirectory:d+'other',blocks:[paragraph()]}),/conflicting/);
});


