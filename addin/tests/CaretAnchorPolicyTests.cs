using System;
using System.IO;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using HAddon;
using Hwp.Addon;

internal static class CaretAnchorPolicyTests {
 static int passed;
 static void Main() {
  AppDomain.CurrentDomain.AssemblyResolve += Resolve;
  try {
   Run();
   Console.WriteLine("PASS: " + passed + " caret anchor checks; synthetic IHwpObject proxies only, no document or UI.");
  } catch(Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; }
 }
 static Assembly Resolve(object sender, ResolveEventArgs args) {
  string name=new AssemblyName(args.Name).Name;
  if(name!="HwpAddonHelper"&&name!="Hnc.Foundation.Interface"&&name!="Hnc.Foundation.InternalInterface")return null;
  return Assembly.LoadFrom(Path.Combine(@"C:\Program Files (x86)\Hnc\Office 2024\HOffice130\Bin",name+".dll"));
 }
 static void Check(bool condition,string message) {if(!condition)throw new Exception(message);passed++;}
 static void Fails(Action callback,string message) {
  try{callback();}catch(InvalidOperationException){passed++;return;}
  throw new Exception("Expected rejection: "+message);
 }
 static void Run(){
  var baseline=new FakeDocument();
  CaretAnchor anchor=CaretAnchor.Capture(baseline.Proxy);
  anchor.RequireCurrent(baseline.Proxy);
  Check(baseline.Calls>0,"baseline consults document metadata");
  Check(!baseline.UnexpectedCall,"capture must not read or mutate document content");
  var alias=new FakeDocument();
  anchor.RequireCurrent(alias.Proxy);
  Check(!alias.UnexpectedCall,"a second wrapper for same document identity remains valid");

  RejectCurrent(d=>d.Id=8,"different document id");
  RejectCurrent(d=>d.Path="other.hwp","different path");
  RejectCurrent(d=>d.List=2,"different list");
  RejectCurrent(d=>d.Paragraph=4,"different paragraph");
  RejectCurrent(d=>d.Position=17,"different caret offset");
  RejectCurrent(d=>d.Mode=1,"active text selection");
  RejectCurrent(d=>d.Mode=2,"rectangle selection");
  RejectCurrent(d=>d.Mode=3,"cell block selection");
  RejectCurrent(d=>d.Mode=4,"object selection");
  RejectCurrent(d=>d.SelectedControl=true,"active selected control");
  RejectCurrent(d=>d.NonemptySelection=true,"nonempty range despite selection mode zero");
  RejectCurrent(d=>d.Mode=32,"unknown selection flag");
  RejectCurrent(d=>d.PositionAvailable=false,"GetPos failure");
  Fails(()=>anchor.RequireCurrent(null),"missing current document");
  Fails(()=>CaretAnchor.Capture(null),"missing capture document");
  Fails(()=>CaretAnchor.Capture(new FakeDocument{Mode=1}.Proxy),"selected text at capture");
  Fails(()=>CaretAnchor.Capture(new FakeDocument{Mode=3}.Proxy),"selected cell block at capture");
  Fails(()=>CaretAnchor.Capture(new FakeDocument{SelectedControl=true}.Proxy),"selected control at capture");
  Fails(()=>CaretAnchor.Capture(new FakeDocument{PositionAvailable=false}.Proxy),"unavailable caret at capture");
  Fails(()=>CaretAnchor.Capture(new FakeDocument{NonemptySelection=true}.Proxy),"nonempty range with mode zero at capture");
  Fails(()=>CaretAnchor.Capture(new FakeDocument{Mode=32}.Proxy),"unknown selection flag at capture");
  Fails(()=>CaretAnchor.Capture(new FakeDocument{MoveAfterFirstGetPos=true}.Proxy),"caret changes during capture");
  Fails(()=>CaretAnchor.Capture(new FakeDocument{List=-1}.Proxy),"negative list");
  Fails(()=>CaretAnchor.Capture(new FakeDocument{Paragraph=-1}.Proxy),"negative paragraph");
  Fails(()=>CaretAnchor.Capture(new FakeDocument{Position=-1}.Proxy),"negative position");

  var knownFlag=new FakeDocument{Mode=16};
  CaretAnchor.Capture(knownFlag.Proxy).RequireCurrent(knownFlag.Proxy);
  Check(!knownFlag.UnexpectedCall,"known selection status flag without low-bit selection is accepted");
  var tableCaret=new FakeDocument{List=12,Paragraph=2,Position=5};
  CaretAnchor.Capture(tableCaret.Proxy).RequireCurrent(tableCaret.Proxy);
  Check(!tableCaret.UnexpectedCall,"non-body caret pins; native composer decides supported area");
 }
 static void RejectCurrent(Action<FakeDocument> change,string label) {
  var doc=new FakeDocument();CaretAnchor anchor=CaretAnchor.Capture(doc.Proxy);change(doc);
  Fails(()=>anchor.RequireCurrent(doc.Proxy),label);
 }
 sealed class CallbackProxy:RealProxy {
  readonly Func<IMethodCallMessage,object[],object> invoke;
  public CallbackProxy(Type type,Func<IMethodCallMessage,object[],object> invoke):base(type){this.invoke=invoke;}
  public override IMessage Invoke(IMessage message) {
   var call=(IMethodCallMessage)message;
   try{object[] arguments=call.Args;object result=invoke(call,arguments);return new ReturnMessage(result,arguments,arguments.Length,call.LogicalCallContext,call);}
   catch(Exception ex){return new ReturnMessage(ex,call);}
  }
 }
 sealed class FakeDocument {
  public readonly IHwpObject Proxy;
  public int Id=7,List=0,Paragraph=3,Position=16,Mode=0,Calls;
  public string Path="fixture.hwp";
  public bool SelectedControl,PositionAvailable=true,UnexpectedCall,NonemptySelection,MoveAfterFirstGetPos;
  int positionReads;
  public FakeDocument(){Proxy=(IHwpObject)new CallbackProxy(typeof(IHwpObject),Call).GetTransparentProxy();}
  object Call(IMethodCallMessage call,object[] args) {
   Calls++;
   switch(call.MethodName) {
    case "GetID": return Id;
    case "GetPath": return Path;
    case "GetSelectionMode": return Mode;
    case "GetCurSelectedCtrl":
     if(!SelectedControl)return null;
     Type ret=((MethodInfo)call.MethodBase).ReturnType;
     return new CallbackProxy(ret,delegate(IMethodCallMessage _,object[] __){throw new Exception("Selected control must not be dereferenced.");}).GetTransparentProxy();
    case "GetPos": args[0]=List;args[1]=Paragraph;args[2]=Position;if(MoveAfterFirstGetPos&&++positionReads==1)Position++;return PositionAvailable;
    case "GetSelectedPos": args[0]=List;args[1]=Paragraph;args[2]=Position;args[3]=List;args[4]=Paragraph;args[5]=Position+(NonemptySelection?1:0);return NonemptySelection;
    default: UnexpectedCall=true;throw new Exception("Unexpected document call: "+call.MethodName);
   }
  }
 }
}
