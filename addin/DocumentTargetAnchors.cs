using System;
using System.Collections.Generic;
using System.Linq;

namespace HAddon {
 // Read-only suggestions for locating duplicate target paragraphs after the
 // AI has chosen the intended XML path. A suggestion is NOT proof that an
 // XML p index equals a native paragraph index and never authorizes a write.
 internal sealed class DocumentTargetAnchor {
  internal string TargetPath,AnchorPath,AnchorText;internal int ParagraphDelta;
 }
 internal static class DocumentTargetAnchors {
  static bool Position(string path,out string parent,out int index){
   parent=null;index=-1;if(String.IsNullOrEmpty(path))return false;
   int slash=path.LastIndexOf('/');if(slash<1)return false;
   string last=path.Substring(slash+1);if(!last.StartsWith("P[",StringComparison.Ordinal)||!last.EndsWith("]",StringComparison.Ordinal)||!Int32.TryParse(last.Substring(2,last.Length-3),out index)||index<0)return false;
   parent=path.Substring(0,slash);return true;
  }
  internal static DocumentTargetAnchor[] Find(DocumentParagraph[] paragraphs,string[] requested){
   if(paragraphs==null||requested==null||paragraphs.Length>20000||requested.Length>256)throw new InvalidOperationException("문서 위치 분석 범위가 올바르지 않습니다.");
   var byPath=new Dictionary<string,DocumentParagraph>(StringComparer.Ordinal);var byParent=new Dictionary<string,List<DocumentParagraph>>(StringComparer.Ordinal);
   foreach(var p in paragraphs){
    if(p==null||String.IsNullOrEmpty(p.Path)||p.Text==null||byPath.ContainsKey(p.Path))throw new InvalidOperationException("문서 문단 주소가 중복되거나 올바르지 않습니다.");
    byPath.Add(p.Path,p);string parent;int index;if(Position(p.Path,out parent,out index)){List<DocumentParagraph> list;if(!byParent.TryGetValue(parent,out list)){list=new List<DocumentParagraph>();byParent.Add(parent,list);}list.Add(p);}
   }
   long total=paragraphs.Sum(p=>(long)p.Text.Length+1);if(total>4000000)throw new InvalidOperationException("문단 위치 분석 텍스트가 너무 깁니다.");
   // Paragraph separators forbid matches that cross native paragraph bounds.
   // One corpus makes repeated uniqueness checks linear in total text rather
   // than traversing all paragraph objects for every candidate anchor.
   string corpus=String.Join("\0",paragraphs.Select(p=>p.Text).ToArray());
   var uniqueness=new Dictionary<string,bool>(StringComparer.Ordinal);
   Func<string,bool> unique=delegate(string text){
    bool result;if(uniqueness.TryGetValue(text,out result))return result;
    int first=corpus.IndexOf(text,StringComparison.Ordinal);result=first>=0&&corpus.IndexOf(text,first+1,StringComparison.Ordinal)<0;uniqueness[text]=result;return result;
   };
   var ids=new HashSet<string>(StringComparer.Ordinal);var output=new List<DocumentTargetAnchor>();
   foreach(string id in requested){
    DocumentParagraph target;string parent;int position;
    if(String.IsNullOrEmpty(id)||!ids.Add(id)||!byPath.TryGetValue(id,out target)||!Position(id,out parent,out position))throw new InvalidOperationException("찾을 대상 문단이 올바르지 않습니다.");
    DocumentParagraph best=null;int bestIndex=0,bestDistance=Int32.MaxValue;
    foreach(var candidate in byParent[parent]){
     string ignored;int index;if(candidate.HasObjects||String.IsNullOrWhiteSpace(candidate.Text)||candidate.Text.Length>12000||candidate.Text.Any(c=>Char.IsControl(c))||!Position(candidate.Path,out ignored,out index)||!unique(candidate.Text))continue;
     int distance=Math.Abs(index-position);if(distance<bestDistance){best=candidate;bestIndex=index;bestDistance=distance;}
    }
    if(best!=null)output.Add(new DocumentTargetAnchor{TargetPath=id,AnchorPath=best.Path,AnchorText=best.Text,ParagraphDelta=position-bestIndex});
   }
   return output.ToArray();
  }
 }
}
