/** Semantic document blocks. Paths, XML, and executable commands never come from the model. */
const fail=message=>{throw Object.assign(new Error(message),{stoomSafe:true});};
const object=value=>value!==null&&typeof value==='object'&&!Array.isArray(value);
const plain=(value,max=12000,allowEmpty=false)=>typeof value==='string'&&(allowEmpty||value.trim().length>0)&&value.length<=max&&!/[\u0000-\u001f\u007f-\u009f\u2028\u2029\ufffc\ud800-\udfff\ufdd0-\ufdef\ufffe\uffff]/.test(value);
const imageBrief=value=>typeof value==='string'&&value.trim().length>0&&value.length<=6000&&!/[\u0000-\u001f\u007f-\u009f\u2028\u2029\ufffc]/.test(value)&&[...value].every(ch=>{const n=ch.codePointAt(0);return !(n>=0xd800&&n<=0xdfff)&&!(n>=0xfdd0&&n<=0xfdef)&&(n&0xffff)!==0xfffe&&(n&0xffff)!==0xffff;});
// HWPX uses individual paragraphs rather than newline characters. Convert
// generated line breaks to separate paragraphs, preserving empty lines and role.
const lines=value=>value.replace(/\r\n?|\u2028|\u2029/g,'\n').replace(/\t/g,'    ').split('\n');
const cellLine=value=>value.replace(/\r\n?|\n|\u2028|\u2029|\t/g,' ');
const unused=(key,value)=>value===null||((key==='headers'||key==='rows')&&Array.isArray(value)&&value.length===0)||((key==='imagePrompt'||key==='caption')&&value==='');
const keys=['type','role','text','headers','rows','imagePrompt','caption'];
export const RICH_SCHEMA={type:'object',additionalProperties:false,required:['summary','blocks'],properties:{summary:{type:'string'},blocks:{type:'array',minItems:1,maxItems:64,items:{type:'object',additionalProperties:false,required:keys,properties:{
 type:{type:'string',enum:['paragraph','table','infographic']},role:{type:['string','null'],enum:['title','heading','body','bullet',null]},text:{type:['string','null']},headers:{type:['array','null'],items:{type:'string'},maxItems:8},rows:{type:['array','null'],maxItems:20,items:{type:'array',maxItems:8,items:{type:'string'}}},imagePrompt:{type:['string','null']},caption:{type:['string','null']}
}}}}};
export function richRequirements(prompt){
 const text=String(prompt??'');
 return{table:/(?:^|[\s,·와과])표\s*(?:로|를|와|과|도|형태|생성|작성|만들|정리)|테이블|\btable\b/i.test(text),image:/인포(?:그래픽)?|infographic|다이어그램|이미지|그림\s*(?:으로|을|생성|만들)/i.test(text)};
}
export function validateRichPlan(plan,requirements={}){
 if(!object(plan)||Object.keys(plan).length!==2||typeof plan.summary!=='string'||plan.summary.length>2000||!Array.isArray(plan.blocks)||!plan.blocks.length||plan.blocks.length>64)fail('표·이미지 본문 계획의 형식 또는 블록 수를 확인하세요.');
 let total=0,tables=0,images=0;
 const blocks=[];
 for(const [index,block] of plan.blocks.entries()){
  if(!object(block)||Object.keys(block).length!==keys.length||keys.some(key=>!Object.hasOwn(block,key))||Object.keys(block).some(key=>!keys.includes(key)))fail('본문 계획에는 허용한 의미 블록만 사용할 수 있습니다.');
  if(block.type==='paragraph'){
   if((block.role!==null&&!['title','heading','body','bullet'].includes(block.role))||(block.text!==null&&typeof block.text!=='string')||['headers','rows','imagePrompt','caption'].some(key=>!unused(key,block[key])))fail((index+1)+'번째 문단의 역할 또는 다른 블록의 내용이 올바르지 않습니다.');
   const sourceRole=block.role??'body';
   for(const text of lines(block.text??'')){
    if(!plain(text,12000,true))fail((index+1)+'번째 문단에 지원하지 않는 문자나 12,000자를 넘는 줄이 있습니다.');
    // A blank line is spacing, not a visible bullet marker.
    const role=sourceRole==='bullet'&&!text.trim()?'body':sourceRole;
    total+=text.length+(role==='bullet'?2:0);blocks.push({type:'paragraph',role,text});
    if(blocks.length>64)fail('줄바꿈을 문단으로 나눈 결과가 64개를 넘습니다.');
   }
   continue;
  }
  if(block.type==='table'){
   if(++tables>3||block.role!==null||block.text!==null||!unused('imagePrompt',block.imagePrompt)||!unused('caption',block.caption)||!Array.isArray(block.headers)||!block.headers.length||block.headers.length>8||block.headers.some(s=>typeof s!=='string')||!Array.isArray(block.rows)||!block.rows.length||block.rows.length>20||block.rows.some(row=>!Array.isArray(row)||row.length!==block.headers.length||row.some(s=>typeof s!=='string')))fail('표는 1~8열, 머리행과 1~20개 내용 행, 최대 3개까지 작성할 수 있습니다.');
   const headers=block.headers.map(cellLine),rows=block.rows.map(row=>row.map(cellLine));
   if(headers.some(s=>!plain(s,1000,true))||rows.some(row=>row.some(s=>!plain(s,2000,true))))fail((index+1)+'번째 표에 지원하지 않는 문자나 너무 긴 셀이 있습니다.');
   total+=headers.join('').length+rows.flat().join('').length;blocks.push({type:'table',headers,rows});
   continue;
  }
  if(block.type==='infographic'){
   if(++images>2||block.role!==null||block.text!==null||!unused('headers',block.headers)||!unused('rows',block.rows)||typeof block.imagePrompt!=='string'||(block.caption!==null&&typeof block.caption!=='string'))fail('인포그래픽은 생성 지시가 있는 이미지 최대 2개까지 지원합니다.');
   const imagePrompt=cellLine(block.imagePrompt),caption=cellLine(block.caption??'');
   if(!imageBrief(imagePrompt)||!plain(caption,1000,true))fail((index+1)+'번째 인포그래픽의 지시 또는 설명에 지원하지 않는 문자가 있습니다.');
   total+=caption.length;blocks.push({type:'infographic',prompt:imagePrompt,caption});
   continue;
  }
  fail('지원하지 않는 본문 블록입니다.');
 }
 if(blocks.length>64)fail('작성할 문단·표·이미지는 총 64개 이하여야 합니다.');
 if(total>24000)fail('표와 문단을 합친 본문은 24,000자 이하여야 합니다.');
 if(requirements.table&&!tables)fail('요청한 실제 표가 계획에 없습니다. 본문은 변경하지 않았습니다.');
 if(requirements.image&&!images)fail('요청한 인포그래픽이 계획에 없습니다. 본문은 변경하지 않았습니다.');
 return{summary:plan.summary,blocks,tableCount:tables,imageCount:images};
}
