using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Threading;
using System.Threading.Tasks;
using HAddon;
using Hwp.Addon;
namespace HAddon {
 internal sealed class BridgeClient {
  internal Action<string> DuringRequest;
  internal Task<Dictionary<string,object>> Request(Dictionary<string,object> request,string id){
   string action=(string)request["action"];object result;
   if(action=="page-scope")result=new Dictionary<string,object>{{"targetPages",new object[]{1}},{"sourcePages",new object[]{2}},{"uncertainties",new object[0]}};
   else if(action=="document-target-discovery")result=new Dictionary<string,object>{{"plan",new Dictionary<string,object>{{"targets",new object[]{"p0"}},{"uncertainties",new object[0]}}}};
   else if(action=="page-edit-plan")result=new Dictionary<string,object>{{"plan",new Dictionary<string,object>{{"changes",new object[0]},{"uncertainties",new object[0]}}}};
   else throw new Exception(action);
   if(DuringRequest!=null)DuringRequest(action);return Task.FromResult((Dictionary<string,object>)result);
  }
  internal static Dictionary<string,object> Data(Dictionary<string,object> value){return value;}
 }
}
class NativeAutomaticPageUpdateEnvironmentTests {
 const string Xml="<HWPML><HEAD><MAPPINGTABLE><CHARSHAPELIST><CHARSHAPE Id='1' Height='1000'/></CHARSHAPELIST></MAPPINGTABLE></HEAD><BODY><SECTION><P><TEXT CharShape='1'><CHAR>Unique summary.</CHAR></TEXT></P><P><TEXT CharShape='1'><CHAR>Unique source details.</CHAR></TEXT></P></SECTION></BODY></HWPML>";
 sealed class Context:SynchronizationContext {
  readonly Queue<KeyValuePair<SendOrPostCallback,object>> queue=new Queue<KeyValuePair<SendOrPostCallback,object>>();readonly AutoResetEvent signal=new AutoResetEvent(false);
  public override void Post(SendOrPostCallback action,object value){lock(queue)queue.Enqueue(new KeyValuePair<SendOrPostCallback,object>(action,value));signal.Set();}
  internal void Finish(Task task){while(!task.IsCompleted){KeyValuePair<SendOrPostCallback,object> work=default(KeyValuePair<SendOrPostCallback,object>);bool hit;lock(queue){hit=queue.Count>0;if(hit)work=queue.Dequeue();}if(hit)work.Key(work.Value);else signal.WaitOne(5000);}task.GetAwaiter().GetResult();}
 }
 sealed class Proxy:RealProxy {
  internal string Stage="read",Raw=Xml;internal readonly Dictionary<string,int> Counts=new Dictionary<string,int>();internal Proxy():base(typeof(IHwpObject)){}
  public override IMessage Invoke(IMessage message){var m=(IMethodCallMessage)message;try{object result;switch(m.MethodName){case"GetID":result=7;break;case"GetPath":result="synthetic.hwpx";break;case"IsReadOnlyMode":result=false;break;case"GetPageCount":result=(long)2;break;case"GetPageText":result=(int)m.Args[0]==0?"Unique summary.":"Unique source details.";break;case"GetTextFile":int count;Counts.TryGetValue(Stage,out count);Counts[Stage]=count+1;result=Raw;break;default:throw new Exception(m.MethodName);}return new ReturnMessage(result,m.Args,m.ArgCount,m.LogicalCallContext,m);}catch(Exception e){return new ReturnMessage(e,m);}}
 }
 // This boundary does not call native APIs. NativeRepeatFindProbe's own full
 // XML read count is measured separately; this isolates wrapper duplication.
 sealed class Probe:IOnDemandNativeProbe {
  readonly OnDemandExpectedTarget target;readonly Action duringInspect;internal Probe(string raw,Action duringInspect){this.duringInspect=duringInspect;var p=DocumentStructure.Read(raw)[0];target=new HwpParagraphProofs(raw).CreateExpected(p.Path);}
  public void CheckSnapshot(){}
  OnDemandObservedParagraph Value(){return new OnDemandObservedParagraph{List=0,Paragraph=0,Start=0,End=target.Text.Length,Text=target.Text,FirstPhysicalPage=1,LastPhysicalPage=1,ContainerToken=target.ContainerToken,StyleToken=target.StyleToken,StructureToken=target.StructureToken,WholeParagraph=true,TextVerified=true,ContainerVerified=true,StyleVerified=true,StructureVerified=true,PhysicalPageVerified=true};}
  public OnDemandAnchorSearch FindAnchor(string text){return new OnDemandAnchorSearch{Complete=true,Matches=new[]{Value()}};}
  public OnDemandObservedParagraph InspectParagraph(int list,int paragraph){if(duringInspect!=null)duringInspect();return Value();}
 }
 static void Main(){AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e){string p=System.IO.Path.Combine(@"C:\Program Files (x86)\Hnc\Office 2024\HOffice130\Bin",new AssemblyName(e.Name).Name+".dll");return System.IO.File.Exists(p)?Assembly.LoadFrom(p):null;};Run();}
 static void One(Context context,Proxy proxy,BridgeClient bridge,Action inspect){var doc=(IHwpObject)proxy.GetTransparentProxy();var env=new NativeAutomaticPageUpdateEnvironment(bridge,()=>doc,"probe","account","auto","auto",null,(c,t)=>{var proofs=new HwpParagraphProofs(c.RawXml);return t.Select(x=>proofs.CreateExpected(x.XmlPath)).ToArray();},c=>new Probe(c.RawXml,inspect),TextOnlyAutomaticPageEvidence.Validate);var flow=new AutomaticPageUpdate();context.Finish(flow.RunAsync(env,"Update page 1 from page 2",CancellationToken.None,(stage,done,total)=>proxy.Stage=stage));}
 static void Reject(Action action){try{action();}catch(InvalidOperationException){return;}throw new Exception("stale boundary accepted");}
 static void Run(){var context=new Context();SynchronizationContext.SetSynchronizationContext(context);var proxy=new Proxy();One(context,proxy,new BridgeClient(),null);foreach(var pair in proxy.Counts)Console.WriteLine(pair.Key+": full XML reads="+pair.Value);if(proxy.Counts.Values.Sum()!=9)throw new Exception("duplicate full XML reads regressed");Console.WriteLine("Total full XML reads="+proxy.Counts.Values.Sum()+"; actual environment/services, synthetic model and zero-native-read locator; no mutation.");foreach(string action in new[]{"page-scope","document-target-discovery","page-edit-plan"}){var changed=new Proxy();var bridge=new BridgeClient{DuringRequest=phase=>{if(phase==action)changed.Raw=Xml.Replace("source details","changed source");}};Reject(()=>One(context,changed,bridge,null));}var nativeChanged=new Proxy();Reject(()=>One(context,nativeChanged,new BridgeClient(),()=>nativeChanged.Raw=Xml.Replace("source details","changed source")));Console.WriteLine("PASS actual coordinated services reject revision changes after every model stage and native location read.");}
}
