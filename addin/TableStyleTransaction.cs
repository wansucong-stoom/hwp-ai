using System;
using System.Threading.Tasks;

namespace HAddon {
 // Synchronous guarded transaction core. The native adapter must pin the
 // originating document and cursor and must never recreate the document.
 internal interface ITableStyleActions {
  ComposeXml ReadDocument();
  string ReadRawDocument();
  string ReadCaret();
  void CheckIdentity();
  void CopyCellStyle(int table,int row,int column);
  void ApplyCellStyle(int table,int row,int column);
  void RestoreCursor();
 }
 // A cursor move is not a content edit. A native adapter may adopt a new
 // independently restorable user location after the complete planned document
 // prefix is verified unchanged. It must not navigate while adopting it.
 internal interface ITableStyleCursorHandoff {
  bool TryAdoptCursor(string expectedCaret);
 }
 internal interface ITableStyleTextRebase { void RefreshAnchorTexts(ComposeXml updated); }
 internal sealed class TableStyleTransaction {
  internal bool RequiresReview{get;private set;}
  internal int AppliedCount{get;private set;}
  internal bool CursorRestored{get;private set;}
  internal int FullVerifications{get;private set;}internal int ReusedVerifications{get;private set;}
  string verifiedDigest;int verifiedCount=-1;
  internal int AttemptedTable{get;private set;}internal int AttemptedRow{get;private set;}internal int AttemptedColumn{get;private set;}internal bool Applying{get;private set;}
  sealed class UserContextChanged:InvalidOperationException {internal UserContextChanged(string message):base(message){}}
  async Task VerifyAsync(ITableStyleActions actions,ComposeXml original,TableStylePlan plan,int count,Func<bool> cancelled){
   actions.CheckIdentity();string caretBefore=actions.ReadCaret(),raw=actions.ReadRawDocument(),caret=actions.ReadCaret();
   string cursorChange=caretBefore==caret?null:"문서 읽기 중 커서 또는 선택 상태가 달라졌습니다. [읽기 전="+caretBefore+"; 읽기 후="+caret+"]";
   Exception verificationError=null;string digest=null;bool reused=false;try{digest=await Task.Run(delegate{string snapshot=TableStableSnapshot.Digest(raw);try{if(count==verifiedCount&&snapshot==verifiedDigest)reused=true;else TableStylePlan.Verify(original,ComposeXml.Parse(raw),plan,count);}catch(Exception ex){verificationError=ex;}return snapshot;});}catch(Exception ex){verificationError=ex;}
   actions.CheckIdentity();
   string afterWait=actions.ReadCaret();if(caret!=afterWait)cursorChange="검증을 기다리는 동안 커서 또는 선택 상태가 달라졌습니다. [대기 전="+caret+"; 대기 후="+afterWait+"]";
   string current=actions.ReadRawDocument();
   string afterRead=actions.ReadCaret();if(afterWait!=afterRead)cursorChange="문서 재확인 중 커서 또는 선택 상태가 달라졌습니다. [읽기 전="+afterWait+"; 읽기 후="+afterRead+"]";
   if(verificationError!=null){if(cursorChange!=null)throw new UserContextChanged(verificationError.Message);throw verificationError;}
   if(raw!=current){bool same=false;try{same=digest!=null&&digest==TableStableSnapshot.Digest(current);}catch{/* Unknown readback is not safe to overwrite. */}
    if(!same){var refresh=actions as ITableStyleTextRebase;
     if(refresh==null||!TableConcurrentText.TryRebase(original,raw,current,plan,count))throw new UserContextChanged("표 작업 중 문서 내용 또는 서식이 바뀌어 추가 적용을 멈췄습니다.");
     refresh.RefreshAnchorTexts(original);digest=TableStableSnapshot.Digest(current);reused=false;
    }
   }
   if(cursorChange!=null){var handoff=actions as ITableStyleCursorHandoff;if(handoff==null||!handoff.TryAdoptCursor(afterRead))throw new UserContextChanged(cursorChange+" 현재 선택을 안전하게 보존할 수 없어 추가 적용을 멈췄습니다.");}
   if(cancelled())throw new OperationCanceledException("표 작업이 중단되었습니다.");
   verifiedDigest=digest;verifiedCount=count;if(reused)ReusedVerifications++;else FullVerifications++;
  }
  // HWP calls stay on the UI thread. Only immutable HWPML parsing and
  // verification run off-thread. Recheck caret and stable snapshot before
  // any subsequent native command so an intervening edit is never overwritten.
  internal async Task ApplyAsync(ITableStyleActions actions,ComposeXml original,TableStylePlan plan,Func<bool> cancelled,Action<int> progress){
   if(actions==null||original==null||plan==null||cancelled==null)throw new ArgumentNullException("plan");
   if(plan.Changes.Length>120)throw new InvalidOperationException("한 번에 적용할 표 셀이 너무 많습니다. 범위를 줄여 주세요.");
   TableStylePlan.VerifyFinalPlan(original,plan);RequiresReview=false;AppliedCount=0;CursorRestored=false;AttemptedTable=-1;AttemptedRow=-1;AttemptedColumn=-1;Applying=false;verifiedDigest=null;verifiedCount=-1;FullVerifications=0;ReusedVerifications=0;
   bool preserveUserCaret=false;actions.CheckIdentity();
   try{
    await VerifyAsync(actions,original,plan,0,cancelled);
    if(plan.Changes.Length==0){CursorRestored=true;return;}
    foreach(var cell in plan.Changes){
     if(cancelled())throw new OperationCanceledException("표 작업이 중단되었습니다.");
     AttemptedTable=plan.SourceIndex;AttemptedRow=cell.SourceRow;AttemptedColumn=cell.SourceColumn;Applying=false;
     RequiresReview=true;actions.CopyCellStyle(plan.SourceIndex,cell.SourceRow,cell.SourceColumn);
     await VerifyAsync(actions,original,plan,AppliedCount,cancelled);RequiresReview=AppliedCount>0;
     AttemptedTable=cell.Table;AttemptedRow=cell.Row;AttemptedColumn=cell.Column;Applying=true;
     RequiresReview=true;actions.ApplyCellStyle(cell.Table,cell.Row,cell.Column);
     await VerifyAsync(actions,original,plan,AppliedCount+1,cancelled);AppliedCount++;if(progress!=null)progress(AppliedCount);
     AttemptedTable=-1;AttemptedRow=-1;AttemptedColumn=-1;Applying=false;
    }
    RequiresReview=false;
   }catch(UserContextChanged){preserveUserCaret=true;throw;}
   catch{
    if(AppliedCount==0)try{actions.CheckIdentity();TableStylePlan.Verify(original,actions.ReadDocument(),plan,0);RequiresReview=false;}catch{RequiresReview=true;}
    throw;
   }finally{if(!preserveUserCaret&&plan.Changes.Length>0)try{actions.RestoreCursor();CursorRestored=true;}catch{}}
  }
  internal void Apply(ITableStyleActions actions,ComposeXml original,TableStylePlan plan){
   if(actions==null||original==null||plan==null)throw new ArgumentNullException("plan");
   if(plan.Changes.Length>120)throw new InvalidOperationException("한 번에 적용할 표 셀이 너무 많습니다. 범위를 줄여 주세요.");
   TableStylePlan.VerifyFinalPlan(original,plan);
   RequiresReview=false;AppliedCount=0;CursorRestored=false;AttemptedTable=-1;AttemptedRow=-1;AttemptedColumn=-1;Applying=false;actions.CheckIdentity();
   try{TableStylePlan.Verify(original,actions.ReadDocument(),plan,0);}catch(Exception ex){throw new InvalidOperationException("표 작업 전 문서 재확인 실패: "+ex.Message,ex);}
   if(plan.Changes.Length==0){CursorRestored=true;return;}
   try{
    foreach(var cell in plan.Changes){
     actions.CheckIdentity();TableStylePlan.Verify(original,actions.ReadDocument(),plan,AppliedCount);
     // The host could unexpectedly mutate the document even during copy.
     AttemptedTable=plan.SourceIndex;AttemptedRow=cell.SourceRow;AttemptedColumn=cell.SourceColumn;Applying=false;
     RequiresReview=true;actions.CopyCellStyle(plan.SourceIndex,cell.SourceRow,cell.SourceColumn);
     actions.CheckIdentity();TableStylePlan.Verify(original,actions.ReadDocument(),plan,AppliedCount);
     RequiresReview=AppliedCount>0;
     // The native command might mutate before it returns or throws.
     AttemptedTable=cell.Table;AttemptedRow=cell.Row;AttemptedColumn=cell.Column;Applying=true;
     RequiresReview=true;actions.ApplyCellStyle(cell.Table,cell.Row,cell.Column);
     actions.CheckIdentity();TableStylePlan.Verify(original,actions.ReadDocument(),plan,AppliedCount+1);AppliedCount++;
     AttemptedTable=-1;AttemptedRow=-1;AttemptedColumn=-1;Applying=false;
    }
    RequiresReview=false;AttemptedTable=-1;AttemptedRow=-1;AttemptedColumn=-1;Applying=false;
   }catch{
    // A failing navigation or copy may have left the document untouched. Only
    // clear the warning after independently verifying the complete original.
    if(AppliedCount==0)try{actions.CheckIdentity();TableStylePlan.Verify(original,actions.ReadDocument(),plan,0);RequiresReview=false;}catch{RequiresReview=true;}
    throw;
   }finally{try{actions.RestoreCursor();CursorRestored=true;}catch{/* Caller reports that the cursor could not be restored. */}}
  }
 }
}
