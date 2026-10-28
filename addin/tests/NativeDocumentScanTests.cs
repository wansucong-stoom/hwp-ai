using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using HAddon;
using Hwp.Addon;
class NativeDocumentScanTests {
 sealed class Fake:RealProxy {
  internal int Step,Restores,Releases,Mode,Selections;internal int List=3,Paragraph=1,Position=2,SelectionEnd=5;internal bool Change,FailMove,PrematureEmpty,LieRestore,ChangeDuringScan,UserMoves,SelectionChanges,ControlText;
  internal Fake():base(typeof(IHwpObject)){}
  public override IMessage Invoke(IMessage message){var m=(IMethodCallMessage)message;var a=m.Args;try{
   object value=null;switch(m.MethodName){
    case "GetID":value=1;break;case "GetPath":value="test.hwp";break;
    case "GetTextFile":value=Change?"<HWPML><BODY><P><TEXT><CHAR>y</CHAR></TEXT></P></BODY></HWPML>":"<HWPML><BODY><P><TEXT><CHAR>x</CHAR></TEXT></P></BODY></HWPML>";break;
    case "GetSelectionMode":value=Mode;break;
    case "GetSelectedPos":a[0]=3;a[1]=1;a[2]=2;a[3]=3;a[4]=1;a[5]=SelectionEnd;value=true;break;
    case "GetPos":a[0]=List;a[1]=Paragraph;a[2]=Position;value=true;break;
    case "InitScan":if((uint)a[0]!=15||(int)a[1]!=119||(int)a[2]!=0||(int)a[3]!=0||(int)a[4]!=-1||(int)a[5]!=-1)throw new Exception("wrong scan options");value=true;break;
    case "GetText":Step++;if(ChangeDuringScan)Change=true;if(UserMoves&&Step==2){List=8;Position=11;}if(SelectionChanges&&Step==2)SelectionEnd=6;
     if(ControlText){a[0]=Step==1?"cell text":Step==4?"after cell":"";value=(long)(Step==1?4:Step==2?2:Step==3?3:Step==4?5:1);}
     else{a[0]=Step==1?"내용":"";value=(long)(Step==1?(PrematureEmpty?0:2):1);}break;
    case "MovePos":if(!FailMove){List=4;Position=6;}value=!FailMove;break;case "ReleaseScan":Releases++;value=true;break;
    case "SetPos":Restores++;if(!LieRestore){List=(int)a[0];Paragraph=(int)a[1];Position=(int)a[2];Mode=0;}value=true;break;case "SelectText":Selections++;Mode=1;value=true;break;
    default:throw new Exception("unexpected "+m.MethodName);
   }return new ReturnMessage(value,a,a.Length,m.LogicalCallContext,m);
  }catch(Exception ex){return new ReturnMessage(ex,m);}}
  internal IHwpObject Document(){return (IHwpObject)GetTransparentProxy();}
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Main(){
  AppDomain.CurrentDomain.AssemblyResolve+=delegate(object s,ResolveEventArgs e){string n=new AssemblyName(e.Name).Name;return new[]{"HwpAddonHelper","Hnc.Foundation.Interface","Hnc.Foundation.InternalInterface"}.Contains(n)?Assembly.LoadFrom(System.IO.Path.Combine(@"C:\Program Files (x86)\Hnc\Office 2024\HOffice130\Bin",n+".dll")):null;};
  Run();
 }
 static void Run(){
  var f=new Fake();var doc=f.Document();var items=NativeDocumentScan.Read(()=>doc);Check(items.Length==1&&items[0].Text=="내용"&&items[0].List==4&&f.Releases==1&&f.Restores==1,"scan and restore");
  f=new Fake{ControlText=true};doc=f.Document();items=NativeDocumentScan.Read(()=>doc);Check(items.Length==2&&items[0].Kind==4&&items[0].Text=="cell text"&&items[1].Kind==5&&items[1].Text=="after cell","control entry/exit text kept and empty events skipped");
  f=new Fake{Mode=1};doc=f.Document();NativeDocumentScan.Read(()=>doc);Check(f.Selections==1&&f.Restores==1,"restore selection");
  f=new Fake{FailMove=true};doc=f.Document();bool failed=false;try{NativeDocumentScan.Read(()=>doc);}catch(InvalidOperationException){failed=true;}Check(failed&&f.Releases==1&&f.Restores==1,"release and restore after error");
  f=new Fake{PrematureEmpty=true};doc=f.Document();failed=false;try{NativeDocumentScan.Read(()=>doc);}catch(InvalidOperationException){failed=true;}Check(failed&&f.Releases==1&&f.Restores==1,"premature no-text is not a complete scan");
  f=new Fake{LieRestore=true};doc=f.Document();failed=false;try{NativeDocumentScan.Read(()=>doc);}catch(InvalidOperationException){failed=true;}Check(failed,"a successful return without actual caret restoration is rejected");
  f=new Fake{ChangeDuringScan=true};doc=f.Document();failed=false;try{NativeDocumentScan.Read(()=>doc);}catch(InvalidOperationException){failed=true;}Check(failed&&f.Releases==1&&f.Restores==1,"document edit after scan must be rejected");
  f=new Fake{UserMoves=true};doc=f.Document();failed=false;try{NativeDocumentScan.Read(()=>doc);}catch(InvalidOperationException){failed=true;}Check(failed&&f.Releases==1&&f.Restores==0&&f.List==8&&f.Position==11,"never overwrite a user-moved cursor");
  f=new Fake{Mode=1,SelectionChanges=true};doc=f.Document();failed=false;try{NativeDocumentScan.Read(()=>doc);}catch(InvalidOperationException){failed=true;}Check(failed&&f.Releases==1&&f.Restores==0&&f.SelectionEnd==6,"never overwrite a changed selection");
  f=new Fake{Mode=3};doc=f.Document();failed=false;try{NativeDocumentScan.Read(()=>doc);}catch(InvalidOperationException){failed=true;}Check(failed&&f.Releases==0&&f.Restores==0,"unsupported selection not disturbed");
  Console.WriteLine("PASS scan position collection and restoration with synthetic HWP proxy; desktop behavior unverified");
 }
}
