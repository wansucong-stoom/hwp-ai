using System;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using HAddon;
using Hwp.Addon;
namespace Newtonsoft.Json.Linq { public sealed class JValue {public object Value{get;set;} public JValue(object value){Value=value;}} }
class NativePageIndicatorTests {
 sealed class Proxy:RealProxy{
  readonly Func<IMethodCallMessage,object> run;
  internal Proxy(Type type,Func<IMethodCallMessage,object> cb):base(type){run=cb;}
  public override IMessage Invoke(IMessage message){var call=(IMethodCallMessage)message;try{return new ReturnMessage(run(call),call.Args,call.ArgCount,call.LogicalCallContext,call);}catch(Exception e){return new ReturnMessage(e,call);}}
 }
 static IHwpObject Fake(object item,bool present){
  IHwpParameterSet set=(IHwpParameterSet)new Proxy(typeof(IHwpParameterSet),m=>{
   if(m.MethodName=="ItemExist")return present&&(string)m.Args[0]=="PrintPageNo";
   if(m.MethodName=="GetItem")return item;
   throw new Exception(m.MethodName);
  }).GetTransparentProxy();
  return (IHwpObject)new Proxy(typeof(IHwpObject),m=>{if(m.MethodName=="KeyIndicator")return set;throw new Exception(m.MethodName);}).GetTransparentProxy();
 }
 static void Main(){
  AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e){string name=new AssemblyName(e.Name).Name;return new[]{"HwpAddonHelper","Hnc.Foundation.Interface","Hnc.Foundation.InternalInterface"}.Contains(name)?Assembly.LoadFrom(System.IO.Path.Combine(@"C:\Program Files (x86)\Hnc\Office 2024\HOffice130\Bin",name+".dll")):null;};
  Run();
 }
 static void Run(){
  int page;if(!NativePageIndicator.TryReadRaw(Fake(7,true),out page)||page!=7)throw new Exception("raw integer value");
  if(!NativePageIndicator.TryReadRaw(Fake(new Newtonsoft.Json.Linq.JValue((long)7),true),out page)||page!=7)throw new Exception("JValue integer value");
  if(NativePageIndicator.TryReadRaw(Fake(new Newtonsoft.Json.Linq.JValue("7"),true),out page)||NativePageIndicator.TryReadRaw(Fake(new Newtonsoft.Json.Linq.JValue(7.0),true),out page))throw new Exception("invalid wrapped value accepted");
  if(NativePageIndicator.TryReadRaw(Fake(1,false),out page)||NativePageIndicator.TryReadRaw(Fake("7",true),out page)||NativePageIndicator.TryReadRaw(Fake(-1,true),out page)||NativePageIndicator.TryReadRaw(null,out page))throw new Exception("invalid indicator accepted");
  string info=NativePageIndicator.Diagnose(Fake("7",true));if(!info.Contains("System.String")||info.Contains("쪽 번호 값: 7"))throw new Exception("non-integer type diagnosis");
  var broken=(IHwpObject)new Proxy(typeof(IHwpObject),m=>{throw new InvalidOperationException("private document text");}).GetTransparentProxy();info=NativePageIndicator.Diagnose(broken);if(!info.Contains("InvalidOperationException")||info.Contains("private document text"))throw new Exception("exception detail privacy");
  var helper=typeof(IHwpObject).Assembly;var type=helper.GetType("Hwp.Addon.HwpParameterSet",true);
  var constructor=type.GetConstructor(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance,null,new[]{typeof(string)},null);
  var actual=(IHwpParameterSet)constructor.Invoke(new object[]{"KeyIndicator"});actual.SetItem("PrintPageNo",5);
  var actualDoc=(IHwpObject)new Proxy(typeof(IHwpObject),m=>{if(m.MethodName=="KeyIndicator")return actual;throw new Exception(m.MethodName);}).GetTransparentProxy();
  if(!NativePageIndicator.TryReadRaw(actualDoc,out page)||page!=5)throw new Exception("installed helper JSON value");
  Console.WriteLine("PASS KeyIndicator item lookup on synthetic proxy; HWP add-in runtime not tested");
 }
}
