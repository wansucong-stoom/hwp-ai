// Hancom managed workspace prototype. Native loading needs separate validation.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hnc.Foundation.Interface;
using Hnc.Foundation.InternalInterface;
using Hwp.Addon;
[assembly: AssemblyTitle("STOOM Codex for Hancom HWP")]
[assembly: AssemblyVersion("0.6.28.0")]
namespace HAddon {
 public sealed class AddonMain : HncAddonBase {
  readonly IHncFrameEngine owner;
  public AddonMain(IHncFrameEngine engine,IAddonAdapter adapter):base(engine,adapter){owner=engine;}
  protected override bool OnStartup(){return true;}
  protected override void OnShutdown(){SelectionRequests.Shutdown(owner);}
 }
 public sealed class SelectionEditActor : IHncAddonActor {
  public void Dispose(){}
  public bool Run(IHncWidget sender,IHncFrameEngine frameEngine,HncAddonBase addonMain){
   try{return SelectionRequests.Open(frameEngine);}catch(Exception ex){frameEngine.ShowToast("AI 요청 창을 열지 못했습니다: "+ex.Message);return false;}
  }
 }
 public sealed class ChatOpenActor : IHncAddonActor {
  public void Dispose(){}
  public bool Run(IHncWidget sender,IHncFrameEngine frameEngine,HncAddonBase addonMain){
   try{return SelectionRequests.Open(frameEngine,false);}catch(Exception ex){frameEngine.ShowToast("AI 대화를 열지 못했습니다: "+ex.Message);return false;}
  }
 }
 internal sealed class SelectionRequest {
  public SelectionEditor Editor;public Dictionary<string,object> State;public string Prompt;
  public CaretAnchor Caret;public string CaretError;public bool FromPopup;public TableCursorContext Table;public string TableError;
  public FrameRoute Route;
  // A Loaded handler can deliver synchronously inside SetWorkspaceItemVisibility.
  internal bool Delivered;
 }
 internal static class SelectionRequests {
  const string WorkspaceId="IDC_STOOM_CODEX_WORKSPACE";
  static readonly Dictionary<FrameRoute,WorkspaceBody> workspaces=new Dictionary<FrameRoute,WorkspaceBody>();
  static readonly Dictionary<FrameRoute,SelectionRequest> pending=new Dictionary<FrameRoute,SelectionRequest>();
  static readonly Dictionary<FrameRoute,AiRequestPopup> popups=new Dictionary<FrameRoute,AiRequestPopup>();

  internal static IHwpObject Document(IHncFrameEngine frame){
   var engine=frame as IHncInternalFrameEngine;
   if(engine==null)throw new InvalidOperationException("한글 문서 연결을 확인할 수 없습니다.");
   var doc=HwpObjectFactory.Create(engine);if(doc==null)throw new InvalidOperationException("열린 문서를 확인할 수 없습니다.");return doc;
  }
  internal static IHwpObject Document(FrameRoute route){
   if(route==null)throw new InvalidOperationException("고정된 한글 문서 창이 없습니다.");
   route.RequireCurrent();var doc=Document(route.Engine);route.RequireCurrent();return doc;
  }
  static bool Owns(FrameRoute route,WorkspaceBody workspace){
   return route!=null&&workspace!=null&&route.Equals(workspace.OwnerRoute);
  }
  static void Release(SelectionRequest request){
   if(request!=null&&request.Editor!=null){try{request.Editor.EndRequest();}catch{}}
  }
  static void ValidateRoute(FrameRoute route,SelectionRequest request){
   if(route==null||request==null||request.Route==null||!route.Equals(request.Route))
    throw new InvalidOperationException("요청을 시작한 한글 문서 창 정보가 일치하지 않습니다.");
   route.RequireCurrent();
  }

  public static bool Open(IHncFrameEngine frame){return Open(frame,true);}
  public static bool Open(IHncFrameEngine frame,bool usePopup){
   // IHncFrameEngine is shared; capture the actual frame ID exactly once here.
   FrameRoute route=FrameRoute.Capture(frame);route.RequireCurrent();
   WorkspaceBody workspace;
   if(workspaces.TryGetValue(route,out workspace)&&Owns(route,workspace)&&workspace.IsEditing){
    if(!usePopup){frame.SetWorkspaceItemVisibility(WorkspaceId,AttrType.Visibility.Show);route.RequireCurrent();workspace.FocusComposer();return true;}
    if(!workspace.CanSteer){frame.ShowToast("Codex 연결을 준비하고 있습니다. 잠시 뒤 다시 요청하세요.");return false;}
    string target=workspace.ActiveRequestId;WorkspaceBody owner=workspace;
    var steerRequest=new SelectionRequest {Route=route};
    ShowPopup(route,steerRequest,"진행 중인 요청에 추가 지시","",true,delegate(string prompt){
     route.RequireCurrent();WorkspaceBody current;
     if(!workspaces.TryGetValue(route,out current)||!Object.ReferenceEquals(current,owner)||!Owns(route,owner))return false;
     bool accepted=owner.SubmitSteerPopup(target,prompt);route.RequireCurrent();return accepted;
    });return true;
   }
   TableCursorContext table=null;string tableError=null;try{table=TableCursorContext.CaptureForDesign(Document(route));}catch(Exception ex){tableError=ex.Message;}
   var editor=new SelectionEditor(delegate{return Document(route);});
   // Freeze selection before opening or focusing any instruction UI.
   var request=new SelectionRequest {Route=route,Editor=editor,State=editor.BeginRequest() as Dictionary<string,object>,FromPopup=usePopup,Table=table,TableError=tableError};
   try{
    route.RequireCurrent();
    if(!usePopup){bool queued=Queue(route,request);if(!queued)Release(request);return queued;}
    object rawContext,rawCaps,present;
    var context=request.State!=null&&request.State.TryGetValue("context",out rawContext)?rawContext as Dictionary<string,object>:null;
    bool general=context!=null&&context.TryGetValue("selectionPresent",out present)&&present is bool&&!(bool)present;
    if(general){try{request.Caret=CaretAnchor.Capture(Document(route));}catch(Exception ex){request.CaretError=ex.Message;}}
    var caps=request.State!=null&&request.State.TryGetValue("capabilities",out rawCaps)?rawCaps as Dictionary<string,object>:null;object rewrite,format;
    object selectionMode;bool cellBlock=context!=null&&context.TryGetValue("selectionMode",out selectionMode)&&selectionMode is int&&(((int)selectionMode&15)==3);
    bool supported=general||request.Table!=null||cellBlock||(caps!=null&&caps.TryGetValue("rewrite",out rewrite)&&rewrite is bool&&caps.TryGetValue("format",out format)&&format is bool&&((bool)rewrite||(bool)format));
    string scope=general?"AI 요청":request.Table!=null?"선택한 표를 기준으로 AI 요청":supported?"선택한 글에 AI 편집":context==null?"선택 영역을 확인하지 못했습니다.":BridgeClient.GetString(context,"reason");
    ShowPopup(route,request,scope,BridgeClient.GetString(request.State??new Dictionary<string,object>(),"text"),supported,
     delegate(string prompt){request.Prompt=prompt;return Queue(route,request);});return true;
   }catch{if(!request.Delivered)Release(request);throw;}
  }

  static void ShowPopup(FrameRoute route,SelectionRequest request,string scope,string preview,bool canSend,Func<string,bool> send){
   ValidateRoute(route,request);AiRequestPopup popup=null;
   try{
    AiRequestPopup previous;if(popups.TryGetValue(route,out previous))previous.Close();
    route.RequireCurrent();
    popup=new AiRequestPopup(request,scope,preview,canSend,send);popups[route]=popup;
    AiRequestPopup ownedPopup=popup;
    popup.Closed+=delegate{AiRequestPopup current;if(popups.TryGetValue(route,out current)&&Object.ReferenceEquals(current,ownedPopup))popups.Remove(route);};
    popup.Show();route.RequireCurrent();
   }catch{if(popup!=null){try{popup.Close();}catch{}}else Release(request);throw;}
  }

  static bool Queue(FrameRoute route,SelectionRequest request){
   ValidateRoute(route,request);
   WorkspaceBody workspace;
   if(workspaces.TryGetValue(route,out workspace)&&(!Owns(route,workspace)||workspace.IsEditing))return false;
   SelectionRequest prior;if(pending.TryGetValue(route,out prior)&&!Object.ReferenceEquals(prior,request))Release(prior);
   pending[route]=request;
   try{
    route.RequireCurrent();route.Engine.SetWorkspaceItemVisibility(WorkspaceId,AttrType.Visibility.Show);route.RequireCurrent();
    // Register/Deliver may have run reentrantly during the visibility call.
    if(request.Delivered)return true;
    if(workspaces.TryGetValue(route,out workspace))return Deliver(route,workspace);
    SelectionRequest queued;return pending.TryGetValue(route,out queued)&&Object.ReferenceEquals(queued,request);
   }catch{
    SelectionRequest queued;
    if(pending.TryGetValue(route,out queued)&&Object.ReferenceEquals(queued,request)){pending.Remove(route);Release(request);}
    throw;
   }
  }

  public static void Register(FrameRoute route,WorkspaceBody workspace){
   if(!Owns(route,workspace))return;
   workspaces[route]=workspace;
   // A background frame may load after focus moved. Retain its registration,
   // but never deliver a native request while a different frame is current.
   if(route.IsCurrent)Deliver(route,workspace);
  }
  static bool Deliver(FrameRoute route,WorkspaceBody workspace){
   SelectionRequest request;if(!pending.TryGetValue(route,out request))return false;
   ValidateRoute(route,request);
   if(!Owns(route,workspace)||workspace.IsEditing){pending.Remove(route);Release(request);return false;}
   pending.Remove(route);bool accepted=false;
   try{
    route.RequireCurrent();
    if(!String.IsNullOrWhiteSpace(request.Prompt))accepted=workspace.SubmitPopup(request,request.Prompt);
    else{workspace.AcceptSelection(request);accepted=true;}
    request.Delivered=accepted;
    route.RequireCurrent();
    if(accepted)workspace.FocusComposer();else Release(request);
    return accepted;
   }catch{if(!accepted)Release(request);throw;}
  }
  public static void Unregister(FrameRoute route,WorkspaceBody workspace){
   if(route==null)return;WorkspaceBody current;
   if(workspaces.TryGetValue(route,out current)&&Object.ReferenceEquals(current,workspace))workspaces.Remove(route);
  }
  public static void Shutdown(IHncFrameEngine ownerEngine){
   if(ownerEngine==null)return;
   var routes=workspaces.Keys.Concat(pending.Keys).Concat(popups.Keys).Where(r=>r.IsOwnedBy(ownerEngine)).Distinct().ToArray();
   foreach(var route in routes){
    AiRequestPopup popup;if(popups.TryGetValue(route,out popup)){popups.Remove(route);try{popup.Close();}catch{}}
    SelectionRequest request;if(pending.TryGetValue(route,out request)){pending.Remove(route);Release(request);}
    WorkspaceBody workspace;if(workspaces.TryGetValue(route,out workspace)){workspaces.Remove(route);try{workspace.CloseOwnedConnection();}catch{}}
   }
  }
 }
 internal sealed class ModelOption {public readonly string Id,Name;public ModelOption(string id,string name){Id=id;Name=name;}public override string ToString(){return Name;}}
 internal sealed class BridgeConfig {
  public string NodePath {get;set;}public string HostPath {get;set;}public string StoomModulePath {get;set;}
  public static BridgeConfig Load(){string dir=System.IO.Path.GetDirectoryName(typeof(BridgeConfig).Assembly.Location);var config=new JavaScriptSerializer().Deserialize<BridgeConfig>(File.ReadAllText(System.IO.Path.Combine(dir,"hwp-codex.json"),Encoding.UTF8));if(config==null)throw new InvalidOperationException("Codex 연결 설정을 읽지 못했습니다.");Validate(config.NodePath,"NodePath");Validate(config.HostPath,"HostPath");Validate(config.StoomModulePath,"StoomModulePath");return config;}
  static void Validate(string path,string name){if(String.IsNullOrWhiteSpace(path)||!System.IO.Path.IsPathRooted(path)||!File.Exists(path)||path.IndexOfAny(new char[]{'\r','\n','\0'})>=0)throw new InvalidOperationException(name+" 파일 경로가 올바르지 않습니다.");}
 }
 internal sealed class BridgeClient : IDisposable {
  public event Action<Dictionary<string,object>> EditorRequest;
  static readonly object registryLock=new object();static readonly HashSet<BridgeClient> instances=new HashSet<BridgeClient>();
  readonly object gate=new object();readonly Dictionary<string,TaskCompletionSource<Dictionary<string,object>>> pending=new Dictionary<string,TaskCompletionSource<Dictionary<string,object>>>();
  readonly JavaScriptSerializer serializer=new JavaScriptSerializer {MaxJsonLength=1024*1024};
  readonly Process process;readonly StreamWriter input;bool disposed;
  public bool IsAlive {get{lock(gate){try{return !disposed&&!process.HasExited;}catch{return false;}}}}
  public BridgeClient(BridgeConfig config){
   var start=new ProcessStartInfo {FileName=config.NodePath,Arguments=Quote(config.HostPath)+" --stoom-module "+Quote(config.StoomModulePath),UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8,WorkingDirectory=System.IO.Path.GetDirectoryName(config.HostPath)};
   process=new Process {StartInfo=start,EnableRaisingEvents=true};
   process.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs e){Receive(e.Data);};process.ErrorDataReceived+=delegate{};
   process.Exited+=delegate{FailAll(new InvalidOperationException("Codex 연결 프로세스가 종료되었습니다. 연결 확인을 다시 누르세요."));};
   try{
    if(!process.Start())throw new InvalidOperationException("Codex 연결 프로세스를 시작하지 못했습니다.");
    input=new StreamWriter(process.StandardInput.BaseStream,new UTF8Encoding(false)){AutoFlush=true};process.BeginOutputReadLine();process.BeginErrorReadLine();lock(registryLock)instances.Add(this);
   }catch{try{if(!process.HasExited)process.Kill();}catch{}process.Dispose();throw;}
  }
  static string Quote(string value){var output=new StringBuilder("\"");int slashes=0;foreach(char ch in value){if(ch=='\\'){slashes++;continue;}if(ch=='"'){output.Append('\\',slashes*2+1);output.Append('"');slashes=0;continue;}output.Append('\\',slashes);slashes=0;output.Append(ch);}output.Append('\\',slashes*2);return output.Append('"').ToString();}
  public async Task<Dictionary<string,object>> Request(Dictionary<string,object> request,string id){
   var completion=new TaskCompletionSource<Dictionary<string,object>>();id=id??Guid.NewGuid().ToString("N");request["id"]=id;
   lock(gate){if(disposed||process.HasExited)throw new InvalidOperationException("Codex 연결을 다시 확인하세요.");pending.Add(id,completion);try{input.WriteLine(serializer.Serialize(request));}catch(Exception ex){pending.Remove(id);completion.TrySetException(ex);}}
   // Bound a broken child independently of its own protocol timers.
   var timeout=Task.Delay(TimeSpan.FromSeconds(TimeoutSeconds(request.ContainsKey("action")?request["action"] as string:null)));
   if(await Task.WhenAny(completion.Task,timeout)!=completion.Task){Dispose();throw new TimeoutException("Codex 연결 응답 시간이 초과되었습니다. 연결을 다시 확인하세요.");}
   return await completion.Task;
  }
  internal static int TimeoutSeconds(string action){
   if(action=="rich-compose")return 800;
   if(action=="route"||action=="generate"||action=="edit"||action=="compose"||action=="table-design"||action=="page-scope"||action=="page-edit-plan"||action=="document-target-discovery")return 250;
   return 65;
  }
  void Receive(string line){
   if(String.IsNullOrEmpty(line))return;
   if(line.Length>1024*1024){FailAll(new InvalidOperationException("Codex 응답이 너무 큽니다."));Dispose();return;}
   try{
    Dictionary<string,object> response;lock(gate)response=serializer.Deserialize<Dictionary<string,object>>(line);if(response==null)return;
    if(GetString(response,"kind")=="editor.request"){var listener=EditorRequest;if(listener==null)SendEditorResult(GetString(response,"id"),GetString(response,"requestId"),null,"한글 편집기가 연결되지 않았습니다.");else listener(response);return;}
    string id=GetString(response,"id");
    if(String.IsNullOrEmpty(id)){FailAll(new InvalidOperationException(GetString(response,"error")));return;}
    TaskCompletionSource<Dictionary<string,object>> completion;lock(gate){if(!pending.TryGetValue(id,out completion))return;pending.Remove(id);}
    object ok;if(response.TryGetValue("ok",out ok)&&Convert.ToBoolean(ok))completion.TrySetResult(response);else{string message=GetString(response,"error");completion.TrySetException(new InvalidOperationException(String.IsNullOrWhiteSpace(message)?"Codex 요청을 완료하지 못했습니다.":message));}
   }catch{FailAll(new InvalidOperationException("Codex 연결 응답을 읽지 못했습니다."));}
  }
  public static string GetString(Dictionary<string,object> data,string key){object value;return data.TryGetValue(key,out value)&&value!=null?Convert.ToString(value):"";}
  public void SendEditorResult(string id,string requestId,object result,string error){
   var response=new Dictionary<string,object>{{"kind","editor.result"},{"id",id},{"requestId",requestId}};
   if(error!=null)response["error"]=error;else response["result"]=result;
   lock(gate){if(disposed)return;try{input.WriteLine(serializer.Serialize(response));}catch{FailAll(new InvalidOperationException("한글 편집 결과를 Codex에 전달하지 못했습니다."));}}
  }
  public static Dictionary<string,object> Data(Dictionary<string,object> response){object data;if(!response.TryGetValue("data",out data)||!(data is Dictionary<string,object>))throw new InvalidOperationException("Codex 응답 데이터가 없습니다.");return (Dictionary<string,object>)data;}
  void FailAll(Exception error){TaskCompletionSource<Dictionary<string,object>>[] outstanding;lock(gate){outstanding=pending.Values.ToArray();pending.Clear();}foreach(var completion in outstanding)completion.TrySetException(error);}
  public void Dispose(){
   lock(gate){if(disposed)return;disposed=true;try{input.Close();}catch{}}
   FailAll(new OperationCanceledException("Codex 연결을 종료했습니다."));
   Task.Run(delegate{try{if(!process.WaitForExit(2500)&&!process.HasExited)process.Kill();}catch{}finally{process.Dispose();}});
   lock(registryLock)instances.Remove(this);
  }
  public static void DisposeAll(){BridgeClient[] clients;lock(registryLock)clients=instances.ToArray();foreach(var client in clients)client.Dispose();}
 }
}


