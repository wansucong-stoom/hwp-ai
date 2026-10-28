// Live Codex inference with synthetic table data; never connects to HWP.
import { readFile, writeFile } from 'node:fs/promises';
import { loadStoomRuntime } from './hwp-codex-host.mjs';
import { createEditorHandler } from './editor-host.mjs';
import { spawn } from 'node:child_process';
const configPath = process.argv[2];
if (!configPath || !process.argv.includes('--generate')) throw Error('Supply a build config and --generate');
const config = JSON.parse((await readFile(configPath, 'utf8')).replace(/^\uFEFF/, ''));
const { runtime } = await loadStoomRuntime(config.StoomModulePath);
let finalText='';
const handle = createEditorHandler({ runtime, connectionOptions: { spawnImpl: (...args) => {
 const child=spawn(...args);let buffer='';child.stdout.on('data',chunk=>{buffer+=String(chunk);for(;;){const at=buffer.indexOf('\n');if(at<0)break;const line=buffer.slice(0,at);buffer=buffer.slice(at+1);try{const event=JSON.parse(line);if(event.method==='item/completed'&&event.params?.item?.type==='agentMessage')finalText=event.params.item.text;}catch{}}});return child;
} }, callEditor: async () => { throw Error('Native editor must not be called'); } });
const status = await handle({ id: 'table-status', action: 'status' });
if (!status.ready) throw Error(status.message);
const cell = (id,row,column,text,span=1) => ({id,row,column,rowSpan:1,columnSpan:span,text,style:{borderFill:'example',paragraphs:[],characters:[]}});
const synthetic={referenceTableId:'t0',tables:[
 {id:'t0',rows:2,columns:2,cells:[cell('c0_0',0,0,'항목'),cell('c0_1',0,1,'내용'),cell('c1_0',1,0,'계획'),cell('c1_1',1,1,'작성')]},
 {id:'t1',rows:2,columns:2,cells:[cell('c0_0',0,0,'설명',2),cell('c1_0',1,0,'단계'),cell('c1_1',1,1,'확인')]}
]};
const snapshot=process.argv[3]&&process.argv[3]!=='--generate'?JSON.parse(await readFile(process.argv[3],'utf8')):synthetic;
let result;try{result=await handle({id:'table-design-test',action:'table-design',accountId:status.accountId,usageConsent:true,model:'auto',prompt:'기준 표 디자인을 다른 표에도 맞춰줘.',snapshot});}catch(error){if(snapshot===synthetic)console.error('Synthetic model response:',finalText);throw error;}
const outputIndex=process.argv.indexOf('--plan-output');if(outputIndex>=0)await writeFile(process.argv[outputIndex+1],JSON.stringify(result.plan),'utf8');
console.log(JSON.stringify({passed:true,model:result.model,assignments:result.plan.assignments.length,uncertainties:result.plan.uncertainties.length,hwpInvoked:false}));
