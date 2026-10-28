using System;
using System.Collections.Generic;
using System.Linq;

namespace HAddon {
 // Text-only analysis contract. The entire structural text must accompany
 // the planning request, so GetPageText is used for page attribution rather
 // than being treated as an exhaustive representation of every object.
 // This does not prove rendered page boundaries, interpret pictures, or
 // authorize a native position. Native locator proofs are separate.
 internal static class TextOnlyAutomaticPageEvidence {
  internal static void Validate(AutomaticPageCapture capture,PageScope scope){
   if(capture==null||capture.Snapshot==null||scope==null||scope.Targets==null||scope.Sources==null||String.IsNullOrWhiteSpace(capture.RawXml))throw new InvalidOperationException("쪽 참조의 원본 내용이 없습니다.");
   var s=capture.Snapshot;if(s.Pages==null||s.Paragraphs==null||s.Pages.Length<1||s.Pages.Length>256||String.IsNullOrEmpty(s.Digest)||TableStableSnapshot.Digest(capture.RawXml)!=s.Digest)throw new InvalidOperationException("쪽 참조의 원본 상태가 일치하지 않습니다.");
   var all=new HashSet<int>(scope.Targets.Concat(scope.Sources));
   if(scope.Targets.Length==0||scope.Sources.Length==0||scope.Targets.Distinct().Count()!=scope.Targets.Length||scope.Sources.Distinct().Count()!=scope.Sources.Length||all.Any(p=>p<1||p>s.Pages.Length)||all.Count!=s.Pages.Length)throw new InvalidOperationException("전체 문서 구조를 참고하려면 요청 범위가 문서 전체를 포함해야 합니다.");
   long pageChars=0;foreach(string page in s.Pages){if(page==null||page.Length>30000)throw new InvalidOperationException("쪽 텍스트가 누락되거나 너무 큽니다.");pageChars+=page.Length;}
   if(pageChars>400000||!scope.Sources.Any(p=>!String.IsNullOrWhiteSpace(s.Pages[p-1])))throw new InvalidOperationException("참조 쪽 텍스트를 읽지 못했거나 분석 한도를 넘습니다.");
   var original=DocumentStructure.Read(capture.RawXml);if(original.Length!=s.Paragraphs.Length)throw new InvalidOperationException("문서의 일부 문단만 분석 대상으로 전달됐습니다.");
   long paragraphChars=0;for(int i=0;i<original.Length;i++){
    var a=original[i];var b=s.Paragraphs[i];if(b==null||a.Path!=b.Path||a.Text!=b.Text||a.CellPath!=b.CellPath||a.HasObjects!=b.HasObjects)throw new InvalidOperationException("문서 구조 텍스트가 원본과 일치하지 않습니다.");paragraphChars+=a.Text.Length;
   }
   if(paragraphChars>400000)throw new InvalidOperationException("구조 텍스트가 분석 한도를 넘습니다.");
   // Whole-document text coverage is only a diagnostic. The authoritative
   // text context is the full structured list verified above, not a claimed
   // complete extraction of pictures, fields or controls from rendered pages.
  }
 }
}
