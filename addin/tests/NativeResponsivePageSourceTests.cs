using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using HAddon;
using Hwp.Addon;
class NativeResponsivePageSourceTests {
 sealed class Proxy:RealProxy {
  internal int Id=7;internal bool SwitchDuringPage;internal int Calls;
  internal Proxy():base(typeof(IHwpObject)){}
  public override IMessage Invoke(IMessage message){var c=(IMethodCallMessage)message;var a=c.Args;try{object r;
   switch(c.MethodName){
    case "GetID":r=Id;break;case "GetPath":r="fixture.hwpx";break;case "GetTextFile":r="<HWPML><BODY/></HWPML>";break;
    case "GetPageCount":r=(long)2;break;
    case "GetPageText":Calls++;r="page";if(SwitchDuringPage)Id=8;break;
    default:throw new Exception("unexpected or mutating HWP call: "+c.MethodName);
   }
   return new ReturnMessage(r,a,a.Length,c.LogicalCallContext,c);
  }catch(Exception e){return new ReturnMessage(e,c);}}
  internal IHwpObject Doc(){return (IHwpObject)GetTransparentProxy();}
 }
 static void Assert(bool value,string message){if(!value)throw new Exception(message);}
 static void Main(){
  AppDomain.CurrentDomain.AssemblyResolve+=delegate(object s,ResolveEventArgs e){string n=new AssemblyName(e.Name).Name;return new[]{"HwpAddonHelper","Hnc.Foundation.Interface","Hnc.Foundation.InternalInterface"}.Contains(n)?Assembly.LoadFrom(System.IO.Path.Combine(@"C:\Program Files (x86)\Hnc\Office 2024\HOffice130\Bin",n+".dll")):null;};
  Run();
 }
 static void Run(){
  var p=new Proxy();var doc=p.Doc();var source=new NativeResponsivePageSource(()=>doc);
  Assert(source.Identity()=="7\0fixture.hwpx"&&source.PageCount()==2&&source.ReadPage(0,7)=="page","native read");
  bool rejected=false;try{source.ReadPage(0,0);}catch(InvalidOperationException){rejected=true;}Assert(rejected&&p.Calls==1,"reject unsupported options before HWP call");
  p.SwitchDuringPage=true;rejected=false;try{source.ReadPage(1,1);}catch(InvalidOperationException){rejected=true;}Assert(rejected,"reject document change during native call");
  Console.WriteLine("PASS native responsive reader adapter identity and read-only synthetic proxy");
 }
}
