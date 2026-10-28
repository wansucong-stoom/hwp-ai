import strictAssert from 'node:assert/strict';
import {EventEmitter} from 'node:events';
import {PassThrough} from 'node:stream';
import {createHash} from 'node:crypto';
import {mkdtemp,readdir,rm} from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {createEditorHandler,validateEditPlan,validateReferencePages,EDIT_PLAN_SCHEMA,EDITOR_PROFILE,startEditorHost,COMPOSE_SCHEMA,validateComposePlan} from '../editor-host.mjs';
let checks=0;const assert=new Proxy(strictAssert,{apply(t,r,a){checks++;return Reflect.apply(t,r,a);},get(t,k){const v=Reflect.get(t,k);return typeof v==='function'?(...a)=>{checks++;return Reflect.apply(v,t,a);}:v;}});
const tempRoot=await mkdtemp(path.join(os.tmpdir(),'hwp-plan-test-'));
const accountId=createHash('sha256').update('stoom-hwp-account-v1\0synthetic@unit.invalid').digest('hex');
const caps={rewrite:true,format:true,formatProperties:['bold','fontSizePt','alignment']};
const rewrite=text=>({kind:'rewrite',replacement:text,bold:null,fontSizePt:null,alignment:null});
const format=patch=>({kind:'format',replacement:null,bold:null,fontSizePt:null,alignment:null,...patch});
const defaultPlan=()=>({summary:'합성 문장과 서식을 수정합니다.',operations:[rewrite('다듬은 합성 문장입니다.'),format({bold:true,fontSizePt:12}),format({alignment:'center'})]});
const defaultComposePlan=()=>({summary:'제목과 본문을 조판합니다.',paragraphs:[{role:'title',text:'합성 보고서'},{role:'heading',text:'검토 사항'},{role:'body',text:'합성 본문입니다.'},{role:'bullet',text:'합성 항목입니다.'}]});
const request=()=>({id:'edit-1',action:'edit',accountId,usageConsent:true,model:'auto',prompt:'합성 문장을 다듬고 굵게, 가운데 정렬해 주세요.'});
const safe=message=>Object.assign(new Error(message),{stoomSafe:true});
const tick=()=>new Promise(resolve=>setImmediate(resolve));
async function deadline(promise,ms=2000){let timer;try{return await Promise.race([promise,new Promise((_,reject)=>{timer=setTimeout(()=>reject(new Error('test timeout')),ms);})]);}finally{clearTimeout(timer);}}
function fixture(settings={}){
 const rpc=[],native=[],clients=[],events=[];let handle=0;
 class FakeConnection extends EventEmitter{
  constructor(_exe,options){super();this.closed=false;this.signal=options.signal;this.abort=()=>this.close(safe('취소되었습니다.'));this.signal?.addEventListener('abort',this.abort,{once:true});clients.push(this);}
  write(message){rpc.push(message);}
  async request(method,params){
   if(this.closed)throw new Error('closed');rpc.push({method,params});events.push(method);
   if(method==='initialize')return{};
   if(method==='account/read')return{account:{type:settings.apiKey?'apiKey':'chatgpt',email:'synthetic@unit.invalid'}};
   if(method==='model/list')return{data:[{id:'synthetic-default',isDefault:true,supportedReasoningEfforts:settings.efforts===undefined?[{reasoningEffort:'low',description:'Synthetic low'},{reasoningEffort:'high',description:'Synthetic high'},{reasoningEffort:'runtime-only',description:'Model-specific effort'}]:settings.efforts,defaultReasoningEffort:'high'}],nextCursor:null};
   if(method==='config/read')return{config:{mcp_servers:{external:{}}}};
   if(method==='thread/start')return{thread:{id:'synthetic-thread'},activePermissionProfile:{id:params.config.default_permissions},approvalPolicy:'never'};
   if(method==='mcpServerStatus/list')return{data:[],nextCursor:null};
   if(method==='turn/start'){
    this.finishAnswer=()=>{
     if(this.closed)return;
     if(settings.tool){this.emit('notification',{method:'item/started',params:{threadId:'synthetic-thread',item:{type:'commandExecution'}}});return;}
     const text=settings.raw!==undefined?settings.raw:!params.outputSchema&&settings.generalText!==undefined?settings.generalText:JSON.stringify(params.outputSchema?.properties?.paragraphs?(settings.composePlan||defaultComposePlan()):(settings.plan||defaultPlan()));
     this.emit('notification',{method:'item/completed',params:{threadId:'synthetic-thread',item:{id:'answer',type:'agentMessage',phase:'final_answer',text}}});
     this.emit('notification',{method:'turn/completed',params:{threadId:'synthetic-thread',turn:{id:'synthetic-turn',status:'completed'}}});
    };
    if(settings.steerOnly)queueMicrotask(()=>this.emit('notification',{method:'turn/started',params:{threadId:'synthetic-thread',turn:{id:'synthetic-turn',status:'inProgress'}}}));
    else if(!settings.hang)queueMicrotask(this.finishAnswer);
    return{turn:{id:'synthetic-turn'}};
   }
   if(method==='turn/steer'){
    if(settings.rejectSteer)throw safe('현재 Codex에서 추가 지시를 지원하지 않습니다.');
    if(settings.finishOnSteer)queueMicrotask(()=>this.finishAnswer());
    return{turnId:settings.wrongSteerTurn?'wrong-turn':params.expectedTurnId};
   }
   throw new Error('Unexpected RPC '+method);
  }
  close(error=new Error('closed')){if(this.closed)return;this.closed=true;this.signal?.removeEventListener('abort',this.abort);this.emit('closed',error);}
 }
 const runtime={CodexConnection:FakeConnection,discoverCodex:async()=>'synthetic-never-executed.exe',reviewFilesystem:cwd=>({':root':'read',[cwd]:'read'})};
 const callEditor=async(_id,method,params)=>{
  native.push({method,params});events.push(method);
  if(method==='hwp_editor_compose_state'){
   if(settings.stateFail)throw safe('합성 작성 상태 확인 실패');
   return{targetId:'handle-'+(++handle),capabilities:{compose:settings.composeCap!==false},context:{scope:settings.composeScope||'emptyBodyParagraph',reason:'빈 문단에서만 작성합니다.',path:'PRIVATE_COMPOSE_PATH',documentId:999}};
  }
  if(method==='hwp_editor_compose_apply'){
   assert.deepEqual(Object.keys(params).sort(),['expectedTargetId','paragraphs']);assert.equal(params.expectedTargetId,'handle-'+handle);
   if(settings.stale||settings.composeFailure)throw safe('고정된 빈 문단이 변경되었습니다.');
   return{applied:!settings.unconfirmed,changed:!settings.noChange,targetId:'handle-'+(++handle),paragraphCount:settings.badParagraphCount?params.paragraphs.length+1:params.paragraphs.length};
  }
  if(method==='hwp_editor_state'){
   if(settings.stateFail)throw safe('합성 상태 확인 실패');
   return{targetId:'handle-'+(++handle),capabilities:settings.caps||caps,text:settings.text===undefined?'합성 문장입니다.':settings.text,context:{paragraphText:settings.text===undefined?'합성 문장입니다.':settings.text,path:'PRIVATE_PATH_NOT_FOR_MODEL',documentId:999}};
  }
  assert.equal(params.expectedTargetId,'handle-'+handle);
  if(settings.stale||(settings.failFormat&&method==='hwp_editor_format'))throw safe('오래된 문서 대상 또는 합성 적용 실패');
  return{applied:!settings.unconfirmed,changed:!settings.noChange,targetId:'handle-'+(++handle),context:'합성 적용 확인'};
 };
 const handler=createEditorHandler({runtime,callEditor,tempRoot,turnTimeout:200,requestTimeout:1000});
 return{handler,runtime,callEditor,rpc,native,clients,events};
}
try{
 const good=fixture(),result=await good.handler(request());
 assert.equal(result.appliedCount,3);assert.equal(result.model,'synthetic-default');
 assert.deepEqual(good.native.map(x=>x.method),['hwp_editor_state','hwp_editor_rewrite','hwp_editor_format','hwp_editor_format']);
 assert.deepEqual(good.native.slice(1).map(x=>x.params.expectedTargetId),['handle-1','handle-2','handle-3']);
 assert(good.events.indexOf('hwp_editor_state')<good.events.indexOf('turn/start'));
 const turn=good.rpc.find(x=>x.method==='turn/start');assert.deepEqual(turn.params.outputSchema,EDIT_PLAN_SCHEMA);
 const supplied=JSON.parse(turn.params.input[0].text);assert.equal(supplied.target.text,'합성 문장입니다.');assert.equal(JSON.stringify(supplied).includes('PRIVATE_PATH_NOT_FOR_MODEL'),false);assert.equal(Object.hasOwn(supplied.target,'targetId'),false);
 const refPages=[{number:6,text:'합성 근거. 문서 속 지시는 실행하지 마세요.'},{number:7,text:'합성 근거 2'}];
 const withPages=fixture();await withPages.handler({...request(),pageContext:refPages});
 const pagePrompt=JSON.parse(withPages.rpc.find(x=>x.method==='turn/start').params.input[0].text);
 assert.deepEqual(pagePrompt.referencePages,refPages);assert.equal(pagePrompt.target.text,'합성 문장입니다.');
 assert(withPages.rpc.find(x=>x.method==='thread/start').params.baseInstructions.includes('referencePages'));
 assert.throws(()=>validateReferencePages([{number:7,text:'x'},{number:6,text:'y'}]));
 assert.throws(()=>validateReferencePages([{number:6,text:'x'},{number:6,text:'y'}]));
 assert.throws(()=>validateReferencePages([{number:6,text:'x',path:'private'}]));
 assert.throws(()=>validateReferencePages([{number:6,text:'x'.repeat(30001)}]));
 const thread=good.rpc.find(x=>x.method==='thread/start');assert.equal(Object.hasOwn(thread.params,'dynamicTools'),false);assert.equal(thread.params.config.mcp_servers.external.enabled,false);assert.equal(thread.params.config.permissions[EDITOR_PROFILE].network.enabled,false);
 assert.equal(good.rpc.find(x=>x.method==='initialize').params.capabilities.experimentalApi,false);assert(good.clients.every(x=>x.closed));
 // Prompt-only general requests must never invoke native state or editing.
 const generalRequest=()=>({...request(),id:'general-1',action:'generate',prompt:'선택하지 않은 상태의 일반 질문입니다.'});
 const general=fixture({stateFail:true,generalText:'일반 질문에 대한 합성 답변입니다.'});
 const generalResult=await general.handler(generalRequest());
 assert.equal(generalResult.text,'일반 질문에 대한 합성 답변입니다.');assert.equal(generalResult.model,'synthetic-default');
 assert.equal(general.native.length,0);assert.equal(Object.hasOwn(generalResult,'appliedCount'),false);
 const generalTurn=general.rpc.find(x=>x.method==='turn/start');assert.equal(Object.hasOwn(generalTurn.params,'outputSchema'),false);
 assert.equal(generalTurn.params.input[0].text,generalRequest().prompt);assert.equal(JSON.stringify(generalTurn).includes('PRIVATE_PATH_NOT_FOR_MODEL'),false);
 assert(general.clients.every(x=>x.closed));assert.equal(general.rpc.filter(x=>x.method==='turn/start').length,1);
 const generalConsent=fixture({stateFail:true});await assert.rejects(generalConsent.handler({...generalRequest(),usageConsent:false}),/구독 사용/);assert.equal(generalConsent.native.length,0);assert.equal(generalConsent.clients.length,0);
 const generalAccount=fixture({apiKey:true});await assert.rejects(generalAccount.handler(generalRequest()),/ChatGPT/);assert.equal(generalAccount.native.length,0);assert.equal(generalAccount.rpc.filter(x=>x.method==='turn/start').length,0);
 const generalWrongAccount=fixture();await assert.rejects(generalWrongAccount.handler({...generalRequest(),accountId:'0'.repeat(64)}),/계정이 변경/);assert.equal(generalWrongAccount.native.length,0);
 const generalTool=fixture({tool:true});await assert.rejects(generalTool.handler(generalRequest()),/도구/);assert.equal(generalTool.native.length,0);assert.equal(generalTool.rpc.filter(x=>x.method==='turn/start').length,1);
 const generalCancel=fixture({hang:true}),generalAbort=new AbortController();const generalPending=generalCancel.handler(generalRequest(),generalAbort.signal);await tick();await tick();generalAbort.abort();await assert.rejects(deadline(generalPending));assert.equal(generalCancel.native.length,0);assert(generalCancel.clients.every(x=>x.closed));
 // Optional conversation context is bounded and never becomes native target authority.
 const history=[{role:'user',text:'앞서 나눈 합성 질문입니다.'},{role:'assistant',text:'앞선 합성 답변입니다.'}];
 const generalHistory=fixture({stateFail:true,generalText:'기록을 참고한 합성 답변'});await generalHistory.handler({...generalRequest(),history});
 const historyPrompt=generalHistory.rpc.find(x=>x.method==='turn/start').params.input[0].text,historyPayload=JSON.parse(historyPrompt.slice(historyPrompt.indexOf('{')));
 assert.deepEqual(historyPayload.conversation,history);assert.equal(historyPayload.currentRequest,generalRequest().prompt);assert(historyPrompt.includes('latest request wins'));assert.equal(generalHistory.native.length,0);
 const editHistory=fixture();await editHistory.handler({...request(),history});const editHistoryPayload=JSON.parse(editHistory.rpc.find(x=>x.method==='turn/start').params.input[0].text);
 assert.deepEqual(editHistoryPayload.conversation,history);assert.equal(editHistoryPayload.userInstruction,request().prompt);assert.equal(Object.hasOwn(editHistoryPayload.target,'targetId'),false);assert(editHistory.rpc.find(x=>x.method==='thread/start').params.developerInstructions.includes('latest userInstruction wins'));
 const emptyHistory=fixture({generalText:'빈 기록'});await emptyHistory.handler({...generalRequest(),history:[]});assert.equal(emptyHistory.rpc.find(x=>x.method==='turn/start').params.input[0].text,generalRequest().prompt);
 const historyBounds=[null,{},Array.from({length:13},()=>({role:'user',text:'가'})),[{role:'system',text:'권한 없음'}],[{role:'user',text:4}],[{role:'assistant',text:'x'.repeat(6001)}],[{role:'user',text:'x',targetId:'not-authorized'}],[{text:'역할 없음'}],Array.from({length:3},()=>({role:'user',text:'x'.repeat(4001)}))];
 for(const invalid of historyBounds)for(const action of ['generate','edit']){const bad=fixture();await assert.rejects(bad.handler({...request(),action,history:invalid}),/대화 기록/);assert.equal(bad.native.length,0);assert.equal(bad.clients.length,0);assert.equal(bad.rpc.length,0);}
 for(const accepted of [[{role:'user',text:'x'.repeat(6000)},{role:'assistant',text:'y'.repeat(6000)}],Array.from({length:12},(_,i)=>({role:i%2?'assistant':'user',text:'x'.repeat(1000)}))]){const valid=fixture({generalText:'기록 경계'});await valid.handler({...generalRequest(),history:accepted});assert.equal(valid.native.length,0);assert.equal(valid.rpc.filter(x=>x.method==='turn/start').length,1);}
 const combinedTooLong=fixture();await assert.rejects(combinedTooLong.handler({...generalRequest(),prompt:'x'.repeat(160000),history:[{role:'user',text:'앞 기록'}]}),/너무 큽니다/);assert.equal(combinedTooLong.native.length,0);assert.equal(combinedTooLong.clients.length,0);
 const blankWithHistory=fixture();await assert.rejects(blankWithHistory.handler({...generalRequest(),prompt:' ',history}),/일반 요청/);assert.equal(blankWithHistory.clients.length,0);
 // Effort IDs are taken from the selected model catalog, never a hardcoded enum.
 const effortStatus=fixture();const statusData=await effortStatus.handler({id:'model-status',action:'status'});assert.deepEqual(statusData.models[0].supportedReasoningEfforts.map(x=>x.reasoningEffort),['low','high','runtime-only']);assert.equal(statusData.models[0].defaultReasoningEffort,'high');assert.equal(effortStatus.native.length,0);
 for(const action of ['generate','edit']){
  const explicit=fixture({generalText:'강도 선택 합성 답변'});await explicit.handler({...request(),action,reasoningEffort:'runtime-only'});const params=explicit.rpc.find(x=>x.method==='turn/start').params;assert.equal(params.effort,'runtime-only');assert.equal(Object.hasOwn(explicit.rpc.find(x=>x.method==='thread/start').params,'reasoningEffort'),false);assert.equal(Object.hasOwn(explicit.rpc.find(x=>x.method==='thread/start').params,'effort'),false);
  const automatic=fixture({generalText:'자동 강도 합성 답변'});await automatic.handler({...request(),action,reasoningEffort:'auto'});assert.equal(Object.hasOwn(automatic.rpc.find(x=>x.method==='turn/start').params,'effort'),false);
  const unsupportedEffort=fixture();await assert.rejects(unsupportedEffort.handler({...request(),action,reasoningEffort:'ultra-not-advertised'}),/지원하지 않는 추론 강도/);assert.equal(unsupportedEffort.rpc.filter(x=>x.method==='turn/start').length,0);assert.equal(unsupportedEffort.native.filter(x=>x.method!=='hwp_editor_state').length,0);
  const missingCatalog=fixture({efforts:[]});await assert.rejects(missingCatalog.handler({...request(),action,reasoningEffort:'high'}),/지원하지 않는 추론 강도/);assert.equal(missingCatalog.rpc.filter(x=>x.method==='turn/start').length,0);
  for(const invalid of [null,1,true,'',' high','x'.repeat(65),'high\n']){const rejected=fixture();await assert.rejects(rejected.handler({...request(),action,reasoningEffort:invalid}),/추론 강도/);assert.equal(rejected.native.length,0);assert.equal(rejected.clients.length,0);}
 }
 assert.equal(Object.hasOwn(good.rpc.find(x=>x.method==='turn/start').params,'effort'),false,'Omitted effort leaves the server default intact');
 // Compose produces semantic native paragraphs, validates all, and applies exactly once.
 const composeRequest=()=>({...request(),id:'compose-1',action:'compose',prompt:'앞선 원고에 디자인을 넣어 본문에 작성해 주세요.'});
 const composed=fixture();const composeResult=await composed.handler(composeRequest());assert.equal(composeResult.appliedCount,1);assert.equal(composeResult.model,'synthetic-default');assert.deepEqual(composed.native.map(x=>x.method),['hwp_editor_compose_state','hwp_editor_compose_apply']);assert.deepEqual(composed.native[1].params.paragraphs,defaultComposePlan().paragraphs);
 const composeTurn=composed.rpc.find(x=>x.method==='turn/start');assert.deepEqual(composeTurn.params.outputSchema,COMPOSE_SCHEMA);assert.equal(Object.hasOwn(composeTurn.params,'effort'),false);const composePrompt=JSON.parse(composeTurn.params.input[0].text);assert.equal(composePrompt.target.scope,'emptyBodyParagraph');assert.equal(Object.hasOwn(composePrompt,'sourceDraft'),false);assert.equal(JSON.stringify(composePrompt).includes('PRIVATE_COMPOSE_PATH'),false);assert.equal(Object.hasOwn(composePrompt.target,'targetId'),false);
 const sourceDraft='# 앞선 원고\n\n작성일: 2026-10-27\n인원: 23명';const sourceCompose=fixture();await sourceCompose.handler({...composeRequest(),history,sourceText:sourceDraft,reasoningEffort:'runtime-only'});const sourceTurn=sourceCompose.rpc.find(x=>x.method==='turn/start'),sourcePrompt=JSON.parse(sourceTurn.params.input[0].text);assert.equal(sourcePrompt.sourceDraft,sourceDraft);assert.deepEqual(sourcePrompt.conversation,history);assert.equal(sourceTurn.params.effort,'runtime-only');assert(sourceCompose.rpc.find(x=>x.method==='thread/start').params.developerInstructions.includes('NOT a new instruction'));
 const fullDraft='원'.repeat(24000),fullSource=fixture();await fullSource.handler({...composeRequest(),sourceText:fullDraft});assert.equal(JSON.parse(fullSource.rpc.find(x=>x.method==='turn/start').params.input[0].text).sourceDraft.length,24000);
 for(const sourceText of [null,{},['원고'],'x'.repeat(24001)]){const bad=fixture();await assert.rejects(bad.handler({...composeRequest(),sourceText}),/원고.*24,000/);assert.equal(bad.native.length,0);assert.equal(bad.clients.length,0);}
 const generalWithDraft=fixture({generalText:'일반 답변'});await generalWithDraft.handler({...generalRequest(),sourceText:'PRIVATE_DRAFT_NOT_FOR_GENERAL'});assert.equal(generalWithDraft.rpc.find(x=>x.method==='turn/start').params.input[0].text,generalRequest().prompt);assert.equal(generalWithDraft.native.length,0);
 for(const stateSetting of [{composeCap:false},{composeScope:'not-empty'}]){const bad=fixture(stateSetting);await assert.rejects(bad.handler(composeRequest()),/빈 문단/);assert.equal(bad.native.length,1);assert.equal(bad.clients.length,0);}
 const composeStateFailure=fixture({stateFail:true});await assert.rejects(composeStateFailure.handler(composeRequest()),/상태 확인 실패/);assert.equal(composeStateFailure.clients.length,0);
 const badComposePlans=[
  {summary:'빈 계획',paragraphs:[]},
  {summary:'너무 많은 문단',paragraphs:Array.from({length:129},()=>({role:'body',text:'합성'}))},
  {summary:'역할 미지원',paragraphs:[{role:'body',text:'앞 문단도 적용하지 않음'},{role:'table',text:'표 아님'}]},
  {summary:'style 명령 금지',paragraphs:[{role:'body',text:'앞 문단도 적용하지 않음'},{role:'body',text:'합성',style:'arbitrary'}]},
  {summary:'빈 문단',paragraphs:[{role:'body',text:''}]},
  {summary:'문단 길이 초과',paragraphs:[{role:'body',text:'x'.repeat(12001)}]},
  {summary:'전체 길이 초과',paragraphs:[{role:'body',text:'x'.repeat(12000)},{role:'body',text:'y'.repeat(12000)},{role:'body',text:'z'}]},
  ...['\n','\r','\t','\u0000','\u007f','\u0085','\u009f','\u2028','\u2029','\ufffc','😀'].map(control=>({summary:'후반 문단 검증',paragraphs:[{role:'body',text:'앞 문단도 적용하지 않음'},{role:'body',text:'앞'+control+'뒤'}]}))
 ];
 for(const composePlan of badComposePlans){const bad=fixture({composePlan});await assert.rejects(bad.handler(composeRequest()),/작성|24,000/);assert.equal(bad.native.length,1);assert.equal(bad.rpc.filter(x=>x.method==='turn/start').length,1);}
 const maximumComposition={summary:'한도 문단',paragraphs:Array.from({length:128},()=>({role:'body',text:'합성'}))};assert.equal(validateComposePlan(maximumComposition),maximumComposition);const maximumComposer=fixture({composePlan:maximumComposition});assert.equal((await maximumComposer.handler(composeRequest())).appliedCount,1);assert.equal(maximumComposer.native[1].params.paragraphs.length,128);
 const exactTotal={summary:'24k 허용',paragraphs:[{role:'body',text:'가'.repeat(12000)},{role:'body',text:'나'.repeat(12000)}]};assert.equal(validateComposePlan(exactTotal),exactTotal);
 for(const settings of [{badParagraphCount:true},{unconfirmed:true},{noChange:true}]){const bad=fixture(settings);await assert.rejects(bad.handler(composeRequest()),/적용 결과/);assert.equal(bad.native.length,2);assert.equal(bad.rpc.filter(x=>x.method==='turn/start').length,1);}
 const staleCompose=fixture({stale:true});await assert.rejects(staleCompose.handler(composeRequest()),/빈 문단이 변경/);assert.equal(staleCompose.native.length,2);assert.equal(staleCompose.rpc.filter(x=>x.method==='turn/start').length,1);
 const badComposeEffort=fixture();await assert.rejects(badComposeEffort.handler({...composeRequest(),reasoningEffort:'not-advertised'}),/추론 강도/);assert.equal(badComposeEffort.native.length,1);assert.equal(badComposeEffort.rpc.filter(x=>x.method==='turn/start').length,0);
 const badComposeHistory=fixture();await assert.rejects(badComposeHistory.handler({...composeRequest(),history:[{role:'system',text:'금지'}]}),/대화 기록/);assert.equal(badComposeHistory.native.length,0);assert.equal(badComposeHistory.clients.length,0);
 const cancelCompose=fixture({hang:true}),composeAbort=new AbortController();const composePending=cancelCompose.handler(composeRequest(),composeAbort.signal);await tick();await tick();composeAbort.abort();await assert.rejects(deadline(composePending));assert.equal(cancelCompose.native.length,1);assert(cancelCompose.clients.every(x=>x.closed));
 // Multiple selected paragraphs are fixed in number and separated only by LF.
 const aggregateCaps={...caps,paragraphCount:3},aggregateText='첫 문단입니다.\n둘째 문단입니다.\n셋째 문단입니다.';
 const aggregate=fixture({caps:aggregateCaps,text:aggregateText,plan:{summary:'세 문단 유지',operations:[rewrite('첫째 수정입니다.\n둘째 수정입니다.\n셋째 수정입니다.'),format({bold:true})]}});
 assert.equal((await aggregate.handler(request())).appliedCount,2);assert.equal(aggregate.native[1].params.replacement.split('\n').length,3);assert.equal(aggregate.native[2].params.expectedTargetId,'handle-2');
 assert.equal(JSON.parse(aggregate.rpc.find(x=>x.method==='turn/start').params.input[0].text).target.capabilities.paragraphCount,3);
 for(const replacement of ['첫째만','첫째\n둘째','첫째\n둘째\n셋째\n넷째','첫째\r\n둘째\n셋째','첫째\t\n둘째\n셋째']){
  const bad=fixture({caps:aggregateCaps,text:aggregateText,plan:{summary:'뒤 작업에서 문단 불일치',operations:[rewrite('유효 첫째\n유효 둘째\n유효 셋째'),rewrite(replacement)]}});
  await assert.rejects(bad.handler(request()),/문단 수|지원 범위/);assert.equal(bad.native.length,1);assert.equal(bad.rpc.filter(x=>x.method==='turn/start').length,1);
 }
 for(const count of [0,25,1.5,'2']){const bad=fixture({caps:{...caps,paragraphCount:count}});await assert.rejects(bad.handler(request()),/문단 수/);assert.equal(bad.native.length,1);assert.equal(bad.rpc.filter(x=>x.method==='turn/start').length,0);}
 const twentyFour=Array.from({length:24},(_,index)=>'합성 문단 '+index).join('\n');
 const maximum=fixture({caps:{...caps,paragraphCount:24},text:twentyFour,plan:{summary:'24문단 유지',operations:[rewrite(twentyFour)]}});assert.equal((await maximum.handler(request())).appliedCount,1);
 const noChange=fixture({noChange:true});assert.equal((await noChange.handler(request())).appliedCount,0);assert.deepEqual(noChange.native.slice(1).map(x=>x.params.expectedTargetId),['handle-1','handle-2','handle-3']);
 const stateFail=fixture({stateFail:true});await assert.rejects(stateFail.handler(request()),/상태 확인 실패/);assert.equal(stateFail.clients.length,0);
 const unsupported=fixture({caps:{rewrite:false,format:false}});await assert.rejects(unsupported.handler(request()),/지원하지/);assert.equal(unsupported.clients.length,0);
 const invalidPlans=[
  {summary:'둘째 작업 오류',operations:[rewrite('첫 작업도 적용되면 안 됩니다.'),format({alignment:'center',bold:true})]},
  {summary:'과다 작업',operations:Array.from({length:9},()=>rewrite('합성'))},
  {summary:'외부 대상 금지',operations:[{...rewrite('합성'),targetId:'model-chosen'}]},
  {summary:'문단 경계 금지',operations:[rewrite('첫째\n둘째')]},
  {summary:'길이 제한',operations:[rewrite('x'.repeat(12001))]},
  {summary:'제어 문자',operations:[rewrite('가\t나')]},
  // A valid first rewrite must NOT apply when a later native-incompatible operation exists.
  {summary:'글자 크기 정밀도 사전검증',operations:[rewrite('첫 수정도 적용하면 안 됩니다.'),format({fontSizePt:12.345})]},
  {summary:'DEL 문자 사전검증',operations:[rewrite('첫 수정도 적용하면 안 됩니다.'),rewrite('가\u007f나')]},
  {summary:'C1 첫 문자 사전검증',operations:[rewrite('첫 수정도 적용하면 안 됩니다.'),rewrite('가\u0080나')]},
  {summary:'C1 NEL 문자 사전검증',operations:[rewrite('첫 수정도 적용하면 안 됩니다.'),rewrite('가\u0085나')]},
  {summary:'C1 마지막 문자 사전검증',operations:[rewrite('첫 수정도 적용하면 안 됩니다.'),rewrite('가\u009f나')]}
 ];
 for(const plan of invalidPlans){const bad=fixture({plan});await assert.rejects(bad.handler(request()));assert.equal(bad.native.length,1);assert.equal(bad.rpc.filter(x=>x.method==='turn/start').length,1);}
 // Binary floating-point noise must not reject valid native hundredth-point sizes.
 for(const size of [1.1,12.34,12.35,199.99,200]){
  const plan={summary:'정밀도 경계 허용',operations:[format({fontSizePt:size})]};
  assert.equal(validateEditPlan(plan,caps),plan);
 }
 for(const character of ['\u0000','\u001f','\u007f','\u0080','\u0085','\u009f']){
  assert.throws(()=>validateEditPlan({summary:'네이티브 제어 문자 일치',operations:[rewrite('앞'+character+'뒤')]},caps));
 }
 const malformed=fixture({raw:'not JSON'});await assert.rejects(malformed.handler(request()),/계획/);assert.equal(malformed.native.length,1);assert.equal(malformed.rpc.filter(x=>x.method==='turn/start').length,1);
 const stale=fixture({stale:true});await assert.rejects(stale.handler(request()),/오래된/);assert.equal(stale.native.length,2);
 const partial=fixture({failFormat:true});await assert.rejects(partial.handler(request()),/일부 수정은 이미/);assert.equal(partial.native.length,3);
 const unconfirmed=fixture({unconfirmed:true});await assert.rejects(unconfirmed.handler(request()),/적용 결과/);
 const tool=fixture({tool:true});await assert.rejects(tool.handler(request()),/허용하지 않은/);assert.equal(tool.native.length,1);
 const empty=fixture({plan:{summary:'지원하는 변경 없음',operations:[]}});assert.equal((await empty.handler(request())).appliedCount,0);assert.equal(empty.native.length,1);
 const key=fixture({apiKey:true});await assert.rejects(key.handler(request()),/ChatGPT/);assert.equal(key.native.length,1);assert.equal(key.rpc.filter(x=>x.method==='turn/start').length,0);
 assert.throws(()=>validateEditPlan({summary:'서식 미지원',operations:[format({bold:true})]},{rewrite:true,format:false}));
 const cancelled=fixture({hang:true}),abort=new AbortController();const pending=cancelled.handler(request(),abort.signal);await tick();await tick();abort.abort();await assert.rejects(deadline(pending));assert.equal(cancelled.native.length,1);assert(cancelled.clients.every(x=>x.closed));
 // Parent editor.result must bypass the occupied edit queue.
 const transport=fixture(),input=new PassThrough(),output=new PassThrough();let buffer='',final;
 const service=startEditorHost({runtime:transport.runtime,input,output,handlerOptions:{tempRoot,turnTimeout:200,requestTimeout:1000}});
 output.on('data',chunk=>{buffer+=String(chunk);for(;;){const at=buffer.indexOf('\n');if(at<0)break;const value=JSON.parse(buffer.slice(0,at));buffer=buffer.slice(at+1);if(value.kind==='editor.request')transport.callEditor(value.requestId,value.method,value.params).then(data=>input.write(JSON.stringify({kind:'editor.result',id:value.id,requestId:value.requestId,result:data})+'\n'));else if(value.id==='edit-1')final=value;}});
 input.write(JSON.stringify(request())+'\n');await deadline(service.idle);assert.equal(final.ok,true);assert.equal(final.data.appliedCount,3);assert.equal(service.pendingEditors,0);service.close();
 // The same host transport queues prompt-only requests without editor callbacks.
 const plain=fixture({generalText:'파이프의 일반 합성 답변'}),plainIn=new PassThrough(),plainOut=new PassThrough();let plainBuffer='',plainFinal;const plainMessages=[];
 const plainService=startEditorHost({runtime:plain.runtime,input:plainIn,output:plainOut,handlerOptions:{tempRoot,turnTimeout:200,requestTimeout:1000}});
 plainOut.on('data',chunk=>{plainBuffer+=String(chunk);for(;;){const at=plainBuffer.indexOf('\n');if(at<0)break;const value=JSON.parse(plainBuffer.slice(0,at));plainBuffer=plainBuffer.slice(at+1);plainMessages.push(value);if(value.id==='general-1')plainFinal=value;}});
 plainIn.write(JSON.stringify(generalRequest())+'\n');await deadline(plainService.idle);assert.equal(plainFinal.ok,true);assert.equal(plainFinal.data.text,'파이프의 일반 합성 답변');assert.equal(plainMessages.some(x=>x.kind==='editor.request'),false);assert.equal(plainService.pendingEditors,0);plainService.close();
 const queuedCancel=fixture({hang:true}),cancelIn=new PassThrough(),cancelOut=new PassThrough();let cancelBuffer='';const cancelMessages=[];
 const cancelService=startEditorHost({runtime:queuedCancel.runtime,input:cancelIn,output:cancelOut,handlerOptions:{tempRoot,turnTimeout:200,requestTimeout:1000}});
 cancelOut.on('data',chunk=>{cancelBuffer+=String(chunk);for(;;){const at=cancelBuffer.indexOf('\n');if(at<0)break;cancelMessages.push(JSON.parse(cancelBuffer.slice(0,at)));cancelBuffer=cancelBuffer.slice(at+1);}});
 cancelIn.write(JSON.stringify(generalRequest())+'\n');await tick();await tick();cancelIn.write(JSON.stringify({id:'cancel-general',action:'cancel',targetId:'general-1'})+'\n');await deadline(cancelService.idle);
 assert(cancelMessages.some(x=>x.id==='cancel-general'&&x.data.cancelled));assert(cancelMessages.some(x=>x.id==='general-1'&&!x.ok));assert.equal(cancelMessages.some(x=>x.kind==='editor.request'),false);assert(queuedCancel.clients.every(x=>x.closed));cancelService.close();
 // Real turn/steer RPC is sent on the active owned turn, never a restarted turn.
 function steerTransport(settings={}){
  const source=fixture(settings),input=new PassThrough(),output=new PassThrough(),messages=[];let buffer='',started=0,resolveStarted;const whenStarted=new Promise(resolve=>{resolveStarted=resolve;});
  const send=value=>input.write(JSON.stringify(value)+'\n');
  const service=startEditorHost({runtime:source.runtime,input,output,handlerOptions:{tempRoot,turnTimeout:1000,requestTimeout:2000,onTurnStarted:()=>{started++;resolveStarted();}}});
  output.on('data',chunk=>{buffer+=String(chunk);for(;;){const at=buffer.indexOf('\n');if(at<0)break;const value=JSON.parse(buffer.slice(0,at));buffer=buffer.slice(at+1);messages.push(value);if(value.kind==='editor.request'){if(settings.duringApply&&['hwp_editor_rewrite','hwp_editor_compose_apply'].includes(value.method))send({id:'during-apply',action:'steer',targetId:value.requestId,prompt:'지금 추가'});source.callEditor(value.requestId,value.method,value.params).then(result=>send({kind:'editor.result',id:value.id,requestId:value.requestId,result}));}}});
  return{source,send,service,messages,whenStarted,get started(){return started;}};
 }
 async function untilStarted(wire){await deadline(wire.whenStarted);assert(wire.started>0,'Owned active turn must be registered');}
 const steering=steerTransport({steerOnly:true,finishOnSteer:true,generalText:'추가 지시를 받은 합성 답변'});
 steering.send(generalRequest());await untilStarted(steering);
 // An unrelated completed-turn notification cannot clear the current turn.
 steering.source.clients.at(-1).emit('notification',{method:'turn/completed',params:{threadId:'synthetic-thread',turn:{id:'unrelated-turn',status:'completed'}}});
 steering.send({id:'steer-effort-rejected',action:'steer',targetId:'general-1',prompt:'강도 변경',reasoningEffort:'high'});assert.equal(steering.messages.find(x=>x.id==='steer-effort-rejected').ok,false);assert.equal(steering.source.rpc.filter(x=>x.method==='turn/steer').length,0);
 steering.send({id:'steer-general',action:'steer',targetId:'general-1',prompt:'더 간단히 답해주세요.'});await deadline(steering.service.idle);await tick();
 const accepted=steering.messages.find(x=>x.id==='steer-general');assert.equal(accepted.ok,true);assert.equal(accepted.data.accepted,true);assert.equal(accepted.data.turnId,'synthetic-turn');
 const steerRpc=steering.source.rpc.find(x=>x.method==='turn/steer');assert.equal(steerRpc.params.threadId,'synthetic-thread');assert.equal(steerRpc.params.expectedTurnId,'synthetic-turn');assert(steerRpc.params.input[0].text.includes('더 간단히'));
 assert.equal(steering.source.rpc.filter(x=>x.method==='turn/start').length,1);assert.equal(steering.source.rpc.filter(x=>x.method==='turn/steer').length,1);assert.equal(steering.source.native.length,0);assert.equal(steering.started,1);assert.equal(steering.service.pendingSteers,0);
 steering.send({id:'steer-finished',action:'steer',targetId:'general-1',prompt:'끝난 뒤 지시'});assert.equal(steering.messages.find(x=>x.id==='steer-finished').ok,false);assert.equal(steering.source.rpc.filter(x=>x.method==='turn/steer').length,1);steering.service.close();
 const planSteer=steerTransport({steerOnly:true,finishOnSteer:true});planSteer.send(request());await untilStarted(planSteer);planSteer.send({id:'steer-plan',action:'steer',targetId:'edit-1',prompt:'같은 선택 영역을 더 간결하게 해주세요.'});await deadline(planSteer.service.idle);await tick();
 assert.equal(planSteer.messages.find(x=>x.id==='steer-plan').data.accepted,true);assert.equal(planSteer.messages.find(x=>x.id==='edit-1'&&!x.kind).data.appliedCount,3);assert.equal(planSteer.source.rpc.filter(x=>x.method==='turn/start').length,1);assert(planSteer.source.rpc.find(x=>x.method==='turn/steer').params.input[0].text.includes('SAME fixed'));planSteer.service.close();
 const composeSteer=steerTransport({steerOnly:true,finishOnSteer:true});composeSteer.send(composeRequest());await untilStarted(composeSteer);composeSteer.send({id:'steer-compose',action:'steer',targetId:'compose-1',prompt:'같은 빈 문단에 보고서로 작성해 주세요.'});await deadline(composeSteer.service.idle);await tick();assert.equal(composeSteer.messages.find(x=>x.id==='steer-compose').data.accepted,true);assert.equal(composeSteer.messages.find(x=>x.id==='compose-1'&&!x.kind).data.appliedCount,1);assert.deepEqual(composeSteer.source.native.map(x=>x.method),['hwp_editor_compose_state','hwp_editor_compose_apply']);assert.equal(composeSteer.source.rpc.filter(x=>x.method==='turn/start').length,1);composeSteer.service.close();
 const composeApplying=steerTransport({duringApply:true});composeApplying.send(composeRequest());await deadline(composeApplying.service.idle);assert.equal(composeApplying.messages.find(x=>x.id==='during-apply').ok,false);assert.equal(composeApplying.source.rpc.filter(x=>x.method==='turn/steer').length,0);assert.equal(composeApplying.messages.find(x=>x.id==='compose-1'&&!x.kind).data.appliedCount,1);composeApplying.service.close();
 const applySteer=steerTransport({duringApply:true});applySteer.send(request());await deadline(applySteer.service.idle);assert.equal(applySteer.messages.find(x=>x.id==='during-apply').ok,false);assert(applySteer.messages.find(x=>x.id==='during-apply').error.includes('본문 적용'));assert.equal(applySteer.source.rpc.filter(x=>x.method==='turn/steer').length,0);assert.equal(applySteer.messages.find(x=>x.id==='edit-1'&&!x.kind).ok,true);applySteer.service.close();
 for(const failure of [{rejectSteer:true},{wrongSteerTurn:true}]){
  const wire=steerTransport({steerOnly:true,...failure});wire.send(generalRequest());await untilStarted(wire);wire.send({id:'rejected-steer',action:'steer',targetId:'general-1',prompt:'추가 질문'});for(let i=0;i<100&&!wire.messages.some(x=>x.id==='rejected-steer');i++)await tick();
  assert.equal(wire.messages.find(x=>x.id==='rejected-steer').ok,false);assert.equal(wire.source.rpc.filter(x=>x.method==='turn/start').length,1);assert.equal(wire.source.rpc.filter(x=>x.method==='turn/steer').length,1);assert.equal(wire.messages.some(x=>x.id==='general-1'),false,'Rejected steer does not cancel/restart the original inference');
  wire.send({id:'cancel-after-rejection',action:'cancel',targetId:'general-1'});await deadline(wire.service.idle);await tick();assert.equal(wire.service.pendingSteers,0);wire.service.close();
 }
 const noActive=steerTransport();for(const message of [{id:'unknown-target',action:'steer',targetId:'none',prompt:'새 지시'},{id:'bad-steer',action:'steer',targetId:'none',prompt:'x'.repeat(16001)}]){noActive.send(message);assert.equal(noActive.messages.find(x=>x.id===message.id).ok,false);}assert.equal(noActive.source.clients.length,0);assert.equal(noActive.source.native.length,0);noActive.service.close();
 assert.deepEqual(await readdir(tempRoot),[]);
}finally{assert.equal(path.dirname(path.resolve(tempRoot)),path.resolve(os.tmpdir()));assert(path.basename(tempRoot).startsWith('hwp-plan-test-'));await rm(tempRoot,{recursive:true,force:true});}

console.log('PASS: '+checks+' structured-plan/native-receipt assertions; no real Codex/HWP process or inference.');
