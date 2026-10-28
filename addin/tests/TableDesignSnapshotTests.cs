using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Web.Script.Serialization;
using HAddon;
class TableDesignSnapshotTests {
 static XmlElement Cell(ComposeXml x,int table,int row,int col){return x.Document.GetElementsByTagName("TABLE").Cast<XmlElement>().ElementAt(table).GetElementsByTagName("CELL").Cast<XmlElement>().Single(c=>c.GetAttribute("RowAddr")==row.ToString()&&c.GetAttribute("ColAddr")==col.ToString());}
 static void Simulate(ComposeXml xml,TableStylePlan plan,TableStyleCell change){
  var target=Cell(xml,change.Table,change.Row,change.Column);var source=Cell(xml,plan.SourceIndex,change.SourceRow,change.SourceColumn);
  var list=(XmlElement)xml.Document.GetElementsByTagName("BORDERFILLLIST")[0];Func<string,XmlElement> definition=id=>list.ChildNodes.OfType<XmlElement>().Single(x=>x.GetAttribute("Id")==id);
  var composed=(XmlElement)definition(source.GetAttribute("BorderFill")).CloneNode(true);string[] names={"LEFTBORDER","RIGHTBORDER","TOPBORDER","BOTTOMBORDER"};
  for(int i=0;i<4;i++){var edge=definition(Cell(xml,plan.SourceIndex,change.EdgeRows[i],change.EdgeColumns[i]).GetAttribute("BorderFill")).SelectSingleNode(names[i]);composed.ReplaceChild(xml.Document.ImportNode(edge,true),composed.SelectSingleNode(names[i]));}
  var cmp=(XmlElement)composed.CloneNode(true);cmp.RemoveAttribute("Id");string key=ComposeXml.Canonical(cmp);
  var existing=list.ChildNodes.OfType<XmlElement>().FirstOrDefault(x=>{var copy=(XmlElement)x.CloneNode(true);copy.RemoveAttribute("Id");return ComposeXml.Canonical(copy)==key;});
  if(existing==null){int id=list.ChildNodes.OfType<XmlElement>().Max(x=>Int32.Parse(x.GetAttribute("Id")))+1;composed.SetAttribute("Id",id.ToString());list.AppendChild(composed);list.SetAttribute("Count",list.ChildNodes.OfType<XmlElement>().Count().ToString());existing=composed;}
  target.SetAttribute("BorderFill",existing.GetAttribute("Id"));foreach(XmlElement p in target.GetElementsByTagName("P")){p.SetAttribute("ParaShape",change.Paragraph);foreach(XmlElement t in p.GetElementsByTagName("TEXT"))t.SetAttribute("CharShape",change.Character);}TableBorderSimulation.Propagate(xml.Document,change.Table,change.Row,change.Column);
 }
 static void Main(string[] args){
  var document=ComposeXml.Parse(File.ReadAllText(args[0]));var snapshot=TableDesignSnapshot.Create(document,0);
  var tables=(List<object>)snapshot["tables"];
  if(tables.Count<2 || (string)snapshot["referenceTableId"]!="t0")throw new Exception("table count or source");
  int count=0;
  foreach(Dictionary<string,object> table in tables){
   foreach(Dictionary<string,object> cell in (List<object>)table["cells"]){
    var style=(Dictionary<string,object>)cell["style"];
    if(String.IsNullOrEmpty((string)style["borderFill"]))throw new Exception("missing style");
    count++;
   }
  }
  if(count==0)throw new Exception("no cells");
  var assignments=new List<object>();
  foreach(Dictionary<string,object> table in tables){
   if((string)table["id"]=="t0")continue;
   foreach(Dictionary<string,object> cell in (List<object>)table["cells"])assignments.Add(new Dictionary<string,object>{
    {"tableId",table["id"]},{"cellId",cell["id"]},{"role","body"},{"fillSource","c0_0"},{"paragraphSource","c0_0"},{"characterSource","c0_0"},
    {"edges",new Dictionary<string,object>{{"left","c0_0"},{"right","c0_2"},{"top","c0_0"},{"bottom","c0_0"}}}
   });
  }
  var plan=new Dictionary<string,object>{{"assignments",assignments},{"uncertainties",new object[0]}};
  var compiled=TableDesignCompiler.Compile(snapshot,plan);
  if(compiled.Length!=count-((List<object>)((Dictionary<string,object>)tables[0])["cells"]).Count || compiled[0].Left.Column!=0 || compiled[0].Right.Column!=2)throw new Exception("incorrect compiled plan");
  var nativePlan=TableStylePlan.BuildFromDesign(document,0,compiled);
  if(nativePlan.Changes.Length!=compiled.Length||nativePlan.Changes[0].EdgeColumns[0]!=0||nativePlan.Changes[0].EdgeColumns[1]!=2)throw new Exception("independent edge plan was not retained");
  var reversed=TableStylePlan.BuildFromDesign(document,0,compiled.Reverse().ToArray());
  if(!nativePlan.Changes.Select(x=>x.Table+":"+x.Row+":"+x.Column).SequenceEqual(reversed.Changes.Select(x=>x.Table+":"+x.Row+":"+x.Column)))throw new Exception("model response order changed execution order");
  TableStylePlan.Verify(document,ComposeXml.Parse(document.Document.OuterXml),nativePlan,0);
  var legacy=TableStylePlan.Build(document,0);var referenceDesign=new List<TableDesignChange>();
  foreach(var c in legacy.Changes){var source=new TableDesignCellRef{Table=0,Row=c.SourceRow,Column=c.SourceColumn};referenceDesign.Add(new TableDesignChange{Target=new TableDesignCellRef{Table=c.Table,Row=c.Row,Column=c.Column},Fill=source,Paragraph=source,Character=source,Left=source,Right=source,Top=source,Bottom=source,Role="body"});}
  var equivalent=TableStylePlan.BuildFromDesign(document,0,referenceDesign.ToArray());TableStylePlan.VerifyFinalPlan(document,equivalent);
  var merged=ComposeXml.Parse(document.Document.OuterXml);var targetTable=(XmlElement)merged.Document.GetElementsByTagName("TABLE")[1];
  var targetRow=targetTable.ChildNodes.OfType<XmlElement>().Where(x=>x.LocalName=="ROW").ElementAt(2);
  var rowCells=targetRow.ChildNodes.OfType<XmlElement>().Where(x=>x.LocalName=="CELL").OrderBy(x=>Int32.Parse(x.GetAttribute("ColAddr"))).ToArray();
  var list=(XmlElement)rowCells[0].GetElementsByTagName("PARALIST")[0];foreach(var removed in rowCells.Skip(1))foreach(XmlElement p in removed.GetElementsByTagName("P").Cast<XmlElement>().ToArray())list.AppendChild(p.CloneNode(true));
  rowCells[0].SetAttribute("ColSpan","3");targetRow.RemoveChild(rowCells[1]);targetRow.RemoveChild(rowCells[2]);
  var mergedChanges=referenceDesign.Where(x=>x.Target.Table!=1||x.Target.Row!=2||x.Target.Column==0).ToArray();
  var mergedChange=mergedChanges.Single(x=>x.Target.Table==1&&x.Target.Row==2&&x.Target.Column==0);
  mergedChange.Right=new TableDesignCellRef{Table=0,Row=mergedChange.Fill.Row,Column=2};
  var mergedPlan=TableStylePlan.BuildFromDesign(merged,0,mergedChanges);
  if(mergedPlan.Changes.Single(x=>x.Table==1&&x.Row==2&&x.Column==0).ColSpan!=3)throw new Exception("full width merged cell not retained");
  TableStylePlan.VerifyFinalPlan(merged,mergedPlan);
  TableStylePlan.Verify(merged,ComposeXml.Parse(merged.Document.OuterXml),mergedPlan,0);
  var simulated=ComposeXml.Parse(merged.Document.OuterXml);int applied=0;foreach(var change in mergedPlan.Changes){Simulate(simulated,mergedPlan,change);applied++;TableStylePlan.Verify(merged,simulated,mergedPlan,applied);}
  // Reproduce the reported topology: a two-row vertical merge adjacent to a
  // normal cell, and an unexpected absent shared edge on native readback.
  var vertical=ComposeXml.Parse(document.Document.OuterXml);var top=Cell(vertical,1,3,1);var bottom=Cell(vertical,1,4,1);top.SetAttribute("RowSpan","2");bottom.ParentNode.RemoveChild(bottom);
  var verticalDesign=legacy.Changes.Where(c=>c.Table!=1||c.Row!=4||c.Column!=1).Select(c=>{var s=new TableDesignCellRef{Table=0,Row=c.SourceRow,Column=c.SourceColumn};return new TableDesignChange{Target=new TableDesignCellRef{Table=c.Table,Row=c.Row,Column=c.Column},Fill=s,Paragraph=s,Character=s,Left=s,Right=s,Top=s,Bottom=s,Role="body"};}).ToArray();
  var verticalPlan=TableStylePlan.BuildFromDesign(vertical,0,verticalDesign);var readback=ComposeXml.Parse(vertical.Document.OuterXml);int prefix=0;
  foreach(var change in verticalPlan.Changes){Simulate(readback,verticalPlan,change);prefix++;if(change.Table==1&&change.Row==3&&change.Column==0)break;}
  TableStylePlan.Verify(vertical,readback,verticalPlan,prefix);
  var neighbor=Cell(readback,1,3,1);var borderList=(XmlElement)readback.Document.GetElementsByTagName("BORDERFILLLIST")[0];var border=(XmlElement)borderList.ChildNodes.OfType<XmlElement>().Single(e=>e.GetAttribute("Id")==neighbor.GetAttribute("BorderFill")).CloneNode(true);
  ((XmlElement)border.SelectSingleNode("LEFTBORDER")).SetAttribute("Type","None");border.SetAttribute("Id",(borderList.ChildNodes.OfType<XmlElement>().Max(e=>Int32.Parse(e.GetAttribute("Id")))+1).ToString());borderList.AppendChild(border);borderList.SetAttribute("Count",borderList.ChildNodes.OfType<XmlElement>().Count().ToString());neighbor.SetAttribute("BorderFill",border.GetAttribute("Id"));
  TableStylePlan.Verify(vertical,readback,verticalPlan,prefix);
  bool missingEdge=false;try{TableStylePlan.Verify(vertical,readback,verticalPlan,verticalPlan.Changes.Length);}catch(InvalidOperationException){missingEdge=true;}
  if(!missingEdge)throw new Exception("deferred merged-neighbour edge was accepted as final success");
  var continuation=verticalPlan.Changes.Skip(prefix).ToArray();foreach(var change in continuation){Simulate(readback,verticalPlan,change);prefix++;TableStylePlan.Verify(vertical,readback,verticalPlan,prefix);}
  TableStylePlan.Verify(vertical,readback,verticalPlan,verticalPlan.Changes.Length);
  var corrupt=ComposeXml.Parse(readback.Document.OuterXml);var corruptCell=Cell(corrupt,1,3,1);var corruptList=(XmlElement)corrupt.Document.GetElementsByTagName("BORDERFILLLIST")[0];var corruptDef=(XmlElement)corruptList.ChildNodes.OfType<XmlElement>().Single(e=>e.GetAttribute("Id")==corruptCell.GetAttribute("BorderFill")).CloneNode(true);
  ((XmlElement)corruptDef.SelectSingleNode("LEFTBORDER")).SetAttribute("Type","None");corruptDef.SetAttribute("Id",(corruptList.ChildNodes.OfType<XmlElement>().Max(e=>Int32.Parse(e.GetAttribute("Id")))+1).ToString());corruptList.AppendChild(corruptDef);corruptList.SetAttribute("Count",corruptList.ChildNodes.OfType<XmlElement>().Count().ToString());corruptCell.SetAttribute("BorderFill",corruptDef.GetAttribute("Id"));
  bool strictFinal=false;try{TableStylePlan.Verify(vertical,corrupt,verticalPlan,verticalPlan.Changes.Length);}catch(InvalidOperationException e){strictFinal=e.Message.Contains("왼쪽 선 Type")&&e.Message.Contains("실제=None");}
  if(!strictFinal)throw new Exception("final merged neighbour edge was not strictly verified");
  ((Dictionary<string,object>)assignments[0])["cellId"]="unknown";
  bool rejected=false;try{TableDesignCompiler.Compile(snapshot,plan);}catch(InvalidOperationException){rejected=true;}
  if(!rejected)throw new Exception("invalid target accepted");
  if(args.Length>1){var serializer=new JavaScriptSerializer();serializer.MaxJsonLength=4*1024*1024;File.WriteAllText(args[1],serializer.Serialize(snapshot));}
  Console.WriteLine("PASS: read-only table snapshot: "+tables.Count+" tables, "+count+" physical cells");
 }
}
