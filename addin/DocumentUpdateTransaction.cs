using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HAddon {
 // Multi-location document update. The caller must supply proven native
 // addresses; XML paths or page-text matches alone are not addresses.
 internal sealed class DocumentUpdateTarget {
  internal string Id,Original,Replacement;
 }
 internal interface IDocumentUpdateBackend {
  // Each Prepare must prove the native address, original text and formatting
  // while binding it to the same immutable document revision.
  void Prepare(string revision,DocumentUpdateTarget target);
  void Verify(string revision,IList<DocumentUpdateTarget> plan,int applied);
  void Replace(DocumentUpdateTarget target);
 }
 internal sealed class DocumentUpdateTransaction {
  internal int AppliedCount{get;private set;}
  internal int ChangedCount{get;private set;}
  internal bool RequiresReview{get;private set;}
  internal string AttemptedId{get;private set;}
  static void EnsurePlan(DocumentUpdateTarget[] plan,DocumentUpdateTarget[] baseline){
   if(plan.Length!=baseline.Length)throw new InvalidOperationException("고정된 문서 편집 계획이 바뀌었습니다.");
   for(int i=0;i<plan.Length;i++)if(plan[i]==null||plan[i].Id!=baseline[i].Id||plan[i].Original!=baseline[i].Original||plan[i].Replacement!=baseline[i].Replacement)throw new InvalidOperationException("고정된 문서 편집 계획이 바뀌었습니다.");
  }
  internal async Task ApplyAsync(IDocumentUpdateBackend backend,string revision,IList<DocumentUpdateTarget> supplied,Func<bool> cancelled,Action<int> progress){
   if(backend==null||cancelled==null)throw new ArgumentNullException("backend");
   if(String.IsNullOrEmpty(revision)||supplied==null||supplied.Count==0||supplied.Count>256)throw new InvalidOperationException("문서 편집 계획이 올바르지 않습니다.");
   // Freeze caller-owned objects before the first await. Never accept a plan
   // that could rewrite the same target twice or use unverified source text.
   var plan=supplied.Select(t=>t==null?null:new DocumentUpdateTarget{Id=t.Id,Original=t.Original,Replacement=t.Replacement}).ToArray();
   var ids=new HashSet<string>(StringComparer.Ordinal);
   foreach(var t in plan){
    if(t==null||String.IsNullOrEmpty(t.Id)||!ids.Add(t.Id)||String.IsNullOrEmpty(t.Original)||String.IsNullOrEmpty(t.Replacement)||t.Original.Length>12000||t.Replacement.Length>12000||
     t.Replacement.Any(c=>Char.IsControl(c)||Char.IsSurrogate(c)||c=='\u2028'||c=='\u2029'||c=='\uFFFC'))throw new InvalidOperationException("편집 대상 또는 대체 내용이 올바르지 않습니다.");
   }
   var baseline=plan.Select(t=>new DocumentUpdateTarget{Id=t.Id,Original=t.Original,Replacement=t.Replacement}).ToArray();
   AppliedCount=0;ChangedCount=0;RequiresReview=false;AttemptedId=null;
   if(cancelled())throw new OperationCanceledException("문서 작업이 중단되었습니다.");
   foreach(var target in plan){backend.Prepare(revision,target);EnsurePlan(plan,baseline);if(cancelled())throw new OperationCanceledException("문서 작업이 중단되었습니다.");}
   backend.Verify(revision,plan,0);EnsurePlan(plan,baseline);
   if(cancelled())throw new OperationCanceledException("문서 작업이 중단되었습니다.");
   try{foreach(var target in plan){
    if(cancelled())throw new OperationCanceledException("문서 작업이 중단되었습니다.");
    // The adapter must independently compare the entire document to the
    // planned prefix, including every untouched location, on every check.
    backend.Verify(revision,plan,AppliedCount);EnsurePlan(plan,baseline);
    // Native readback may re-enter the UI and deliver a cancellation. Never
    // start a write on a cancellation received during that readback.
    if(cancelled())throw new OperationCanceledException("문서 작업이 중단되었습니다.");
    if(target.Original!=target.Replacement){
     AttemptedId=target.Id;RequiresReview=true;
     backend.Replace(target); // A throwing native call may already have mutated.
     EnsurePlan(plan,baseline);
    }
    backend.Verify(revision,plan,AppliedCount+1);EnsurePlan(plan,baseline);
    AppliedCount++;if(target.Original!=target.Replacement)ChangedCount++;RequiresReview=false;AttemptedId=null;
    if(progress!=null)progress(AppliedCount);
    // Let the host process cancel/input events between targets. A native
    // adapter must also protect user cursor/selection and content changes.
    await Task.Yield();
   }}catch{if(ChangedCount>0)RequiresReview=true;throw;}
  }
 }
}
