import assert from 'node:assert/strict';
import {EventEmitter} from 'node:events';
import {mkdtemp,mkdir,rm} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import path from 'node:path';
import os from 'node:os';
import {createRichComposeHandler} from '../editor-host.mjs';
import {validateRichPlan,richRequirements} from '../rich-plan.mjs';
const root=await mkdtemp(path.join(os.tmpdir(),'stoom-rich-policy-'));
const accountId=createHash('sha256').update('stoom-hwp-account-v1\0fixture@unit.invalid').digest('hex');
const paragraph=text=>({type:'paragraph',role:'title',text,headers:null,rows:null,imagePrompt:null,caption:null});
const table=()=>({type:'table',role:null,text:null,headers:['단계','내용'],rows:[['요청','본문 편집']],imagePrompt:null,caption:null});
const image=()=>({type:'infographic',role:null,text:null,headers:null,rows:null,imagePrompt:'요청에서 본문 반영으로 이어지는 인포그래픽',caption:'문서 작업 흐름'});
const plan=()=>({summary:'표와 인포그래픽으로 구성합니다.',blocks:[paragraph('합성 보고서'),table(),image()]});
let scenarios=0;
async function fixture(settings={}){
 const calls=[],rpc=[],directory=path.join(root,String(++scenarios));await mkdir(directory);
 class Connection extends EventEmitter{
  constructor(){super();this.closed=false;}
  write(){}
  close(error){if(this.closed)return;this.closed=true;this.emit('closed',error);}
  async request(method,params){
   rpc.push({method,params});
   if(method==='initialize')return{};
   if(method==='account/read')return{account:{type:'chatgpt',email:'fixture@unit.invalid'}};
   if(method==='model/list')return{data:[{id:'fixture-model',isDefault:true,supportedReasoningEfforts:[{reasoningEffort:'low'}]}]};
   if(method==='config/read')return{config:{mcp_servers:{}}};
   if(method==='mcpServerStatus/list')return{data:[]};
   if(method==='thread/start')return{thread:{id:'fixture-thread'},activePermissionProfile:{id:params.config.default_permissions},approvalPolicy:'never'};
   if(method==='turn/start'){
    queueMicrotask(()=>{
     this.emit('notification',{method:'item/completed',params:{threadId:'fixture-thread',item:{type:'agentMessage',id:'answer',phase:'final_answer',text:JSON.stringify(settings.plan||plan())}}});
     this.emit('notification',{method:'turn/completed',params:{threadId:'fixture-thread',turn:{id:'fixture-turn',status:'completed'}}});
    });return{turn:{id:'fixture-turn'}};
   }
   throw Error(method);
  }
 }
 const runtime={CodexConnection:Connection,discoverCodex:async()=>'never-executed.exe',reviewFilesystem:cwd=>({[cwd]:'read'})};
 const callEditor=async(id,method,args)=>{
  calls.push(method);
  if(method==='hwp_editor_rich_state')return{targetId:'fixed-target',text:settings.selectedSource,capabilities:{compose:!settings.noCapability,tables:true,images:true},context:{scope:settings.selectedSource!==undefined?'wholeBodyParagraphSelection':'emptyBodyParagraph',selectionPresent:settings.selectedSource!==undefined,fragmentDirectory:directory,contentWidthHwpUnit:42000}};
  if(method==='hwp_editor_rich_progress')return{accepted:true};
  if(method==='hwp_editor_rich_apply'){
   assert.equal(args.expectedTargetId,'fixed-target');
   if(settings.stale)throw Object.assign(Error('문서가 바뀌었습니다.'),{stoomSafe:true});
   return{applied:!settings.unconfirmed,changed:true,targetId:'fixed-target',paragraphCount:args.expected.paragraphTexts.length,tableCount:args.expected.tables.length,imageCount:args.expected.images.length};
  }
  throw Error(method);
 };
 const generateImage=async options=>{calls.push('generateImage');assert.equal(options.outputDirectory,directory);if(settings.imageFails)throw Object.assign(Error('합성 이미지 실패'),{stoomSafe:true});return{path:path.join(directory,'image.png'),sha256:'a'.repeat(64),width:1200,height:800};};
 const buildFragment=async options=>{calls.push('buildFragment');assert.equal(options.outputDir,directory);assert.equal(options.contentWidthHwpUnit,42000);assert.equal(options.blocks.some(block=>block.type==='infographic'),false);return{fragmentPath:path.join(directory,'fragment.hwpx'),fragmentSha256:'b'.repeat(64),expected:{paragraphTexts:options.blocks.filter(block=>block.type==='paragraph').map(block=>block.text),tables:options.blocks.filter(block=>block.type==='table').map(block=>({rows:2,columns:2,cells:[block.headers,...block.rows]})),images:options.blocks.filter(block=>block.type==='image').map(block=>({sha256:block.sha256,width:42000,height:28000}))}};};
 return{calls,rpc,handle:createRichComposeHandler({runtime,callEditor,generateImage,buildFragment,tempRoot:root,turnTimeout:1000,requestTimeout:3000}),request:{id:'fixture',action:'rich-compose',usageConsent:true,accountId,model:'auto',reasoningEffort:'low',prompt:'표와 인포그래픽으로도 정리해줄래?'}};
}
try{
 assert.deepEqual(richRequirements('표와 인포그래픽으로도 정리해줄래?'),{table:true,image:true});
 const good=await fixture();const result=await good.handle(good.request);assert.equal(result.appliedCount,1);assert(good.calls.indexOf('generateImage')<good.calls.indexOf('buildFragment'));assert(good.calls.indexOf('buildFragment')<good.calls.indexOf('hwp_editor_rich_apply'));assert.equal(good.calls.filter(x=>x==='hwp_editor_rich_apply').length,1);assert.equal(good.rpc.filter(x=>x.method==='turn/start').length,1);assert.equal(good.rpc.find(x=>x.method==='turn/start').params.effort,'low');
 assert.equal(JSON.stringify(good.rpc.find(x=>x.method==='turn/start')).includes('fragment.hwpx'),false);
 for(const settings of [{noCapability:true},{imageFails:true},{plan:{summary:'누락',blocks:[paragraph('제목')]}},{plan:{summary:'파일 경로는 안 됨',blocks:[{...image(),path:'C:/private'}]}}]){
  const bad=await fixture(settings);await assert.rejects(bad.handle(bad.request));assert.equal(bad.calls.includes('hwp_editor_rich_apply'),false);
 }
 const imageFailure=await fixture({imageFails:true});await assert.rejects(imageFailure.handle(imageFailure.request));assert.equal(imageFailure.calls.includes('buildFragment'),false);
 const stale=await fixture({stale:true});await assert.rejects(stale.handle(stale.request),/바뀌/);assert.equal(stale.calls.filter(x=>x==='hwp_editor_rich_apply').length,1);
 const unconfirmed=await fixture({unconfirmed:true});await assert.rejects(unconfirmed.handle(unconfirmed.request),/결과/);
 const tableOnly=await fixture({plan:{summary:'실제 표',blocks:[table()]}});await tableOnly.handle({...tableOnly.request,prompt:'표로 정리해줘'});assert.equal(tableOnly.calls.includes('generateImage'),false);
 const selected=await fixture({selectedSource:'고정한 실제 선택 원문',plan:{summary:'선택을 표로',blocks:[table()]}});await selected.handle({...selected.request,prompt:'표로 다시 조판',sourceText:'오래된 채팅 초안'});assert.equal(JSON.parse(selected.rpc.find(x=>x.method==='turn/start').params.input[0].text).sourceDraft,'고정한 실제 선택 원문');
 const emptySelection=await fixture({selectedSource:''});await assert.rejects(emptySelection.handle(emptySelection.request),/원문/);assert.equal(emptySelection.rpc.length,0);
 for(const block of [{...table(),rows:[['한 셀']]},{...table(),headers:['A'],rows:Array.from({length:21},()=>['A'])},{...image(),imagePrompt:'x'.repeat(6001)},{...paragraph('x'),text:'A\u0001B'}])assert.throws(()=>validateRichPlan({summary:'실패',blocks:[block]}));
 // Real failure: a valid title returned by Codex had one embedded line break.
 const normalized=validateRichPlan({summary:'제목과 문단',blocks:[{...paragraph('제목 첫 줄\r\n제목 둘째 줄\n\n마지막 줄'),headers:[],rows:[],caption:'',imagePrompt:''}]});
 assert.deepEqual(normalized.blocks.map(x=>[x.role,x.text]),[['title','제목 첫 줄'],['title','제목 둘째 줄'],['title',''],['title','마지막 줄']]);
 assert.equal(validateRichPlan({summary:'탭',blocks:[paragraph('A\tB')]}).blocks[0].text,'A    B');
 const blankBullet=validateRichPlan({summary:'공백',blocks:[{...paragraph('항목\n\n다음'),role:'bullet'}]});
 assert.deepEqual(blankBullet.blocks.map(x=>x.role),['bullet','body','bullet']);
 const multilineTable=validateRichPlan({summary:'셀',blocks:[{...table(),headers:['열\n이름',''],rows:[['하나\r\n둘','']]}]});
 assert.deepEqual(multilineTable.blocks[0].headers,['열 이름','']);assert.deepEqual(multilineTable.blocks[0].rows,[['하나 둘','']]);
 assert.deepEqual(validateRichPlan({summary:'빈 문단',blocks:[{...paragraph(''),role:null,text:null}]}).blocks,[{type:'paragraph',role:'body',text:''}]);
 assert.equal(validateRichPlan({summary:'그림',blocks:[{...image(),caption:null,imagePrompt:'기호 🖼️ 포함한 삽화'}]}).blocks[0].caption,'');
 assert.throws(()=>validateRichPlan({summary:'잘못된 기호',blocks:[{...image(),imagePrompt:'그림\ud800'}]}));
 assert.throws(()=>validateRichPlan({summary:'다른 블록',blocks:[{...paragraph('텍스트'),headers:['감추면 안 되는 표']}]}));
 assert.throws(()=>validateRichPlan({summary:'줄 수',blocks:[paragraph(Array(66).fill('줄').join('\n'))]}));
 const titleLines=await fixture({plan:{...plan(),blocks:[paragraph('첫 제목 줄\n둘째 제목 줄'),table(),image()]}});await titleLines.handle(titleLines.request);assert.equal(titleLines.calls.filter(x=>x==='hwp_editor_rich_apply').length,1);
 for(const invalid of ['\ufffe','\uffff','\ufdd0'])assert.throws(()=>validateRichPlan({summary:'실패',blocks:[paragraph('A'+invalid)]}));
 assert.throws(()=>validateRichPlan({summary:'실패',blocks:[{...paragraph('A'.repeat(12000)),role:'bullet'},paragraph('B'.repeat(12000))]}));
 console.log('PASS: rich plan and two-phase composition scenarios; no real model, HWP or image generation.');
}finally{await rm(root,{recursive:true,force:true});}
