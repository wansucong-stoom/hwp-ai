using System;
using HAddon;
class TableStyleBatchPlannerTests {
 static TableStyleCell Cell(int table,int row,int col,int width=1,int height=1,int source=1){return new TableStyleCell{Table=table,Row=row,Column=col,ColSpan=width,RowSpan=height,SourceRow=source,SourceColumn=source,Border="b",Paragraph="p",Character="c",EdgeRows=new[]{source,source,source,source},EdgeColumns=new[]{source,source,source,source}};}
 static void Main(){
  var p=new TableStylePlan{Changes=new[]{Cell(1,0,0),Cell(1,0,1),Cell(1,0,2,2),Cell(1,1,0),Cell(1,1,1,1,1,2),Cell(2,0,0)}};
  var groups=TableStyleBatchPlanner.Build(p);
  if(groups.Length!=4||groups[0].Cells.Length!=3||groups[0].EndColumn!=4||groups[1].Cells.Length!=1||groups[2].Cells.Length!=1||groups[3].Table!=2)throw new Exception("invalid contiguous batching");
  var a=Cell(1,0,0);var b=Cell(1,0,1);b.EdgeRows[0]=2;p.Changes=new[]{a,b};if(TableStyleBatchPlanner.Build(p).Length!=2)throw new Exception("different edge source combined");
  p.Changes=new[]{Cell(1,0,0,1,2),Cell(1,0,1)};if(TableStyleBatchPlanner.Build(p).Length!=2)throw new Exception("different height combined");
  Console.WriteLine("PASS conservative contiguous batching plan; no native bulk execution");
 }
}
