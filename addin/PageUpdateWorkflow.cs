using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HAddon {
 // The production environment must prove physical page association for each
 // target and completeness of the requested source pages. No such native
 // proof provider currently exists. This workflow is intentionally not wired
 // into the UI or advertised as an available edit capability.
 internal interface IPageUpdateEnvironment {
  NativePageSnapshot Capture();
  DocumentAddressCandidate[] Collect(NativePageSnapshot snapshot);
  void RequireEvidence(NativePageSnapshot snapshot,DocumentAddressCandidate[] targets,int[] targetPages,int[] sourcePages);
  void VerifyRevision(NativePageSnapshot snapshot);
  Task<Dictionary<string,object>> Analyze(PageEditPlanning scoped,string prompt);
  IDocumentUpdateBackend CreateBackend(NativePageSnapshot snapshot,DocumentAddressCandidate[] targets);
 }
 internal sealed class PageUpdateWorkflow {
  internal DocumentUpdateTransaction Transaction{get;private set;}
  static NativePageSnapshot CopySnapshot(NativePageSnapshot value){
   if(value==null)return null;
   return new NativePageSnapshot{DocumentId=value.DocumentId,Path=value.Path,Digest=value.Digest,Pages=value.Pages==null?null:(string[])value.Pages.Clone(),
    TablePages=value.TablePages==null?null:(string[])value.TablePages.Clone(),Paragraphs=value.Paragraphs==null?null:value.Paragraphs.Select(p=>p==null?null:new DocumentParagraph{Path=p.Path,Text=p.Text,CellPath=p.CellPath,HasObjects=p.HasObjects}).ToArray()};
  }
  static DocumentAddressCandidate[] CopyCandidates(DocumentAddressCandidate[] values){return values.Select(p=>p==null?null:new DocumentAddressCandidate{XmlPath=p.XmlPath,Text=p.Text,Page=p.Page,List=p.List,Paragraph=p.Paragraph,Start=p.Start,End=p.End}).ToArray();}
  internal async Task<int> RunAsync(IPageUpdateEnvironment environment,int[] targetPages,int[] sourcePages,string prompt,Func<bool> cancelled,Action<int> progress){
   Transaction=null;
   if(environment==null||cancelled==null||targetPages==null||sourcePages==null)throw new ArgumentNullException("environment");
   if(String.IsNullOrWhiteSpace(prompt)||prompt.Length>16000)throw new InvalidOperationException("쪽 편집 요청이 올바르지 않습니다.");
   if(cancelled())throw new OperationCanceledException("쪽 편집이 취소되었습니다.");
   // Copy mutable caller-owned ranges before the first asynchronous boundary.
   var target=(int[])targetPages.Clone();var source=(int[])sourcePages.Clone();
   var raw=environment.Capture();
   if(raw==null||String.IsNullOrEmpty(raw.Digest)||raw.Pages==null)throw new InvalidOperationException("문서 스냅샷이 없습니다.");
   var captured=CopySnapshot(raw);
   var collected=environment.Collect(CopySnapshot(captured));if(collected==null)throw new InvalidOperationException("편집 위치를 읽지 못했습니다.");
   var candidates=CopyCandidates(collected);
   var scope=new PageEditPlanning(captured.Pages,candidates,target,source);
   var inScope=candidates.Where(c=>c!=null&&target.Contains(c.Page)).ToArray();
   environment.RequireEvidence(CopySnapshot(captured),CopyCandidates(inScope),(int[])target.Clone(),(int[])source.Clone());
   environment.VerifyRevision(CopySnapshot(captured));
   if(cancelled())throw new OperationCanceledException("쪽 편집이 취소되었습니다.");
   // Analyzer returns data only. Compile is the sole authority for turning it
   // into allowed operations and rejects uncertain/out-of-scope plans.
   var plan=scope.Compile(await environment.Analyze(scope,prompt));
   if(cancelled())throw new OperationCanceledException("쪽 편집이 취소되었습니다.");
   environment.VerifyRevision(CopySnapshot(captured));
   environment.RequireEvidence(CopySnapshot(captured),CopyCandidates(inScope),(int[])target.Clone(),(int[])source.Clone());
   if(plan.Length==0)return 0;
   // Pass only selected plan targets to the native adapter, not every known
   // candidate. An AI response cannot expand the frozen target scope.
   var ids=new HashSet<string>(plan.Select(p=>p.Id),StringComparer.Ordinal);
   var selected=inScope.Where(p=>ids.Contains(p.XmlPath)).ToArray();
   if(selected.Length!=plan.Length)throw new InvalidOperationException("쪽 편집 대상이 계획과 일치하지 않습니다.");
   var backend=environment.CreateBackend(CopySnapshot(captured),CopyCandidates(selected));if(backend==null)throw new InvalidOperationException("한글 편집 연결이 없습니다.");
   Transaction=new DocumentUpdateTransaction();await Transaction.ApplyAsync(backend,captured.Digest,plan,cancelled,progress);
   return Transaction.ChangedCount;
  }
 }
}
