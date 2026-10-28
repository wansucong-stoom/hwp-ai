using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using HAddon;
using Hwp.Addon;

class NativeRepeatFindProbeTests {
 sealed class Proxy:RealProxy {
  readonly Func<IMethodCallMessage,object[],object> call;
  internal Proxy(Type t,Func<IMethodCallMessage,object[],object> c):base(t){call=c;}
  public override IMessage Invoke(IMessage message){var m=(IMethodCallMessage)message;var a=m.Args;try{return new ReturnMessage(call(m,a),a,a.Length,m.LogicalCallContext,m);}catch(Exception e){return new ReturnMessage(e,m);}}
 }
 sealed class Record {internal int List,Para,Page;internal string Text;}
 const string Header="<HEAD><MAPPINGTABLE><BORDERFILLLIST><BORDERFILL Id='1' FillColor='white'/></BORDERFILLLIST><CHARSHAPELIST><CHARSHAPE Id='0' Height='1000' BorderFillId='1'/></CHARSHAPELIST><PARASHAPELIST><PARASHAPE Id='0' Align='Left'><PARABORDER BorderFill='1'/></PARASHAPE></PARASHAPELIST><STYLELIST><STYLE Id='0' Name='body' CharShape='0' ParaShape='0' NextStyle='0'/></STYLELIST></MAPPINGTABLE></HEAD>";
 static string P(string text){return "<P ParaShape='0' Style='0'><TEXT CharShape='0'><CHAR>"+text+"</CHAR></TEXT></P>";}
 sealed class Fake {
  internal readonly Record[] Records={new Record{List=0,Para=0,Page=1,Text="unique anchor"},new Record{List=0,Para=1,Page=1,Text="repeat"},new Record{List=0,Para=2,Page=1,Text="repeat"},new Record{List=2,Para=0,Page=2,Text="table unique"}};
  internal int List=0,Para=0,Pos=2,Mode,Start,End,SearchIndex,Restores,Executions,GetDefaults,FullXmlReads;
  internal long Mask=123;internal bool BadSelection,MutateDocument,WrongPage;internal string LastText;
  internal readonly List<Dictionary<string,object>> Conditions=new List<Dictionary<string,object>>();
  internal string FullXml{get{return "<HWPML>"+Header+"<BODY><SECTION>"+P("unique anchor")+P("repeat")+P("repeat")+"<P><TEXT><TABLE RowCount='1' ColCount='1'><ROW><CELL RowAddr='0' ColAddr='0' RowSpan='1' ColSpan='1' BorderFill='1'><PARALIST>"+P("table unique")+"</PARALIST></CELL></ROW></TABLE></TEXT></P></SECTION></BODY></HWPML>";}}
  Record Current{get{return Records.Single(r=>r.List==List&&r.Para==Para);}}
  internal IHwpObject Document;
  internal Fake(){Document=(IHwpObject)new Proxy(typeof(IHwpObject),Invoke).GetTransparentProxy();}
  internal NativePageSnapshot Snapshot(){return new NativePageSnapshot{DocumentId=4,Path="synthetic.hwpx",Digest=TableStableSnapshot.Digest(FullXml),Pages=new[]{"unique anchor repeat repeat","table unique"}};}
  internal NativeEditor.ParagraphInfo Inspect(IHwpObject doc,int list,int para){var r=Records.Single(x=>x.List==list&&x.Para==para);List=list;Para=para;Pos=r.Text.Length;Start=0;End=r.Text.Length;Mode=1;return new NativeEditor.ParagraphInfo{Text=r.Text,NativeStart=0,NativeEnd=r.Text.Length};}
  IHwpParameterSet Set(Dictionary<string,object> values){return (IHwpParameterSet)new Proxy(typeof(IHwpParameterSet),(m,a)=>{switch(m.MethodName){case "SetItem":values[(string)a[0]]=a[1];return null;case "GetItem":return values[(string)a[0]];case "ItemExist":return values.ContainsKey((string)a[0]);default:throw new Exception("parameter "+m.MethodName);}}).GetTransparentProxy();}
  IHwpAction Action(){return (IHwpAction)new Proxy(typeof(IHwpAction),(m,a)=>{switch(m.MethodName){case "CreateSet":var values=new Dictionary<string,object>();Conditions.Add(values);return Set(values);case "GetDefault":GetDefaults++;throw new Exception("GetDefault must not carry stale style settings");case "Execute":
    var p=(IHwpParameterSet)a[0];string text=(string)p.GetItem("FindString");Executions++;if(LastText!=text){LastText=text;SearchIndex=0;}
    var matches=Records.Where(r=>r.Text.Contains(text)).ToArray();if(SearchIndex>=matches.Length){if(matches.Length>0){List=matches[0].List;Para=matches[0].Para;Pos=0;Mode=0;}return 0;}
    var hit=matches[SearchIndex++];List=hit.List;Para=hit.Para;Start=hit.Text.IndexOf(text,StringComparison.Ordinal);End=Start+text.Length;Pos=End;Mode=1;
    if(BadSelection){List=99;Para=0;Pos=1;Mode=0;}return 1;
   default:throw new Exception("action "+m.MethodName);}}).GetTransparentProxy();}
  object Invoke(IMethodCallMessage m,object[] a){switch(m.MethodName){
   case "GetID":return 4;case "GetPath":return "synthetic.hwpx";case "IsReadOnlyMode":return false;
   case "GetTextFile":if((string)a[1]=="saveblock")return (string)a[0]=="TEXT"?Current.Text.Substring(Start,End-Start):"<HWPML>"+Header+"<BODY><SECTION>"+P(Current.Text.Substring(Start,End-Start))+"</SECTION></BODY></HWPML>";FullXmlReads++;return MutateDocument?FullXml.Replace("white","red"):FullXml;
   case "GetSelectText":return Current.Text.Substring(Start,End-Start);
   case "GetSelectionMode":return Mode;
   case "GetPos":a[0]=List;a[1]=Para;a[2]=Pos;return true;
   case "GetSelectedPos":a[0]=List;a[1]=Para;a[2]=Start;a[3]=List;a[4]=Para;a[5]=End;return true;
   case "GetMessageBoxMode":return Mask;case "SetMessageBoxMode":int old=(int)Mask;Mask=Convert.ToInt64(a[0],CultureInfo.InvariantCulture);return old;
   case "MovePos":if((int)a[0]!=2)throw new Exception("unsupported move");List=0;Para=0;Pos=0;Mode=0;SearchIndex=0;LastText=null;return true;
   case "SetPos":List=(int)a[0];Para=(int)a[1];Pos=(int)a[2];Mode=0;if(List==0&&Para==0&&Pos==2)Restores++;return true;
   case "SelectText":if((int)a[0]!=(int)a[2])return false;Para=(int)a[0];Start=(int)a[1];End=(int)a[3];Pos=End;Mode=1;return true;
   case "KeyIndicator":return Set(new Dictionary<string,object>{{"PrintPageNo",WrongPage?1:Current.Page}});
   case "CreateAction":if((string)a[0]!="RepeatFind")throw new Exception("unexpected document mutation action");return Action();
   default:throw new Exception("unexpected native method "+m.MethodName);
  }}
 }
 static void Check(bool value,string name){if(!value)throw new Exception(name);}
 static NativeRepeatFindProbe Probe(Fake f,bool physical=true,bool relative=false){return new NativeRepeatFindProbe(()=>f.Document,f.Snapshot(),new HwpParagraphProofs(f.FullXml),physical,relative,f.Inspect);}
 static void Main(){AppDomain.CurrentDomain.AssemblyResolve+=delegate(object s,ResolveEventArgs e){string path=System.IO.Path.Combine(@"C:\Program Files (x86)\Hnc\Office 2024\HOffice130\Bin",new AssemblyName(e.Name).Name+".dll");return System.IO.File.Exists(path)?Assembly.LoadFrom(path):null;};try{Run();}catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}}
 static void Run(){
  var f=new Fake();var probe=Probe(f);var result=probe.FindAnchor("unique anchor");Check(result.Complete&&result.Matches.Length==1&&result.Matches[0].List==0&&result.Matches[0].PhysicalPageVerified,"unique body");Check(f.Mask==123&&f.List==0&&f.Para==0&&f.Pos==2&&f.Mode==0,"message mask and cursor restore");Check(f.GetDefaults==0&&probe.SearchSettingsMayHaveChanged,"fresh find settings");
  Check(f.Conditions.All(c=>Convert.ToInt32(c["IgnoreMessage"])==1&&Convert.ToInt32(c["MatchCase"])==1&&Convert.ToInt32(c["FindRegExp"])==0&&!c.ContainsKey("FindCharShape")),"literal search conditions");
  result=probe.FindAnchor("table unique");Check(result.Complete&&result.Matches.Length==1&&result.Matches[0].List==2&&result.Matches[0].FirstPhysicalPage==2&&result.Matches[0].ContainerVerified,"table anchor");
  result=probe.FindAnchor("repeat");Check(result.Complete&&result.Matches.Length==2,"duplicate search enumeration");
  result=probe.FindAnchor("missing");Check(result.Complete&&result.Matches.Length==0,"absent text");
  f=new Fake();probe=Probe(f,false);result=probe.FindAnchor("table unique");Check(!result.Matches[0].PhysicalPageVerified,"page semantics are explicit");
  f=new Fake{WrongPage=true};probe=Probe(f);result=probe.FindAnchor("table unique");Check(!result.Matches[0].PhysicalPageVerified,"printed page reset fails page text cross-check");
  f=new Fake();probe=Probe(f,true,true);probe.FindAnchor("unique anchor");var observed=probe.InspectParagraph(0,2);var expected=new HwpParagraphProofs(f.FullXml).CreateExpected("BODY/SECTION[0]/P[2]");Check(observed.ContainerVerified&&observed.ContainerToken==expected.ContainerToken&&observed.Text=="repeat","relative duplicate target evidence");
  f=new Fake{BadSelection=true};probe=Probe(f);bool rejected=false;try{probe.FindAnchor("unique anchor");}catch(InvalidOperationException){rejected=true;}Check(rejected&&f.List==99&&f.Restores==0&&f.Mask==123,"do not restore over user movement");
  f=new Fake();probe=Probe(f);f.MutateDocument=true;rejected=false;try{probe.FindAnchor("unique anchor");}catch(InvalidOperationException){rejected=true;}Check(rejected&&f.Executions==0,"stale document prevents search");
  f=new Fake();probe=Probe(f);int standaloneReads=f.FullXmlReads;probe.FindAnchor("unique anchor");Check(f.FullXmlReads-standaloneReads==4,"standalone search independently checks complete digest");
  f=new Fake();probe=new NativeRepeatFindProbe(()=>f.Document,f.Snapshot(),new HwpParagraphProofs(f.FullXml),true,true,f.Inspect,true);probe.FindAnchor("unique anchor");probe.InspectParagraph(0,2);Check(f.FullXmlReads==0&&f.List==0&&f.Para==0&&f.Pos==2,"coordinated read avoids duplicate complete serialization and restores cursor");
  f=new Fake();probe=Probe(f);bool wrongThreadRejected=false;var wrongThread=new System.Threading.Thread(delegate(){try{probe.FindAnchor("unique anchor");}catch(InvalidOperationException){wrongThreadRejected=true;}});wrongThread.Start();wrongThread.Join();Check(wrongThreadRejected&&f.Executions==0,"different thread prevents native access");
  f=new Fake{Mode=1,Start=1,End=4,Pos=4};probe=Probe(f);result=probe.FindAnchor("table unique");Check(result.Complete&&result.Matches.Length==1&&f.Mode==1&&f.List==0&&f.Para==0&&f.Pos==4&&f.Start==1&&f.End==4,"original selection is restored");
  Console.WriteLine("PASS native RepeatFind probe on synthetic IHwpObject: literal flags, duplicate/absent/table matches, tokens, pages, masks, stale document, thread affinity and user cursor/selection protection");
 }
}
