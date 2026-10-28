using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hnc.Foundation.Interface;
using Hwp.Addon;

namespace HAddon {
 internal sealed class NativePageSnapshot {public int DocumentId;public string Path,Digest;public string[] Pages,TablePages;public object[] Paragraphs;}
 internal sealed class DocumentAddressCandidate {public string XmlPath,Text;public int List,Paragraph,Start,End;}
 internal sealed class PagePositionEvidence {public int RawPage;}
 internal sealed class NativePagePositionBackend {public NativePagePositionBackend(Func<IHwpObject> getDoc,NativePageSnapshot snapshot){}}
 internal static class NativePagePositionEvidence {public static PagePositionEvidence[] Collect(NativePagePositionBackend b,DocumentAddressCandidate[] c){return new[]{new PagePositionEvidence{RawPage=TestState.SelectedPage}};}}
 internal static class NativePageIndicator {public static bool TryReadRaw(IHwpObject doc,out int n){n=1;return true;}public static string Diagnose(IHwpObject doc){return "synthetic";}}
 internal sealed class NativeResponsivePageSource {public NativeResponsivePageSource(Func<IHwpObject> getDoc){}}
 internal sealed class ResponsivePageResult {public string Digest="v1";public string[] Pages={"앞 고정 선택 A 뒤","둘째 쪽 근거","셋째 쪽 근거"},TablePages;public object[] Paragraphs;}
 internal static class ResponsivePageReader {public static Task<ResponsivePageResult> ReadAsync(NativeResponsivePageSource source,CancellationToken token,Action<int,int> progress){token.ThrowIfCancellationRequested();if(TestState.DeferRead){TestState.PendingRead=new TaskCompletionSource<ResponsivePageResult>();token.Register(()=>TestState.PendingRead.TrySetCanceled());return TestState.PendingRead.Task;}return Task.FromResult(new ResponsivePageResult());}}
 internal sealed class PageScope {public int[] Targets,Sources;}
 internal static class PageScopeService {public static Task<PageScope> Analyze(BridgeClient b,NativePageSnapshot s,Func<IHwpObject> getDoc,string id,string account,string model,string effort,string prompt,object history){return Task.FromResult(new PageScope{Targets=new[]{1},Sources=new[]{2,3}});}}
 internal sealed class NativeAutomaticPageUpdateEnvironment {
  public static NativeAutomaticPageUpdateEnvironment CreateTextOnly(BridgeClient bridge,Func<IHwpObject> document,string id,string account,string model,string effort,object history,bool physical,bool relative=false){if(!physical||relative)throw new Exception("unexpected native page validation flags");TestState.AutoEnvironments++;return new NativeAutomaticPageUpdateEnvironment();}
 }
 internal sealed class PageClarificationRequiredException:InvalidOperationException {public string[] Questions;}
 internal sealed class AutomaticPageUpdateResult {public int ChangedCount;public string[] Uncertainties;public bool NeedsClarification;}
 internal sealed class DocumentUpdateTransaction {public int ChangedCount;public bool RequiresReview;}
 internal sealed class AutomaticPageUpdate {
  public DocumentUpdateTransaction Transaction;
  public async Task<AutomaticPageUpdateResult> RunAsync(NativeAutomaticPageUpdateEnvironment environment,string prompt,CancellationToken token,Action<string,int,int> progress){
   TestState.AutoRuns++;TestState.AutoPrompt=prompt;token.ThrowIfCancellationRequested();progress("read",1,3);
   if(TestState.DeferAuto){TestState.PendingAuto=new TaskCompletionSource<int>();using(token.Register(()=>TestState.PendingAuto.TrySetCanceled()))await TestState.PendingAuto.Task;}
   token.ThrowIfCancellationRequested();progress("plan",0,2);
   if(TestState.AutoFailAfterWrite){Transaction=new DocumentUpdateTransaction{ChangedCount=1,RequiresReview=true};throw new InvalidOperationException("synthetic partial native write");}
   return new AutomaticPageUpdateResult{ChangedCount=TestState.AutoUncertain?0:2,Uncertainties=TestState.AutoUncertain?new[]{"확인할 상세 내용"}:new string[0],NeedsClarification=TestState.AutoClarify};
  }
 }
 internal sealed class FrameRoute {
  public IHncFrameEngine Engine; public static FrameRoute Capture(IHncFrameEngine engine){return new FrameRoute();}
  public void RequireCurrent() {}
 }
 internal sealed class SelectionRequest {
  public FrameRoute Route;public SelectionEditor Editor;public Dictionary<string,object> State;
  public CaretAnchor Caret;public string CaretError;public bool FromPopup;public TableCursorContext Table;public string TableError;
 }
 internal static class SelectionRequests {
  public static IHwpObject Document(FrameRoute route){return TestState.Doc.Proxy;}
  public static void Register(FrameRoute route,WorkspaceBody pane){}
  public static void Unregister(FrameRoute route,WorkspaceBody pane){}
 }
 internal sealed class SelectionEditor {
  public bool Changed,RequiresReview;public string Text="고정 선택 A";
  public void EndRequest(){}
  public object Invoke(string method,Dictionary<string,object> args){
   if(method!="hwp_editor_state")throw new Exception("unexpected selection method");
   if(Changed)throw new InvalidOperationException("사용자의 선택 범위가 바뀌었습니다.");
   return new Dictionary<string,object>{{"targetId","selection-A"},{"text",Text}};
  }
 }
 internal sealed class NativeComposer {
  public bool RequiresReview;
  public NativeComposer(Func<IHwpObject> doc){}
  public object BeginRequest(){TestState.ComposeBegins++;if(TestState.MoveDuringBegin)TestState.Doc.Position++;if(TestState.FailCompose)return new Dictionary<string,object>{{"capabilities",new Dictionary<string,object>{{"compose",false}}},{"context",new Dictionary<string,object>{{"reason","빈 문단이 아닙니다."}}}};return new Dictionary<string,object>{{"capabilities",new Dictionary<string,object>{{"compose",true}}}};}
  public object Invoke(string method,Dictionary<string,object> args){return new Dictionary<string,object>();}
  public void EndRequest(){}
 }
 internal sealed class NativeRichComposer {
  public bool RequiresReview;
  public NativeRichComposer(Func<IHwpObject> doc){}
  public object BeginRequest(){
   TestState.RichBegins++;bool selected=TestState.RichHasSelection;if(TestState.RichDuringBegin!=null)TestState.RichDuringBegin();
   return new Dictionary<string,object>{{"text",selected?"고정 선택 A":""},{"capabilities",new Dictionary<string,object>{{"compose",true}}},{"context",new Dictionary<string,object>{{"selectionPresent",selected},{"scope",selected?"wholeBodyParagraphSelection":"emptyBodyParagraph"}}}};
  }
  public object Invoke(string method,Dictionary<string,object> args){return new Dictionary<string,object>();}
  public void EndRequest(){}
 }
 internal sealed class TableCursorContext {
  public int SourceIndex,TableCount=3;public object Original;public FakePlan Plan=new FakePlan();
  public static TableCursorContext Capture(IHwpObject doc){if(!TestState.HasTableCursor)throw new InvalidOperationException("not in table");TestState.TableCaptures++;if(TestState.FailTableCapture)throw new InvalidOperationException("표 3의 2행 2열에 병합된 셀이 있습니다.");return new TableCursorContext{SourceIndex=TestState.TableSourceIndex};}
  public static TableCursorContext CaptureForDesign(IHwpObject doc){return Capture(doc);}
  public static TableCursorContext CaptureFirstForDesign(IHwpObject doc){TestState.TableCaptures++;if(TestState.FailTableCapture)throw new InvalidOperationException("표를 찾지 못했습니다.");return new TableCursorContext{SourceIndex=0};}
  public TableCursorContext WithPlan(FakePlan plan){Plan=plan;return this;}
  public static bool HasTableCaret(IHwpObject doc){return TestState.HasTableCursor;}
  public static bool HasBodyTables(IHwpObject doc){return TestState.HasTables;}
 }
 internal sealed class FakePlan {public object[] Changes=new object[]{new object()};}
 internal static class TableStylePlan {public static FakePlan BuildFromDesign(object original,int source,object design){return new FakePlan();}}
 internal static class TableDesignService {public static Task<object> Analyze(BridgeClient bridge,TableCursorContext table,Func<IHwpObject> doc,string requestId,string account,string model,string effort,string prompt,object history=null){TestState.DesignReference=table.SourceIndex;TestState.DesignPrompt=prompt;TestState.DesignHistory=history;return Task.FromResult<object>(new object());}}
 internal sealed class NativeTableStyleActions {public NativeTableStyleActions(Func<IHwpObject> doc,TableCursorContext context){}}
 internal sealed class TableStyleTransaction {
  public bool RequiresReview,CursorRestored;public int AppliedCount;
  public int AttemptedTable=-1,AttemptedRow=-1,AttemptedColumn=-1;public bool Applying;
  public Task ApplyAsync(NativeTableStyleActions actions,object original,object plan,Func<bool> cancelled,Action<int> progress){TestState.TableApplies++;AppliedCount=30;CursorRestored=true;if(progress!=null)progress(AppliedCount);return Task.FromResult(0);}
 }
 internal sealed class BridgeConfig {public static BridgeConfig Load(){return new BridgeConfig();}}
 internal sealed class BridgeClient : IDisposable {
  public event Action<Dictionary<string,object>> EditorRequest;
  public bool IsAlive {get{return true;}}
  public BridgeClient(BridgeConfig config){}
  public Task<Dictionary<string,object>> Request(Dictionary<string,object> request,string id){
   TestState.Requests.Add(new Dictionary<string,object>(request));string action=(string)request["action"];
   if(action=="route"){
    if(TestState.FailRoute)return Task.FromException<Dictionary<string,object>>(new InvalidOperationException("분류 실패"));
    if(TestState.DeferRoute){TestState.DeferRoute=false;TestState.Pending=new TaskCompletionSource<Dictionary<string,object>>();return TestState.Pending.Task;}
    return Task.FromResult(Wrap(TestState.Decision));
   }
   if(action=="generate")return Task.FromResult(Wrap(new Dictionary<string,object>{{"text",TestState.Answer}}));
   if(action=="compose"||action=="rich-compose"||action=="edit")return Task.FromResult(Wrap(new Dictionary<string,object>{{"text","합성 완료"},{"appliedCount",0}}));
   if(action=="steer"){
    if(TestState.DeferSteer){TestState.PendingSteer=new TaskCompletionSource<Dictionary<string,object>>();TestState.SteerTarget=request["targetId"];return TestState.PendingSteer.Task;}
    return Task.FromResult(Wrap(new Dictionary<string,object>{{"accepted",true},{"targetId",request["targetId"]}}));
   }
   if(action=="cancel")return Task.FromResult(Wrap(new Dictionary<string,object>{{"cancelled",true}}));
   throw new Exception("Unexpected bridge action: "+action);
  }
  public static Dictionary<string,object> Wrap(Dictionary<string,object> data){return new Dictionary<string,object>{{"data",data}};}
  public static Dictionary<string,object> Data(Dictionary<string,object> data){return (Dictionary<string,object>)data["data"];}
  public static string GetString(Dictionary<string,object> value,string key){object result;return value.TryGetValue(key,out result)&&result!=null?Convert.ToString(result):"";}
  public void SendEditorResult(string id,string requestId,object value,string error){}
  public void Dispose(){}
 }
 internal static class TestState {
  public static FakeDocument Doc;public static List<Dictionary<string,object>> Requests;public static Dictionary<string,object> Decision;
  public static TaskCompletionSource<Dictionary<string,object>> Pending,PendingSteer;public static TaskCompletionSource<ResponsivePageResult> PendingRead;public static object SteerTarget;
  public static TaskCompletionSource<int> PendingAuto;public static bool DeferAuto,AutoUncertain,AutoFailAfterWrite,AutoClarify;public static int AutoEnvironments,AutoRuns;public static string AutoPrompt;
  public static int DesignReference;public static string DesignPrompt;public static object DesignHistory;
  public static bool DeferRoute,DeferSteer,DeferRead,FailRoute,FailCompose,RichHasSelection,MoveDuringBegin,HasTableCursor,HasTables,FailTableCapture,SelectedTextRange;public static Action RichDuringBegin;public static int ComposeBegins,RichBegins,TableCaptures,TableApplies,TableSourceIndex,SelectedPage;public static string Answer;
  public static void Reset(){
   Doc=new FakeDocument();Requests=new List<Dictionary<string,object>>();Decision=DecisionFor("question");Pending=PendingSteer=null;PendingRead=null;SteerTarget=null;
   DeferRoute=DeferSteer=DeferRead=FailRoute=FailCompose=RichHasSelection=MoveDuringBegin=HasTableCursor=FailTableCapture=SelectedTextRange=false;HasTables=true;RichDuringBegin=null;ComposeBegins=RichBegins=TableCaptures=TableApplies=TableSourceIndex=0;SelectedPage=1;Answer="이전 대화 답변";
   PendingAuto=null;DeferAuto=AutoUncertain=AutoFailAfterWrite=AutoClarify=false;AutoEnvironments=AutoRuns=0;AutoPrompt=null;
   DesignReference=-1;DesignPrompt=null;DesignHistory=null;
  }
  public static Dictionary<string,object> DecisionFor(string intent,bool table=false,bool image=false,bool previous=false,bool selected=false,string reference=null){
   return new Dictionary<string,object>{{"intent",intent},{"needsTable",table},{"needsImage",image},{"usePreviousAnswer",previous},{"useSelectionInQuestion",selected},{"needsDocumentContext",false},{"tableReference",reference??(intent=="table_style"?"caret":"none")},{"model","fixture-model"}};
  }
 }
 internal sealed class CallbackProxy:RealProxy {
  readonly Func<IMethodCallMessage,object[],object> call;
  public CallbackProxy(Type type,Func<IMethodCallMessage,object[],object> callback):base(type){call=callback;}
  public override IMessage Invoke(IMessage message){
   var request=(IMethodCallMessage)message;
   try { object[] args=request.Args;object result=call(request,args);return new ReturnMessage(result,args,args.Length,request.LogicalCallContext,request); }
   catch(Exception ex){return new ReturnMessage(ex,request);}
  }
 }
 internal sealed class FakeDocument {
  public readonly IHwpObject Proxy;public int Id=7,List,Paragraph=2,Position=16,Mode;public string Path="synthetic.hwp";
  readonly IHwpCtrlCode table;
  public FakeDocument(){table=(IHwpCtrlCode)new CallbackProxy(typeof(IHwpCtrlCode),(m,a)=>{if(m.MethodName=="get_CtrlID")return "tbl";throw new Exception(m.MethodName);}).GetTransparentProxy();Proxy=(IHwpObject)new CallbackProxy(typeof(IHwpObject),Call).GetTransparentProxy();}
  object Call(IMethodCallMessage method,object[] args){
   switch(method.MethodName){
    case "GetID":return Id; case "GetPath":return Path;case "GetSelectionMode":return Mode;case "GetCurSelectedCtrl":return null;case "GetParentCtrl":return TestState.HasTableCursor?table:null;
    case "GetPos":args[0]=List;args[1]=Paragraph;args[2]=Position;return true;
    case "GetSelectedPos":args[0]=List;args[1]=Paragraph;args[2]=Position;args[3]=List;args[4]=Paragraph;args[5]=Position+(TestState.SelectedTextRange?"고정 선택 A".Length:0);return TestState.SelectedTextRange;
    default:throw new Exception("unexpected document call "+method.MethodName);
   }
  }
 }
}
internal static class ConversationWorkflowTests {
 static int checks;
 static void Assert(bool value,string label){checks++;if(!value)throw new Exception(label);}
 static object Field(object obj,string name){return obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(obj);}
 static void Set(object obj,string name,object value){obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(obj,value);}
 static void Invoke(object obj,string name){obj.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(obj,null);}
 static TextBox Composer(HAddon.WorkspaceBody pane){return(TextBox)Field(pane,"composer");}
 static string Status(HAddon.WorkspaceBody pane){return((TextBlock)Field(pane,"status")).Text;}
 static HAddon.WorkspaceBody Pane(){
  var pane=new HAddon.WorkspaceBody();Set(pane,"ownerRoute",new HAddon.FrameRoute());Set(pane,"ready",true);Set(pane,"accountId","synthetic-account");Set(pane,"bridge",new HAddon.BridgeClient(new HAddon.BridgeConfig()));return pane;
 }
 static void Send(HAddon.WorkspaceBody pane,string text){Composer(pane).Text=text;Invoke(pane,"SendComposer");Pump();}
 static void Pump(){
  var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);
 }
 static Dictionary<string,object> LastAction(string action){return HAddon.TestState.Requests.LastOrDefault(x=>(string)x["action"]==action);}
 static void CompleteRoute(){HAddon.TestState.Pending.SetResult(HAddon.BridgeClient.Wrap(HAddon.TestState.Decision));Pump();}
 [STAThread] static void Main(){
  AppDomain.CurrentDomain.AssemblyResolve += delegate(object s,ResolveEventArgs e){
   string name=new AssemblyName(e.Name).Name;
   if(name!="HwpAddonHelper"&&name!="Hnc.Foundation.Interface"&&name!="Hnc.Foundation.InternalInterface")return null;
   return Assembly.LoadFrom(Path.Combine(@"C:\Program Files (x86)\Hnc\Office 2024\HOffice130\Bin",name+".dll"));
  };
  SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
  try{Run();Console.WriteLine("PASS: "+checks+" offscreen semantic conversation workflow checks; no HWP UI or inference.");}
  catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}
 }
 static void Run(){
  HAddon.TestState.Reset();var previewPane=Pane();var selectedText=new string('가',289);var previewRequest=new HAddon.SelectionRequest{Route=(HAddon.FrameRoute)Field(previewPane,"ownerRoute"),Editor=new HAddon.SelectionEditor{Text=selectedText},FromPopup=true,State=new Dictionary<string,object>{{"text",selectedText},{"context",new Dictionary<string,object>{{"selectionPresent",true}}},{"capabilities",new Dictionary<string,object>{{"rewrite",true},{"format",false}}}}};
  previewPane.AcceptSelection(previewRequest);var preview=(TextBox)Field(previewPane,"preview");var scope=(TextBlock)Field(previewPane,"scope");
  Assert(preview.Text==selectedText&&preview.Visibility==Visibility.Visible&&scope.Text.Contains("289자")&&scope.Text.Contains("요청 전 확인"),"selection preview shows the complete selected target and count");
  preview.Text="갱신된 문장";Invoke(previewPane,"RefreshScope");Assert(scope.Text.Contains(preview.Text.Length+"자")&&!scope.Text.Contains("289자"),"selection count updates when preview changes");
  HAddon.TestState.Reset();var first=Pane();HAddon.TestState.Decision=HAddon.TestState.DecisionFor("table_style",reference:"first");HAddon.TestState.Decision["needsDocumentContext"]=true;
  Send(first,"첫번째 표로 다른 표 수정해줘");Assert((bool)LastAction("route")["hasTables"]&&!(bool)LastAction("route")["hasTableCursor"]&&HAddon.TestState.TableCaptures==1&&HAddon.TestState.TableApplies==1&&HAddon.TestState.ComposeBegins==0&&HAddon.TestState.AutoRuns==0&&HAddon.TestState.DesignReference==0,"semantic first table reference uses the first native table from a body caret without entering page-scope");
  Assert(HAddon.TestState.DesignPrompt=="첫번째 표로 다른 표 수정해줘"&&HAddon.TestState.DesignHistory!=null,"the natural user instruction and conversation reach table design without keyword rewriting");
  HAddon.TestState.Reset();first=Pane();Set(first,"generalMode",false);Set(first,"native",new HAddon.SelectionEditor());HAddon.TestState.Doc.Mode=1;HAddon.TestState.SelectedTextRange=true;HAddon.TestState.Decision=HAddon.TestState.DecisionFor("table_style",reference:"first");
  Send(first,"그 기준으로 나머지도 맞춰줘");Assert(HAddon.TestState.TableApplies==1&&HAddon.TestState.DesignReference==0&&HAddon.TestState.AutoRuns==0&&LastAction("edit")==null,"semantic table reference remains valid with a selected paragraph and a contextual follow-up");
  HAddon.TestState.Reset();first=Pane();HAddon.TestState.HasTables=false;HAddon.TestState.Decision=HAddon.TestState.DecisionFor("table_style",reference:"first");Send(first,"첫번째 표로 다른 표 수정해줘");Assert(HAddon.TestState.TableApplies==0&&HAddon.TestState.AutoRuns==0&&HAddon.TestState.ComposeBegins==0,"an unavailable native first table never falls through to page edits or new writing");
  HAddon.TestState.Reset();first=Pane();HAddon.TestState.Decision=HAddon.TestState.DecisionFor("table_style",reference:"first");HAddon.TestState.DeferRoute=true;Send(first,"첫번째 표로 다른 표 수정해줘");HAddon.TestState.Doc.Id++;CompleteRoute();Assert(HAddon.TestState.TableCaptures==0&&HAddon.TestState.TableApplies==0&&HAddon.TestState.AutoRuns==0,"switching documents during semantic routing cannot recapture another document's first table");
  foreach(string intent in new[]{"compose","edit"}){
   HAddon.TestState.Reset();var guarded=Pane();HAddon.TestState.Decision=HAddon.TestState.DecisionFor(intent);HAddon.TestState.Decision["needsDocumentContext"]=true;
   Send(guarded,"6쪽 이후를 참고해서 1~5쪽을 갱신해줘");
   Assert(HAddon.TestState.AutoEnvironments==1&&HAddon.TestState.AutoRuns==1&&HAddon.TestState.AutoPrompt=="6쪽 이후를 참고해서 1~5쪽을 갱신해줘"&&Status(guarded)=="본문 갱신 완료","unselected context edit uses automatic page workflow");
   Assert(HAddon.TestState.ComposeBegins==0&&HAddon.TestState.RichBegins==0&&LastAction("generate")==null&&LastAction("edit")==null,"automatic page workflow bypasses ordinary composition and selection paths");
  }
  HAddon.TestState.Reset();var pageQuestion=Pane();HAddon.TestState.Decision=HAddon.TestState.DecisionFor("question");HAddon.TestState.Decision["needsDocumentContext"]=true;Send(pageQuestion,"6쪽의 내용을 설명해줘");Assert(HAddon.TestState.AutoRuns==0,"page question does not start automatic writes");
  HAddon.TestState.Reset();var autoPane=Pane();HAddon.TestState.Decision=HAddon.TestState.DecisionFor("edit");HAddon.TestState.Decision["needsDocumentContext"]=true;HAddon.TestState.DeferAuto=true;Send(autoPane,"6쪽 이후로 요약을 갱신해줘");Assert(HAddon.TestState.PendingAuto!=null&&(string)Field(autoPane,"activeKind")=="page-update"&&((Button)Field(autoPane,"stop")).IsEnabled,"automatic page update exposes cancellation");Invoke(autoPane,"Cancel");Pump();Assert(LastAction("cancel")!=null&&!(bool)Field(autoPane,"busy")&&Status(autoPane)!="본문 갱신 완료","automatic page cancellation reaches both token and active bridge request");
  HAddon.TestState.Reset();autoPane=Pane();HAddon.TestState.Decision=HAddon.TestState.DecisionFor("edit");HAddon.TestState.Decision["needsDocumentContext"]=true;HAddon.TestState.AutoUncertain=true;Send(autoPane,"6쪽 이후로 요약을 갱신해줘");Assert(Status(autoPane)=="요청을 완료하지 못했습니다."&&!(bool)Field(autoPane,"requiresReview"),"unresolved automatic target does not claim body success");
  HAddon.TestState.Reset();autoPane=Pane();HAddon.TestState.Decision=HAddon.TestState.DecisionFor("edit");HAddon.TestState.Decision["needsDocumentContext"]=true;HAddon.TestState.AutoUncertain=true;HAddon.TestState.AutoClarify=true;Send(autoPane,"6쪽 이후를 참고해 요약을 고쳐줘");Assert(Status(autoPane)=="확인 답변 대기"&&HAddon.TestState.AutoRuns==1,"uncertain evidence asks before editing");HAddon.TestState.AutoUncertain=false;HAddon.TestState.AutoClarify=false;Send(autoPane,"뒤쪽 세부 내용을 기준으로 해줘");Assert(HAddon.TestState.AutoRuns==2&&HAddon.TestState.AutoPrompt.Contains("6쪽 이후를 참고해 요약을 고쳐줘")&&HAddon.TestState.AutoPrompt.Contains("뒤쪽 세부 내용을 기준으로 해줘")&&Status(autoPane)=="본문 갱신 완료","clarification resumes the original request with the answer");
  HAddon.TestState.Reset();autoPane=Pane();HAddon.TestState.Decision=HAddon.TestState.DecisionFor("edit");HAddon.TestState.Decision["needsDocumentContext"]=true;HAddon.TestState.AutoFailAfterWrite=true;Send(autoPane,"6쪽 이후로 요약을 갱신해줘");Assert((bool)Field(autoPane,"requiresReview")&&(int)Field(autoPane,"appliedCount")==1&&Status(autoPane)!="본문 갱신 완료","automatic partial write retains transaction review state");
  HAddon.TestState.Reset();var selectedPagePane=Pane();Set(selectedPagePane,"generalMode",false);Set(selectedPagePane,"native",new HAddon.SelectionEditor());HAddon.TestState.Doc.Mode=1;HAddon.TestState.SelectedTextRange=true;HAddon.TestState.Decision=HAddon.TestState.DecisionFor("edit");HAddon.TestState.Decision["needsDocumentContext"]=true;
  Send(selectedPagePane,"6쪽 이후를 참고해 선택한 요약을 갱신해줘");var selectedEdit=LastAction("edit");
  Assert(selectedEdit!=null&&((object[])selectedEdit["pageContext"]).Length==2&&LastAction("compose")==null&&HAddon.TestState.AutoRuns==0,"page references reach only selected edit target");
  Assert(((System.Collections.IEnumerable)Field(selectedPagePane,"conversation")).Cast<object>().Any(x=>((string)x.GetType().GetField("Text",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(x)).Contains("본문 변경 없이")&&!((string)x.GetType().GetField("Text",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(x)).Contains("선택한 한 문단만 적용했으며")),"no-op result must not claim a write");
  HAddon.TestState.Reset();selectedPagePane=Pane();Set(selectedPagePane,"generalMode",false);Set(selectedPagePane,"native",new HAddon.SelectionEditor());HAddon.TestState.Doc.Mode=1;HAddon.TestState.SelectedTextRange=true;HAddon.TestState.SelectedPage=3;HAddon.TestState.Decision=HAddon.TestState.DecisionFor("edit");HAddon.TestState.Decision["needsDocumentContext"]=true;
  Send(selectedPagePane,"6쪽 이후를 참고해 선택한 요약을 갱신해줘");Assert(LastAction("edit")==null,"a selected page outside target scope must never write");
  HAddon.TestState.Reset();selectedPagePane=Pane();Set(selectedPagePane,"generalMode",false);Set(selectedPagePane,"native",new HAddon.SelectionEditor());HAddon.TestState.Doc.Mode=1;HAddon.TestState.SelectedTextRange=true;HAddon.TestState.DeferRead=true;HAddon.TestState.Decision=HAddon.TestState.DecisionFor("edit");HAddon.TestState.Decision["needsDocumentContext"]=true;
  Send(selectedPagePane,"6쪽 이후를 참고해 선택한 요약을 갱신해줘");Assert(HAddon.TestState.PendingRead!=null&&((Button)Field(selectedPagePane,"stop")).IsEnabled,"page reading can be cancelled");Invoke(selectedPagePane,"Cancel");Pump();Assert(LastAction("edit")==null,"cancelled page read never writes");
  HAddon.TestState.Reset();var pane=Pane();HAddon.TestState.HasTableCursor=true;HAddon.TestState.Doc.List=4;HAddon.TestState.Decision=HAddon.TestState.DecisionFor("table_style",reference:"first");
  Send(pane,"지금 첫번째 표 양식으로 나머지 양식을 다 통일해줘");
  Assert((bool)LastAction("route")["hasTableCursor"]&&(bool)LastAction("route")["hasTables"]&&HAddon.TestState.TableApplies==1&&HAddon.TestState.DesignReference==0,"first table formatting uses semantic native availability and not the page workflow");
  Assert(HAddon.TestState.RichBegins==0&&Status(pane)=="표 서식 통일 완료","table formatting never enters rich document insertion");
  HAddon.TestState.Reset();pane=Pane();HAddon.TestState.HasTableCursor=true;HAddon.TestState.Doc.List=4;HAddon.TestState.TableSourceIndex=1;HAddon.TestState.Decision=HAddon.TestState.DecisionFor("table_style",reference:"first");
  Send(pane,"첫 번째 표 양식으로 나머지 표를 통일해줘");Assert(HAddon.TestState.TableApplies==1&&HAddon.TestState.DesignReference==0,"semantic first table is selected even if caret is in a different table");
  HAddon.TestState.Reset();pane=Pane();HAddon.TestState.HasTableCursor=true;HAddon.TestState.Doc.List=4;HAddon.TestState.Decision=HAddon.TestState.DecisionFor("compose",table:true);
  Send(pane,"이 표처럼 두번째 표만 바꿔줘");Assert(HAddon.TestState.TableApplies==0,"single-target scope cannot silently become all tables");
  HAddon.TestState.Reset();pane=Pane();HAddon.TestState.Decision=HAddon.TestState.DecisionFor("table_style");
  var selectedRequest=new HAddon.SelectionRequest{Route=(HAddon.FrameRoute)Field(pane,"ownerRoute"),Editor=new HAddon.SelectionEditor(),FromPopup=true,Table=new HAddon.TableCursorContext{SourceIndex=0},State=new Dictionary<string,object>{{"context",new Dictionary<string,object>{{"selectionPresent",true}}},{"capabilities",new Dictionary<string,object>{{"rewrite",false},{"format",false}}}}};
  pane.AcceptSelection(selectedRequest);Send(pane,"이 표 양식을 다른 표에도 같은 디자인으로 통일해");
  Assert((bool)LastAction("route")["hasSelection"]&&(bool)LastAction("route")["hasTableCursor"]&&HAddon.TestState.TableApplies==1,"selected table is pinned before popup focus and routes natural other-tables request");
  HAddon.TestState.Reset();pane=Pane();HAddon.TestState.Decision=HAddon.TestState.DecisionFor("table_style");
  Send(pane,"이 표의 양식으로 다른 표를 통일해줘");
  Assert(HAddon.TestState.TableApplies==0&&HAddon.TestState.RichBegins==0&&HAddon.TestState.ComposeBegins==0&&Status(pane)=="요청을 완료하지 못했습니다.","failed table capture cannot fall through to document composition");
  HAddon.TestState.Reset();pane=Pane();HAddon.TestState.HasTableCursor=true;HAddon.TestState.Decision=HAddon.TestState.DecisionFor("table_style");
  Send(pane,"이 표의 양식으로 다른 표를 통일해줘");
  Assert(HAddon.TestState.TableApplies==1&&HAddon.TestState.RichBegins==0,"semantic reference-style request follows the pinned table");
  HAddon.TestState.Reset();pane=Pane();HAddon.TestState.HasTableCursor=true;HAddon.TestState.Decision=HAddon.TestState.DecisionFor("table_style");
  Send(pane,"이 표 모양대로 다른 표도 통일해줘");
  Assert(HAddon.TestState.TableApplies==1&&HAddon.TestState.RichBegins==0,"the user's natural 모양대로 wording routes to pinned table");
  HAddon.TestState.Reset();pane=Pane();HAddon.TestState.Decision=HAddon.TestState.DecisionFor("table_style");
  Send(pane,"이 표 모양대로 다른 표도 통일해줘");
  Assert(HAddon.TestState.TableApplies==0&&HAddon.TestState.ComposeBegins==0&&HAddon.TestState.RichBegins==0,"모양대로 never becomes unrelated composition when table capture fails");
  HAddon.TestState.Reset();pane=Pane();HAddon.TestState.HasTableCursor=true;HAddon.TestState.Doc.List=4;HAddon.TestState.FailTableCapture=true;HAddon.TestState.Decision=HAddon.TestState.DecisionFor("table_style");
  Send(pane,"모양을 다른 표에 맞춰줘");
  Assert((bool)LastAction("route")["hasTableCursor"]&&HAddon.TestState.RichBegins==0&&HAddon.TestState.ComposeBegins==0,"unsupported table structure still routes with actual table-caret context");
  Assert(((System.Collections.IEnumerable)Field(pane,"conversation")).Cast<object>().Any(x=>((string)x.GetType().GetField("Text",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(x)).Contains("표 3의 2행 2열")),"unsupported table reports the specific capture failure");
  HAddon.TestState.Reset();pane=Pane();HAddon.TestState.HasTableCursor=true;HAddon.TestState.Doc.List=4;HAddon.TestState.FailTableCapture=true;HAddon.TestState.Decision=HAddon.TestState.DecisionFor("compose",table:true);
  Send(pane,"모양을 다른 표에 맞춰줘");
  Assert(HAddon.TestState.RichBegins==0&&HAddon.TestState.ComposeBegins==0,"unsupported table does not fall through to rich composition even if router says compose");
  HAddon.TestState.Reset();pane=Pane();HAddon.TestState.HasTableCursor=true;HAddon.TestState.Decision=HAddon.TestState.DecisionFor("question");
  Send(pane,"이 표 모양대로 다른 표도 통일할 수 있어?");
  Assert(HAddon.TestState.TableApplies==0&&LastAction("generate")!=null,"asking about table styling remains a question without changing cells");

  HAddon.TestState.Reset();pane=Pane();
  Send(pane,"이 방식의 장단점은?");
  Assert(LastAction("route")!=null&&LastAction("generate")!=null,"question routes to generation");
  Assert(HAddon.TestState.ComposeBegins==0&&HAddon.TestState.RichBegins==0,"question does not start native compose");
  Assert((string)Field(pane,"lastDraft")=="이전 대화 답변","question response saved for conversation");

  HAddon.TestState.Decision=HAddon.TestState.DecisionFor("compose",previous:true);
  Send(pane,"이걸 문서화해줘");
  Assert((string)LastAction("compose")["sourceText"]=="이전 대화 답변","documentization uses prior answer");
  Assert((bool)LastAction("route")["hasPreviousAnswer"],"router sees prior answer exists");

  HAddon.TestState.Decision=HAddon.TestState.DecisionFor("compose");
  Send(pane,"새 주제로 회의 안내문을 작성해줘");
  Assert(!LastAction("compose").ContainsKey("sourceText"),"unrelated new document excludes old draft");

  HAddon.TestState.Reset();pane=Pane();HAddon.TestState.Decision=HAddon.TestState.DecisionFor("compose",image:true);
  Send(pane,"AI와 함께 글쓰기 강의자료 1페이지 시안 만들고 작업 흐름도 인포그래픽으로 1장 추가");
  Assert(LastAction("rich-compose")!=null && (bool)LastAction("rich-compose")["needsImage"],"infographic becomes rich body request");
  Assert(HAddon.TestState.RichBegins==1&&HAddon.TestState.ComposeBegins==0,"infographic begins rich target");

  HAddon.TestState.Reset();pane=Pane();HAddon.TestState.Decision=HAddon.TestState.DecisionFor("compose");HAddon.TestState.MoveDuringBegin=true;
  Send(pane,"본문에 원고를 작성해줘");
  Assert(LastAction("compose")==null,"caret change during native begin must prevent composition");
  Assert(Composer(pane).Text=="본문에 원고를 작성해줘","caret change during native begin preserves request");

  HAddon.TestState.Reset();pane=Pane();var matchingSelection=new HAddon.SelectionEditor();Set(pane,"generalMode",false);Set(pane,"native",matchingSelection);
  HAddon.TestState.Decision=HAddon.TestState.DecisionFor("edit",table:true);HAddon.TestState.RichHasSelection=true;HAddon.TestState.RichDuringBegin=delegate{matchingSelection.Changed=true;};
  Send(pane,"선택 내용을 표로 바꿔줘");
  Assert(LastAction("rich-compose")==null,"selection change during native begin must prevent rich composition");
  Assert(Composer(pane).Text=="선택 내용을 표로 바꿔줘","selection change during native begin preserves request");

  HAddon.TestState.Reset();pane=Pane();var selected=new HAddon.SelectionEditor();Set(pane,"generalMode",false);Set(pane,"native",selected);
  HAddon.TestState.Decision=HAddon.TestState.DecisionFor("edit",table:true);HAddon.TestState.DeferRoute=true;
  Send(pane,"선택한 내용을 표로 바꿔줘");Assert(HAddon.TestState.Pending!=null,"selected request waits for route");
  selected.Changed=true;CompleteRoute();
  Assert(HAddon.TestState.RichBegins==0&&LastAction("rich-compose")==null,"selection A changed to B causes no native rich begin");
  Assert(Composer(pane).Text=="선택한 내용을 표로 바꿔줘","selection failure restores request");

  HAddon.TestState.Reset();pane=Pane();var caret=HAddon.CaretAnchor.Capture(HAddon.TestState.Doc.Proxy);
  Set(pane,"popupCaret",caret);Set(pane,"popupCaretBound",true);
  HAddon.TestState.Decision=HAddon.TestState.DecisionFor("compose");HAddon.TestState.DeferRoute=true;
  Send(pane,"제목 작성해줘");HAddon.TestState.Doc.Position++;CompleteRoute();
  Assert(HAddon.TestState.ComposeBegins==0&&LastAction("compose")==null,"popup caret A changed to B causes no write");
  Assert(Composer(pane).Text=="제목 작성해줘","caret failure restores request");

  HAddon.TestState.Reset();pane=Pane();caret=HAddon.CaretAnchor.Capture(HAddon.TestState.Doc.Proxy);
  Set(pane,"popupCaret",caret);Set(pane,"popupCaretBound",true);
  HAddon.TestState.Decision=HAddon.TestState.DecisionFor("question");HAddon.TestState.DeferRoute=true;
  Send(pane,"이 기능은 무엇인가요?");HAddon.TestState.Doc.Position++;CompleteRoute();
  Assert(LastAction("generate")!=null&&HAddon.TestState.ComposeBegins==0,"question remains allowed after caret change");

  HAddon.TestState.Reset();pane=Pane();HAddon.TestState.Decision=HAddon.TestState.DecisionFor("compose");HAddon.TestState.DeferRoute=true;
  Send(pane,"AI 활용 방법을 설명해줘");
  Composer(pane).Text="그 내용을 한글 본문 보고서로 작성해줘";Invoke(pane,"SendComposer");Pump();
  Assert(LastAction("steer")!=null,"additional instruction reaches active route");
  CompleteRoute();
  var afterSteer=LastAction("compose");
  Assert(afterSteer!=null,"steered route enters native composition");
  var seenSteer=((string)afterSteer["prompt"]).Contains("한글 본문 보고서");
  object historyRaw;
  if(afterSteer.TryGetValue("history",out historyRaw)){
   var history=historyRaw as System.Collections.IEnumerable;
   if(history!=null)foreach(var raw in history){
    var row=raw as Dictionary<string,object>;object value;
    if(row!=null&&row.TryGetValue("text",out value)&&value is string&&((string)value).Contains("한글 본문 보고서"))seenSteer=true;
   }
  }
  Assert(seenSteer,"accepted route-phase steering reaches subsequent document planning");

  HAddon.TestState.Reset();pane=Pane();HAddon.TestState.Decision=HAddon.TestState.DecisionFor("compose");HAddon.TestState.DeferRoute=true;HAddon.TestState.DeferSteer=true;
  Send(pane,"AI 활용 방법을 설명해줘");
  Composer(pane).Text="그 내용을 본문 보고서로 작성해줘";Invoke(pane,"SendComposer");Pump();
  Assert(HAddon.TestState.PendingSteer!=null,"route-phase steer is pending");
  CompleteRoute();
  Assert(HAddon.TestState.Requests.Count(x=>(string)x["action"]=="route")==1&&LastAction("compose")==null,"routing waits for steer acknowledgment before moving on");
  HAddon.TestState.PendingSteer.SetResult(HAddon.BridgeClient.Wrap(new Dictionary<string,object>{{"accepted",true},{"targetId",HAddon.TestState.SteerTarget}}));Pump();
  Assert(HAddon.TestState.Requests.Count(x=>(string)x["action"]=="route")==2&&LastAction("compose")!=null,"acknowledged steering triggers reroute and composition");
  Assert(((string)LastAction("compose")["prompt"]).Contains("본문 보고서"),"acknowledged steering reaches final composition prompt");

  HAddon.TestState.Reset();pane=Pane();HAddon.TestState.Decision=HAddon.TestState.DecisionFor("compose");HAddon.TestState.DeferRoute=true;
  Send(pane,new string('A',15000));
  var tooLongSteer=new string('B',1001);Composer(pane).Text=tooLongSteer;Invoke(pane,"SendComposer");Pump();
  Assert(Composer(pane).Text==tooLongSteer&&LastAction("steer")==null,"over-limit steer remains editable and is not sent");

  HAddon.TestState.Reset();pane=Pane();var questionSelection=new HAddon.SelectionEditor();Set(pane,"generalMode",false);Set(pane,"native",questionSelection);
  HAddon.TestState.Decision=HAddon.TestState.DecisionFor("question",selected:true);
  Send(pane,"이 선택 내용은 무슨 뜻이야?");
  Assert(LastAction("generate")!=null&&HAddon.TestState.ComposeBegins==0&&HAddon.TestState.RichBegins==0,"selection question generates an answer without writing");
  Assert(LastAction("generate").Values.Any(v=>v is string&&((string)v).Contains("고정 선택 A")),"selection question supplies selected text");
  HAddon.TestState.Reset();pane=Pane();questionSelection=new HAddon.SelectionEditor();Set(pane,"generalMode",false);Set(pane,"native",questionSelection);
  HAddon.TestState.Decision=HAddon.TestState.DecisionFor("question",selected:false);
  Send(pane,"일반 질문이야");
  Assert(LastAction("generate")!=null&&!LastAction("generate").Values.Any(v=>v is string&&((string)v).Contains("고정 선택 A")),"unrelated question does not disclose selected text");
  HAddon.TestState.Reset();pane=Pane();HAddon.TestState.FailRoute=true;
  Send(pane,"문서 요청 내용");
  Assert(Composer(pane).Text=="문서 요청 내용"&&LastAction("compose")==null,"route failure preserves input without write");
  HAddon.TestState.Reset();pane=Pane();HAddon.TestState.Decision=HAddon.TestState.DecisionFor("compose");HAddon.TestState.FailCompose=true;
  Send(pane,"문서 요청 내용");
  Assert(Composer(pane).Text=="문서 요청 내용"&&LastAction("compose")==null,"native preflight failure preserves input without write");
 }
}
