using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace HAddon {
 internal sealed class PageClarificationRequiredException:InvalidOperationException {
  internal readonly string[] Questions;
  internal PageClarificationRequiredException(string[] questions):base("본문을 갱신하기 전에 확인할 내용이 있습니다."){Questions=questions;}
 }
 // Read-only model boundary. This validates scope; it does not prove that
 // GetPageText contains every object or that a candidate is on a given page.
 // Callers must independently establish those facts before native writes.
 internal sealed class PageEditPlanning {
  readonly Dictionary<string,DocumentAddressCandidate> targets;
  readonly HashSet<int> sourcePages;
  readonly Dictionary<int,string> sourceTexts;
  readonly int[] selectedPageNumbers;readonly string[] frozenPages;readonly List<string> targetOrder;
  readonly DocumentParagraph[] structuredParagraphs;
  // Produce an independent payload for each caller. A retained or modified
  // analyzer request cannot change later requests or the validated plan.
  internal Dictionary<string,object> Snapshot {get{
   var value=new Dictionary<string,object>{{"pages",selectedPageNumbers.Select(p=>(object)new Dictionary<string,object>{{"number",p},{"text",frozenPages[p-1]}}).ToArray()},
    {"targets",targetOrder.Select(id=>(object)new Dictionary<string,object>{{"id",id},{"page",targets[id].Page},{"text",targets[id].Text}}).ToArray()}};
   if(structuredParagraphs!=null)value["structuredParagraphs"]=structuredParagraphs.Select(p=>(object)new Dictionary<string,object>{{"text",p.Text},{"hasObjects",p.HasObjects}}).ToArray();
   return value;
  }}
  internal PageEditPlanning(string[] pages,DocumentAddressCandidate[] candidates,IEnumerable<int> targetPages,IEnumerable<int> sourcePages):this(pages,candidates,targetPages,sourcePages,null){}
  internal PageEditPlanning(string[] pages,DocumentAddressCandidate[] candidates,IEnumerable<int> targetPages,IEnumerable<int> sourcePages,DocumentParagraph[] structure){
   if(pages==null||candidates==null||targetPages==null||sourcePages==null||pages.Length==0||pages.Length>256)throw new InvalidOperationException("쪽 분석 범위가 올바르지 않습니다.");
   var targetSet=new HashSet<int>(targetPages);this.sourcePages=new HashSet<int>(sourcePages);
   if(targetSet.Count==0||this.sourcePages.Count==0||targetSet.Any(x=>x<1||x>pages.Length)||this.sourcePages.Any(x=>x<1||x>pages.Length))throw new InvalidOperationException("대상 쪽 또는 참조 쪽이 문서 범위를 벗어납니다.");
   selectedPageNumbers=targetSet.Concat(this.sourcePages).Distinct().OrderBy(x=>x).ToArray();frozenPages=(string[])pages.Clone();
   if(selectedPageNumbers.Any(p=>frozenPages[p-1]==null))throw new InvalidOperationException("문서 쪽 내용을 읽지 못했습니다.");
   if(structure!=null){
    if(selectedPageNumbers.Length!=pages.Length||structure.Length>100000||structure.Any(p=>p==null||p.Text==null)||structure.Sum(p=>(long)p.Text.Length)>400000)throw new InvalidOperationException("요청 범위의 전체 구조 텍스트를 확인하지 못했습니다.");
    structuredParagraphs=structure.Select(p=>new DocumentParagraph{Text=p.Text,HasObjects=p.HasObjects}).ToArray();
   }
   sourceTexts=this.sourcePages.ToDictionary(p=>p,p=>frozenPages[p-1]);
   targets=new Dictionary<string,DocumentAddressCandidate>(StringComparer.Ordinal);targetOrder=new List<string>();
   foreach(var candidate in candidates){
    if(candidate==null||!targetSet.Contains(candidate.Page))continue;
    if(String.IsNullOrEmpty(candidate.XmlPath)||String.IsNullOrEmpty(candidate.Text)||targets.ContainsKey(candidate.XmlPath))throw new InvalidOperationException("대상 문단 주소가 중복되거나 유효하지 않습니다.");
    // A caller may retain and mutate candidate objects while the analyzer is
    // awaiting Codex. Bind the operation to the original content and native
    // address rather than to the caller-owned mutable instance.
    var frozen=new DocumentAddressCandidate{XmlPath=candidate.XmlPath,Page=candidate.Page,Text=candidate.Text,List=candidate.List,Paragraph=candidate.Paragraph,Start=candidate.Start,End=candidate.End};
    targets.Add(frozen.XmlPath,frozen);targetOrder.Add(frozen.XmlPath);
   }
   if(targets.Count==0)throw new InvalidOperationException("대상 쪽에서 확인 가능한 문단이 없습니다.");
  }
  static object[] ArrayValue(object value){if(value==null||value is string||value is IDictionary)throw new InvalidOperationException("쪽 편집 계획의 배열이 올바르지 않습니다.");var values=value as IEnumerable;if(values==null)throw new InvalidOperationException("쪽 편집 계획의 배열이 올바르지 않습니다.");return values.Cast<object>().ToArray();}
  static int Page(object value){if(!(value is int||value is long))throw new InvalidOperationException("참조 쪽이 올바르지 않습니다.");try{return Convert.ToInt32(value);}catch{throw new InvalidOperationException("참조 쪽이 올바르지 않습니다.");}}
  internal DocumentUpdateTarget[] Compile(Dictionary<string,object> plan){
   if(plan==null||plan.Count!=2||!plan.ContainsKey("changes")||!plan.ContainsKey("uncertainties"))throw new InvalidOperationException("쪽 편집 계획을 확인하지 못했습니다.");
   var uncertainties=ArrayValue(plan["uncertainties"]);if(uncertainties.Length>64||uncertainties.Any(x=>!(x is string)||((string)x).Length>500))throw new InvalidOperationException("쪽 편집 불확실성 정보가 올바르지 않습니다.");
   if(uncertainties.Length>0)throw new PageClarificationRequiredException(uncertainties.Cast<string>().ToArray());
   var changes=ArrayValue(plan["changes"]);if(changes.Length>256)throw new InvalidOperationException("수정 문단이 너무 많습니다.");
   var used=new HashSet<string>(StringComparer.Ordinal);var result=new List<DocumentUpdateTarget>();
   foreach(var raw in changes){
    var change=raw as Dictionary<string,object>;if(change==null||change.Count!=3||!change.ContainsKey("id")||!change.ContainsKey("replacement")||!change.ContainsKey("sourcePages"))throw new InvalidOperationException("수정 계획의 항목이 올바르지 않습니다.");
    var id=change["id"] as string;var replacement=change["replacement"] as string;DocumentAddressCandidate target;
    if(id==null||!used.Add(id)||!targets.TryGetValue(id,out target)||String.IsNullOrEmpty(replacement)||replacement.Length>12000||replacement.Any(c=>Char.IsControl(c)||Char.IsSurrogate(c)||c=='\u2028'||c=='\u2029'||c=='\uFFFC'))throw new InvalidOperationException("계획에 포함된 문단 또는 대체 내용이 유효하지 않습니다.");
    var refs=ArrayValue(change["sourcePages"]);if(refs.Length==0||refs.Length>256)throw new InvalidOperationException("참조 쪽 수가 올바르지 않습니다.");
    var referenced=new HashSet<int>();foreach(var rawPage in refs){int page=Page(rawPage);if(!sourcePages.Contains(page)||!referenced.Add(page)||String.IsNullOrWhiteSpace(sourceTexts[page]))throw new InvalidOperationException("요청 범위 밖이거나 중복되었거나 내용이 없는 참조 쪽을 사용했습니다.");}
    result.Add(new DocumentUpdateTarget{Id=id,Original=target.Text,Replacement=replacement});
   }
   return result.ToArray();
  }
 }
}
