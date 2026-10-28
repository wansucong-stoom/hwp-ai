using System;
using System.Collections.Generic;
using System.Linq;

namespace HAddon {
 // Read-only candidate search. NativeRepeatFindProbe supplies actual HWP
 // positions; XML paths and text matches alone never authorize a write.
 internal sealed class OnDemandExpectedTarget {
  internal string XmlPath,Text,ContainerToken,StyleToken,StructureToken;
 }
 internal sealed class OnDemandObservedParagraph {
  internal int List,Paragraph,Start,End,FirstPhysicalPage,LastPhysicalPage;
  internal string Text,ContainerToken,StyleToken,StructureToken;
  internal bool TextVerified,ContainerVerified,StyleVerified,StructureVerified,PhysicalPageVerified,WholeParagraph,HasObjects;
 }
 internal sealed class OnDemandAnchorSearch {
  internal bool Complete;internal OnDemandObservedParagraph[] Matches;
 }
 internal sealed class OnDemandUnresolvedTarget {internal string XmlPath,Reason;}
 internal sealed class OnDemandLocationResult {
  internal DocumentAddressCandidate[] Targets;internal OnDemandUnresolvedTarget[] Unresolved;
  // Even true only means the experimental provider supplied matching proofs;
  // caller must also independently prove source-page completeness and plan.
  internal bool AllResolved{get{return Unresolved!=null&&Unresolved.Length==0;}}
 }
 internal interface IOnDemandNativeProbe {
  // Provider must bind every method to the same document and revision, and
  // preserve a user's independent cursor/selection while performing reads.
  void CheckSnapshot();
  OnDemandAnchorSearch FindAnchor(string exactText);
  OnDemandObservedParagraph InspectParagraph(int list,int paragraph);
 }
 internal static class OnDemandTargetLocator {
  static bool Token(string value){return !String.IsNullOrWhiteSpace(value)&&value.Length<=4096;}
  static bool Plain(string value){return !String.IsNullOrEmpty(value)&&value.Length<=12000&&!value.Any(c=>Char.IsControl(c)||Char.IsSurrogate(c)||c=='\u2028'||c=='\u2029'||c=='\uFFFC');}
  static OnDemandObservedParagraph[] Matches(OnDemandAnchorSearch search){return search==null||search.Matches==null?new OnDemandObservedParagraph[0]:search.Matches;}
  static OnDemandAnchorSearch CopySearch(OnDemandAnchorSearch value){
   if(value==null)return null;
   return new OnDemandAnchorSearch{Complete=value.Complete,Matches=value.Matches==null?null:value.Matches.Select(p=>p==null?null:new OnDemandObservedParagraph{List=p.List,Paragraph=p.Paragraph,Start=p.Start,End=p.End,FirstPhysicalPage=p.FirstPhysicalPage,LastPhysicalPage=p.LastPhysicalPage,Text=p.Text,ContainerToken=p.ContainerToken,StyleToken=p.StyleToken,StructureToken=p.StructureToken,TextVerified=p.TextVerified,ContainerVerified=p.ContainerVerified,StyleVerified=p.StyleVerified,StructureVerified=p.StructureVerified,PhysicalPageVerified=p.PhysicalPageVerified,WholeParagraph=p.WholeParagraph,HasObjects=p.HasObjects}).ToArray()};
  }
  internal static OnDemandLocationResult Resolve(IOnDemandNativeProbe probe,DocumentParagraph[] document,OnDemandExpectedTarget[] requested,int[] targetPages,Func<bool> cancelled){
   if(probe==null||document==null||requested==null||targetPages==null||cancelled==null)throw new ArgumentNullException("target");
   if(requested.Length>256||targetPages.Length==0||targetPages.Any(p=>p<1||p>256)||targetPages.Distinct().Count()!=targetPages.Length)throw new InvalidOperationException("문단 위치 범위가 올바르지 않습니다.");
   // Copy before invoking the provider: a reentrant UI callback must not be
   // able to mutate the snapshot, tokens, IDs, or allowed pages.
   var paragraphs=document.Select(p=>p==null?null:new DocumentParagraph{Path=p.Path,Text=p.Text,CellPath=p.CellPath,HasObjects=p.HasObjects}).ToArray();
   var expected=requested.Select(p=>p==null?null:new OnDemandExpectedTarget{XmlPath=p.XmlPath,Text=p.Text,ContainerToken=p.ContainerToken,StyleToken=p.StyleToken,StructureToken=p.StructureToken}).ToArray();
   var pages=new HashSet<int>(targetPages);var byPath=new Dictionary<string,DocumentParagraph>(StringComparer.Ordinal);
   foreach(var p in paragraphs){if(p==null||String.IsNullOrEmpty(p.Path)||p.Text==null||byPath.ContainsKey(p.Path))throw new InvalidOperationException("문서 문단 구조가 올바르지 않습니다.");byPath.Add(p.Path,p);}
   var seen=new HashSet<string>(StringComparer.Ordinal);
   foreach(var t in expected)if(t==null||String.IsNullOrEmpty(t.XmlPath)||!seen.Add(t.XmlPath)||!byPath.ContainsKey(t.XmlPath)||byPath[t.XmlPath].Text!=t.Text)throw new InvalidOperationException("고정 문단과 원문이 일치하지 않습니다.");
   var anchors=DocumentTargetAnchors.Find(paragraphs,expected.Select(t=>t.XmlPath).ToArray()).ToDictionary(a=>a.TargetPath,StringComparer.Ordinal);
   var found=new List<DocumentAddressCandidate>();var unresolved=new List<OnDemandUnresolvedTarget>();var searches=new Dictionary<string,OnDemandAnchorSearch>(StringComparer.Ordinal);
   Action<string,string> miss=(path,reason)=>unresolved.Add(new OnDemandUnresolvedTarget{XmlPath=path,Reason=reason});
   foreach(var target in expected){
    if(cancelled())throw new OperationCanceledException("문단 위치 확인이 중단되었습니다.");probe.CheckSnapshot();
    DocumentTargetAnchor anchor;
    if(byPath[target.XmlPath].HasObjects||!Plain(target.Text)){miss(target.XmlPath,"대상 문단에 개체 또는 지원되지 않는 텍스트가 있습니다.");continue;}
    if(!Token(target.ContainerToken)||!Token(target.StyleToken)||!Token(target.StructureToken)){miss(target.XmlPath,"대상의 셀·서식·구조 기준이 없습니다.");continue;}
    if(!anchors.TryGetValue(target.XmlPath,out anchor)){miss(target.XmlPath,"고유한 주변 앵커를 찾지 못했습니다.");continue;}
    OnDemandAnchorSearch search;
    if(!searches.TryGetValue(anchor.AnchorText,out search)){search=CopySearch(probe.FindAnchor(anchor.AnchorText));searches.Add(anchor.AnchorText,search);}
    probe.CheckSnapshot();if(cancelled())throw new OperationCanceledException("문단 위치 확인이 중단되었습니다.");
    var matches=Matches(search);if(search==null||!search.Complete||matches.Length!=1){miss(target.XmlPath,"앵커 검색의 완전성 또는 유일성을 확인하지 못했습니다.");continue;}
    var hit=matches[0];if(hit==null||!hit.TextVerified||!hit.WholeParagraph||hit.HasObjects||hit.Text!=anchor.AnchorText||hit.List<0||hit.Paragraph<0){miss(target.XmlPath,"원래 앵커의 한글 문단을 확인하지 못했습니다.");continue;}
    long index=(long)hit.Paragraph+anchor.ParagraphDelta;if(index<0||index>Int32.MaxValue){miss(target.XmlPath,"대상 문단의 상대 위치가 올바르지 않습니다.");continue;}
    var actual=probe.InspectParagraph(hit.List,(int)index);probe.CheckSnapshot();if(cancelled())throw new OperationCanceledException("문단 위치 확인이 중단되었습니다.");
    if(actual==null||actual.List!=hit.List||actual.Paragraph!=(int)index||actual.Start<0||actual.End<=actual.Start||actual.End-actual.Start!=target.Text.Length||
       !actual.TextVerified||!actual.WholeParagraph||actual.HasObjects||actual.Text!=target.Text||
       !actual.ContainerVerified||!actual.StyleVerified||!actual.StructureVerified||!actual.PhysicalPageVerified||
       actual.ContainerToken!=target.ContainerToken||actual.StyleToken!=target.StyleToken||actual.StructureToken!=target.StructureToken||
       actual.FirstPhysicalPage!=actual.LastPhysicalPage||!pages.Contains(actual.FirstPhysicalPage)){
     miss(target.XmlPath,"대상 원문·쪽·셀·서식·문단 구조를 모두 확인하지 못했습니다.");continue;
    }
    found.Add(new DocumentAddressCandidate{XmlPath=target.XmlPath,Text=target.Text,Page=actual.FirstPhysicalPage,List=actual.List,Paragraph=actual.Paragraph,Start=actual.Start,End=actual.End});
   }
   probe.CheckSnapshot();if(cancelled())throw new OperationCanceledException("문단 위치 확인이 중단되었습니다.");
   // A collision makes every claimant ambiguous, including the first one.
   var collisions=new HashSet<string>(found.GroupBy(x=>x.List+":"+x.Paragraph,StringComparer.Ordinal).Where(g=>g.Count()>1).SelectMany(g=>g.Select(x=>x.XmlPath)),StringComparer.Ordinal);
   foreach(var path in collisions)miss(path,"서로 다른 문단이 같은 한글 위치를 가리킵니다.");
   return new OnDemandLocationResult{Targets=found.Where(x=>!collisions.Contains(x.XmlPath)).ToArray(),Unresolved=unresolved.ToArray()};
  }
 }
}
