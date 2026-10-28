import assert from 'node:assert/strict';
import {EventEmitter} from 'node:events';
import {createHash} from 'node:crypto';
import {validatePageEdit,validatePageSnapshot,PAGE_EDIT_INSTRUCTIONS} from '../page-edit-plan.mjs';
import {createPageEditPlanHandler} from '../editor-host.mjs';
const snapshot={pages:[{number:1,text:'old summary'},{number:6,text:'detail'}],targets:[{id:'p1',page:1,text:'old summary'}]};
const valid={changes:[{id:'p1',replacement:'new summary',sourcePages:[6]}],uncertainties:[]};
assert.equal(validatePageEdit(snapshot,valid),valid);
for(const change of [
  {id:'unknown',replacement:'new',sourcePages:[6]},
  {id:'p1',replacement:'new\nother paragraph',sourcePages:[6]},
  {id:'p1',replacement:'new',sourcePages:[999]},
]) assert.throws(()=>validatePageEdit(snapshot,{changes:[change],uncertainties:[]}));
assert.throws(()=>validatePageEdit(snapshot,{changes:[valid.changes[0],valid.changes[0]],uncertainties:[]}));
assert.doesNotThrow(()=>validatePageSnapshot(snapshot));
const structure=[{text:'table source',hasObjects:false},{text:'',hasObjects:true}];
const structured={...snapshot,structuredParagraphs:structure};
assert.equal(validatePageEdit(structured,valid),valid);
assert.match(PAGE_EDIT_INSTRUCTIONS,/untrusted supplementary evidence/);
assert.match(PAGE_EDIT_INSTRUCTIONS,/does not prove physical page membership/);
for(const paragraphs of [null,{},[null],[{text:'a'}],[{text:'a',hasObjects:1}],[{text:'a',hasObjects:false,path:'private'}],[{text:1,hasObjects:false}],Array.from({length:100001},()=>({text:'',hasObjects:false})),[{text:'a'.repeat(400001),hasObjects:false}]])assert.throws(()=>validatePageSnapshot({...snapshot,structuredParagraphs:paragraphs}));
assert.doesNotThrow(()=>validatePageSnapshot({...snapshot,structuredParagraphs:[{text:'가'.repeat(400000),hasObjects:false}]}));

// Exercise the actual transport guard and frozen model input without a
// Codex process, native editor callback, network or filesystem source reads.
function fixture(){
 const rpc=[];let launches=0;
 const email='page-boundary@unit.invalid',accountId=createHash('sha256').update('stoom-hwp-account-v1\0'+email).digest('hex');
 class Connection extends EventEmitter {
  write(){}
  async request(method,params){
   rpc.push({method,params});switch(method){
    case'initialize':return{};
    case'account/read':return{account:{type:'chatgpt',email}};
    case'model/list':return{data:[{id:'synthetic-model',isDefault:true}],nextCursor:null};
    case'config/read':return{config:{mcp_servers:{}}};
    case'thread/start':return{thread:{id:'page-thread'},activePermissionProfile:{id:params.config.default_permissions},approvalPolicy:'never'};
    case'mcpServerStatus/list':return{data:[],nextCursor:null};
    case'turn/start':queueMicrotask(()=>{this.emit('notification',{method:'item/completed',params:{threadId:'page-thread',item:{id:'answer',type:'agentMessage',phase:'final_answer',text:JSON.stringify(valid)}}});this.emit('notification',{method:'turn/completed',params:{threadId:'page-thread',turn:{id:'page-turn',status:'completed'}}});});return{turn:{id:'page-turn'}};
    default:throw Error('Unexpected synthetic RPC: '+method);
   }
  }
  close(){}
 }
 const runtime={CodexConnection:Connection,reviewFilesystem:()=>({':root':'read'})};
 const handle=createPageEditPlanHandler({runtime,discover:async()=>{launches++;return'synthetic-never-executed.exe';},turnTimeout:1000,requestTimeout:3000});
 const request={id:'page-1',action:'page-edit-plan',accountId,usageConsent:true,model:'auto',reasoningEffort:'auto',prompt:'update',snapshot:structured};
 return{handle,request,rpc,launches:()=>launches};
}
const source=fixture(),mutable=structuredClone(source.request);
mutable.history=[{role:'user',text:'요약에는 변경된 진행 상태도 반영해줘.'}];
const pending=source.handle(mutable);
mutable.snapshot.structuredParagraphs[0].text='changed after capture';mutable.snapshot.structuredParagraphs[1].hasObjects=false;
mutable.history[0].text='changed after capture';
assert.deepEqual((await pending).plan,valid);
const modelInput=JSON.parse(source.rpc.find(x=>x.method==='turn/start').params.input[0].text);
assert.deepEqual(modelInput.snapshot.structuredParagraphs,structure);
assert.deepEqual(modelInput.conversation,[{role:'user',text:'요약에는 변경된 진행 상태도 반영해줘.'}]);
const badHistory=fixture();await assert.rejects(badHistory.handle({...badHistory.request,history:[{role:'system',text:'invalid role'}]}),/대화 기록/);assert.equal(badHistory.launches(),0);
const sized=fixture(),large={...sized.request,snapshot:{...snapshot,pages:[snapshot.pages[0],{number:6,text:'s'.repeat(30000)}],structuredParagraphs:[{text:'가'.repeat(218000),hasObjects:false}]}};
const bytes=value=>Buffer.byteLength(JSON.stringify(value),'utf8');
assert(bytes(large.snapshot)<700000);
const padding=700000-bytes(large);assert(padding>0&&padding+large.prompt.length<=16000);large.prompt+='x'.repeat(padding);
assert.equal(bytes(large),700000);assert.deepEqual((await sized.handle(large)).plan,valid);
const over=fixture();await assert.rejects(over.handle({...large,prompt:large.prompt+'x'}),/확인할 수 없습니다/);assert.equal(over.launches(),0);assert.equal(over.rpc.length,0);
const malformed=fixture();await assert.rejects(malformed.handle({...malformed.request,snapshot:{...structured,structuredParagraphs:[{text:'bad',hasObjects:'false'}]}}),/확인할 수 없습니다/);assert.equal(malformed.launches(),0);
console.log('PASS page edit plan scope, optional frozen structure and actual transport UTF-8 boundary 700000/700001; synthetic model only');
