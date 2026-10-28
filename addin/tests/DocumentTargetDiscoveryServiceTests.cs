using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using HAddon;
using Hwp.Addon;
namespace HAddon {
 internal sealed class NativePageSnapshot{internal int DocumentId;internal string Path,Digest;internal string[] Pages;internal DocumentParagraph[] Paragraphs;}
 internal sealed class PageScope{internal int[] Targets,Sources;}
 internal sealed class BridgeClient{
  internal Action DuringRequest;internal Dictionary<string,object> Sent;internal string TargetId="p0";internal int Calls;
  public Task<Dictionary<string,object>> Request(Dictionary<string,object> request,string id){Calls++;Sent=request;if(DuringRequest!=null)DuringRequest();return Task.FromResult(new Dictionary<string,object>{{"plan",new Dictionary<string,object>{{"targets",new object[]{TargetId}},{"uncertainties",new object[]{"needs review"}}}}});}
  public static Dictionary<string,object> Data(Dictionary<string,object> input){return input;}
 }
}
class DocumentTargetDiscoveryServiceTests {
 const string Xml="<HWPML><BODY><SECTION><P><TEXT><CHAR>old</CHAR></TEXT></P></SECTION></BODY></HWPML>";
 sealed class Proxy:RealProxy{
  internal int Id=7;internal string XmlValue=Xml;internal Proxy():base(typeof(IHwpObject)){}
  public override IMessage Invoke(IMessage message){var m=(IMethodCallMessage)message;try{object r;switch(m.MethodName){case "GetID":r=Id;break;case "GetPath":r="PRIVATE_PATH";break;case "GetPageCount":r=(long)2;break;case "GetTextFile":r=XmlValue;break;default:throw new Exception("unexpected or mutating native call: "+m.MethodName);}return new ReturnMessage(r,m.Args,m.ArgCount,m.LogicalCallContext,m);}catch(Exception e){return new ReturnMessage(e,m);}}
  internal IHwpObject Doc(){return (IHwpObject)GetTransparentProxy();}
 }
 static NativePageSnapshot Snapshot(){return new NativePageSnapshot{DocumentId=7,Path="PRIVATE_PATH",Digest=TableStableSnapshot.Digest(Xml),Pages=new[]{"old","source"},Paragraphs=new[]{new DocumentParagraph{Path="BODY/SECTION[0]/P[0]",Text="old"}}};}
 static PageScope Scope(){return new PageScope{Targets=new[]{1},Sources=new[]{2}};}
 static void Reject(Action a){try{a();}catch(InvalidOperationException){return;}throw new Exception("expected rejection");}
 static void Assert(bool ok,string msg){if(!ok)throw new Exception(msg);}
 static void Main(){
  AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e){string n=new AssemblyName(e.Name).Name;return new[]{"HwpAddonHelper","Hnc.Foundation.Interface","Hnc.Foundation.InternalInterface"}.Contains(n)?Assembly.LoadFrom(System.IO.Path.Combine(@"C:\Program Files (x86)\Hnc\Office 2024\HOffice130\Bin",n+".dll")):null;};
  Run();
 }
 static void Run(){
  var p=new Proxy();var doc=p.Doc();var snapshot=Snapshot();var bridge=new BridgeClient();
  bridge.DuringRequest=()=>{snapshot.Pages[0]="forged";snapshot.Paragraphs[0].Text="forged";snapshot.Path="forged";};
  var result=DocumentTargetDiscoveryService.Analyze(bridge,snapshot,Scope(),()=>doc,"id","acct","auto","auto","요약 갱신").GetAwaiter().GetResult();
  Assert(result.Targets.Length==1&&result.Targets[0].Text=="old"&&result.Uncertainties[0]=="needs review","frozen targets and uncertainty");
  string payload=new JavaScriptSerializer().Serialize(bridge.Sent);
  Assert((string)bridge.Sent["action"]=="document-target-discovery"&&payload.Contains("old")&&!payload.Contains("PRIVATE_PATH")&&!payload.Contains("BODY/SECTION"),"scope or path leaked");
  p=new Proxy();doc=p.Doc();bridge=new BridgeClient();bridge.DuringRequest=()=>p.XmlValue=Xml.Replace("old","new");
  Reject(()=>DocumentTargetDiscoveryService.Analyze(bridge,Snapshot(),Scope(),()=>doc,"id","acct","auto","auto","요약 갱신").GetAwaiter().GetResult());
  p=new Proxy();doc=p.Doc();bridge=new BridgeClient();bridge.DuringRequest=()=>p.Id=8;
  Reject(()=>DocumentTargetDiscoveryService.Analyze(bridge,Snapshot(),Scope(),()=>doc,"id","acct","auto","auto","요약 갱신").GetAwaiter().GetResult());
  p=new Proxy();doc=p.Doc();bridge=new BridgeClient{TargetId="p999"};
  Reject(()=>DocumentTargetDiscoveryService.Analyze(bridge,Snapshot(),Scope(),()=>doc,"id","acct","auto","auto","요약 갱신").GetAwaiter().GetResult());
  p=new Proxy();doc=p.Doc();bridge=new BridgeClient();var large=Snapshot();large.Pages=new[]{new string('가',30000),new string('나',30000)};large.Paragraphs[0].Text=new string('다',165000);
  Reject(()=>DocumentTargetDiscoveryService.Analyze(bridge,large,Scope(),()=>doc,"id","acct","auto","auto",new string('라',10000)).GetAwaiter().GetResult());
  Assert(bridge.Calls==0&&large.Paragraphs[0].Text.Length==165000,"oversized full request sent or truncated");
  Console.WriteLine("PASS read-only discovery bridge, pinned snapshot, stale document and response checks; no HWP runtime");
 }
}
