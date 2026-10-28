using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Text;
using System.Xml;
using System.Web.Script.Serialization;
using HAddon;
using Hwp.Addon;

internal static class NativeTableStyleActionsTests {
 static int checks;
 static void Assert(bool value,string label){checks++;if(!value)throw new Exception(label);}
 sealed class Proxy:RealProxy{
  readonly Func<IMethodCallMessage,object[],object> callback;
  internal Proxy(Type type,Func<IMethodCallMessage,object[],object> fn):base(type){callback=fn;}
  public override IMessage Invoke(IMessage message){var call=(IMethodCallMessage)message;try{object[] args=call.Args;object result=callback(call,args);return new ReturnMessage(result,args,args.Length,call.LogicalCallContext,call);}catch(Exception ex){return new ReturnMessage(ex,call);}}
 }
 sealed class Fake {
  internal readonly IHwpObject Doc;readonly IHwpCtrlCode[] controls=new IHwpCtrlCode[3];readonly XmlDocument xml;
  int activeTable,row,col,mode,userPosition=7,selectionStart,selectionEnd;public int Copies,Applies,RestoreCount,LastCopyRow,LastCopyColumn;public bool ReversedAxis,CorruptOnApply,KeepBlockOnCancel,CompactAt17,CompactCharAt18,NonRowMajor,SkipTarget,DownFirst,NoCellText,BodyCaret,DriftOnCellBlock;
  internal void UserTextSelection(int start,int end){mode=1;selectionStart=start;selectionEnd=end;userPosition=end;}
  internal void UserBodyCaret(int position){BodyCaret=true;mode=0;userPosition=position;}
  string border,para,character;string[] mergedTexts;
  public bool MergedPreserved(){var table=(XmlElement)xml.GetElementsByTagName("TABLE")[2];var cells=table.GetElementsByTagName("CELL").Cast<XmlElement>().ToArray();var cell=cells.Single(c=>c.GetAttribute("RowAddr")=="1"&&c.GetAttribute("ColAddr")=="1");return cells.Length==8&&cell.GetAttribute("RowSpan")=="2"&&cell.GetElementsByTagName("P").Cast<XmlElement>().Select(x=>x.InnerText).SequenceEqual(mergedTexts);}
  XmlElement Cell(){return xml.GetElementsByTagName("TABLE").Cast<XmlElement>().ToArray()[activeTable].GetElementsByTagName("CELL").Cast<XmlElement>().Single(x=>x.GetAttribute("RowAddr")==row.ToString()&&x.GetAttribute("ColAddr")==col.ToString());}
  IHwpParameterSet Params(bool anchor,int index=0){var values=new Dictionary<string,object>();if(anchor){values["List"]=0;values["Para"]=new[]{5,15,18}[index];values["Pos"]=0;}else foreach(string key in new[]{"Type","CellAttr","CellBorder","CellFill","TypeBodyAndCellOnly"})values[key]=0;
   return (IHwpParameterSet)new Proxy(typeof(IHwpParameterSet),(m,a)=>{switch(m.MethodName){case "ItemExist":return values.ContainsKey((string)a[0]);case "GetItem":return values[(string)a[0]];case "SetItem":values[(string)a[0]]=a[1];return null;default:throw new Exception("parameter:"+m.MethodName);}}).GetTransparentProxy();}
  IHwpAction Action(){return (IHwpAction)new Proxy(typeof(IHwpAction),(m,a)=>{switch(m.MethodName){case "CreateSet":return Params(false);case "GetDefault":return true;case "Execute":
    var cell=Cell();var p=(XmlElement)cell.GetElementsByTagName("P")[0];var text=(XmlElement)p.GetElementsByTagName("TEXT")[0];
    if(mode==0){border=cell.GetAttribute("BorderFill");para=p.GetAttribute("ParaShape");character=text.GetAttribute("CharShape");LastCopyRow=row;LastCopyColumn=col;Copies++;}
    else{cell.SetAttribute("BorderFill",border);foreach(XmlElement paraNode in cell.GetElementsByTagName("P")){paraNode.SetAttribute("ParaShape",para);foreach(XmlElement textNode in paraNode.GetElementsByTagName("TEXT"))textNode.SetAttribute("CharShape",character);}TableBorderSimulation.Propagate(xml,activeTable,row,col);if(CorruptOnApply)((XmlElement)text.GetElementsByTagName("CHAR")[0]).InnerText="tampered";Applies++;if(CompactAt17&&Applies==17)TableBorderSimulation.Compact(xml,3);if(CompactCharAt18&&Applies==18)TableBorderSimulation.CompactTextStyle(xml,"CHARSHAPELIST","CHARSHAPE","CharShape",9);}return 1;
   default:throw new Exception("action:"+m.MethodName);}}).GetTransparentProxy();}
  IHwpAction BorderAction(){
   var values=new Dictionary<string,object>();var set=(IHwpParameterSet)new Proxy(typeof(IHwpParameterSet),(m,a)=>{switch(m.MethodName){case "ItemExist":return values.ContainsKey((string)a[0]);case "GetItem":return values[(string)a[0]];case "SetItem":values[(string)a[0]]=a[1];return null;default:throw new Exception(m.MethodName);}}).GetTransparentProxy();
   Func<XmlElement> borderDef=()=>{var list=(XmlElement)xml.GetElementsByTagName("BORDERFILLLIST")[0];return list.ChildNodes.OfType<XmlElement>().Single(x=>x.GetAttribute("Id")==Cell().GetAttribute("BorderFill"));};
   string[] sides={"Left","Right","Top","Bottom"};
   return (IHwpAction)new Proxy(typeof(IHwpAction),(m,a)=>{switch(m.MethodName){case "CreateSet":return set;case "GetDefault":
    var before=borderDef();foreach(string side in sides){var edge=(XmlElement)before.SelectSingleNode(side.ToUpperInvariant()+"BORDER");foreach(string key in new[]{"Type","Width","Color"})values["Border"+key+side]=edge.GetAttribute(key);}return true;
   case "Execute":
    var list=(XmlElement)xml.GetElementsByTagName("BORDERFILLLIST")[0];var clone=(XmlElement)borderDef().CloneNode(true);foreach(string side in sides){var edge=(XmlElement)clone.SelectSingleNode(side.ToUpperInvariant()+"BORDER");foreach(string key in new[]{"Type","Width","Color"}){string value=(string)values["Border"+key+side];if(value.Length==0)edge.RemoveAttribute(key);else edge.SetAttribute(key,value);}}
    var cmp=(XmlElement)clone.CloneNode(true);cmp.RemoveAttribute("Id");string canonical=ComposeXml.Canonical(cmp);var match=list.ChildNodes.OfType<XmlElement>().FirstOrDefault(x=>{var c=(XmlElement)x.CloneNode(true);c.RemoveAttribute("Id");return ComposeXml.Canonical(c)==canonical;});
    if(match==null){clone.SetAttribute("Id",(list.ChildNodes.OfType<XmlElement>().Max(x=>Int32.Parse(x.GetAttribute("Id")))+1).ToString());list.AppendChild(clone);list.SetAttribute("Count",list.ChildNodes.OfType<XmlElement>().Count().ToString());match=clone;}Cell().SetAttribute("BorderFill",match.GetAttribute("Id"));TableBorderSimulation.Propagate(xml,activeTable,row,col);return 1;
   default:throw new Exception(m.MethodName);}}).GetTransparentProxy();
  }
  internal Fake(string path,bool reversed,bool varied=false,bool merged=false,bool tallMerge=false,bool square=false,bool squareAll=false){
   ReversedAxis=reversed;xml=new XmlDocument{PreserveWhitespace=true,XmlResolver=null};xml.LoadXml(File.ReadAllText(path,Encoding.UTF8));
   if(varied){var table=(XmlElement)xml.GetElementsByTagName("TABLE")[1];table.SetAttribute("ColCount","2");foreach(XmlElement r in table.GetElementsByTagName("ROW")){var last=r.ChildNodes.Cast<XmlNode>().OfType<XmlElement>().Single(x=>x.LocalName=="CELL"&&x.GetAttribute("ColAddr")=="2");r.RemoveChild(last);}}
   if(merged){var table=(XmlElement)xml.GetElementsByTagName("TABLE")[2];var rows=table.ChildNodes.Cast<XmlNode>().OfType<XmlElement>().Where(x=>x.LocalName=="ROW").ToArray();table.RemoveChild(rows[4]);table.RemoveChild(rows[3]);table.SetAttribute("RowCount","3");var cells=table.GetElementsByTagName("CELL").Cast<XmlElement>().ToArray();var target=cells.Single(c=>c.GetAttribute("RowAddr")=="1"&&c.GetAttribute("ColAddr")=="1");target.SetAttribute("RowSpan","2");var covered=cells.Single(c=>c.GetAttribute("RowAddr")=="2"&&c.GetAttribute("ColAddr")=="1");covered.ParentNode.RemoveChild(covered);var paras=(XmlElement)target.GetElementsByTagName("PARALIST")[0];paras.AppendChild(paras.GetElementsByTagName("P")[0].CloneNode(true));mergedTexts=target.GetElementsByTagName("P").Cast<XmlElement>().Select(x=>x.InnerText).ToArray();}
   if(tallMerge){var table=(XmlElement)xml.GetElementsByTagName("TABLE")[1];var cells=table.GetElementsByTagName("CELL").Cast<XmlElement>().ToArray();var target=cells.Single(c=>c.GetAttribute("RowAddr")=="1"&&c.GetAttribute("ColAddr")=="1");target.SetAttribute("RowSpan","4");var paras=(XmlElement)target.GetElementsByTagName("PARALIST")[0];foreach(var covered in cells.Where(c=>Int32.Parse(c.GetAttribute("RowAddr"))>=2&&c.GetAttribute("ColAddr")=="1")){foreach(XmlElement p in covered.GetElementsByTagName("P").Cast<XmlElement>().ToArray())paras.AppendChild(p.CloneNode(true));covered.ParentNode.RemoveChild(covered);}}
   if(square){var table=(XmlElement)xml.GetElementsByTagName("TABLE")[1];foreach(var r in table.ChildNodes.OfType<XmlElement>().Where(x=>x.LocalName=="ROW").Skip(3).ToArray())table.RemoveChild(r);table.SetAttribute("RowCount","3");}
   if(squareAll){foreach(XmlElement table in xml.GetElementsByTagName("TABLE")){foreach(var r in table.ChildNodes.OfType<XmlElement>().Where(x=>x.LocalName=="ROW").Skip(3).ToArray())table.RemoveChild(r);table.SetAttribute("RowCount","3");}}
   for(int i=0;i<controls.Length;i++){int x=i;controls[i]=(IHwpCtrlCode)new Proxy(typeof(IHwpCtrlCode),(m,a)=>{switch(m.MethodName){case "get_CtrlID":return "tbl";case "GetNext":return x+1<controls.Length?controls[x+1]:null;case "IsSame":return Object.ReferenceEquals(a[0],controls[x]);case "GetAnchorPos":return Params(true,x);default:throw new Exception("ctrl:"+m.MethodName);}}).GetTransparentProxy();}
   Doc=(IHwpObject)new Proxy(typeof(IHwpObject),(m,a)=>{switch(m.MethodName){case "IsReadOnlyMode":return false;case "GetSelectionMode":return mode;case "GetCurSelectedCtrl":return null;
    case "GetID":return 7;case "GetPath":return "synthetic.hwp";case "GetTextFile":if((string)a[0]=="TEXT"&&(string)a[1]=="saveblock")return NoCellText?"":Cell().InnerText;return xml.OuterXml;case "GetHeadCtrl":return controls[0];case "GetParentCtrl":return BodyCaret?null:controls[activeTable];
    case "GetPos":a[0]=BodyCaret?0:41;a[1]=0;a[2]=userPosition;return true;case "GetSelectedPos":a[0]=BodyCaret?0:41;a[1]=0;a[2]=selectionStart;a[3]=BodyCaret?0:41;a[4]=0;a[5]=selectionEnd;return mode==1;case "GetTableCellAddr":return (long)(((long)a[0]==0)^ReversedAxis?row:col);
    case "SelectCtrl":BodyCaret=false;activeTable=Array.FindIndex(controls,c=>Object.ReferenceEquals(c,a[0]));mode=4;return activeTable>=0;
    case "Run":switch((string)a[0]){case "ShapeObjTableSelCell":row=0;col=0;mode=3;return true;case "TableRightCell":var all=((XmlElement)xml.GetElementsByTagName("TABLE")[activeTable]).GetElementsByTagName("CELL").Cast<XmlElement>().OrderBy(x=>Int32.Parse(x.GetAttribute("RowAddr"))).ThenBy(x=>Int32.Parse(x.GetAttribute("ColAddr"))).ToArray();if(NonRowMajor&&all.Length>7){var tmp=all[6];all[6]=all[7];all[7]=tmp;}if(DownFirst&&all.Length>3){var tmp=all[1];all[1]=all[3];all[3]=tmp;}int idx=Array.FindIndex(all,x=>x.GetAttribute("RowAddr")==row.ToString()&&x.GetAttribute("ColAddr")==col.ToString());if(idx<0||idx+1>=all.Length)return false;int next=idx+1;if(SkipTarget&&all[next].GetAttribute("RowAddr")=="2"&&all[next].GetAttribute("ColAddr")=="2")next++;if(next>=all.Length)return false;row=Int32.Parse(all[next].GetAttribute("RowAddr"));col=Int32.Parse(all[next].GetAttribute("ColAddr"));return true;case "TableCellBlock":mode=3;if(DriftOnCellBlock){row=0;col=0;}return true;case "Cancel":if(!KeepBlockOnCancel)mode=0;return true;default:throw new Exception("run:"+a[0]);}
    case "CreateAction":if((string)a[0]=="ShapeCopyPaste")return Action();if((string)a[0]=="CellBorderFill")return BorderAction();throw new Exception("unexpected action");case "SetPos":RestoreCount++;mode=0;BodyCaret=(int)a[0]==0;userPosition=(int)a[2];return true;case "SelectText":selectionStart=(int)a[1];selectionEnd=(int)a[3];userPosition=selectionEnd;mode=1;return true;default:throw new Exception("document:"+m.MethodName);}
   }).GetTransparentProxy();
  }
 }
 static void Main(string[] args){
  AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e){string name=new AssemblyName(e.Name).Name;return new[]{"HwpAddonHelper","Hnc.Foundation.Interface","Hnc.Foundation.InternalInterface"}.Contains(name)?Assembly.LoadFrom(Path.Combine(@"C:\Program Files (x86)\Hnc\Office 2024\HOffice130\Bin",name+".dll")):null;};
  try{Run(args);}catch(Exception ex){Console.Error.WriteLine(ex.Message);Environment.ExitCode=1;}
 }
 static void Run(string[] args){
  if(args.Length<1||args.Length>2)throw new ArgumentException("Pass saved HML fixture and optional AI plan.");
  if(args.Length==2&&args[1]=="--navigation-only"){
   {var sample=new Fake(args[0],false){BodyCaret=true};var context=TableCursorContext.CaptureFirstForDesign(sample.Doc);Assert(context.SourceIndex==0&&context.DesignSnapshot!=null,"explicit first table is pinned with body caret");}
   foreach(bool reverse in new[]{false,true}){var sample=new Fake(args[0],reverse,false,false,true){NonRowMajor=true};var context=TableCursorContext.Capture(sample.Doc);var adapter=new NativeTableStyleActions(()=>sample.Doc,context);adapter.CopyCellStyle(1,2,2);Assert(sample.Copies==1,"merged four-row navigation handles a reordered traversal");}
   foreach(bool reverse in new[]{false,true}){var sample=new Fake(args[0],reverse){DownFirst=true};var context=TableCursorContext.Capture(sample.Doc);var adapter=new NativeTableStyleActions(()=>sample.Doc,context);adapter.CopyCellStyle(1,0,1);Assert(sample.Copies==1,"axis calibration does not confuse down-first traversal with swapped coordinates");}
   {var sample=new Fake(args[0],false,false,false,false,true);var context=TableCursorContext.Capture(sample.Doc);var adapter=new NativeTableStyleActions(()=>sample.Doc,context);adapter.CopyCellStyle(1,0,1);Assert(sample.Copies==1,"square target calibrates axis on a non-square table");}
   foreach(bool reverse in new[]{false,true}){var sample=new Fake(args[0],reverse,false,false,false,false,true){DownFirst=true};var context=TableCursorContext.Capture(sample.Doc);var adapter=new NativeTableStyleActions(()=>sample.Doc,context);adapter.CopyCellStyle(1,0,1);Assert(sample.Copies==1&&sample.LastCopyRow==0&&sample.LastCopyColumn==1,"square geometry is disambiguated by native cell text before using coordinates");}
   {var sample=new Fake(args[0],false,false,false,false,false,true){NoCellText=true};var context=TableCursorContext.Capture(sample.Doc);var adapter=new NativeTableStyleActions(()=>sample.Doc,context);bool rejected=false;try{adapter.CopyCellStyle(1,0,1);}catch(InvalidOperationException){rejected=true;}Assert(rejected&&sample.Copies==0,"ambiguous square and unreadable cell text cannot write a guessed cell");}
   {var sample=new Fake(args[0],false,false,false,true){SkipTarget=true};var context=TableCursorContext.Capture(sample.Doc);var adapter=new NativeTableStyleActions(()=>sample.Doc,context);bool rejected=false;try{adapter.CopyCellStyle(1,2,2);}catch(InvalidOperationException){rejected=true;}Assert(rejected&&sample.Copies==0,"unreachable target never applies to a different cell");}
   Console.WriteLine("PASS "+checks+" merged navigation cases with fake HWP interfaces; no live HWP");return;
  }
  {var values=new Dictionary<string,object>();foreach(string side in new[]{"Left","Right","Top","Bottom"})foreach(string property in new[]{"Type","Width","Color"})values["Border"+property+side]=side+property;
   int executions=0;var set=(IHwpParameterSet)new Proxy(typeof(IHwpParameterSet),(m,a)=>{switch(m.MethodName){case "ItemExist":return values.ContainsKey((string)a[0]);case "GetItem":return values[(string)a[0]];case "SetItem":values[(string)a[0]]=a[1];return null;default:throw new Exception(m.MethodName);}}).GetTransparentProxy();
   var action=(IHwpAction)new Proxy(typeof(IHwpAction),(m,a)=>{switch(m.MethodName){case "CreateSet":return set;case "GetDefault":return true;case "Execute":executions++;return 1;default:throw new Exception(m.MethodName);}}).GetTransparentProxy();
   var doc=(IHwpObject)new Proxy(typeof(IHwpObject),(m,a)=>{if(m.MethodName=="CreateAction"&&(string)a[0]=="CellBorderFill")return action;throw new Exception(m.MethodName);}).GetTransparentProxy();
   var left=NativeTableBorderParameters.Read(doc,"Left");var right=NativeTableBorderParameters.Read(doc,"Right");NativeTableBorderParameters.Apply(doc,new Dictionary<string,NativeTableBorderValue>{{"Left",right},{"Right",left}});
   Assert(executions==1&&(string)values["BorderTypeLeft"]=="RightType"&&(string)values["BorderColorRight"]=="LeftColor","independent native border parameters are combined in one command");
   values.Remove("BorderColorLeft");bool rejected=false;try{NativeTableBorderParameters.Apply(doc,new Dictionary<string,NativeTableBorderValue>{{"Left",left}});}catch(InvalidOperationException){rejected=true;}
   Assert(rejected&&executions==1,"missing border parameter is rejected before native execute");}
  {var fake=new Fake(args[0],false,false,true);var pinned=TableCursorContext.CaptureForDesign(fake.Doc);
   Assert(pinned.Plan==null&&pinned.DesignSnapshot!=null&&pinned.SourceIndex==0&&pinned.TableCount==3,"AI snapshot pins a merged-table document without running the legacy style planner");}
  foreach(bool reverse in new[]{false,true}){var fake=new Fake(args[0],reverse,false,false,true){NonRowMajor=true};var pinned=TableCursorContext.Capture(fake.Doc);var actions=new NativeTableStyleActions(()=>fake.Doc,pinned);actions.CopyCellStyle(1,2,2);Assert(fake.Copies==1,"non-row-major traversal beside a four-row merge reaches requested physical cell under either native axis convention");}
  {var fake=new Fake(args[0],false);var pinned=TableCursorContext.CaptureForDesign(fake.Doc);var legacy=TableStylePlan.Build(pinned.Original,pinned.SourceIndex);
   var design=legacy.Changes.Select(c=>{var s=new TableDesignCellRef{Table=0,Row=c.SourceRow,Column=c.SourceColumn};return new TableDesignChange{Target=new TableDesignCellRef{Table=c.Table,Row=c.Row,Column=c.Column},Fill=s,Paragraph=s,Character=s,Left=s,Right=s,Top=s,Bottom=s,Role="body"};}).ToArray();
   var plan=TableStylePlan.BuildFromDesign(pinned.Original,0,design);var tx=new TableStyleTransaction();tx.Apply(new NativeTableStyleActions(()=>fake.Doc,pinned.WithPlan(plan)),pinned.Original,plan);
   Assert(tx.AppliedCount==30&&!tx.RequiresReview,"semantic plans execute and verify through simulated native border commands");}
  if(args.Length==2){var fake=new Fake(args[0],false);var pinned=TableCursorContext.CaptureForDesign(fake.Doc);var serializer=new JavaScriptSerializer{MaxJsonLength=4*1024*1024};
   var ai=(Dictionary<string,object>)serializer.DeserializeObject(File.ReadAllText(args[1]));var design=TableDesignCompiler.Compile(pinned.DesignSnapshot,ai);var plan=TableStylePlan.BuildFromDesign(pinned.Original,0,design);
   var tx=new TableStyleTransaction();tx.Apply(new NativeTableStyleActions(()=>fake.Doc,pinned.WithPlan(plan)),pinned.Original,plan);Assert(tx.AppliedCount==30&&!tx.RequiresReview,"real AI plan passes simulated native execution and readback");}
  foreach(bool reverse in new[]{false,true}){
   var fake=new Fake(args[0],reverse);var pinned=TableCursorContext.Capture(fake.Doc);var tx=new TableStyleTransaction();tx.Apply(new NativeTableStyleActions(()=>fake.Doc,pinned),pinned.Original,pinned.Plan);
   Assert(fake.Copies==30&&fake.Applies==30&&fake.RestoreCount==1&&!tx.RequiresReview,"all 30 source-to-target native actions with both coordinate conventions");
  }
  {var fake=new Fake(args[0],false){CompactAt17=true};var pinned=TableCursorContext.Capture(fake.Doc);var tx=new TableStyleTransaction();tx.Apply(new NativeTableStyleActions(()=>fake.Doc,pinned),pinned.Original,pinned.Plan);
   Assert(fake.Copies==30&&fake.Applies==30&&!tx.RequiresReview,"full table operation succeeds when unused border 3 is compacted at the reported cell");}
  {var fake=new Fake(args[0],false){CompactAt17=true,CompactCharAt18=true};var pinned=TableCursorContext.Capture(fake.Doc);var tx=new TableStyleTransaction();tx.ApplyAsync(new NativeTableStyleActions(()=>fake.Doc,pinned),pinned.Original,pinned.Plan,()=>false,null).GetAwaiter().GetResult();
   Assert(fake.Copies==30&&fake.Applies==30&&!tx.RequiresReview,"actual asynchronous path completes all cells after border and character-style compaction");
   Assert(tx.FullVerifications==31&&tx.ReusedVerifications==30,"30 verified cells reuse 30 unchanged post-copy readbacks");}
  {var fake=new Fake(args[0],false,true);var pinned=TableCursorContext.Capture(fake.Doc);var tx=new TableStyleTransaction();tx.Apply(new NativeTableStyleActions(()=>fake.Doc,pinned),pinned.Original,pinned.Plan);
   Assert(fake.Applies==25&&!tx.RequiresReview,"different target column count uses its own coordinates and verified source roles");}
  {var fake=new Fake(args[0],false,false,true);var pinned=TableCursorContext.Capture(fake.Doc);var tx=new TableStyleTransaction();tx.ApplyAsync(new NativeTableStyleActions(()=>fake.Doc,pinned),pinned.Original,pinned.Plan,()=>false,null).GetAwaiter().GetResult();
   Assert(fake.Applies==23&&!tx.RequiresReview&&fake.MergedPreserved(),"merged target is handled once and both existing paragraphs are preserved");
   Assert(tx.FullVerifications==24&&tx.ReusedVerifications==23,"merged-document operation reuses all unchanged copy verifications");}
  {var fake=new Fake(args[0],false){KeepBlockOnCancel=true};var pinned=TableCursorContext.Capture(fake.Doc);var tx=new TableStyleTransaction();bool failed=false;try{tx.Apply(new NativeTableStyleActions(()=>fake.Doc,pinned),pinned.Original,pinned.Plan);}catch(InvalidOperationException){failed=true;}
   Assert(failed&&fake.Copies==0&&fake.Applies==0&&!tx.RequiresReview,"unchanged document is verified after failed selection conversion");}
  {var fake=new Fake(args[0],false){CorruptOnApply=true};var pinned=TableCursorContext.Capture(fake.Doc);var tx=new TableStyleTransaction();bool failed=false;try{tx.Apply(new NativeTableStyleActions(()=>fake.Doc,pinned),pinned.Original,pinned.Plan);}catch(InvalidOperationException){failed=true;}
   Assert(failed&&fake.Applies==1&&tx.RequiresReview&&fake.RestoreCount==1,"content mutation stops before a second cell and restores cursor");}
  {var fake=new Fake(args[0],false);var pinned=TableCursorContext.Capture(fake.Doc);var actions=new NativeTableStyleActions(()=>fake.Doc,pinned);fake.UserBodyCaret(8);string stamp=actions.ReadCaret();Assert(actions.TryAdoptCursor(stamp),"body cursor can be adopted without a native command");actions.CopyCellStyle(0,0,0);actions.RestoreCursor();Assert(actions.ReadCaret()==stamp,"latest body cursor survives explicit source cell navigation");}
  {var fake=new Fake(args[0],false);var pinned=TableCursorContext.Capture(fake.Doc);var actions=new NativeTableStyleActions(()=>fake.Doc,pinned);fake.UserTextSelection(2,5);string stamp=actions.ReadCaret();Assert(actions.TryAdoptCursor(stamp),"text selection endpoints are captured for a handoff");actions.CopyCellStyle(0,0,0);actions.RestoreCursor();Assert(actions.ReadCaret()==stamp,"latest text selection is restored with both endpoints");}
  {var fake=new Fake(args[0],false);var pinned=TableCursorContext.Capture(fake.Doc);var actions=new NativeTableStyleActions(()=>fake.Doc,pinned);fake.UserTextSelection(2,5);string previous=actions.ReadCaret();fake.UserTextSelection(3,5);string latest=actions.ReadCaret();Assert(previous!=latest&&!actions.TryAdoptCursor(previous)&&actions.TryAdoptCursor(latest),"selection endpoints distinguish a new drag even with the same active caret");actions.CopyCellStyle(0,0,0);actions.RestoreCursor();Assert(actions.ReadCaret()==latest,"latest drag endpoints survive native navigation");}
  {var fake=new Fake(args[0],false);var pinned=TableCursorContext.Capture(fake.Doc);var actions=new NativeTableStyleActions(()=>fake.Doc,pinned);fake.Doc.Run("TableCellBlock");string stamp=actions.ReadCaret();Assert(!actions.TryAdoptCursor(stamp)&&fake.Doc.GetSelectionMode()==3&&fake.Copies==0&&fake.Applies==0,"unknown cell-block restore is rejected without cancelling or overwriting the new selection");}
  {var fake=new Fake(args[0],false);var pinned=TableCursorContext.Capture(fake.Doc);var actions=new NativeTableStyleActions(()=>fake.Doc,pinned);actions.CopyCellStyle(0,0,0);fake.DriftOnCellBlock=true;bool failed=false;try{actions.ApplyCellStyle(1,0,1);}catch(InvalidOperationException){failed=true;}Assert(failed&&fake.Applies==0,"cell changed during block selection cannot receive a style meant for another cell");}
  Console.WriteLine("PASS "+checks+" native table adapter scenarios using fake HWP interfaces; no live HWP was accessed.");
 }
}
