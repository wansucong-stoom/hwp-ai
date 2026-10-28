using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Threading.Tasks;
using System.Text;
using System.Web.Script.Serialization;
using HAddon;
using Hwp.Addon;
namespace HAddon {
 internal sealed class BridgeClient {
  internal Action DuringRequest;internal int Calls;internal Dictionary<string,object> Sent;
  public Task<Dictionary<string,object>> Request(Dictionary<string,object> request,string id){
   Calls++;Sent=request;
   if((string)request["action"]!="page-edit-plan"||!(bool)request["usageConsent"])throw new Exception("invalid request");
   if(DuringRequest!=null)DuringRequest();
   var change=new Dictionary<string,object>{{"id","a"},{"replacement","new"},{"sourcePages",new object[]{2}}};
   return Task.FromResult(new Dictionary<string,object>{{"plan",new Dictionary<string,object>{{"changes",new object[]{change}},{"uncertainties",new object[0]}}}});
  }
  public static Dictionary<string,object> Data(Dictionary<string,object> response){return response;}
 }
}
class PageEditServiceTests {
 sealed class Fake:RealProxy {
  internal bool Changed;
  internal Fake():base(typeof(IHwpObject)){}
  public override IMessage Invoke(IMessage message){var m=(IMethodCallMessage)message;var a=m.Args;try{object value;
   switch(m.MethodName){case "GetID":value=1;break;case "GetPath":value="document.hwp";break;case "GetPageCount":value=(long)2;break;case "GetTextFile":value=Changed?"<HWPML><BODY>changed</BODY></HWPML>":"<HWPML><BODY>original</BODY></HWPML>";break;default:throw new Exception("unexpected "+m.MethodName);}
   return new ReturnMessage(value,a,a.Length,m.LogicalCallContext,m);
  }catch(Exception ex){return new ReturnMessage(ex,m);}}
 }
 static void Main(){
  AppDomain.CurrentDomain.AssemblyResolve+=delegate(object s,ResolveEventArgs e){string n=new AssemblyName(e.Name).Name;return Assembly.LoadFrom(System.IO.Path.Combine(@"C:\Program Files (x86)\Hnc\Office 2024\HOffice130\Bin",n+".dll"));};
  Run();
 }
 static void Run(){
  var f=new Fake();var doc=(IHwpObject)f.GetTransparentProxy();var pages=new[]{"old","source"};
  var snapshot=new NativePageSnapshot{DocumentId=1,Path="document.hwp",Digest=TableStableSnapshot.Digest("<HWPML><BODY>original</BODY></HWPML>"),Pages=pages};
  var planning=new PageEditPlanning(pages,new[]{new DocumentAddressCandidate{XmlPath="a",Text="old",Page=1}},new[]{1},new[]{2});
  var bridge=new BridgeClient();var result=PageEditService.Analyze(bridge,snapshot,planning,()=>doc,"request","account","auto","auto","update").GetAwaiter().GetResult();
  if(result.Length!=1||result[0].Replacement!="new")throw new Exception("invalid response");
  bridge.DuringRequest=()=>f.Changed=true;bool rejected=false;try{PageEditService.Analyze(bridge,snapshot,planning,()=>doc,"request","account","auto","auto","update").GetAwaiter().GetResult();}catch(InvalidOperationException){rejected=true;}
  if(!rejected)throw new Exception("stale document accepted");
  f.Changed=false;bridge.DuringRequest=null;
  var structured=new PageEditPlanning(pages,new[]{new DocumentAddressCandidate{XmlPath="a",Text="old",Page=1}},new[]{1},new[]{2},new[]{new DocumentParagraph{Text="table source",HasObjects=true}});
  PageEditService.Analyze(bridge,snapshot,structured,()=>doc,"request","account","auto","auto","update").GetAwaiter().GetResult();
  var sent=(Dictionary<string,object>)bridge.Sent["snapshot"];if((string)((Dictionary<string,object>)((object[])sent["structuredParagraphs"])[0])["text"]!="table source")throw new Exception("structure omitted from bridge request");
  var large=new PageEditPlanning(new[]{"old",new string('s',30000)},new[]{new DocumentAddressCandidate{XmlPath="a",Text="old",Page=1}},new[]{1},new[]{2},new[]{new DocumentParagraph{Text=new string('가',218000)}});
  var serializer=new JavaScriptSerializer{MaxJsonLength=1024*1024};
  var sized=new Dictionary<string,object>{{"action","page-edit-plan"},{"accountId","account"},{"usageConsent",true},{"model","auto"},{"reasoningEffort","auto"},{"prompt","update"},{"snapshot",large.Snapshot},{"id","request"}};
  int baseBytes=Encoding.UTF8.GetByteCount(serializer.Serialize(sized));int padding=700000-baseBytes;if(padding<1||padding+6>16000||Encoding.UTF8.GetByteCount(serializer.Serialize(large.Snapshot))>=700000)throw new Exception("invalid byte-boundary fixture");
  string atLimit="update"+new string('x',padding);PageEditService.Analyze(bridge,snapshot,large,()=>doc,"request","account","auto","auto",atLimit).GetAwaiter().GetResult();int calls=bridge.Calls;
  rejected=false;try{PageEditService.Analyze(bridge,snapshot,large,()=>doc,"request","account","auto","auto",atLimit+"x").GetAwaiter().GetResult();}catch(InvalidOperationException){rejected=true;}
  if(!rejected||bridge.Calls!=calls)throw new Exception("entire request byte limit not enforced before transmission");
  Console.WriteLine("PASS page service optional structure, document-change rejection and whole-request UTF-8 boundary 700000/700001");
 }
}
