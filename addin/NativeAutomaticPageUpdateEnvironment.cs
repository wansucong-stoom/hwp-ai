using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hwp.Addon;

namespace HAddon {
 // Wires the actual read/planning/native-write components together. Runtime
 // validation of physical page behavior is explicit. CreateTextOnly uses
 // full structural text and the actual native RepeatFind probe; custom
 // providers can supply stronger host-validated evidence.
 // This class is not a UI entry point and must be created on HWP's UI thread.
 internal sealed class NativeAutomaticPageUpdateEnvironment:IAutomaticPageUpdateEnvironment {
  readonly BridgeClient bridge;readonly Func<IHwpObject> getDocument;
  readonly string requestId,accountId,model,effort;readonly object history;
  readonly int uiThread;
  readonly Func<AutomaticPageCapture,DocumentDiscoveryTarget[],OnDemandExpectedTarget[]> buildExpected;
  readonly Func<AutomaticPageCapture,IOnDemandNativeProbe> createProbe;
  readonly Action<AutomaticPageCapture,PageScope> sourceEvidence;
  // Concrete text-only integration. These flags must come from actual host
  // validation, never model output or document text. Relative paragraph
  // numbering is disabled by default until its native behavior is verified.
  internal static NativeAutomaticPageUpdateEnvironment CreateTextOnly(BridgeClient bridge,Func<IHwpObject> getDocument,string requestId,string accountId,string model,string effort,object history,bool physicalPageSemanticsVerified,bool relativeParagraphOrderVerified=false){
   if(!physicalPageSemanticsVerified)throw new InvalidOperationException("한글의 실제 쪽 번호 동작을 확인하지 못했습니다.");
   HwpParagraphProofs cachedProofs=null;string cachedRaw=null;
   Func<AutomaticPageCapture,HwpParagraphProofs> proofs=delegate(AutomaticPageCapture c){if(cachedProofs==null||!Object.ReferenceEquals(cachedRaw,c.RawXml)){cachedProofs=new HwpParagraphProofs(c.RawXml);cachedRaw=c.RawXml;}return cachedProofs;};
   return new NativeAutomaticPageUpdateEnvironment(bridge,getDocument,requestId,accountId,model,effort,history,
    delegate(AutomaticPageCapture c,DocumentDiscoveryTarget[] targets){var verified=proofs(c);return targets.Select(t=>verified.CreateExpected(t.XmlPath)).ToArray();},
    delegate(AutomaticPageCapture c){return NativeRepeatFindProbe.CreateCoordinatedRead(getDocument,c.Snapshot,proofs(c),physicalPageSemanticsVerified,relativeParagraphOrderVerified);},
    TextOnlyAutomaticPageEvidence.Validate);
  }
  internal NativeAutomaticPageUpdateEnvironment(BridgeClient bridge,Func<IHwpObject> getDocument,string requestId,string accountId,string model,string effort,object history,
   Func<AutomaticPageCapture,DocumentDiscoveryTarget[],OnDemandExpectedTarget[]> buildExpected,
   Func<AutomaticPageCapture,IOnDemandNativeProbe> createProbe,Action<AutomaticPageCapture,PageScope> sourceEvidence){
   if(bridge==null||getDocument==null||buildExpected==null||createProbe==null||sourceEvidence==null)throw new ArgumentNullException("검증된 한글 편집 연결");
   if(SynchronizationContext.Current==null)throw new InvalidOperationException("한글 UI 실행 컨텍스트를 확인하지 못했습니다.");
   this.bridge=bridge;this.getDocument=getDocument;this.requestId=requestId;this.accountId=accountId;this.model=model;this.effort=effort;this.history=history;this.buildExpected=buildExpected;this.createProbe=createProbe;this.sourceEvidence=sourceEvidence;uiThread=Thread.CurrentThread.ManagedThreadId;
  }
  void Ui(){if(SynchronizationContext.Current==null||Thread.CurrentThread.ManagedThreadId!=uiThread)throw new InvalidOperationException("한글 UI 실행 스레드가 바뀌었습니다.");}
  public async Task<AutomaticPageCapture> CaptureAsync(CancellationToken cancellation,Action<int,int> progress){
   Ui();var doc=getDocument();if(doc==null||doc.IsReadOnlyMode())throw new InvalidOperationException("편집 가능한 한글 문서가 없습니다.");
   int id=doc.GetID();string path=doc.GetPath()??"";
   var read=await ResponsivePageReader.ReadAsync(new NativeResponsivePageSource(getDocument),cancellation,progress);Ui();
   doc=getDocument();if(doc==null||doc.GetID()!=id||(doc.GetPath()??"")!=path)throw new InvalidOperationException("원래 한글 문서가 바뀌었습니다.");
   return new AutomaticPageCapture{RawXml=read.RawXml,Snapshot=new NativePageSnapshot{DocumentId=id,Path=path,Digest=read.Digest,Pages=read.Pages,TablePages=read.TablePages,Paragraphs=read.Paragraphs}};
  }
  public void Verify(AutomaticPageCapture capture){
   Ui();if(capture==null||capture.Snapshot==null||capture.Snapshot.Pages==null)throw new InvalidOperationException("고정 문서 정보가 없습니다.");var s=capture.Snapshot;var doc=getDocument();
   if(doc==null||doc.IsReadOnlyMode()||doc.GetID()!=s.DocumentId||(doc.GetPath()??"")!=s.Path||doc.GetPageCount()!=s.Pages.Length||TableStableSnapshot.Digest(doc.GetTextFile("HWPML2X",""))!=s.Digest)throw new InvalidOperationException("문서 분석 중 원래 내용이나 서식이 바뀌었습니다.");
  }
  // AutomaticPageUpdate owns full revision checks at every await/native-read
  // boundary. Standalone service entry points retain their own checks.
  public async Task<PageScope> ScopeAsync(AutomaticPageCapture c,string prompt){Ui();var result=await PageScopeService.AnalyzeCoordinated(bridge,c.Snapshot,getDocument,requestId,accountId,model,effort,prompt,history);Ui();return result;}
  public void RequireSourceEvidence(AutomaticPageCapture c,PageScope scope){Ui();sourceEvidence(c,scope);Ui();}
  public async Task<DocumentDiscoveryResult> DiscoverAsync(AutomaticPageCapture c,PageScope scope,string prompt){Ui();var result=await DocumentTargetDiscoveryService.AnalyzeCoordinated(bridge,c.Snapshot,scope,getDocument,requestId,accountId,model,effort,prompt,history);Ui();return result;}
  public OnDemandExpectedTarget[] Expected(AutomaticPageCapture c,DocumentDiscoveryTarget[] targets){Ui();var result=buildExpected(c,targets);Ui();return result;}
  public OnDemandLocationResult Locate(AutomaticPageCapture c,PageScope scope,OnDemandExpectedTarget[] targets,Func<bool> cancelled){
   Ui();var probe=createProbe(c);if(probe==null)throw new InvalidOperationException("한글 위치 검증 연결이 없습니다.");var result=OnDemandTargetLocator.Resolve(probe,c.Snapshot.Paragraphs,targets,scope.Targets,cancelled);Ui();return result;
  }
  public async Task<DocumentUpdateTarget[]> PlanAsync(AutomaticPageCapture c,PageScope scope,DocumentAddressCandidate[] targets,string prompt){
   Ui();var planning=new PageEditPlanning(c.Snapshot.Pages,targets,scope.Targets,scope.Sources,c.Snapshot.Paragraphs);var result=await PageEditService.AnalyzeCoordinated(bridge,c.Snapshot,planning,getDocument,requestId,accountId,model,effort,prompt,history);Ui();return result;
  }
  public IDocumentUpdateBackend CreateBackend(AutomaticPageCapture c,DocumentAddressCandidate[] targets){Ui();return new NativeDocumentUpdateBackend(getDocument,c.Snapshot,c.RawXml,targets);}
 }
}
