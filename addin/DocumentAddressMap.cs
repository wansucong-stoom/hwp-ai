using System;
using System.Collections.Generic;
using System.Linq;

namespace HAddon {
 // Candidate matching only. Unique matching helps diagnose the connection
 // between serialised paragraphs, scan addresses and page text; it is not by
 // itself proof of a page boundary or permission to edit a native address.
 internal sealed class InspectedNativeParagraph {internal int List,Paragraph,Start,End;internal string Text;}
 internal sealed class DocumentAddressCandidate {internal string XmlPath,Text;internal int Page,List,Paragraph,Start,End;}
 internal sealed class PagePositionEvidence {internal string XmlPath;internal int List,Paragraph,Start,End,RawPage;}
 internal static class DocumentAddressMap {
  static string Norm(string s){return s==null?null:String.Join(" ",s.Split((char[])null,StringSplitOptions.RemoveEmptyEntries));}
  // Match independently inspected native paragraphs to the serialized XML by
  // exact, globally unique text. Returned page numbers are intentionally zero
  // until native page-position evidence has been collected separately.
  internal static DocumentAddressCandidate[] FindStructural(DocumentParagraph[] structural,InspectedNativeParagraph[] native){
   if(structural==null||native==null)throw new ArgumentNullException("snapshot");
   var xml=structural.Where(p=>p!=null&&!p.HasObjects&&!String.IsNullOrWhiteSpace(p.Text)).GroupBy(p=>p.Text,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.Ordinal);
   var positions=native.Where(p=>p!=null&&p.List>=0&&p.Paragraph>=0&&p.Start>=0&&p.End>p.Start&&!String.IsNullOrWhiteSpace(p.Text)).GroupBy(p=>p.Text,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.Ordinal);
   var output=new List<DocumentAddressCandidate>();foreach(var pair in xml){InspectedNativeParagraph[] found;
    if(pair.Value.Length!=1||!positions.TryGetValue(pair.Key,out found)||found.Length!=1)continue;
    var p=found[0];output.Add(new DocumentAddressCandidate{XmlPath=pair.Value[0].Path,Text=pair.Key,Page=0,List=p.List,Paragraph=p.Paragraph,Start=p.Start,End=p.End});
   }return output.ToArray();
  }
  // Cross-check the independently observed endpoint page with page text.
  // No match is itself proof that page text is complete or that the raw
  // indicator is a physical page; the caller must establish these separately.
  internal static DocumentAddressCandidate[] BindPages(DocumentAddressCandidate[] candidates,PagePositionEvidence[] evidence,string[] pages){
   if(candidates==null||evidence==null||pages==null)throw new ArgumentNullException("snapshot");
   var byPath=new Dictionary<string,PagePositionEvidence>(StringComparer.Ordinal);
   foreach(var e in evidence){if(e==null||String.IsNullOrEmpty(e.XmlPath)||byPath.ContainsKey(e.XmlPath))throw new InvalidOperationException("쪽 위치가 중복되거나 올바르지 않습니다.");byPath.Add(e.XmlPath,e);}
   var output=new List<DocumentAddressCandidate>();foreach(var c in candidates){PagePositionEvidence e;
    if(c==null||String.IsNullOrEmpty(c.XmlPath)||!byPath.TryGetValue(c.XmlPath,out e)||e.List!=c.List||e.Paragraph!=c.Paragraph||e.Start!=c.Start||e.End!=c.End||e.RawPage<1||e.RawPage>pages.Length)continue;
    string page=Norm(pages[e.RawPage-1]),text=Norm(c.Text);if(String.IsNullOrEmpty(page)||String.IsNullOrEmpty(text)||page.IndexOf(text,StringComparison.Ordinal)<0)continue;
    output.Add(new DocumentAddressCandidate{XmlPath=c.XmlPath,Text=c.Text,Page=e.RawPage,List=c.List,Paragraph=c.Paragraph,Start=c.Start,End=c.End});
   }return output.ToArray();
  }
  internal static DocumentAddressCandidate[] Find(DocumentParagraph[] structural,InspectedNativeParagraph[] native,string[] pages){
   if(structural==null||native==null||pages==null)throw new ArgumentNullException("snapshot");
   var xmlGroups=structural.Where(p=>p!=null&&!p.HasObjects&&!String.IsNullOrWhiteSpace(p.Text)).GroupBy(p=>Norm(p.Text)).ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.Ordinal);
   var nativeGroups=native.Where(p=>p!=null&&p.List>=0&&p.Paragraph>=0&&p.Start>=0&&p.End>p.Start&&!String.IsNullOrWhiteSpace(p.Text)).GroupBy(p=>Norm(p.Text)).ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.Ordinal);
   var normalizedPages=pages.Select(Norm).ToArray();var output=new List<DocumentAddressCandidate>();
   foreach(var pair in xmlGroups){
    InspectedNativeParagraph[] candidates;if(pair.Value.Length!=1||!nativeGroups.TryGetValue(pair.Key,out candidates)||candidates.Length!=1)continue;
    int matched=-1,count=0;for(int i=0;i<normalizedPages.Length;i++){
     var page=normalizedPages[i];if(page==null)continue;int from=0;
     while(from<=page.Length-pair.Key.Length){int at=page.IndexOf(pair.Key,from,StringComparison.Ordinal);if(at<0)break;matched=i+1;count++;from=at+1;if(count>1)break;}
     if(count>1)break;
    }
    if(count!=1)continue;var target=candidates[0];output.Add(new DocumentAddressCandidate{XmlPath=pair.Value[0].Path,Text=pair.Value[0].Text,Page=matched,List=target.List,Paragraph=target.Paragraph,Start=target.Start,End=target.End});
   }
   return output.ToArray();
  }
 }
}
