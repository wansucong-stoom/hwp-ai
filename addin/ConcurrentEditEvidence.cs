using System;
using System.Collections.Generic;
using System.Linq;

namespace HAddon {
 // Experimental concurrency contract; no HWP adapter currently supplies it.
 // Evidence must come from a trusted native reader, not Codex or UI strings.
 // Layout must describe physical page boundaries and relevant object geometry;
 // Source must cover the complete source scope, including non-text objects;
 // Mapping must prove that target IDs refer to the same native locations; and
 // each target fingerprint must cover text, objects and formatting.
 internal sealed class ConcurrentEditEvidence {
  internal readonly string Document, Revision, Layout, Mapping, Source;
  internal readonly bool CompleteAndNativeVerified;
  readonly Dictionary<string,string> targets;
  internal ConcurrentEditEvidence(string document,string revision,string layout,string mapping,string source,IDictionary<string,string> targets,bool completeAndNativeVerified){
   if(String.IsNullOrWhiteSpace(document)||String.IsNullOrWhiteSpace(revision)||targets==null||targets.Count==0)throw new ArgumentException("문서 충돌 검사 정보가 부족합니다.");
   Document=document;Revision=revision;Layout=layout;Mapping=mapping;Source=source;CompleteAndNativeVerified=completeAndNativeVerified;
   this.targets=new Dictionary<string,string>(StringComparer.Ordinal);
   foreach(var entry in targets){if(String.IsNullOrWhiteSpace(entry.Key)||String.IsNullOrWhiteSpace(entry.Value)||this.targets.ContainsKey(entry.Key))throw new ArgumentException("대상 증거가 올바르지 않습니다.");this.targets.Add(entry.Key,entry.Value);}
  }
  internal string[] TargetIds {get{return targets.Keys.OrderBy(x=>x,StringComparer.Ordinal).ToArray();}}
  internal string Target(string key){return targets[key];}
 }
 internal enum ConcurrentEditResult {Unchanged,SafeOutsideScope,DocumentChanged,Unproven,LayoutChanged,MappingChanged,SourceChanged,TargetsChanged}
 internal static class ConcurrentEditCheck {
  internal static ConcurrentEditResult Compare(ConcurrentEditEvidence original,ConcurrentEditEvidence current){
   if(original==null||current==null)throw new ArgumentNullException("evidence");
   if(original.Document!=current.Document)return ConcurrentEditResult.DocumentChanged;
   if(original.Revision==current.Revision){
    // A matching revision is authoritative only if any supplied projections
    // are consistent. A faulty reader must not silently contradict itself.
    var sameRevisionIds=original.TargetIds;
    if(!sameRevisionIds.SequenceEqual(current.TargetIds,StringComparer.Ordinal)||sameRevisionIds.Any(id=>original.Target(id)!=current.Target(id))||
     (original.Layout!=null&&current.Layout!=null&&original.Layout!=current.Layout)||
     (original.Mapping!=null&&current.Mapping!=null&&original.Mapping!=current.Mapping)||
     (original.Source!=null&&current.Source!=null&&original.Source!=current.Source))return ConcurrentEditResult.Unproven;
    return ConcurrentEditResult.Unchanged;
   }
   if(!original.CompleteAndNativeVerified||!current.CompleteAndNativeVerified||String.IsNullOrWhiteSpace(original.Layout)||String.IsNullOrWhiteSpace(original.Mapping)||String.IsNullOrWhiteSpace(original.Source)||String.IsNullOrWhiteSpace(current.Layout)||String.IsNullOrWhiteSpace(current.Mapping)||String.IsNullOrWhiteSpace(current.Source))return ConcurrentEditResult.Unproven;
   if(original.Layout!=current.Layout)return ConcurrentEditResult.LayoutChanged;
   if(original.Mapping!=current.Mapping)return ConcurrentEditResult.MappingChanged;
   if(original.Source!=current.Source)return ConcurrentEditResult.SourceChanged;
   var ids=original.TargetIds;if(!ids.SequenceEqual(current.TargetIds,StringComparer.Ordinal)||ids.Any(id=>original.Target(id)!=current.Target(id)))return ConcurrentEditResult.TargetsChanged;
   return ConcurrentEditResult.SafeOutsideScope;
  }
 }
}
