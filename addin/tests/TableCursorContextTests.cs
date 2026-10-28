using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using HAddon;
using Hwp.Addon;

internal static class TableCursorContextTests {
 static int n;
 static void Check(bool ok,string message){n++;if(!ok)throw new Exception(message);}
 static void Fail(Action f,string message){bool ok=false;try{f();}catch(InvalidOperationException){ok=true;}Check(ok,message);}
 sealed class Proxy:RealProxy {
  readonly Func<IMethodCallMessage,object[],object> f;
  public Proxy(Type type,Func<IMethodCallMessage,object[],object> callback):base(type){f=callback;}
  public override IMessage Invoke(IMessage m){var call=(IMethodCallMessage)m;try{var args=call.Args;var result=f(call,args);return new ReturnMessage(result,args,args.Length,call.LogicalCallContext,call);}catch(Exception ex){return new ReturnMessage(ex,call);}}
 }
 sealed class Fake {
  public int List=41,Position=7,Mode=0,Parent=1,AnchorList=0;public bool NoControls,ChangeDuringRead,EquivalentXmlDuringRead,ChangedBodyDuringRead;int reads;public readonly IHwpObject Doc;
  readonly IHwpCtrlCode[] ctrls=new IHwpCtrlCode[3];public string Xml;
  public Fake(){
   for(int i=0;i<ctrls.Length;i++){int index=i;ctrls[i]=(IHwpCtrlCode)new Proxy(typeof(IHwpCtrlCode),(m,a)=>{
    switch(m.MethodName){case "get_CtrlID":return "tbl";case "GetNext":return index+1<ctrls.Length?ctrls[index+1]:null;
    case "IsSame":return false;case "GetAnchorPos":return Param(index,AnchorList);default:throw new Exception(m.MethodName);}
   }).GetTransparentProxy();}
   Xml=Document();Doc=(IHwpObject)new Proxy(typeof(IHwpObject),(m,a)=>{
    switch(m.MethodName){case "IsReadOnlyMode":return false;case "GetSelectionMode":return Mode;case "GetCurSelectedCtrl":return null;
    case "GetPos":a[0]=List;a[1]=0;a[2]=Position;return true;case "GetParentCtrl":return Parent<0?null:ctrls[Parent];case "GetHeadCtrl":return NoControls?null:ctrls[0];
    case "GetID":return 11;case "GetPath":return "synthetic.hwp";case "GetTextFile":reads++;if(ChangeDuringRead){Position++;ChangeDuringRead=false;}if(reads>1&&EquivalentXmlDuringRead)return Xml.Replace("<HEAD>","<HEAD>\n  ");if(reads>1&&ChangedBodyDuringRead)return Xml.Replace(">x</CHAR>",">changed</CHAR>");return Xml;default:throw new Exception(m.MethodName);}
   }).GetTransparentProxy();
  }
  static IHwpParameterSet Param(int para,int list){return (IHwpParameterSet)new Proxy(typeof(IHwpParameterSet),(m,a)=>{switch(m.MethodName){case "ItemExist":return (string)a[0]=="List"||(string)a[0]=="Para"||(string)a[0]=="Pos";case "GetItem":return (string)a[0]=="Para"?para:(string)a[0]=="List"?list:0;default:throw new Exception(m.MethodName);}}).GetTransparentProxy();}
  static string Table(int border){return "<TABLE RowCount='1' ColCount='1'><ROW><CELL RowAddr='0' ColAddr='0' BorderFill='"+border+"'><PARALIST><P ParaShape='0'><TEXT CharShape='0'><CHAR>x</CHAR></TEXT></P></PARALIST></CELL></ROW></TABLE>";}
  static string Document(){return "<HWPML><HEAD><MAPPINGTABLE><BORDERFILLLIST Count='2'><BORDERFILL Id='1'/><BORDERFILL Id='2'/></BORDERFILLLIST><PARASHAPELIST Count='1'><PARASHAPE Id='0'/></PARASHAPELIST><CHARSHAPELIST Count='1'><CHARSHAPE Id='0'/></CHARSHAPELIST></MAPPINGTABLE></HEAD><BODY><SECTION><P><TEXT>"+Table(1)+"</TEXT></P><P><TEXT>"+Table(2)+"</TEXT></P><P><TEXT>"+Table(1)+"</TEXT></P></SECTION></BODY></HWPML>";}
 }
 static void Main(){AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e){string name=new System.Reflection.AssemblyName(e.Name).Name;return new[]{"HwpAddonHelper","Hnc.Foundation.Interface","Hnc.Foundation.InternalInterface"}.Contains(name)?System.Reflection.Assembly.LoadFrom(System.IO.Path.Combine(@"C:\Program Files (x86)\Hnc\Office 2024\HOffice130\Bin",name+".dll")):null;};try{Run();}catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}}
 static void Run(){
  var f=new Fake();var capture=TableCursorContext.Capture(f.Doc);Check(capture.SourceIndex==1&&capture.TableCount==3&&capture.Plan.Changes.Length==2,"caret parent selects reference table without drag");
  Check(f.List==41&&f.Position==7,"read-only capture preserves cursor");
  f=new Fake{List=0};Fail(()=>TableCursorContext.Capture(f.Doc),"body caret is not a cell");
  f=new Fake{Mode=3};Check(TableCursorContext.Capture(f.Doc).SourceIndex==1,"cell block identifies its parent table");
  Check(TableCursorContext.HasTableCaret(f.Doc),"table caret is recognized separately from full document style support");
  f=new Fake{List=0};Check(!TableCursorContext.HasTableCaret(f.Doc),"body caret cannot be mistaken for a table cell");
  Check(TableControlIdentity.Same(f.Doc.GetParentCtrl(),f.Doc.GetParentCtrl()),"table anchor compares numeric values");
  f=new Fake{ChangeDuringRead=true};Fail(()=>TableCursorContext.Capture(f.Doc),"caret drift rejected");
  f=new Fake{EquivalentXmlDuringRead=true};Check(TableCursorContext.Capture(f.Doc).SourceIndex==1,"equivalent serialization is not mistaken for document edits");
  f=new Fake{ChangedBodyDuringRead=true};Fail(()=>TableCursorContext.Capture(f.Doc),"changed cell text is still rejected during capture");
  foreach(int mode in new[]{0,1,3}){
   f=new Fake{List=0,Parent=-1,Mode=mode};var first=TableCursorContext.CaptureFirstForDesign(f.Doc);
   Check(first.SourceIndex==0&&first.TableCount==3&&(string)first.DesignSnapshot["referenceTableId"]=="t0","first table reference is independent of body caret and selection mode "+mode);
   Check(f.List==0&&f.Position==7&&f.Mode==mode,"first table capture never moves the caret or changes selection "+mode);
  }
  f=new Fake{Parent=2,List=82};capture=TableCursorContext.CaptureFirstForDesign(f.Doc);Check(capture.SourceIndex==0&&f.List==82&&f.Position==7,"first table wins over a caret in a different table without navigation");
  f=new Fake{List=0,Parent=-1,ChangeDuringRead=true};Fail(()=>TableCursorContext.CaptureFirstForDesign(f.Doc),"first table capture still rejects cursor drift during capture");
  f=new Fake{List=0,Parent=-1,ChangedBodyDuringRead=true};Fail(()=>TableCursorContext.CaptureFirstForDesign(f.Doc),"first table capture still rejects document changes during capture");
  f=new Fake{List=0,Parent=-1};Check(TableCursorContext.HasBodyTables(f.Doc)&&f.List==0&&f.Position==7,"routing can find the first native body table without moving a body caret");
  f=new Fake{AnchorList=4};Check(!TableCursorContext.HasBodyTables(f.Doc),"nested-only controls do not advertise a top-level body table");
  f=new Fake{NoControls=true};Check(!TableCursorContext.HasBodyTables(f.Doc)&&!TableCursorContext.HasBodyTables(null),"empty document does not invent an available reference table");
  Console.WriteLine("PASS "+n+" table-cursor context checks; fake native proxies only, no HWP UI.");
 }
}
