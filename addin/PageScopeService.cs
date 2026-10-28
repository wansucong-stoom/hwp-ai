using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hwp.Addon;

namespace HAddon {
 // Model proposes page ranges only. It does not prove actual paragraph
 // positions or authorize an update, and is not wired into the UI yet.
 internal sealed class PageScope {internal int[] Targets,Sources;}
 internal static class PageScopeService {
  static object[] ArrayValue(object value){if(value==null||value is string||value is IDictionary)throw new InvalidOperationException("쪽 범위 결과가 올바르지 않습니다.");var list=value as IEnumerable;if(list==null)throw new InvalidOperationException("쪽 범위 결과가 올바르지 않습니다.");return list.Cast<object>().ToArray();}
  static int[] Pages(object value,int count){
   var result=new List<int>();var used=new HashSet<int>();foreach(var item in ArrayValue(value)){
    if(!(item is int||item is long))throw new InvalidOperationException("쪽 번호가 올바르지 않습니다.");
    int n;try{n=Convert.ToInt32(item);}catch{throw new InvalidOperationException("쪽 번호가 올바르지 않습니다.");}
    if(n<1||n>count||!used.Add(n)||(result.Count>0&&n<=result[result.Count-1]))throw new InvalidOperationException("쪽 번호가 문서 범위를 벗어나거나 중복됩니다.");result.Add(n);
   }
   if(result.Count==0)throw new InvalidOperationException("쪽 범위를 정하지 못했습니다.");return result.ToArray();
  }
  internal static PageScope Compile(Dictionary<string,object> data,int pageCount){
   if(pageCount<1||pageCount>256||data==null||!data.ContainsKey("targetPages")||!data.ContainsKey("sourcePages")||!data.ContainsKey("uncertainties"))throw new InvalidOperationException("쪽 범위를 확인하지 못했습니다.");
   var uncertain=ArrayValue(data["uncertainties"]);if(uncertain.Length>64||uncertain.Any(x=>!(x is string)||((string)x).Length>500))throw new InvalidOperationException("쪽 범위의 불확실성 정보가 올바르지 않습니다.");
   if(uncertain.Length>0)throw new InvalidOperationException("요청한 쪽 범위를 확정하지 못했습니다.");
   return new PageScope{Targets=Pages(data["targetPages"],pageCount),Sources=Pages(data["sourcePages"],pageCount)};
  }
  internal static Task<PageScope> Analyze(BridgeClient bridge,NativePageSnapshot snapshot,Func<IHwpObject> getDocument,string requestId,string accountId,string model,string effort,string prompt,object history){return AnalyzeCore(bridge,snapshot,getDocument,requestId,accountId,model,effort,prompt,history,true);}
  // Only AutomaticPageUpdate may own these before/after async checks. This
  // overload performs analysis only; it never authorizes a native mutation.
  internal static Task<PageScope> AnalyzeCoordinated(BridgeClient bridge,NativePageSnapshot snapshot,Func<IHwpObject> getDocument,string requestId,string accountId,string model,string effort,string prompt,object history){return AnalyzeCore(bridge,snapshot,getDocument,requestId,accountId,model,effort,prompt,history,false);}
  static async Task<PageScope> AnalyzeCore(BridgeClient bridge,NativePageSnapshot snapshot,Func<IHwpObject> getDocument,string requestId,string accountId,string model,string effort,string prompt,object history,bool verifyDocument){
   if(bridge==null||snapshot==null||snapshot.Pages==null||getDocument==null)throw new ArgumentNullException("문서 쪽 범위");
   if(verifyDocument)Check(getDocument(),snapshot);
   var request=new Dictionary<string,object>{{"action","page-scope"},{"accountId",accountId},{"usageConsent",true},{"model",model},{"reasoningEffort",effort},{"prompt",prompt},{"pageCount",snapshot.Pages.Length}};
   if(history!=null)request["history"]=history;
   var result=BridgeClient.Data(await bridge.Request(request,requestId));if(verifyDocument)Check(getDocument(),snapshot);return Compile(result,snapshot.Pages.Length);
  }
  static void Check(IHwpObject doc,NativePageSnapshot s){if(doc==null||doc.GetID()!=s.DocumentId||(doc.GetPath()??"")!=s.Path||doc.GetPageCount()!=s.Pages.Length||TableStableSnapshot.Digest(doc.GetTextFile("HWPML2X",""))!=s.Digest)throw new InvalidOperationException("쪽 범위를 분석하는 동안 문서가 바뀌었습니다.");}
 }
}
