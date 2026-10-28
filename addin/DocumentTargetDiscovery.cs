using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HAddon {
 // Model-visible target discovery only. Structural IDs are scoped to this
 // immutable snapshot; they are not native addresses or page evidence.
 // No method in this type can read or modify the live HWP document.
 internal sealed class DocumentDiscoveryTarget {
  internal string Id,XmlPath,Text,CellPath;internal bool HasObjects;
 }
 internal sealed class DocumentDiscoveryResult {
  internal DocumentDiscoveryTarget[] Targets;internal string[] Uncertainties;
 }
 internal sealed class DocumentTargetDiscovery {
  const int MaxPages=256,MaxParagraphs=100000,MaxChars=400000,MaxXmlPath=4096;
  readonly string[] pages;readonly int[] targetPages,sourcePages;
  readonly DocumentDiscoveryTarget[] paragraphs;readonly Dictionary<string,DocumentDiscoveryTarget> byId;
  readonly Dictionary<string,int> cellIds;
  internal DocumentTargetDiscovery(string[] suppliedPages,DocumentParagraph[] suppliedParagraphs,int[] target,int[] source){
   if(suppliedPages==null||suppliedPages.Length<1||suppliedPages.Length>MaxPages||suppliedParagraphs==null||suppliedParagraphs.Length>MaxParagraphs||target==null||source==null)throw new InvalidOperationException("문서 분석 범위가 올바르지 않습니다.");
   pages=(string[])suppliedPages.Clone();targetPages=CheckPages(target,pages.Length);sourcePages=CheckPages(source,pages.Length);
   // This prototype sends all structural paragraphs. Do not disclose pages
   // outside the requested ranges; precise scoped structural attribution is
   // a separate capability and has not been validated yet.
   if(targetPages.Concat(sourcePages).Distinct().Count()!=pages.Length)throw new InvalidOperationException("문서 전체 구조를 보내기 전에 요청한 쪽 범위를 확인해야 합니다.");
   long count=0;foreach(var value in pages){if(value==null||value.Length>30000)throw new InvalidOperationException("쪽 내용을 빠짐없이 읽지 못했습니다.");count+=value.Length;}
   if(count>MaxChars)throw new InvalidOperationException("쪽 내용이 분석 한도를 넘습니다.");
   var paths=new HashSet<string>(StringComparer.Ordinal);var copied=new List<DocumentDiscoveryTarget>();byId=new Dictionary<string,DocumentDiscoveryTarget>(StringComparer.Ordinal);cellIds=new Dictionary<string,int>(StringComparer.Ordinal);
   long structureChars=0;
   for(int i=0;i<suppliedParagraphs.Length;i++){
    var p=suppliedParagraphs[i];if(p==null||String.IsNullOrEmpty(p.Path)||p.Path.Length>MaxXmlPath||p.Text==null||!paths.Add(p.Path))throw new InvalidOperationException("문단 구조가 올바르지 않습니다.");
    structureChars+=p.Text.Length;if(structureChars>MaxChars)throw new InvalidOperationException("문단 내용이 분석 한도를 넘습니다.");
    if(p.CellPath!=null&&!cellIds.ContainsKey(p.CellPath))cellIds.Add(p.CellPath,cellIds.Count);
    var copy=new DocumentDiscoveryTarget{Id="p"+i,XmlPath=p.Path,Text=p.Text,CellPath=p.CellPath,HasObjects=p.HasObjects};copied.Add(copy);byId.Add(copy.Id,copy);
   }
   paragraphs=copied.ToArray();
  }
  static int[] CheckPages(int[] input,int count){
   if(input.Length==0||input.Length>count)throw new InvalidOperationException("쪽 범위가 비어 있습니다.");
   var result=(int[])input.Clone();int prev=0;foreach(int p in result){if(p<=prev||p>count)throw new InvalidOperationException("쪽 번호가 중복되거나 올바르지 않습니다.");prev=p;}return result;
  }
  // Request payload contains no local path or native coordinates. Each call
  // returns fresh containers, so external mutation cannot alter validation.
  internal Dictionary<string,object> Payload(){
   var list=new List<object>(paragraphs.Length);
   foreach(var p in paragraphs){
    list.Add(new Dictionary<string,object>{{"id",p.Id},{"text",p.Text},{"cell",p.CellPath==null?null:(object)("c"+cellIds[p.CellPath])},{"hasObjects",p.HasObjects}});
   }
   return new Dictionary<string,object>{{"pages",pages.Select((p,i)=>(object)new Dictionary<string,object>{{"number",i+1},{"text",p}}).ToArray()},
    {"targetPages",(int[])targetPages.Clone()},{"sourcePages",(int[])sourcePages.Clone()},{"paragraphs",list.ToArray()}};
  }
  static object[] ArrayValue(object value){if(value==null||value is string||value is IDictionary)throw new InvalidOperationException("대상 목록이 올바르지 않습니다.");var a=value as IEnumerable;if(a==null)throw new InvalidOperationException("대상 목록이 올바르지 않습니다.");return a.Cast<object>().ToArray();}
  internal DocumentDiscoveryResult Validate(Dictionary<string,object> plan){
   if(plan==null||plan.Count!=2||!plan.ContainsKey("targets")||!plan.ContainsKey("uncertainties"))throw new InvalidOperationException("대상 분석 응답이 올바르지 않습니다.");
   var entries=ArrayValue(plan["targets"]);var uncertainty=ArrayValue(plan["uncertainties"]);if(entries.Length>256||uncertainty.Length>64||uncertainty.Any(x=>!(x is string)||((string)x).Length>500))throw new InvalidOperationException("대상 분석 응답이 너무 큽니다.");
   var seen=new HashSet<string>(StringComparer.Ordinal);var result=new List<DocumentDiscoveryTarget>();
   foreach(var raw in entries){var id=raw as string;DocumentDiscoveryTarget p;if(String.IsNullOrEmpty(id)||!seen.Add(id)||!byId.TryGetValue(id,out p))throw new InvalidOperationException("분석 결과에 존재하지 않거나 중복된 문단이 있습니다.");
    result.Add(new DocumentDiscoveryTarget{Id=p.Id,XmlPath=p.XmlPath,Text=p.Text,CellPath=p.CellPath,HasObjects=p.HasObjects});
   }
   return new DocumentDiscoveryResult{Targets=result.ToArray(),Uncertainties=uncertainty.Cast<string>().ToArray()};
  }
 }
}
