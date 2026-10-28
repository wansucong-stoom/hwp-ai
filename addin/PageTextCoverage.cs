using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HAddon {
 // Diagnostics only: text coverage is not provable from GetPageText alone.
 // Whole-document checks cannot assign text to a specific source page, and
 // repeated headers or substring matches can conceal missing body content.
 // Neither a zero count nor this object grants permission for a native edit.
 internal sealed class PageTextCoverageReport {
  internal int ParagraphsConsidered,ParagraphsNotFound,RepeatedTextsShortOfOccurrences;
  internal int ObjectBearingParagraphs,EmptyPages,WhitespaceOnlyParagraphs;
  internal bool AnalysisLimited;
  internal string[] PotentiallyMissingPaths;
 }
 internal static class PageTextCoverage {
  const int MaxPageChars=400000,MaxDistinct=4096,MaxTextChars=12000;
  static string Normalize(string value){
   var b=new StringBuilder(value.Length);foreach(char c in value)if(!Char.IsWhiteSpace(c))b.Append(c);return b.ToString();
  }
  internal static PageTextCoverageReport Assess(DocumentParagraph[] paragraphs,string[] pages){
   if(paragraphs==null||pages==null||pages.Length==0||pages.Length>256||pages.Any(p=>p==null))throw new ArgumentException("쪽 내용과 문서 문단이 필요합니다.");
   long total=pages.Sum(p=>(long)p.Length);if(total>MaxPageChars)throw new InvalidOperationException("쪽 진단 크기가 너무 큽니다.");
   var report=new PageTextCoverageReport();report.EmptyPages=pages.Count(p=>String.IsNullOrWhiteSpace(p));
   var corpus=Normalize(String.Concat(pages));var groups=new Dictionary<string,List<string>>(StringComparer.Ordinal);
   foreach(var p in paragraphs){
    if(p==null||p.Text==null||p.Path==null){report.AnalysisLimited=true;continue;}
    if(p.HasObjects)report.ObjectBearingParagraphs++;
    string text=Normalize(p.Text);
    if(text.Length==0){if(p.Text.Length>0)report.WhitespaceOnlyParagraphs++;continue;}
    if(text.Length>MaxTextChars){report.AnalysisLimited=true;continue;}
    List<string> paths;if(!groups.TryGetValue(text,out paths)){
     if(groups.Count>=MaxDistinct){report.AnalysisLimited=true;continue;}
     paths=new List<string>();groups.Add(text,paths);
    }
    paths.Add(p.Path);report.ParagraphsConsidered++;
   }
   var missing=new List<string>();
   foreach(var group in groups){
    int count=0,start=0;
    while(count<group.Value.Count&&start<=corpus.Length-group.Key.Length){
     int at=corpus.IndexOf(group.Key,start,StringComparison.Ordinal);if(at<0)break;
     count++;start=at+group.Key.Length;
    }
    // A duplicate of short text may occur inside another paragraph. This is
    // an omission warning only; matches do not constitute proof of presence.
    if(count<group.Value.Count){
     report.ParagraphsNotFound+=group.Value.Count-count;
     if(group.Value.Count>1)report.RepeatedTextsShortOfOccurrences++;
     // With repeated identical text there is no evidence for which physical
     // occurrence is missing, so report all possible paths in this group.
     missing.AddRange(group.Value);
    }
   }
   report.PotentiallyMissingPaths=missing.ToArray();return report;
  }
 }
}
