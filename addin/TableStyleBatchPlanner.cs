using System;
using System.Collections.Generic;
using System.Linq;

namespace HAddon {
 // Planning only: batch execution must independently prove HWP's selected-cell
 // range, merged-cell behaviour, border propagation and readback semantics.
 internal sealed class TableStyleBatch {
  internal int Table,Row,FirstColumn,EndColumn;
  internal TableStyleCell[] Cells;
 }
 internal static class TableStyleBatchPlanner {
  static bool SameStyle(TableStyleCell a,TableStyleCell b){
   return a.SourceRow==b.SourceRow&&a.SourceColumn==b.SourceColumn&&
    a.Border==b.Border&&a.Paragraph==b.Paragraph&&a.Character==b.Character&&
    ((a.EdgeRows==null&&b.EdgeRows==null)||(a.EdgeRows!=null&&b.EdgeRows!=null&&a.EdgeRows.SequenceEqual(b.EdgeRows)))&&
    ((a.EdgeColumns==null&&b.EdgeColumns==null)||(a.EdgeColumns!=null&&b.EdgeColumns!=null&&a.EdgeColumns.SequenceEqual(b.EdgeColumns)));
  }
  internal static TableStyleBatch[] Build(TableStylePlan plan){
   if(plan==null||plan.Changes==null)throw new ArgumentNullException("plan");
   var batches=new List<TableStyleBatch>();var current=new List<TableStyleCell>();
   Action flush=()=>{if(current.Count==0)return;var first=current[0];var last=current[current.Count-1];batches.Add(new TableStyleBatch{Table=first.Table,Row=first.Row,FirstColumn=first.Column,EndColumn=last.Column+last.ColSpan,Cells=current.ToArray()});current.Clear();};
   // Keep execution order fixed: reordering can change shared edges.
   foreach(var cell in plan.Changes){
    if(cell==null||cell.RowSpan<1||cell.ColSpan<1)throw new InvalidOperationException("표 셀 범위가 올바르지 않습니다.");
    var last=current.Count==0?null:current[current.Count-1];
    if(last!=null&&(last.Table!=cell.Table||last.Row!=cell.Row||last.RowSpan!=cell.RowSpan||last.Column+last.ColSpan!=cell.Column||!SameStyle(last,cell)))flush();
    current.Add(cell);
   }
   flush();return batches.ToArray();
  }
 }
}
