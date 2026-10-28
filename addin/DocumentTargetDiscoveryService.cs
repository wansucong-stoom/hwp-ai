using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Hwp.Addon;

namespace HAddon {
 // Sends a frozen document snapshot to an ephemeral read-only analysis turn.
 // Result is only a structural proposal; no native positions are assumed and
 // no target is written. User prompt must authorize the full supplied scope.
 internal static class DocumentTargetDiscoveryService {
  internal static Task<DocumentDiscoveryResult> Analyze(BridgeClient bridge,NativePageSnapshot snapshot,PageScope scope,Func<IHwpObject> getDocument,string requestId,string accountId,string model,string effort,string prompt){return AnalyzeCore(bridge,snapshot,scope,getDocument,requestId,accountId,model,effort,prompt,true);}
  internal static Task<DocumentDiscoveryResult> AnalyzeCoordinated(BridgeClient bridge,NativePageSnapshot snapshot,PageScope scope,Func<IHwpObject> getDocument,string requestId,string accountId,string model,string effort,string prompt,object history=null){return AnalyzeCore(bridge,snapshot,scope,getDocument,requestId,accountId,model,effort,prompt,false,history);}
  static async Task<DocumentDiscoveryResult> AnalyzeCore(BridgeClient bridge,NativePageSnapshot snapshot,PageScope scope,Func<IHwpObject> getDocument,string requestId,string accountId,string model,string effort,string prompt,bool verifyDocument,object history=null){
   if(bridge==null||snapshot==null||scope==null||getDocument==null||snapshot.Pages==null||snapshot.Paragraphs==null)throw new ArgumentNullException("문서 분석 대상");
   if(String.IsNullOrWhiteSpace(prompt)||prompt.Length>16000)throw new InvalidOperationException("문서 분석 요청이 올바르지 않습니다.");
   int id=snapshot.DocumentId,count=snapshot.Pages.Length;string path=snapshot.Path??"",digest=snapshot.Digest;
   if(String.IsNullOrEmpty(digest))throw new InvalidOperationException("원래 문서 상태를 확인하지 못했습니다.");
   // Constructor copies content, paths and scopes. No caller-owned mutable
   // data survives the first await or determines what is validated later.
   var discovery=new DocumentTargetDiscovery(snapshot.Pages,snapshot.Paragraphs,scope.Targets,scope.Sources);
   if(verifyDocument)Check(getDocument(),id,path,digest,count);
   var request=new Dictionary<string,object>{{"action","document-target-discovery"},{"accountId",accountId},{"usageConsent",true},{"model",model},{"reasoningEffort",effort},{"prompt",prompt},{"snapshot",discovery.Payload()}};
   if(history!=null)request["history"]=history;
   // The transport has a 768 KiB line cap. Count the entire serialized
   // envelope (not merely snapshot characters), including UTF-8 multibyte
   // text and the request ID. Reject without sending or truncating anything.
   var sized=new Dictionary<string,object>(request);sized["id"]=requestId??new string('0',32);
   int bytes;try{string encoded=new JavaScriptSerializer{MaxJsonLength=1024*1024}.Serialize(sized);bytes=Encoding.UTF8.GetByteCount(encoded);}
   catch(Exception){throw new InvalidOperationException("문서 전체 분석 요청의 크기를 확인하지 못했습니다. 내용을 보내지 않았습니다.");}
   if(bytes>700000)throw new InvalidOperationException("문서 전체 분석 요청이 전송 한도를 초과했습니다. 내용을 잘라 보내지 않았습니다.");
   var response=BridgeClient.Data(await bridge.Request(request,requestId));
   if(verifyDocument)Check(getDocument(),id,path,digest,count);object raw;
   if(response==null||!response.TryGetValue("plan",out raw)||!(raw is Dictionary<string,object>))throw new InvalidOperationException("문단 대상 분석 결과를 확인하지 못했습니다.");
   return discovery.Validate((Dictionary<string,object>)raw);
  }
  static void Check(IHwpObject doc,int id,string path,string digest,int count){
   if(doc==null||doc.GetID()!=id||(doc.GetPath()??"")!=path||doc.GetPageCount()!=count||TableStableSnapshot.Digest(doc.GetTextFile("HWPML2X",""))!=digest)throw new InvalidOperationException("문단을 분석하는 동안 문서가 바뀌었습니다. 다시 요청하세요.");
  }
 }
}
