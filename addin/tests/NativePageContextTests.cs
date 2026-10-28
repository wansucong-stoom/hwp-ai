using System;
using System.Reflection;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using HAddon;
using Hwp.Addon;
class NativePageContextTests {
 sealed class Proxy:RealProxy {
  readonly Func<IMethodCallMessage,object[],object> callback;
  internal Proxy(Func<IMethodCallMessage,object[],object> f):base(typeof(IHwpObject)){callback=f;}
  public override IMessage Invoke(IMessage m){var c=(IMethodCallMessage)m;try{var a=c.Args;return new ReturnMessage(callback(c,a),a,a.Length,c.LogicalCallContext,c);}catch(Exception e){return new ReturnMessage(e,c);}}
 }
 static void Main(){
  AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e){string n=new AssemblyName(e.Name).Name;return new[]{"HwpAddonHelper","Hnc.Foundation.Interface","Hnc.Foundation.InternalInterface"}.Contains(n)?Assembly.LoadFrom(System.IO.Path.Combine(@"C:\Program Files (x86)\Hnc\Office 2024\HOffice130\Bin",n+".dll")):null;};
  try{Run();}catch(Exception ex){Console.Error.WriteLine(ex.Message);Environment.ExitCode=1;}
 }
 static void Run(){
  int reads=0;bool changed=false;const string xml="<HWPML><HEAD/><BODY><SECTION><P><TEXT><CHAR>x</CHAR></TEXT></P></SECTION></BODY></HWPML>";
  var doc=(IHwpObject)new Proxy((m,a)=>{switch(m.MethodName){case "GetID":return 7;case "GetPath":return "synthetic.hwp";case "GetPageCount":return (long)2;case "GetTextFile":return changed?xml.Replace(">x<",">y<"):xml;case "GetPageText":reads++;if((int)a[1]!=7&&(int)a[1]!=1)throw new Exception("option");return ((int)a[0]==0?"page one":"page two")+((int)a[1]==1?" table":"");default:throw new Exception("unexpected native command: "+m.MethodName);}}).GetTransparentProxy();
  var snapshot=NativePageContext.Read(()=>doc);if(snapshot.Pages.Length!=2||snapshot.Pages[0]!="page one"||snapshot.TablePages[0]!="page one table"||reads!=4||snapshot.Paragraphs.Length!=1||snapshot.Paragraphs[0].Text!="x")throw new Exception("page/structure extraction failed");
  reads=0;var changing=(IHwpObject)new Proxy((m,a)=>{switch(m.MethodName){case "GetID":return 7;case "GetPath":return "synthetic.hwp";case "GetPageCount":return (long)2;case "GetTextFile":return changed?xml.Replace(">x<",">y<"):xml;case "GetPageText":if(++reads==1)changed=true;return "text";default:throw new Exception(m.MethodName);}}).GetTransparentProxy();
  bool rejected=false;try{NativePageContext.Read(()=>changing);}catch(InvalidOperationException){rejected=true;}if(!rejected)throw new Exception("concurrent document edit accepted");
  changed=false;var empty=(IHwpObject)new Proxy((m,a)=>{switch(m.MethodName){case "GetID":return 7;case "GetPath":return "synthetic.hwp";case "GetPageCount":return (long)2;case "GetTextFile":return xml;case "GetPageText":return "";default:throw new Exception(m.MethodName);}}).GetTransparentProxy();
  rejected=false;try{NativePageContext.Read(()=>empty);}catch(InvalidOperationException){rejected=true;}if(!rejected)throw new Exception("empty native page text accepted");
  var tableOnly=(IHwpObject)new Proxy((m,a)=>{switch(m.MethodName){case "GetID":return 7;case "GetPath":return "synthetic.hwp";case "GetPageCount":return (long)1;case "GetTextFile":return xml;case "GetPageText":return (int)a[1]==1?"table contents":"";default:throw new Exception(m.MethodName);}}).GetTransparentProxy();
  var tableSnapshot=NativePageContext.Read(()=>tableOnly);if(tableSnapshot.Pages[0]!=""||tableSnapshot.TablePages[0]!="table contents")throw new Exception("table-only page not preserved");
  var nullTable=(IHwpObject)new Proxy((m,a)=>{switch(m.MethodName){case "GetID":return 7;case "GetPath":return "synthetic.hwp";case "GetPageCount":return (long)1;case "GetTextFile":return xml;case "GetPageText":return (int)a[1]==1?null:"body";default:throw new Exception(m.MethodName);}}).GetTransparentProxy();
  rejected=false;try{NativePageContext.Read(()=>nullTable);}catch(InvalidOperationException){rejected=true;}if(!rejected)throw new Exception("unknown table extraction accepted");
  Console.WriteLine("PASS page context from synthetic HWP interface, including document-change detection; no live HWP");
 }
}
