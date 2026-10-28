import strictAssert from 'node:assert/strict';
import {EventEmitter} from 'node:events';
import {PassThrough,Writable} from 'node:stream';
import {mkdtemp,mkdir,readdir,rm} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import os from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {createEditorHandler,startEditorHost,INTENT_ROUTE_SCHEMA,PAGE_SCOPE_SCHEMA,validatePageScope} from '../editor-host.mjs';
import {TABLE_DESIGN_SCHEMA} from '../table-design-plan.mjs';
import {PAGE_EDIT_SCHEMA} from '../page-edit-plan.mjs';
import {DOCUMENT_DISCOVERY_SCHEMA} from '../document-target-discovery.mjs';
import {loadStoomRuntime} from '../hwp-codex-host.mjs';
let checks=0;const assert=new Proxy(strictAssert,{apply(t,r,a){checks++;return Reflect.apply(t,r,a);},get(t,k){const v=Reflect.get(t,k);return typeof v==='function'?(...a)=>{checks++;return Reflect.apply(v,t,a);}:v;}});
const at=process.argv.indexOf('--stoom-module');const modulePath=at>=0?process.argv[at+1]:fileURLToPath(new URL('../../dist/StoomCodex/bridge/vendor/stoom-codex-native-host.mjs',import.meta.url));
const {runtime}=await loadStoomRuntime(modulePath);
const root=await mkdtemp(path.join(os.tmpdir(),'hwp-intent-test-'));
const account={type:'chatgpt',email:'synthetic-router@unit.invalid'};
const accountId=createHash('sha256').update('stoom-hwp-account-v1\0'+account.email).digest('hex');
const route=(patch={})=>({intent:'question',tableReference:patch.intent==='table_style'?'caret':'none',needsTable:false,needsImage:false,usePreviousAnswer:false,useSelectionInQuestion:false,needsDocumentContext:false,...patch});
const request=(patch={})=>({id:'route-1',action:'route',accountId,usageConsent:true,model:'auto',reasoningEffort:'auto',prompt:'AI 문서 협업은 어떻게 하나요?',hasSelection:false,hasPreviousAnswer:false,...patch});
const tick=()=>new Promise(resolve=>setImmediate(resolve));
async function deadline(promise,ms=2000){let timer;try{return await Promise.race([promise,new Promise((_,reject)=>{timer=setTimeout(()=>reject(Error('Synthetic test deadline')),ms);})]);}finally{clearTimeout(timer);}}
function fixture(settings={}){
 const rpc=[],launches=[],children=[],native=[],starts=[],finishes=[];let reads=0;
 const spawnImpl=(executable,args,options)=>{
  launches.push({executable,args,options});const child=new EventEmitter();child.pid=999999;child.exitCode=null;child.killed=false;child.stdout=new PassThrough();child.stderr=new PassThrough();let buffer='';
  const send=value=>{if(!child.killed)child.stdout.write(JSON.stringify(value)+'\n');};
  const notify=(method,params)=>send({method,params:{threadId:'intent-thread',...params}});
  child.stdin=new Writable({write(data,_,done){buffer+=data.toString();let index;while((index=buffer.indexOf('\n'))>=0){const message=JSON.parse(buffer.slice(0,index));buffer=buffer.slice(index+1);rpc.push(message);if(!Object.hasOwn(message,'id'))continue;
   queueMicrotask(()=>{
    if(child.killed||settings.hang===message.method)return;
    const reply=result=>send({id:message.id,result});
    if(settings.reject===message.method)return send({id:message.id,error:{code:-32601,message:'Synthetic rejection'}});
    switch(message.method){
     case'initialize':return reply({});
     case'account/read':return reply({account:settings.apiKey?{type:'apiKey'}:settings.changedAccount&&++reads>1?{...account,email:'changed@unit.invalid'}:account});
     case'model/list':return reply({data:[{id:'synthetic-model',isDefault:true,supportedReasoningEfforts:[{reasoningEffort:'low'},{reasoningEffort:'high'},{reasoningEffort:'runtime-only'}],defaultReasoningEffort:'high'}],nextCursor:settings.modelCycle?'repeated':null});
     case'config/read':return reply({config:{mcp_servers:{personal:{command:'never-execute'}}}});
     case'thread/start':return reply({thread:{id:'intent-thread'},activePermissionProfile:{id:settings.badProfile?'wrong':message.params.config.default_permissions},approvalPolicy:'never'});
     case'mcpServerStatus/list':return reply({data:settings.mcp?[{enabled:true,tools:{forbidden:{}}}]:[],nextCursor:null});
     case'turn/start':{
      reply({turn:{id:'intent-turn'}});notify('turn/started',{turn:{id:'intent-turn'}});
      child.finishTurn=()=>{
       if(settings.serverRequest)return send({id:'unsafe-server-request',method:'item/commandExecution/requestApproval',params:{threadId:'intent-thread'}});
       if(settings.tool)return notify('item/started',{item:{id:'forbidden',type:settings.tool,tool:'hwp_editor_state',arguments:{}}});
       if(settings.error)return notify('error',{error:{codexErrorInfo:settings.errorCode||'usageLimitExceeded',message:'synthetic secret'},willRetry:settings.willRetry!==false});
       if(settings.transient)notify('error',{error:{codexErrorInfo:{responseStreamDisconnected:{httpStatusCode:503}},message:'synthetic secret'},willRetry:true});
       const result=settings.response===undefined?route():settings.response,text=settings.raw===undefined?JSON.stringify(result):settings.raw;
       if(settings.completionOnlyTool)return notify('turn/completed',{turn:{id:'intent-turn',status:'completed',items:[{id:'tool-only',type:'dynamicToolCall',tool:'hwp_editor_state',arguments:{}},{id:'answer',type:'agentMessage',text}]}});
       notify('item/completed',{item:{id:'answer',type:'agentMessage',phase:'final_answer',text}});
       notify('turn/completed',{turn:{id:'intent-turn',status:settings.failedTurn?'failed':'completed'}});
      };
      if(!settings.turnHangs&&!settings.finishOnSteer)child.finishTurn();return;
     }
     case'turn/steer':reply({turnId:message.params.expectedTurnId});if(settings.finishOnSteer)child.finishTurn?.();return;
     default:return send({id:message.id,error:{code:-32601,message:'Unknown synthetic method'}});
    }
   });
  }done();}});
  child.kill=()=>{child.killed=true;child.exitCode=0;queueMicrotask(()=>child.emit('close',0));return true;};children.push(child);return child;
 };
 const callEditor=async(id,method,args)=>{native.push({id,method,args});throw Error('Routing must never call a native editor');};
 const options={runtime,discover:async()=>'synthetic-codex.exe',tempRoot:root,turnTimeout:250,requestTimeout:1000,connectionOptions:{spawnImpl,killImpl:child=>child.kill(),rpcTimeout:200,env:{OPENAI_API_KEY:'never-forward'}},onTurnStarted:value=>starts.push(value),onTurnFinished:value=>finishes.push(value),...settings.options};
 const handle=createEditorHandler({...options,callEditor});return{handle,options,rpc,launches,children,native,starts,finishes};
}
const one=(source,method)=>source.rpc.find(item=>item.method===method);
const inferCount=source=>source.rpc.filter(item=>item.method==='turn/start').length;
const paragraph=text=>({type:'paragraph',role:'body',text,headers:null,rows:null,imagePrompt:null,caption:null});
const table=()=>({type:'table',role:null,text:null,headers:['단계'],rows:[['작성']],imagePrompt:null,caption:null});
const image=()=>({type:'infographic',role:null,text:null,headers:null,rows:null,imagePrompt:'합성 작업 흐름 인포그래픽',caption:'작업 흐름'});
let directorySerial=0;
async function richFixture(blocks,memoDefinition=undefined){
 const source=fixture({response:{summary:'합성 조판',blocks}}),directory=path.join(root,'rich-'+(++directorySerial));await mkdir(directory);
 const calls=[];
 const callEditor=async(id,method,args)=>{
  calls.push({method,args});if(method==='hwp_editor_rich_state')return{targetId:'fixed',capabilities:{compose:true,tables:true,images:true},context:{scope:'emptyBodyParagraph',fragmentDirectory:directory,contentWidthHwpUnit:42000,memoDefinition}};
  if(method==='hwp_editor_rich_progress')return{accepted:true};
  if(method==='hwp_editor_rich_apply')return{applied:true,changed:true,targetId:'fixed',paragraphCount:args.expected.paragraphTexts.length,tableCount:args.expected.tables.length,imageCount:args.expected.images.length};
  throw Error(method);
 };
 const handle=createEditorHandler({...source.options,callEditor,generateImage:async()=>{calls.push({method:'image'});return{path:path.join(directory,'image.png'),sha256:'a'.repeat(64),width:10,height:10};},buildFragment:async({blocks,memoDefinition})=>{calls.push({method:'fragment',memoDefinition});return{fragmentPath:path.join(directory,'fragment.hwpx'),fragmentSha256:'b'.repeat(64),expected:{paragraphTexts:['合成'],tables:blocks.filter(x=>x.type==='table').map(x=>({rows:2,columns:1,cells:[x.headers,...x.rows]})),images:blocks.filter(x=>x.type==='image').map(x=>({sha256:x.sha256,width:750,height:750}))}};}});
 return{...source,handle,calls,request:{id:'rich-1',action:'rich-compose',accountId,usageConsent:true,model:'auto',prompt:'방금 논의한 구성대로 본문에 작성해줘'}};
}
try{
 const scopeRequest={id:'scope-1',action:'page-scope',accountId,usageConsent:true,model:'auto',pageCount:39,prompt:'1~5페이지를 6페이지 이후를 참고하여 업데이트해줘'};
 const proposedScope={targetPages:[1,2,3,4,5],sourcePages:Array.from({length:34},(_,i)=>i+6),uncertainties:[]};
 const scope=fixture({response:proposedScope});assert.deepEqual(await deadline(scope.handle(scopeRequest)),{...proposedScope,model:'synthetic-model'});
 assert.deepEqual(one(scope,'turn/start').params.outputSchema,PAGE_SCOPE_SCHEMA);assert.equal(scope.native.length,0);assert.equal(one(scope,'thread/start').params.config.permissions.stoom_hwp_intent_router.network.enabled,false);
 const scopePayload=JSON.parse(one(scope,'turn/start').params.input[0].text);assert.deepEqual(scopePayload,{currentRequest:scopeRequest.prompt,conversation:[],pageCount:39});
 const uncertainScope={targetPages:[],sourcePages:[],uncertainties:['수정 대상 쪽이 명확하지 않습니다.']};
 assert.deepEqual(await fixture({response:uncertainScope}).handle(scopeRequest),{...uncertainScope,model:'synthetic-model'});
 for(const invalid of [
  {targetPages:[1,1],sourcePages:[6],uncertainties:[]},{targetPages:[2,1],sourcePages:[6],uncertainties:[]},
  {targetPages:[0],sourcePages:[6],uncertainties:[]},{targetPages:[40],sourcePages:[6],uncertainties:[]},
  {targetPages:[1],sourcePages:[2.5],uncertainties:[]},{targetPages:[1],sourcePages:[],uncertainties:[]},
  {targetPages:[1],sourcePages:[6],uncertainties:['ambiguous']},{targetPages:[],sourcePages:[],uncertainties:['']},
  {targetPages:[1],sourcePages:[6],uncertainties:[],unexpected:'unsafe'}]){
   assert.throws(()=>validatePageScope(invalid,39));const bad=fixture({response:invalid});await assert.rejects(bad.handle(scopeRequest),/검증하지 못했습니다/);assert.equal(bad.native.length,0);
 }
 const scopeTool=fixture({tool:'commandExecution'});await assert.rejects(deadline(scopeTool.handle(scopeRequest)));assert.equal(scopeTool.native.length,0);
 for(const pageCount of [0,257,'39',1.5]){const invalid=fixture();await assert.rejects(invalid.handle({...scopeRequest,pageCount}),/쪽 범위/);assert.equal(invalid.launches.length,0);}
 const tableSnapshot={referenceTableId:'ref',tables:[{id:'ref',rows:1,columns:1,cells:[{id:'r',row:0,column:0,rowSpan:1,columnSpan:1,text:'기준',style:{}}]},{id:'target',rows:1,columns:1,cells:[{id:'t',row:0,column:0,rowSpan:1,columnSpan:1,text:'내용',style:{}}]}]};
 const tablePlan={assignments:[{tableId:'target',cellId:'t',role:'머리행',fillSource:'r',paragraphSource:'r',characterSource:'r',edges:{left:'r',right:'r',top:'r',bottom:'r'}}],uncertainties:[]};
 const tableDesign=fixture({response:tablePlan});
 const tableDesignRequest={id:'design-1',action:'table-design',accountId,usageConsent:true,model:'auto',prompt:'기준 표에 맞춰줘',history:[{role:'user',text:'내용과 구조는 유지하고 기준 디자인을 적용해줘.'}],snapshot:tableSnapshot};
 assert.deepEqual(await deadline(tableDesign.handle(tableDesignRequest)),{plan:tablePlan,model:'synthetic-model'});
 assert.deepEqual(one(tableDesign,'turn/start').params.outputSchema,TABLE_DESIGN_SCHEMA);
 assert.equal(tableDesign.native.length,0);assert.equal(one(tableDesign,'thread/start').params.config.permissions.stoom_hwp_intent_router.network.enabled,false);
 assert.deepEqual(JSON.parse(one(tableDesign,'turn/start').params.input[0].text).conversation,tableDesignRequest.history);
 const frozenTable=fixture({response:tablePlan}),mutableTable=structuredClone(tableDesignRequest),tablePending=frozenTable.handle(mutableTable);
 mutableTable.history[0].text='changed after capture';mutableTable.prompt='changed after capture';mutableTable.snapshot.tables[1].cells[0].id='changed-after-capture';
 assert.deepEqual(await deadline(tablePending),{plan:tablePlan,model:'synthetic-model'});
 const frozenTableInput=JSON.parse(one(frozenTable,'turn/start').params.input[0].text);assert.deepEqual(frozenTableInput.conversation,tableDesignRequest.history);assert.equal(frozenTableInput.currentRequest,tableDesignRequest.prompt);assert.deepEqual(frozenTableInput.snapshot,tableSnapshot);
 for(const history of [[{role:'system',text:'invalid role'}],Array.from({length:13},()=>({role:'user',text:'invalid count'}))]){const invalid=fixture({response:tablePlan});await assert.rejects(invalid.handle({...tableDesignRequest,history}),/대화 기록/);assert.equal(invalid.launches.length,0);assert.equal(invalid.native.length,0);}
 const invalidDesign=fixture({response:{...tablePlan,assignments:[]}});await assert.rejects(invalidDesign.handle(tableDesignRequest),/검증하지 못했습니다/);assert.equal(invalidDesign.native.length,0);
 const pageSnapshot={pages:[{number:1,text:'기존 요약'},{number:6,text:'세부 내용'}],targets:[{id:'p1',page:1,text:'기존 요약'}]};
 const pagePlan={changes:[{id:'p1',replacement:'새로운 요약',sourcePages:[6]}],uncertainties:[]};
 const pageRequest={id:'page-1',action:'page-edit-plan',accountId,usageConsent:true,model:'auto',prompt:'6쪽을 참고하여 1쪽 갱신',snapshot:pageSnapshot};
 const page=fixture({response:pagePlan});assert.deepEqual(await deadline(page.handle(pageRequest)),{plan:pagePlan,model:'synthetic-model'});
 assert.deepEqual(one(page,'turn/start').params.outputSchema,PAGE_EDIT_SCHEMA);assert.equal(page.native.length,0);
 assert.equal(one(page,'thread/start').params.config.permissions.stoom_hwp_intent_router.network.enabled,false);
 const invalidPage=fixture({response:{...pagePlan,changes:[{id:'missing',replacement:'잘못된 대상',sourcePages:[6]}]}});await assert.rejects(invalidPage.handle(pageRequest),/검증하지 못했습니다/);assert.equal(invalidPage.native.length,0);
 const pageTool=fixture({tool:'commandExecution'});await assert.rejects(deadline(pageTool.handle(pageRequest)));assert.equal(pageTool.native.length,0);
 const invalidSnapshot=fixture();await assert.rejects(invalidSnapshot.handle({...pageRequest,snapshot:{...pageSnapshot,targets:[{...pageSnapshot.targets[0],page:99}]}}),/확인할 수 없습니다/);assert.equal(invalidSnapshot.launches.length,0);
 const discoverySnapshot={pages:[{number:1,text:'기존 요약'},{number:2,text:'상세 내용'}],targetPages:[1],sourcePages:[2],paragraphs:[{id:'p0',text:'같은 문단',cell:'c0',hasObjects:false},{id:'p1',text:'같은 문단',cell:'c1',hasObjects:true}]};
 const discoveryRequest={id:'discovery-1',action:'document-target-discovery',accountId,usageConsent:true,model:'auto',prompt:'2쪽을 참고해 1쪽 요약을 갱신해줘',snapshot:discoverySnapshot};
 const discovery=fixture({response:{targets:['p0','p1'],uncertainties:['둘째 문단에는 개체가 있습니다.']}});
 const discoveryResult=await deadline(discovery.handle(discoveryRequest));
 assert.deepEqual(discoveryResult,{plan:{targets:['p0','p1'],uncertainties:['둘째 문단에는 개체가 있습니다.']},model:'synthetic-model'});
 assert.equal(discovery.native.length,0);assert.deepEqual(one(discovery,'turn/start').params.outputSchema,DOCUMENT_DISCOVERY_SCHEMA);
 const discoveryInput=JSON.parse(one(discovery,'turn/start').params.input[0].text);
 assert.deepEqual(discoveryInput.snapshot,discoverySnapshot);assert.equal(JSON.stringify(discoveryInput).includes('xmlPath'),false);
 assert.match(one(discovery,'thread/start').params.baseInstructions,/untrusted/);assert.equal(one(discovery,'thread/start').params.config.permissions.stoom_hwp_intent_router.network.enabled,false);
 for(const badResponse of [{targets:['p99'],uncertainties:[]},{targets:['p0','p0'],uncertainties:[]},{targets:['p0'],uncertainties:[],operation:'edit'}]){
  const bad=fixture({response:badResponse});await assert.rejects(deadline(bad.handle(discoveryRequest)),/검증하지 못했습니다/);assert.equal(bad.native.length,0);
 }
 const noConsent=fixture();await assert.rejects(noConsent.handle({...discoveryRequest,usageConsent:false}),/요청을 확인/);assert.equal(noConsent.launches.length,0);
 const partialScope=fixture();await assert.rejects(partialScope.handle({...discoveryRequest,snapshot:{...discoverySnapshot,sourcePages:[1]}}),/범위를 확인/);assert.equal(partialScope.launches.length,0);
 const extraPath=fixture();await assert.rejects(extraPath.handle({...discoveryRequest,snapshot:{...discoverySnapshot,paragraphs:[{...discoverySnapshot.paragraphs[0],path:'PRIVATE'}]}}),/범위를 확인/);assert.equal(extraPath.launches.length,0);
 const discoveryTool=fixture({tool:'commandExecution'});await assert.rejects(deadline(discoveryTool.handle(discoveryRequest)));assert.equal(discoveryTool.native.length,0);
 const largeSnapshot={...discoverySnapshot,paragraphs:[{id:'p0',text:'한'.repeat(110000),cell:null,hasObjects:false}]};
 assert(Buffer.byteLength(JSON.stringify(largeSnapshot),'utf8')>300000);
 const largeDiscovery=fixture({response:{targets:['p0'],uncertainties:[]}});await deadline(largeDiscovery.handle({...discoveryRequest,snapshot:largeSnapshot}));assert.equal(largeDiscovery.native.length,0);
 const delayed=fixture({response:{targets:['p0'],uncertainties:[]}}),mutable=structuredClone(discoveryRequest);const promise=delayed.handle(mutable);mutable.snapshot.paragraphs[0].text='mutated';mutable.prompt='mutated';await deadline(promise);
 const frozenDiscovery=JSON.parse(one(delayed,'turn/start').params.input[0].text);assert.equal(frozenDiscovery.snapshot.paragraphs[0].text,'같은 문단');assert.equal(frozenDiscovery.currentRequest,discoveryRequest.prompt);
 const examples=[
  {prompt:'AI와 함께 글쓰기 강의자료 1페이지 시안 만들고 작업 흐름도 인포그래픽으로 1장 추가',response:route({intent:'compose',needsImage:true})},
  {prompt:'이걸 문서화해줘',hasPreviousAnswer:true,history:[{role:'user',text:'AI와 함께 글쓰기 강의를 기획해줘'},{role:'assistant',text:'강의 목표와 실습 흐름을 설명한 합성 원고'}],response:route({intent:'compose',usePreviousAnswer:true})},
  {prompt:'그럼 이 방식의 장단점은?',hasPreviousAnswer:true,history:[{role:'assistant',text:'합성 협업 방식'}],response:route()},
  {prompt:'선택한 문단의 문체를 간결하게 다듬어줘',hasSelection:true,response:route({intent:'edit'})},
  {prompt:'이 선택 영역은 무슨 뜻이야?',hasSelection:true,response:route({useSelectionInQuestion:true})}
  ,{prompt:'1~5페이지는 요약이고 6페이지 이후는 세부내용이야. 6페이지 이후를 참고해서 1~5페이지 업데이트를 해줘',response:route({intent:'compose',needsDocumentContext:true})}
 ];
 for(const example of examples){
  const source=fixture({response:example.response});const {response,...input}=example;const result=await deadline(source.handle(request(input)));
  assert.deepEqual(result,{...response,model:'synthetic-model'});assert.equal(source.native.length,0);assert.equal(inferCount(source),1);assert.equal(source.launches.length,1);assert(source.children.every(x=>x.killed));
  const thread=one(source,'thread/start').params,turn=one(source,'turn/start').params,payload=JSON.parse(turn.input[0].text);
  assert.equal(payload.currentRequest,input.prompt);assert.deepEqual(payload.conversation,input.history||[]);assert.equal(payload.hasSelection,input.hasSelection||false);assert.equal(payload.hasPreviousAnswer,input.hasPreviousAnswer||false);assert.equal(payload.hasTables,input.hasTables||false);
  assert.deepEqual(turn.outputSchema,INTENT_ROUTE_SCHEMA);assert.equal(Object.hasOwn(turn,'effort'),false);assert.equal(thread.ephemeral,true);assert.equal(thread.approvalPolicy,'never');assert.equal(thread.config.permissions[thread.config.default_permissions].network.enabled,false);
  assert.deepEqual(thread.config.mcp_servers.personal,{enabled:false});assert.equal(Object.hasOwn(thread,'dynamicTools'),false);assert.match(thread.baseInstructions,/not punctuation/);assert.match(thread.baseInstructions,/Never call tools/);
  assert(source.launches[0].args.includes('features.code_mode_host=false'));assert.equal(source.launches[0].args.includes('features.code_mode_host=true'),false);assert(source.launches[0].args.includes('features.shell_tool=false'));assert(source.launches[0].args.includes('features.browser_use=false'));
  assert.equal(source.launches[0].options.env.OPENAI_API_KEY,undefined);assert.equal(source.starts.length,1);assert.equal(source.starts[0].action,'route');assert.equal(source.starts[0].requestId,'route-1');assert(source.finishes.length>=1);
 }
 const normalized=fixture({response:route({intent:'edit',usePreviousAnswer:true,useSelectionInQuestion:true})});assert.deepEqual(await normalized.handle(request()),route({intent:'compose',model:'synthetic-model'}));assert.equal(normalized.native.length,0);
 const tableRequest=fixture({response:route({intent:'table_style'})});const tableDecision=await tableRequest.handle(request({hasTableCursor:true,prompt:'지금 1번째 표 양식으로 나머지 양식을 다 통일해줘'}));assert.equal(tableDecision.intent,'table_style');assert.equal(tableRequest.native.length,0);
 const noTable=fixture({response:route({intent:'table_style'})});await assert.rejects(noTable.handle(request()),/기준 표/);assert.equal(noTable.native.length,0);
 const selectedTable=fixture({response:route({intent:'table_style'})});assert.equal((await selectedTable.handle(request({hasTableCursor:true,hasSelection:true}))).intent,'table_style');assert.equal(selectedTable.native.length,0);
 for(const patch of [
  {prompt:'첫번째 표로 다른표 수정해줘',hasTables:true},
  {prompt:'첫표 기준으로 나머지 맞춰줘',hasTables:true,history:[{role:'assistant',text:'표의 내용은 유지하고 기준 디자인을 나머지 표에 적용합니다.'}]},
  {prompt:'첫 표 양식으로 나머지 표 통일해줘',hasTables:true,hasTableCursor:true}
 ]){
  const firstTable=fixture({response:route({intent:'table_style',tableReference:'first',needsDocumentContext:true})});
  const result=await firstTable.handle(request(patch));assert.equal(result.intent,'table_style');assert.equal(result.tableReference,'first');assert.equal(result.needsDocumentContext,true);
  const payload=JSON.parse(one(firstTable,'turn/start').params.input[0].text);assert.equal(payload.hasTables,true);assert.equal(payload.hasTableCursor,patch.hasTableCursor||false);assert.deepEqual(payload.conversation,patch.history||[]);
  assert.equal(inferCount(firstTable),1);assert.equal(firstTable.native.length,0);assert.equal(firstTable.starts[0].action,'route');assert.equal(firstTable.rpc.some(x=>x.method==='turn/start'&&x.params.outputSchema===PAGE_SCOPE_SCHEMA),false);
  assert.match(one(firstTable,'thread/start').params.baseInstructions,/does not require the caret/);assert.match(one(firstTable,'thread/start').params.baseInstructions,/table values\/content/);
 }
 const missingFirst=fixture({response:route({intent:'table_style',tableReference:'first'})});await assert.rejects(missingFirst.handle(request({hasTableCursor:true})),/첫 번째 기준 표/);assert.equal(missingFirst.native.length,0);
 const missingReference=fixture({response:route({intent:'table_style',tableReference:'none'})});await assert.rejects(missingReference.handle(request({hasTables:true,hasTableCursor:true})),/기준 표/);assert.equal(missingReference.native.length,0);
 const unrelatedReference=fixture({response:route({intent:'compose',tableReference:'first'})});await assert.rejects(unrelatedReference.handle(request({hasTables:true})),/표 편집이 아닌/);assert.equal(unrelatedReference.native.length,0);
 const contentUpdate=fixture({response:route({intent:'compose',needsDocumentContext:true})});assert.equal((await contentUpdate.handle(request({hasTables:true,prompt:'첫 표의 매출 수치로 다른 표의 값을 갱신해줘'}))).tableReference,'none');assert.equal(contentUpdate.native.length,0);
 const invalidTable=fixture();await assert.rejects(invalidTable.handle(request({hasTableCursor:'yes'})),/커서/);assert.equal(invalidTable.launches.length,0);
 const invalidTables=fixture();await assert.rejects(invalidTables.handle(request({hasTables:'yes'})),/문서 표/);assert.equal(invalidTables.launches.length,0);
 const questionFlags=fixture({response:route({needsImage:true,needsTable:true,useSelectionInQuestion:true})});const question=await questionFlags.handle(request());assert.equal(question.needsImage,false);assert.equal(question.needsTable,false);assert.equal(question.useSelectionInQuestion,false);
 const high=fixture();await high.handle(request({reasoningEffort:'runtime-only',model:'synthetic-model'}));assert.equal(one(high,'turn/start').params.effort,'runtime-only');
 for(const [patch,pattern] of [[{hasSelection:'yes'},/선택/],[{hasPreviousAnswer:1},/이전 답변/],[{prompt:''},/의도/],[{prompt:'x'.repeat(16001)},/의도/],[{usageConsent:false},/의도/],[{accountId:'bad'},/의도/],[{history:[{role:'system',text:'unsafe'}]},/대화/],[{history:Array.from({length:13},()=>({role:'user',text:'x'}))},/대화/],[{history:[{role:'user',text:'x'.repeat(6001)}]},/대화/],[{history:[{role:'user',text:'x'.repeat(6000)},{role:'assistant',text:'x'.repeat(6000)},{role:'user',text:'x'}]},/대화/]]){
  const f=fixture();await assert.rejects(f.handle(request(patch)),pattern);assert.equal(f.launches.length,0);assert.equal(f.native.length,0);
 }
 for(const response of [null,[],{},route({intent:'delete'}),route({tableReference:'second'}),route({tableReference:true}),route({needsTable:'yes'}),route({needsImage:null}),route({usePreviousAnswer:0}),route({useSelectionInQuestion:'false'}),{...route(),path:'C:/private'},Object.fromEntries(Object.entries(route()).filter(([key])=>key!=='needsImage')),Object.fromEntries(Object.entries(route()).filter(([key])=>key!=='tableReference'))]){
  const f=fixture({response});await assert.rejects(f.handle(request()),/형식/);assert.equal(inferCount(f),1);assert.equal(f.native.length,0);assert(f.children.every(x=>x.killed));
 }
 for(const [settings,patch,pattern] of [[{apiKey:true},{},/ChatGPT/],[{changedAccount:true},{},/계정/],[{badProfile:true},{},/권한/],[{mcp:true},{},/MCP/],[{}, {model:'missing'},/모델/],[{}, {reasoningEffort:'unknown'},/추론 강도/],[{modelCycle:true},{},/페이지/]]){
  const f=fixture(settings);await assert.rejects(f.handle(request(patch)),pattern);assert.equal(inferCount(f),0);assert.equal(f.native.length,0);
 }
 for(const settings of [{raw:'not JSON'},{tool:'dynamicToolCall'},{tool:'commandExecution'},{completionOnlyTool:true},{serverRequest:true},{error:true},{failedTurn:true}]){
  const f=fixture(settings);await assert.rejects(f.handle(request()));assert.equal(inferCount(f),1);assert.equal(f.native.length,0);assert(f.children.every(x=>x.killed));
 }
 const recovered=fixture({transient:true});assert.equal((await recovered.handle(request())).intent,'question');assert.equal(inferCount(recovered),1);assert.equal(recovered.native.length,0);
 const unavailable=fixture({error:true,errorCode:{httpConnectionFailed:{httpStatusCode:503}},willRetry:false});await assert.rejects(unavailable.handle(request()),e=>/연결에 실패/.test(e.message)&&/HTTP 503/.test(e.message)&&!e.message.includes('synthetic secret'));
 const preController=new AbortController();preController.abort(Error('synthetic pre-cancel'));const pre=fixture();await assert.rejects(pre.handle(request(),preController.signal),/pre-cancel/);assert.equal(pre.launches.length,0);
 const controller=new AbortController(),live=fixture({turnHangs:true,options:{onTurnStarted:()=>controller.abort(Error('synthetic live cancel'))}});await assert.rejects(live.handle(request(),controller.signal),/live cancel/);assert.equal(live.native.length,0);assert(live.children.every(x=>x.killed));
 const timeout=fixture({turnHangs:true,options:{turnTimeout:15}});await assert.rejects(deadline(timeout.handle(request())),/시간이 초과/);assert.equal(inferCount(timeout),1);assert.equal(timeout.native.length,0);
 // Main JSONL dispatcher accepts route without producing any native callbacks.
 const transport=fixture(),input=new PassThrough(),output=new PassThrough(),messages=[];output.on('data',chunk=>{for(const line of chunk.toString().trim().split('\n'))if(line)messages.push(JSON.parse(line));});
 const service=startEditorHost({runtime,input,output,handlerOptions:transport.options});input.write(JSON.stringify(request({id:'stdio-route'}))+'\n');await deadline(service.idle);assert.equal(messages.length,1);assert.equal(messages[0].ok,true);assert.equal(messages[0].data.intent,'question');assert.equal(messages.some(x=>x.kind==='editor.request'),false);service.close();
 // Routing hooks use the same active-turn correlation as generation. A steer
 // reaches this exact turn; cancellation does not execute any native callback.
 for(const cancel of [false,true]){
  const source=fixture(cancel?{turnHangs:true}:{finishOnSteer:true});const input=new PassThrough(),output=new PassThrough(),messages=[];let started;
  const ready=new Promise(resolve=>{started=resolve;});output.on('data',chunk=>{for(const line of chunk.toString().trim().split('\n'))if(line)messages.push(JSON.parse(line));});
  const service=startEditorHost({runtime,input,output,handlerOptions:{...source.options,onTurnStarted:value=>{source.starts.push(value);started(value);}}});
  input.write(JSON.stringify(request({id:'active-route'}))+'\n');const active=await deadline(ready);assert.equal(active.action,'route');assert.equal(active.requestId,'active-route');
  input.write(JSON.stringify(cancel?{id:'cancel-route',action:'cancel',targetId:'active-route'}:{id:'steer-route',action:'steer',targetId:'active-route',prompt:'질문 대신 방금 원고를 문서화해줘'})+'\n');await deadline(service.idle);await tick();
  assert.equal(source.native.length,0);assert.equal(inferCount(source),1);assert.equal(messages.some(x=>x.kind==='editor.request'),false);
  if(cancel){assert.equal(messages.find(x=>x.id==='cancel-route').data.cancelled,true);assert.equal(messages.find(x=>x.id==='active-route').ok,false);assert(source.children.every(x=>x.killed));}
  else{assert.equal(messages.find(x=>x.id==='steer-route').data.accepted,true);const steered=one(source,'turn/steer').params;assert.equal(steered.threadId,'intent-thread');assert.equal(steered.expectedTurnId,'intent-turn');assert.match(steered.input[0].text,/Latest user message/);assert.equal(Object.hasOwn(steered,'effort'),false);assert.equal(messages.find(x=>x.id==='active-route').ok,true);}
  service.close();
 }
 // Explicit route flags keep contextual assets mandatory before any generation,
 // fragment write or native mutation. False cannot remove lexical requirements.
 for(const [blocks,flags] of [[[paragraph('본문')],{needsTable:true}],[[paragraph('본문'),image()],{needsTable:true}],[[paragraph('본문')],{needsImage:true}],[[paragraph('본문'),table()],{needsImage:true}]]){
  const f=await richFixture(blocks);await assert.rejects(f.handle({...f.request,...flags}),/계획에 없습니다/);assert.equal(f.calls.some(x=>['image','fragment','hwp_editor_rich_apply'].includes(x.method)),false);assert.equal(inferCount(f),1);
  const payload=JSON.parse(one(f,'turn/start').params.input[0].text);if(flags.needsImage)assert.equal(payload.requirements.image,true);if(flags.needsTable)assert.equal(payload.requirements.table,true);
 }
 const all=await richFixture([paragraph('본문'),table(),image()]);assert.equal((await all.handle({...all.request,needsTable:true,needsImage:true})).appliedCount,1);assert.equal(all.calls.filter(x=>x.method==='image').length,1);assert.equal(all.calls.filter(x=>x.method==='hwp_editor_rich_apply').length,1);
 const memo={id:1,width:15591,lineColor:'#B6D7AE',fillColor:'#F0FFE9',activeColor:'#CFF1C7'};const memoFixture=await richFixture([table()],memo);await memoFixture.handle({...memoFixture.request,needsTable:true});assert.deepEqual(memoFixture.calls.find(x=>x.method==='fragment').memoDefinition,memo);
 const lexical=await richFixture([paragraph('본문')]);await assert.rejects(lexical.handle({...lexical.request,prompt:'표와 인포그래픽을 넣어줘',needsTable:false,needsImage:false}),/계획에 없습니다/);assert.equal(lexical.calls.some(x=>x.method==='image'),false);
 for(const bad of [{needsTable:'true'},{needsImage:1},{needsImage:null}]){const f=await richFixture([table(),image()]);await assert.rejects(f.handle({...f.request,...bad}),/boolean/);assert.equal(f.calls.length,0);assert.equal(f.launches.length,0);}
 assert.equal((await readdir(root)).some(name=>name.startsWith('stoom-hwp-route-')),false);
 console.log('PASS '+checks+' intent-routing/asset-requirement assertions. Real STOOM RPC class with synthetic pipes; no Codex/HWP process or inference.');
}finally{assert.equal(path.dirname(root),path.resolve(os.tmpdir()));assert(path.basename(root).startsWith('hwp-intent-test-'));await rm(root,{recursive:true,force:true});}
