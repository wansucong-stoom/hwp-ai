using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hwp.Addon;

namespace HAddon {
 // Analysis is read-only. The compiled result is not applied by this service.
 // A future executor must recheck the frozen snapshot and safely verify writes.
 internal static class TableDesignService {
  internal static async Task<TableDesignChange[]> Analyze(BridgeClient bridge,TableCursorContext pinned,Func<IHwpObject> getDocument,string requestId,string accountId,string model,string effort,string prompt,object history=null){
   if(bridge==null||pinned==null||pinned.DesignSnapshot==null||getDocument==null)throw new ArgumentNullException("표 분석 대상");
   var doc=getDocument();Check(pinned,doc);
   var payload=new Dictionary<string,object>{{"action","table-design"},{"accountId",accountId},{"usageConsent",true},{"model",model},{"reasoningEffort",effort},{"prompt",prompt},{"snapshot",pinned.DesignSnapshot}};
   if(history!=null)payload["history"]=history;
   var data=BridgeClient.Data(await bridge.Request(payload,requestId));
   Check(pinned,getDocument());object raw;
   if(!data.TryGetValue("plan",out raw)||!(raw is Dictionary<string,object>))throw new InvalidOperationException("표 디자인 분석 결과를 확인하지 못했습니다.");
   return TableDesignCompiler.Compile(pinned.DesignSnapshot,(Dictionary<string,object>)raw);
  }
  static void Check(TableCursorContext pinned,IHwpObject doc){
   if(doc==null||doc.IsReadOnlyMode()||doc.GetID()!=pinned.DocumentId||(doc.GetPath()??"")!=pinned.Path)throw new InvalidOperationException("기준 표를 포함한 원래 문서를 확인하지 못했습니다.");
   string current=doc.GetTextFile("HWPML2X","");
   if(TableStableSnapshot.Digest(pinned.Original.Document.OuterXml)!=TableStableSnapshot.Digest(current))throw new InvalidOperationException("표를 분석하는 동안 문서 내용이나 서식이 바뀌었습니다. 다시 요청하세요.");
  }
 }
}
