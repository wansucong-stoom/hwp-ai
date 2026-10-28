using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using HAddon;
using Hwp.Addon;
class NativeDocumentUpdateBackendTests {
 sealed class Fake:RealProxy {
  internal Fake():base(typeof(IHwpObject)){}
  public override IMessage Invoke(IMessage message){var m=(IMethodCallMessage)message;var a=m.Args;try{object result=null;switch(m.MethodName){
   case "GetID":result=1;break;case "GetPath":result="test.hwp";break;case "IsReadOnlyMode":result=false;break;
   case "GetSelectionMode":result=0;break;case "GetPos":a[0]=0;a[1]=0;a[2]=0;result=true;break;
   default:throw new Exception("unexpected "+m.MethodName);
  }return new ReturnMessage(result,a,a.Length,m.LogicalCallContext,m);}catch(Exception e){return new ReturnMessage(e,m);}}
 }
 static void Main(){
  AppDomain.CurrentDomain.AssemblyResolve+=delegate(object s,ResolveEventArgs e){string n=new AssemblyName(e.Name).Name;return new[]{"HwpAddonHelper","Hnc.Foundation.Interface","Hnc.Foundation.InternalInterface"}.Contains(n)?Assembly.LoadFrom(System.IO.Path.Combine(@"C:\Program Files (x86)\Hnc\Office 2024\HOffice130\Bin",n+".dll")):null;};
  Run();
 }
 static void Run(){
  const string xml="<HWPML><HEAD/><BODY><SECTION><P><TEXT><CHAR>original</CHAR></TEXT></P></SECTION></BODY></HWPML>";
  var doc=(IHwpObject)new Fake().GetTransparentProxy();var snapshot=new NativePageSnapshot{DocumentId=1,Path="test.hwp",Digest=TableStableSnapshot.Digest(xml)};
  var first=new DocumentAddressCandidate{XmlPath="BODY/SECTION[0]/P[0]",Text="original",List=1,Paragraph=0,Start=0,End=8};
  var second=new DocumentAddressCandidate{XmlPath="BODY/SECTION[0]/P[1]",Text="another",List=1,Paragraph=0,Start=0,End=7};
  bool rejected=false;try{new NativeDocumentUpdateBackend(()=>doc,snapshot,xml,new[]{first,second});}catch(InvalidOperationException){rejected=true;}if(!rejected)throw new Exception("duplicate native location accepted");
  var backend=new NativeDocumentUpdateBackend(()=>doc,snapshot,xml,new[]{first});first.List=99;first.Text="changed";
  var field=typeof(NativeDocumentUpdateBackend).GetField("addresses",BindingFlags.Instance|BindingFlags.NonPublic);
  var frozen=(Dictionary<string,DocumentAddressCandidate>)field.GetValue(backend);if(frozen[first.XmlPath].List!=1||frozen[first.XmlPath].Text!="original")throw new Exception("target was not frozen");
  var address=frozen[first.XmlPath];
  if(!NativeDocumentUpdateBackend.OwnsRange(address,8,1,1,0,8,1,0,0))throw new Exception("reversed owned selection rejected");
  if(NativeDocumentUpdateBackend.OwnsRange(address,8,1,1,0,0,1,1,8)||NativeDocumentUpdateBackend.OwnsRange(address,8,0,1,0,0,1,0,8)||NativeDocumentUpdateBackend.OwnsRange(address,8,1,1,0,1,1,0,8))throw new Exception("user selection misidentified as owned");
  if(NativeDocumentUpdateBackend.RemapPosition(1,0,8,address,3)!=3||NativeDocumentUpdateBackend.RemapPosition(1,0,6,address,3)!=3||NativeDocumentUpdateBackend.RemapPosition(1,0,2,address,3)!=2||NativeDocumentUpdateBackend.RemapPosition(1,0,10,address,3)!=5||NativeDocumentUpdateBackend.RemapPosition(2,0,8,address,3)!=8)throw new Exception("cursor does not follow a shortening rewrite while other cells stay fixed");
  Console.WriteLine("PASS native backend rejects duplicate physical paragraph addresses and freezes target descriptors; synthetic proxy only");
 }
}
