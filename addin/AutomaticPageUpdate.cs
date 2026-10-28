using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HAddon {
 internal sealed class AutomaticPageCapture {
  internal NativePageSnapshot Snapshot;
  internal string RawXml;
 }
 internal sealed class AutomaticPageUpdateResult {
  internal int DiscoveredCount,LocatedCount,ChangedCount;
  internal string[] Uncertainties;
  internal bool NeedsClarification;
  internal bool Applied,NoChanges;
 }
 // One automatic request, from read-only capture through verified native
 // changes. Implementations must provide independently established source
 // evidence and a locator that proves its native positions; this workflow
 // cannot turn an unverified page-text match into edit authorization.
 internal interface IAutomaticPageUpdateEnvironment {
  Task<AutomaticPageCapture> CaptureAsync(CancellationToken cancellation,Action<int,int> progress);
  Task<PageScope> ScopeAsync(AutomaticPageCapture capture,string prompt);
  void Verify(AutomaticPageCapture capture);
  void RequireSourceEvidence(AutomaticPageCapture capture,PageScope scope);
  Task<DocumentDiscoveryResult> DiscoverAsync(AutomaticPageCapture capture,PageScope scope,string prompt);
  OnDemandExpectedTarget[] Expected(AutomaticPageCapture capture,DocumentDiscoveryTarget[] targets);
  OnDemandLocationResult Locate(AutomaticPageCapture capture,PageScope scope,OnDemandExpectedTarget[] targets,Func<bool> cancelled);
  Task<DocumentUpdateTarget[]> PlanAsync(AutomaticPageCapture capture,PageScope scope,DocumentAddressCandidate[] targets,string prompt);
  IDocumentUpdateBackend CreateBackend(AutomaticPageCapture capture,DocumentAddressCandidate[] targets);
 }
 internal sealed class AutomaticPageUpdate {
  internal DocumentUpdateTransaction Transaction{get;private set;}
  static NativePageSnapshot Snapshot(NativePageSnapshot s){return s==null?null:new NativePageSnapshot{DocumentId=s.DocumentId,Path=s.Path,Digest=s.Digest,Pages=s.Pages==null?null:(string[])s.Pages.Clone(),TablePages=s.TablePages==null?null:(string[])s.TablePages.Clone(),Paragraphs=s.Paragraphs==null?null:s.Paragraphs.Select(p=>p==null?null:new DocumentParagraph{Path=p.Path,Text=p.Text,CellPath=p.CellPath,HasObjects=p.HasObjects}).ToArray()};}
  static AutomaticPageCapture Capture(AutomaticPageCapture c){return c==null?null:new AutomaticPageCapture{RawXml=c.RawXml,Snapshot=Snapshot(c.Snapshot)};}
  static PageScope Scope(PageScope s){return s==null?null:new PageScope{Targets=s.Targets==null?null:(int[])s.Targets.Clone(),Sources=s.Sources==null?null:(int[])s.Sources.Clone()};}
  static DocumentDiscoveryTarget[] Discovery(DocumentDiscoveryTarget[] values){return values.Select(p=>p==null?null:new DocumentDiscoveryTarget{Id=p.Id,XmlPath=p.XmlPath,Text=p.Text,CellPath=p.CellPath,HasObjects=p.HasObjects}).ToArray();}
  static OnDemandExpectedTarget[] Targets(OnDemandExpectedTarget[] values){return values.Select(p=>p==null?null:new OnDemandExpectedTarget{XmlPath=p.XmlPath,Text=p.Text,ContainerToken=p.ContainerToken,StyleToken=p.StyleToken,StructureToken=p.StructureToken}).ToArray();}
  static DocumentAddressCandidate[] Addresses(DocumentAddressCandidate[] values){return values.Select(p=>p==null?null:new DocumentAddressCandidate{XmlPath=p.XmlPath,Text=p.Text,Page=p.Page,List=p.List,Paragraph=p.Paragraph,Start=p.Start,End=p.End}).ToArray();}
  static string[] Uncertainty(string[] values){if(values==null||values.Length>256||values.Any(x=>x==null||x.Length>1000))throw new InvalidOperationException("문서 분석의 불확실성 정보가 올바르지 않습니다.");return (string[])values.Clone();}
  static void Range(int[] pages,int count){if(pages==null||pages.Length==0||pages.Length>count||pages.Any(p=>p<1||p>count)||pages.Distinct().Count()!=pages.Length)throw new InvalidOperationException("문서 쪽 범위가 올바르지 않습니다.");}
  static void Check(IAutomaticPageUpdateEnvironment e,AutomaticPageCapture c,CancellationToken token){token.ThrowIfCancellationRequested();e.Verify(Capture(c));token.ThrowIfCancellationRequested();}
  static void Notify(Action<string,int,int> progress,string stage,int done,int total,CancellationToken token){if(progress!=null)progress(stage,done,total);token.ThrowIfCancellationRequested();}
  internal async Task<AutomaticPageUpdateResult> RunAsync(IAutomaticPageUpdateEnvironment environment,string prompt,CancellationToken cancellation,Action<string,int,int> progress){
   if(environment==null)throw new ArgumentNullException("environment");if(String.IsNullOrWhiteSpace(prompt)||prompt.Length>16000)throw new InvalidOperationException("쪽 편집 요청이 올바르지 않습니다.");Transaction=null;
   cancellation.ThrowIfCancellationRequested();Notify(progress,"read",0,0,cancellation);
   var captured=Capture(await environment.CaptureAsync(cancellation,delegate(int done,int total){Notify(progress,"read",done,total,cancellation);}));
   if(captured==null||captured.Snapshot==null||captured.Snapshot.Pages==null||captured.Snapshot.Paragraphs==null||captured.Snapshot.Pages.Length<1||captured.Snapshot.Pages.Length>256||String.IsNullOrEmpty(captured.RawXml)||String.IsNullOrEmpty(captured.Snapshot.Digest))throw new InvalidOperationException("고정된 문서 내용을 확인하지 못했습니다.");
   Check(environment,captured,cancellation);Notify(progress,"scope",0,0,cancellation);
   var scoped=Scope(await environment.ScopeAsync(Capture(captured),prompt));Check(environment,captured,cancellation);
   if(scoped==null)throw new InvalidOperationException("수정할 쪽과 참조할 쪽이 없습니다.");Range(scoped.Targets,captured.Snapshot.Pages.Length);Range(scoped.Sources,captured.Snapshot.Pages.Length);
   environment.RequireSourceEvidence(Capture(captured),Scope(scoped));Check(environment,captured,cancellation);
   Notify(progress,"discover",0,0,cancellation);var discovered=await environment.DiscoverAsync(Capture(captured),Scope(scoped),prompt);Check(environment,captured,cancellation);
   if(discovered==null||discovered.Targets==null||discovered.Targets.Length>256)throw new InvalidOperationException("수정 대상 분석 결과가 올바르지 않습니다.");
   var uncertain=Uncertainty(discovered.Uncertainties);var selected=Discovery(discovered.Targets);var result=new AutomaticPageUpdateResult{DiscoveredCount=selected.Length,Uncertainties=uncertain};
   if(uncertain.Length>0){result.NeedsClarification=true;return result;}
   if(selected.Length==0){result.NoChanges=true;return result;}
   var byPath=captured.Snapshot.Paragraphs.Where(p=>p!=null).ToDictionary(p=>p.Path,StringComparer.Ordinal);var seen=new HashSet<string>(StringComparer.Ordinal);
   foreach(var p in selected){DocumentParagraph original;if(p==null||String.IsNullOrEmpty(p.XmlPath)||!seen.Add(p.XmlPath)||!byPath.TryGetValue(p.XmlPath,out original)||p.Text!=original.Text||p.HasObjects!=original.HasObjects||p.CellPath!=original.CellPath)throw new InvalidOperationException("분석된 문단이 고정한 원문과 다릅니다.");}
   var expected=environment.Expected(Capture(captured),Discovery(selected));if(expected==null||expected.Length!=selected.Length)throw new InvalidOperationException("문단의 원본 구조와 서식을 확인하지 못했습니다.");expected=Targets(expected);
   var expectedPaths=new HashSet<string>(StringComparer.Ordinal);foreach(var e in expected){if(e==null||!seen.Contains(e.XmlPath)||!expectedPaths.Add(e.XmlPath)||e.Text!=byPath[e.XmlPath].Text)throw new InvalidOperationException("문단 구조의 대상 범위가 달라졌습니다.");}
   Check(environment,captured,cancellation);Notify(progress,"locate",0,selected.Length,cancellation);
   var locations=environment.Locate(Capture(captured),Scope(scoped),Targets(expected),delegate{return cancellation.IsCancellationRequested;});Check(environment,captured,cancellation);
   if(locations==null||locations.Targets==null||locations.Unresolved==null)throw new InvalidOperationException("한글 문단 위치를 확인하지 못했습니다.");
   if(locations.Unresolved.Length>0){result.LocatedCount=locations.Targets.Length;result.Uncertainties=locations.Unresolved.Select(x=>x==null?"위치 미확인":x.Reason??"위치 미확인").ToArray();return result;}
   var targets=Addresses(locations.Targets);result.LocatedCount=targets.Length;
   var nativeSeen=new HashSet<string>(StringComparer.Ordinal);var locationPaths=new HashSet<string>(StringComparer.Ordinal);
   foreach(var t in targets){if(t==null||!seen.Contains(t.XmlPath)||!locationPaths.Add(t.XmlPath)||t.Text!=byPath[t.XmlPath].Text||!scoped.Targets.Contains(t.Page)||t.List<0||t.Paragraph<0||t.Start<0||t.End<=t.Start||t.End-t.Start!=t.Text.Length||!nativeSeen.Add(t.List+":"+t.Paragraph))throw new InvalidOperationException("고정된 한글 위치가 수정 범위와 다릅니다.");}
   if(targets.Length!=selected.Length)throw new InvalidOperationException("일부 수정 대상의 한글 위치가 없습니다.");
   // Source evidence describes the frozen private snapshot. Copies supplied
   // to providers cannot change it; revision checks protect the live document.
   Notify(progress,"plan",0,targets.Length,cancellation);
   var plan=await environment.PlanAsync(Capture(captured),Scope(scoped),Addresses(targets),prompt);Check(environment,captured,cancellation);
   if(plan==null||plan.Length>256)throw new InvalidOperationException("본문 수정 계획이 올바르지 않습니다.");
   var targetByPath=targets.ToDictionary(t=>t.XmlPath,StringComparer.Ordinal);seen.Clear();
   var frozenPlan=plan.Select(p=>p==null?null:new DocumentUpdateTarget{Id=p.Id,Original=p.Original,Replacement=p.Replacement}).ToArray();
   foreach(var p in frozenPlan){DocumentAddressCandidate t;if(p==null||String.IsNullOrEmpty(p.Id)||!seen.Add(p.Id)||!targetByPath.TryGetValue(p.Id,out t)||p.Original!=t.Text)throw new InvalidOperationException("본문 계획이 확인한 대상 범위를 벗어납니다.");}
   if(frozenPlan.Length==0){result.NoChanges=true;return result;}
   var needed=targets.Where(t=>seen.Contains(t.XmlPath)).ToArray();var backend=environment.CreateBackend(Capture(captured),Addresses(needed));if(backend==null)throw new InvalidOperationException("한글 편집 연결이 없습니다.");
   Check(environment,captured,cancellation);Notify(progress,"apply",0,frozenPlan.Length,cancellation);Transaction=new DocumentUpdateTransaction();await Transaction.ApplyAsync(backend,captured.Snapshot.Digest,frozenPlan,delegate{return cancellation.IsCancellationRequested;},delegate(int done){if(progress!=null)progress("apply",done,frozenPlan.Length);});
   result.ChangedCount=Transaction.ChangedCount;result.Applied=result.ChangedCount>0;result.NoChanges=result.ChangedCount==0;return result;
  }
 }
}
