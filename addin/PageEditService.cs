using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text;
using System.Web.Script.Serialization;
using Hwp.Addon;

namespace HAddon {
 // Read-only planning. The automatic workflow establishes native target
 // positions and source context before using these proposed replacements.
 internal static class PageEditService {
  internal static Task<DocumentUpdateTarget[]> Analyze(BridgeClient bridge,NativePageSnapshot captured,PageEditPlanning scoped,Func<IHwpObject> getDocument,string requestId,string accountId,string model,string effort,string prompt){return AnalyzeCore(bridge,captured,scoped,getDocument,requestId,accountId,model,effort,prompt,true);}
  internal static Task<DocumentUpdateTarget[]> AnalyzeCoordinated(BridgeClient bridge,NativePageSnapshot captured,PageEditPlanning scoped,Func<IHwpObject> getDocument,string requestId,string accountId,string model,string effort,string prompt,object history=null){return AnalyzeCore(bridge,captured,scoped,getDocument,requestId,accountId,model,effort,prompt,false,history);}
  static async Task<DocumentUpdateTarget[]> AnalyzeCore(BridgeClient bridge,NativePageSnapshot captured,PageEditPlanning scoped,Func<IHwpObject> getDocument,string requestId,string accountId,string model,string effort,string prompt,bool verifyDocument,object history=null){
   if(bridge==null||captured==null||scoped==null||getDocument==null)throw new ArgumentNullException("페이지 분석 대상");
   if(verifyDocument)Check(getDocument(),captured);
   var request=new Dictionary<string,object>{{"action","page-edit-plan"},{"accountId",accountId},{"usageConsent",true},{"model",model},{"reasoningEffort",effort},{"prompt",prompt},{"snapshot",scoped.Snapshot}};
   if(history!=null)request["history"]=history;
   var sized=new Dictionary<string,object>(request);sized["id"]=requestId??new string('0',32);
   if(Encoding.UTF8.GetByteCount(new JavaScriptSerializer{MaxJsonLength=1024*1024}.Serialize(sized))>700000)throw new InvalidOperationException("쪽 편집 문맥이 전송 한도를 넘습니다. 내용을 자르거나 보내지 않았습니다.");
   var data=BridgeClient.Data(await bridge.Request(request,requestId));if(verifyDocument)Check(getDocument(),captured);object raw;
   if(data==null||!data.TryGetValue("plan",out raw)||!(raw is Dictionary<string,object>))throw new InvalidOperationException("쪽 편집 계획 응답을 확인하지 못했습니다.");
   return scoped.Compile((Dictionary<string,object>)raw);
  }
  static void Check(IHwpObject doc,NativePageSnapshot snapshot){
   if(doc==null||doc.GetID()!=snapshot.DocumentId||(doc.GetPath()??"")!=snapshot.Path||doc.GetPageCount()!=snapshot.Pages.Length||TableStableSnapshot.Digest(doc.GetTextFile("HWPML2X",""))!=snapshot.Digest)throw new InvalidOperationException("쪽을 분석하는 동안 문서가 바뀌었습니다. 다시 요청하세요.");
  }
 }
}
