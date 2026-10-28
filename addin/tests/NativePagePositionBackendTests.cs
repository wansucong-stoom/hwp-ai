using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using HAddon;
using Hwp.Addon;
class NativePagePositionBackendTests {
 sealed class Proxy:RealProxy {
  readonly Func<IMethodCallMessage,object[],object> call;internal Proxy(Type type,Func<IMethodCallMessage,object[],object> fn):base(type){call=fn;}
  public override IMessage Invoke(IMessage msg){var c=(IMethodCallMessage)msg;var a=c.Args;try{return new ReturnMessage(call(c,a),a,a.Length,c.LogicalCallContext,c);}catch(Exception e){return new ReturnMessage(e,c);}}
 }
 sealed class Fake {
  internal int List=0,Paragraph=0,Position=15,PageReads;internal bool UserMoves,Split;
  internal const string Xml="<HWPML><BODY><SECTION><P><TEXT><CHAR>abc</CHAR></TEXT></P></SECTION></BODY></HWPML>";
  internal IHwpObject Make(){
   var set=(IHwpParameterSet)new Proxy(typeof(IHwpParameterSet),(m,a)=>{if(m.MethodName=="ItemExist")return true;if(m.MethodName=="GetItem")return Split&&PageReads==2?6:5;throw new Exception(m.MethodName);}).GetTransparentProxy();
   return (IHwpObject)new Proxy(typeof(IHwpObject),(m,a)=>{switch(m.MethodName){
    case "GetID":return 7;case "GetPath":return "fixture.hwpx";case "GetPageCount":return (long)6;case "GetTextFile":return Xml;
    case "GetSelectionMode":return 0;
    case "GetPos":a[0]=List;a[1]=Paragraph;a[2]=Position;return true;
    case "SetPos":List=(int)a[0];Paragraph=(int)a[1];Position=(int)a[2];return true;
    case "KeyIndicator":PageReads++;if(UserMoves)Position=123;return set;
    default:throw new Exception(m.MethodName);
   }}).GetTransparentProxy();
  }
  internal NativePageSnapshot Snapshot(){return new NativePageSnapshot{DocumentId=7,Path="fixture.hwpx",Digest=TableStableSnapshot.Digest(Xml),Pages=new string[6]};}
 }
 static void Check(bool value,string text){if(!value)throw new Exception(text);}
 static void Main(){
  AppDomain.CurrentDomain.AssemblyResolve+=delegate(object s,ResolveEventArgs e){string n=new AssemblyName(e.Name).Name;return new[]{"HwpAddonHelper","Hnc.Foundation.Interface","Hnc.Foundation.InternalInterface"}.Contains(n)?Assembly.LoadFrom(System.IO.Path.Combine(@"C:\Program Files (x86)\Hnc\Office 2024\HOffice130\Bin",n+".dll")):null;};
  Run();
 }
 static void Run(){
  var candidate=new DocumentAddressCandidate{XmlPath="path",Text="abc",List=2,Paragraph=0,Start=0,End=3,Page=5};
  var fake=new Fake();var doc=fake.Make();var backend=new NativePagePositionBackend(()=>doc,fake.Snapshot());var result=NativePagePositionEvidence.Collect(backend,new[]{candidate});
  Check(result.Length==1&&result[0].RawPage==5&&fake.List==0&&fake.Position==15,"native proxy success and restore");
  fake=new Fake{Split=true};doc=fake.Make();backend=new NativePagePositionBackend(()=>doc,fake.Snapshot());result=NativePagePositionEvidence.Collect(backend,new[]{candidate});Check(result.Length==0&&fake.Position==15,"split page rejection");
  fake=new Fake{UserMoves=true};doc=fake.Make();backend=new NativePagePositionBackend(()=>doc,fake.Snapshot());bool failed=false;try{NativePagePositionEvidence.Collect(backend,new[]{candidate});}catch(InvalidOperationException){failed=true;}Check(failed&&fake.Position==123,"keep user cursor on reentrant change");
  Console.WriteLine("PASS native indicator/caret proxy for same-page, split-page and reentrant cursor changes; no HWP runtime");
 }
}
