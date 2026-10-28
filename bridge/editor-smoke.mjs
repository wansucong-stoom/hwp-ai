/** Live Codex dynamic-tool smoke; its editor is an in-memory fixture, never HWP. */
import { readFile } from 'node:fs/promises';
import { loadStoomRuntime } from './hwp-codex-host.mjs';
import { createEditorHandler } from './editor-host.mjs';
import path from 'node:path';
import { spawn } from 'node:child_process';
const file=process.argv[2];
if(!file||!path.isAbsolute(file)||!process.argv.includes('--generate'))throw new Error('Usage: node editor-smoke.mjs <absolute hwp-codex.json> --generate');
const config=JSON.parse((await readFile(file,'utf8')).replace(/^\uFEFF/,''));
const {runtime,source}=await loadStoomRuntime(config.StoomModulePath);
let text='우리 팀은 내일 회의를 진행할 예정입니다.',bold=false,revision=1;
const calls=[];
const diagnostics=[];
const state=()=>({targetId:'fixture-'+revision,capabilities:{rewrite:true,format:true,formatProperties:['bold','fontSizePt','alignment']},text,context:{scope:'singleParagraphSelection',charShape:{bold,fontSizePt:10},paraShape:{alignment:'left'}}});
const handle=createEditorHandler({runtime,connectionOptions:{spawnImpl:(...args)=>{
 const child=spawn(...args);let buffer='';
 child.stdout.on('data',chunk=>{buffer+=String(chunk);for(;;){const at=buffer.indexOf('\n');if(at<0)break;const line=buffer.slice(0,at);buffer=buffer.slice(at+1);try{const m=JSON.parse(line);if(m.method==='item/completed'&&m.params?.item?.type==='agentMessage')diagnostics.push({phase:m.params.item.phase,text:m.params.item.text});if(m.method==='item/started')diagnostics.push({itemType:m.params?.item?.type,tool:m.params?.item?.tool});}catch{}}});
 return child;
}},callEditor:async(requestId,method,args)=>{
 calls.push(method);
 if(method==='hwp_editor_state')return state();
 if(args.expectedTargetId!=='fixture-'+revision)throw new Error('Fixture stale target');
 if(method==='hwp_editor_rewrite')text=args.replacement;
 else if(method==='hwp_editor_format'&&typeof args.patch.bold==='boolean')bold=args.patch.bold;
 else throw new Error('Unsupported fixture operation');
 revision++;
 return {applied:true,changed:true,targetId:'fixture-'+revision,context:state().context};
}});
const status=await handle({id:'fixture-status',action:'status'});
if(!status.ready)throw new Error(status.message);
let result;
try{result=await handle({id:'fixture-edit',action:'edit',accountId:status.accountId,usageConsent:true,model:'auto',prompt:'선택한 본문에서 내일을 모레로 바꾸고, 선택한 글을 진하게 해 주세요. 다른 표현과 숫자는 유지하세요. 도구를 사용해 본문에 직접 적용하세요.'});}
catch(error){console.log(JSON.stringify({passed:false,error:error.message,calls,diagnostics,userDocumentRead:false,hwpInvoked:false}));process.exit(1);}
const passed=text==='우리 팀은 모레 회의를 진행할 예정입니다.'&&bold&&result.appliedCount===2&&calls[0]==='hwp_editor_state';
console.log(JSON.stringify({passed,userDocumentRead:false,hwpInvoked:false,sourceSha256:source.sha256,calls,text,bold,appliedCount:result.appliedCount,model:result.model}));
if(!passed)process.exitCode=1;
