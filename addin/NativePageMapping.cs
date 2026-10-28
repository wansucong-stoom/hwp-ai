using System;
using System.Linq;
using Hwp.Addon;

namespace HAddon {
 // This pipeline produces provisional mappings, never authority to edit.
 // The source snapshot must be pinned by the caller. These operations are
 // synchronous and are not suitable for a large active document's UI thread.
 // Raw KeyIndicator numbering, text extraction completeness, and concurrent
 // user edits require independent native-HWP evidence before any write.
 internal interface IPageMappingSource {
  void Verify(NativePageSnapshot snapshot);
  NativeScanItem[] Scan();
  InspectedNativeParagraph[] Inspect(NativePageSnapshot snapshot,NativeScanItem[] scan);
  PagePositionEvidence[] PagePositions(NativePageSnapshot snapshot,DocumentAddressCandidate[] candidates);
 }
 internal sealed class PageMappingResult {
  internal DocumentAddressCandidate[] Candidates;
  internal int ScannedEvents,InspectedParagraphs,StructuralMatches,PositionMatches;
  internal int SnapshotParagraphs,TextParagraphs,ObjectParagraphs,UnmappedTextParagraphs;
 }
 internal static class NativePageMapping {
  internal static PageMappingResult Collect(IPageMappingSource source,NativePageSnapshot snapshot,Func<bool> cancelled){
   if(source==null||snapshot==null||cancelled==null)throw new ArgumentNullException("snapshot");
   if(snapshot.Pages==null||snapshot.Pages.Length<1||snapshot.Pages.Length>256||snapshot.Pages.Any(p=>p==null)||snapshot.Paragraphs==null||String.IsNullOrEmpty(snapshot.Digest))throw new InvalidOperationException("원래 쪽 스냅샷이 올바르지 않습니다.");
   // Freeze all caller-owned data before making calls that can re-enter HWP.
   var frozen=new NativePageSnapshot{DocumentId=snapshot.DocumentId,Path=snapshot.Path,Digest=snapshot.Digest,Pages=(string[])snapshot.Pages.Clone(),
    TablePages=snapshot.TablePages==null?null:(string[])snapshot.TablePages.Clone(),Paragraphs=snapshot.Paragraphs.Select(p=>p==null?null:new DocumentParagraph{Path=p.Path,Text=p.Text,CellPath=p.CellPath,HasObjects=p.HasObjects}).ToArray()};
   Check(source,frozen,cancelled);var scan=source.Scan();if(scan==null)throw new InvalidOperationException("문서 탐색 결과가 없습니다.");
   Check(source,frozen,cancelled);var inspected=source.Inspect(frozen,scan);if(inspected==null)throw new InvalidOperationException("문단 확인 결과가 없습니다.");
   Check(source,frozen,cancelled);var matched=DocumentAddressMap.FindStructural(frozen.Paragraphs,inspected);
   if(matched.Length==0)return Result(frozen,scan,inspected,matched,new PagePositionEvidence[0],new DocumentAddressCandidate[0]);
   var positions=source.PagePositions(frozen,matched);if(positions==null)throw new InvalidOperationException("쪽 위치 확인 결과가 없습니다.");
   Check(source,frozen,cancelled);var bound=DocumentAddressMap.BindPages(matched,positions,frozen.Pages);Check(source,frozen,cancelled);
   return Result(frozen,scan,inspected,matched,positions,bound);
  }
  static PageMappingResult Result(NativePageSnapshot snapshot,NativeScanItem[] scan,InspectedNativeParagraph[] inspected,DocumentAddressCandidate[] matched,PagePositionEvidence[] positions,DocumentAddressCandidate[] bound){
   int text=snapshot.Paragraphs.Count(p=>p!=null&&!p.HasObjects&&!String.IsNullOrWhiteSpace(p.Text));
   return new PageMappingResult{Candidates=bound,ScannedEvents=scan.Length,InspectedParagraphs=inspected.Length,StructuralMatches=matched.Length,PositionMatches=positions.Length,
    SnapshotParagraphs=snapshot.Paragraphs.Length,TextParagraphs=text,ObjectParagraphs=snapshot.Paragraphs.Count(p=>p!=null&&p.HasObjects),UnmappedTextParagraphs=text-bound.Length};
  }
  static void Check(IPageMappingSource source,NativePageSnapshot snapshot,Func<bool> cancelled){if(cancelled())throw new OperationCanceledException("쪽 위치 확인을 중단했습니다.");source.Verify(snapshot);if(cancelled())throw new OperationCanceledException("쪽 위치 확인을 중단했습니다.");}
 }
 // Experimental native implementation. All methods must run on the host UI
 // thread. A long scan cannot be resumed safely while the user edits; do not
 // wire this adapter to the live product until that issue has been resolved.
 internal sealed class NativePageMappingSource:IPageMappingSource {
  readonly Func<IHwpObject> getDocument;
  internal NativePageMappingSource(Func<IHwpObject> getDocument){if(getDocument==null)throw new ArgumentNullException("getDocument");this.getDocument=getDocument;}
  public void Verify(NativePageSnapshot s){
   var doc=getDocument();if(doc==null||doc.GetID()!=s.DocumentId||(doc.GetPath()??"")!=(s.Path??"")||doc.GetPageCount()!=s.Pages.Length||TableStableSnapshot.Digest(doc.GetTextFile("HWPML2X",""))!=s.Digest)throw new InvalidOperationException("쪽 위치를 확인하는 동안 문서가 바뀌었습니다.");
  }
  public NativeScanItem[] Scan(){return NativeDocumentScan.Read(getDocument);}
  public InspectedNativeParagraph[] Inspect(NativePageSnapshot s,NativeScanItem[] items){return NativeDocumentInspection.Inspect(getDocument,s,items);}
  public PagePositionEvidence[] PagePositions(NativePageSnapshot s,DocumentAddressCandidate[] items){return NativePagePositionEvidence.Collect(new NativePagePositionBackend(getDocument,s),items);}
 }
}
