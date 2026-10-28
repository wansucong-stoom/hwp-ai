using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using HAddon;
internal static class TableStylePlanTests {
 static int n;
 static void Assert(bool value,string label){n++;if(!value)throw new Exception(label);}
 static void Fail(Action action,string label){bool ok=false;try{action();}catch(InvalidOperationException){ok=true;}Assert(ok,label);}
 static string Cell(int r,int c,int span=1){return "<CELL RowAddr='"+r+"' ColAddr='"+c+"' RowSpan='"+span+"' ColSpan='1' BorderFill='"+(r==0?3:4)+"'><PARALIST><P ParaShape='5'><TEXT CharShape='"+(r==0?6:0)+"'><CHAR>x</CHAR></TEXT></P></PARALIST></CELL>";}
 static string Table(int columns=2,int span=1){return "<TABLE RowCount='2' ColCount='"+columns+"'><ROW>"+Cell(0,0,span)+(columns>1?Cell(0,1):"")+(columns>2?Cell(0,2):"")+"</ROW><ROW>"+Cell(1,0)+(columns>1?Cell(1,1):"")+(columns>2?Cell(1,2):"")+"</ROW></TABLE>";}
 static string Doc(string tables){return "<HWPML><HEAD><MAPPINGTABLE/></HEAD><BODY><SECTION><P><TEXT>"+tables+"</TEXT></P></SECTION></BODY></HWPML>";}
 static XmlElement At(ComposeXml xml,int table,int row,int col){return xml.Document.GetElementsByTagName("TABLE").Cast<XmlElement>().ElementAt(table).GetElementsByTagName("CELL").Cast<XmlElement>().Single(x=>x.GetAttribute("RowAddr")==row.ToString()&&x.GetAttribute("ColAddr")==col.ToString());}
 static XmlElement ForkBorder(ComposeXml xml,int table,int row,int col){
  var cell=At(xml,table,row,col);var list=(XmlElement)xml.Document.GetElementsByTagName("BORDERFILLLIST")[0];var defs=list.GetElementsByTagName("BORDERFILL").Cast<XmlElement>().ToArray();
  var next=(XmlElement)defs.Single(x=>x.GetAttribute("Id")==cell.GetAttribute("BorderFill")).CloneNode(true);string id=(defs.Max(x=>Int32.Parse(x.GetAttribute("Id")))+1).ToString();
  next.SetAttribute("Id",id);list.AppendChild(next);list.SetAttribute("Count",(defs.Length+1).ToString());cell.SetAttribute("BorderFill",id);return next;
 }
 static void Apply(ComposeXml xml,TableStyleCell op,bool borders=true){
  var cell=At(xml,op.Table,op.Row,op.Column);cell.SetAttribute("BorderFill",op.Border);foreach(XmlElement p in cell.GetElementsByTagName("P")){p.SetAttribute("ParaShape",op.Paragraph);foreach(XmlElement text in p.GetElementsByTagName("TEXT"))text.SetAttribute("CharShape",op.Character);}
  if(borders)TableBorderSimulation.Propagate(xml.Document,op.Table,op.Row,op.Column);
 }
 static void CompactUnusedBorder(ComposeXml xml,int removed){
  var list=(XmlElement)xml.Document.GetElementsByTagName("BORDERFILLLIST")[0];var defs=list.GetElementsByTagName("BORDERFILL").Cast<XmlElement>().ToArray();
  foreach(XmlElement element in xml.Document.SelectNodes("//*"))foreach(XmlAttribute attr in element.Attributes)if(attr.LocalName=="BorderFill"||attr.LocalName=="BorderFillId"){
   int id=Int32.Parse(attr.Value);if(id==removed)throw new Exception("Synthetic compact removed a referenced style");if(id>removed)attr.Value=(id-1).ToString();
  }
  list.RemoveChild(defs.Single(x=>x.GetAttribute("Id")==removed.ToString()));foreach(XmlElement def in list.GetElementsByTagName("BORDERFILL")){int id=Int32.Parse(def.GetAttribute("Id"));if(id>removed)def.SetAttribute("Id",(id-1).ToString());}
  list.SetAttribute("Count",list.GetElementsByTagName("BORDERFILL").Count.ToString());
 }
 static void Main(string[] args){
  try{
   string other=Table().Replace("BorderFill='3'","BorderFill='30'").Replace("BorderFill='4'","BorderFill='40'");
   var xml=ComposeXml.Parse(Doc(Table()+other+other));string hash=RichFragmentFile.Digest(Encoding.UTF8.GetBytes(xml.Document.OuterXml));
   var plan=TableStylePlan.Build(xml,0);Assert(plan.Rows==2&&plan.Columns==2&&plan.Changes.Length==8,"same-size tables map each non-reference cell");
   Assert(plan.Changes.All(c=>c.Table!=0)&&plan.Changes.Single(c=>c.Table==1&&c.Row==0&&c.Column==0).Character=="6","source table remains untouched and header uses source shape");
   Assert(RichFragmentFile.Digest(Encoding.UTF8.GetBytes(xml.Document.OuterXml))==hash,"planning does not modify document");
   var varied=TableStylePlan.Build(ComposeXml.Parse(Doc(Table(3)+other)),0);Assert(varied.Changes.Length==4&&varied.TableColumns[1]==2&&varied.Changes[2].SourceRow==1&&varied.Changes[1].SourceColumn==2,"different dimensions retain header, body and outer-edge roles");
   Assert(TableStylePlan.Build(ComposeXml.Parse(Doc(Table()+Table())),0).Changes.Length==0,"already identical target styles never trigger writes");
   Fail(()=>TableStylePlan.Build(ComposeXml.Parse(Doc(Table()+Table(1))),0),"mixed single-column geometry cannot preserve both edges");
   Fail(()=>TableStylePlan.Build(ComposeXml.Parse(Doc(Table(2,2)+Table())),0),"merged reference cell rejected");
   Fail(()=>TableStylePlan.Build(xml,3),"missing source rejected");
   if(args.Length>0){
    var realXml=ComposeXml.Parse(File.ReadAllText(args[0],Encoding.UTF8));var real=TableStylePlan.Build(realXml,0);Assert(real.Rows==5&&real.Columns==3&&real.Changes.Length==30,"real HML three-table plan keeps every cell");
    var mergedReal=ComposeXml.Parse(realXml.Document.OuterXml);var mergedTable=(XmlElement)mergedReal.Document.GetElementsByTagName("TABLE")[2];var mergedRows=mergedTable.ChildNodes.Cast<XmlNode>().OfType<XmlElement>().Where(x=>x.LocalName=="ROW").ToArray();
    mergedTable.RemoveChild(mergedRows[4]);mergedTable.RemoveChild(mergedRows[3]);mergedTable.SetAttribute("RowCount","3");var mergedCell=At(mergedReal,2,1,1);mergedCell.SetAttribute("RowSpan","2");var overlapped=At(mergedReal,2,2,1);overlapped.ParentNode.RemoveChild(overlapped);var paras=(XmlElement)mergedCell.GetElementsByTagName("PARALIST")[0];paras.AppendChild(paras.GetElementsByTagName("P")[0].CloneNode(true));
    var mergedPlan=TableStylePlan.Build(mergedReal,0);Assert(mergedPlan.TableRows[2]==3&&mergedPlan.TableAnchors[2].Length==8&&mergedPlan.Changes.Count(c=>c.Table==2)==8,"three-row table with eight cells, two-row merge and two paragraphs is planned once per physical cell");
    Assert(mergedPlan.Changes.Single(c=>c.Table==2&&c.Row==1&&c.Column==1).SourceRow==4,"merged cell reaching the bottom uses the reference table's last-row role");
    TableStylePlan.VerifyFinalPlan(mergedReal,mergedPlan);Assert(true,"merged geometry has a coherent planned border result");
    int mergedIndex=Array.FindIndex(mergedPlan.Changes,c=>c.Table==2&&c.Row==1&&c.Column==1);var mergedApplied=ComposeXml.Parse(mergedReal.Document.OuterXml);foreach(var op in mergedPlan.Changes.Take(mergedIndex+1))Apply(mergedApplied,op);
    TableStylePlan.Verify(mergedReal,mergedApplied,mergedPlan,mergedIndex+1);Assert(true,"merged cell style verifies while retaining both paragraphs");
    var damagedMerged=ComposeXml.Parse(mergedApplied.Document.OuterXml);var mergedPs=At(damagedMerged,2,1,1).GetElementsByTagName("P");((XmlElement)((XmlElement)mergedPs[1]).GetElementsByTagName("CHAR")[0]).InnerText="tampered";
    Fail(()=>TableStylePlan.Verify(mergedReal,damagedMerged,mergedPlan,mergedIndex+1),"second paragraph text in a merged cell remains protected");
    TableStylePlan.VerifyFinalPlan(realXml,real);Assert(true,"real source borders can all be achieved after shared-edge propagation");
    var copy=ComposeXml.Parse(realXml.Document.OuterXml);var tables=copy.Document.GetElementsByTagName("TABLE").Cast<XmlElement>().ToArray();
    TableStylePlan.Verify(realXml,copy,real,0);Assert(true,"zero applied operations preserve the document");
    var swapped=ComposeXml.Parse(realXml.Document.OuterXml);TableBorderSimulation.SwapTextStyles(swapped.Document,"PARASHAPELIST","PARASHAPE","ParaShape",0,1);TableBorderSimulation.SwapTextStyles(swapped.Document,"CHARSHAPELIST","CHARSHAPE","CharShape",0,1);
    TableStylePlan.Verify(realXml,swapped,real,0);Assert(true,"unchanged text styles can be renumbered while every body and style reference is preserved");
    var changedCell=ComposeXml.Parse(realXml.Document.OuterXml);var metadataCell=(XmlElement)changedCell.Document.GetElementsByTagName("TABLE")[1].SelectSingleNode("ROW/CELL");metadataCell.SetAttribute("Dirty",metadataCell.GetAttribute("Dirty")=="true"?"false":"true");
    bool namedLocation=false;try{TableStylePlan.Verify(realXml,changedCell,real,0);}catch(InvalidOperationException e){namedLocation=e.Message.Contains("위치=BODY/")&&e.Message.Contains("속성=Dirty");}Assert(namedLocation,"unexpected structural mutation gives a location without revealing document text");
    var wrappedBinary=ComposeXml.Parse(realXml.Document.OuterXml);var binary=(XmlElement)wrappedBinary.Document.GetElementsByTagName("BINDATA")[0];binary.InnerText=Convert.ToBase64String(Convert.FromBase64String(binary.InnerText));
    TableStylePlan.Verify(realXml,wrappedBinary,real,0);Assert(true,"same real image bytes with different Base64 line wrapping are accepted");
    var changedBinary=ComposeXml.Parse(wrappedBinary.Document.OuterXml);var changedPayload=(XmlElement)changedBinary.Document.GetElementsByTagName("BINDATA")[0];var bytes=Convert.FromBase64String(changedPayload.InnerText);bytes[0]^=1;changedPayload.InnerText=Convert.ToBase64String(bytes);
    Fail(()=>TableStylePlan.Verify(realXml,changedBinary,real,0),"different decoded image bytes are rejected");
    var changedSize=ComposeXml.Parse(wrappedBinary.Document.OuterXml);var resizedPayload=(XmlElement)changedSize.Document.GetElementsByTagName("BINDATA")[0];resizedPayload.SetAttribute("Size","999");
    TableStylePlan.Verify(realXml,changedSize,real,0);Assert(true,"only Size difference is allowed for identical decoded image bytes");
    changedPayload.SetAttribute("Size","999");Fail(()=>TableStylePlan.Verify(realXml,changedBinary,real,0),"changed bytes are rejected even when Size also changed");
    changedBinary=ComposeXml.Parse(wrappedBinary.Document.OuterXml);changedPayload=(XmlElement)changedBinary.Document.GetElementsByTagName("BINDATA")[0];changedPayload.SetAttribute("Size","invalid");
    Fail(()=>TableStylePlan.Verify(realXml,changedBinary,real,0),"invalid image Size metadata is rejected");
    changedBinary=ComposeXml.Parse(wrappedBinary.Document.OuterXml);changedPayload=(XmlElement)changedBinary.Document.GetElementsByTagName("BINDATA")[0];changedPayload.RemoveAttribute("Size");
    Fail(()=>TableStylePlan.Verify(realXml,changedBinary,real,0),"missing image Size metadata is rejected");
    changedBinary=ComposeXml.Parse(wrappedBinary.Document.OuterXml);changedPayload=(XmlElement)changedBinary.Document.GetElementsByTagName("BINDATA")[0];changedPayload.SetAttribute("Compress","false");
    Fail(()=>TableStylePlan.Verify(realXml,changedBinary,real,0),"changed image compression metadata is rejected");
    var changedDefinition=ComposeXml.Parse(realXml.Document.OuterXml);var changedBorder=(XmlElement)changedDefinition.Document.GetElementsByTagName("BORDERFILL")[0];changedBorder.SetAttribute("ThreeD",changedBorder.GetAttribute("ThreeD")=="true"?"false":"true");
    bool namedRecord=false;try{TableStylePlan.Verify(realXml,changedDefinition,real,0);}catch(InvalidOperationException e){namedRecord=e.Message.Contains("종류=BORDERFILL")&&e.Message.Contains("ID=");}Assert(namedRecord,"real changed style definition is rejected and identified without exposing contents");
    var first=ComposeXml.Parse(realXml.Document.OuterXml);Apply(first,real.Changes[0]);
    TableStylePlan.Verify(realXml,first,real,1);Assert(true,"first apply permits exactly the saved right and bottom reciprocal border propagation");
    var sourceAlias=ComposeXml.Parse(first.Document.OuterXml);ForkBorder(sourceAlias,0,0,0);
    TableStylePlan.Verify(realXml,sourceAlias,real,1);Assert(true,"unchanged source cell accepts a different border ID only if its complete definition is identical");
    var changedSourceAlias=ComposeXml.Parse(sourceAlias.Document.OuterXml);ForkBorder(changedSourceAlias,0,0,0).SelectSingleNode("RIGHTBORDER").Attributes["Width"].Value="0.5mm";
    Fail(()=>TableStylePlan.Verify(realXml,changedSourceAlias,real,1),"source cell border with changed actual edge is rejected");
    var noPropagation=ComposeXml.Parse(realXml.Document.OuterXml);Apply(noPropagation,real.Changes[0],false);
    TableStylePlan.Verify(realXml,noPropagation,real,1);Assert(true,"pending neighbour shared edge can settle later");
    Fail(()=>TableStylePlan.Verify(realXml,noPropagation,real,real.Changes.Length),"deferred edge cannot yield false final success");
    var badNeighbor=ComposeXml.Parse(first.Document.OuterXml);ForkBorder(badNeighbor,1,0,1).SelectSingleNode("FILLBRUSH/WINDOWBRUSH").Attributes["FaceColor"].Value="0";
    Fail(()=>TableStylePlan.Verify(realXml,badNeighbor,real,1),"adjacent cell fill changes remain forbidden");
    badNeighbor=ComposeXml.Parse(first.Document.OuterXml);ForkBorder(badNeighbor,1,0,1).SelectSingleNode("LEFTBORDER").Attributes["Width"].Value="0.5mm";
    TableStylePlan.Verify(realXml,badNeighbor,real,1);Assert(true,"only the shared edge of a pending neighbour may be deferred");
    Fail(()=>TableStylePlan.Verify(realXml,badNeighbor,real,real.Changes.Length),"pending edge is strict in final verification");
    badNeighbor=ComposeXml.Parse(first.Document.OuterXml);ForkBorder(badNeighbor,1,0,1).SelectSingleNode("RIGHTBORDER").Attributes["Width"].Value="0.5mm";
    bool detailed=false;try{TableStylePlan.Verify(realXml,badNeighbor,real,1);}catch(InvalidOperationException e){detailed=e.Message.Contains("오른쪽 선 Width")&&e.Message.Contains("실제=0.5mm");}Assert(detailed,"unrelated changed border is reported without cell text");
    Fail(()=>TableStylePlan.Verify(realXml,badNeighbor,real,1),"untouched edge of adjacent cell remains protected");
    badNeighbor=ComposeXml.Parse(first.Document.OuterXml);ForkBorder(badNeighbor,1,2,2).SelectSingleNode("LEFTBORDER").Attributes["Width"].Value="0.5mm";
    Fail(()=>TableStylePlan.Verify(realXml,badNeighbor,real,1),"non-adjacent cell border changes remain forbidden");
    badNeighbor=ComposeXml.Parse(first.Document.OuterXml);ForkBorder(badNeighbor,0,0,1).SelectSingleNode("LEFTBORDER").Attributes["Width"].Value="0.5mm";
    Fail(()=>TableStylePlan.Verify(realXml,badNeighbor,real,1),"reference table mutation remains forbidden");
    badNeighbor=ComposeXml.Parse(first.Document.OuterXml);At(badNeighbor,1,0,1).GetElementsByTagName("CHAR")[0].InnerText="tampered";
    Fail(()=>TableStylePlan.Verify(realXml,badNeighbor,real,1),"adjacent text changes remain forbidden");
    var inconsistent=ComposeXml.Parse(realXml.Document.OuterXml);ForkBorder(inconsistent,0,0,1).SelectSingleNode("LEFTBORDER").Attributes["Width"].Value="0.5mm";
    var temporalPlan=TableStylePlan.Build(inconsistent,0);var temporal=ComposeXml.Parse(inconsistent.Document.OuterXml);Apply(temporal,temporalPlan.Changes[0]);Apply(temporal,temporalPlan.Changes[1]);
    TableStylePlan.Verify(inconsistent,temporal,temporalPlan,2);Assert(true,"later adjacent write determines shared edge of an already-applied cell");
    Fail(()=>TableStylePlan.VerifyFinalPlan(inconsistent,temporalPlan),"conflicting reference shared edges are rejected before native writes instead of claiming exact source styles");
    var skipped=ComposeXml.Parse(inconsistent.Document.OuterXml);var initial=temporalPlan.Changes[0];var skippedCell=At(skipped,initial.Table,initial.Row,initial.Column);
    skippedCell.SetAttribute("BorderFill",initial.Border);var skippedP=(XmlElement)skippedCell.GetElementsByTagName("P")[0];skippedP.SetAttribute("ParaShape",initial.Paragraph);((XmlElement)skippedP.GetElementsByTagName("TEXT")[0]).SetAttribute("CharShape",initial.Character);
    var skippedPlan=TableStylePlan.Build(skipped,0);Assert(!skippedPlan.Changes.Any(x=>x.Table==1&&x.Row==0&&x.Column==0),"matching first target is skipped in preflight scenario");
    Fail(()=>TableStylePlan.VerifyFinalPlan(skipped,skippedPlan),"preflight catches later neighbor changing a skipped matching cell");
    var retry=TableStylePlan.Build(first,0);var retried=ComposeXml.Parse(first.Document.OuterXml);foreach(var op in retry.Changes)Apply(retried,op);
    TableStylePlan.Verify(first,retried,retry);Assert(true,"retry from the saved partial state completes remaining cells with shared edges");
    var compacted=ComposeXml.Parse(realXml.Document.OuterXml);foreach(var op in real.Changes.Take(17))Apply(compacted,op);CompactUnusedBorder(compacted,3);
    TableStylePlan.Verify(realXml,compacted,real,17);Assert(true,"after third table's second cell an unreferenced old border can be compacted and all remaining IDs remapped");
    var badCompact=ComposeXml.Parse(compacted.Document.OuterXml);((XmlElement)badCompact.Document.GetElementsByTagName("TABLE")[0].SelectSingleNode("ROW/CELL")).SetAttribute("BorderFill","3");
    Fail(()=>TableStylePlan.Verify(realXml,badCompact,real,17),"renumbering cannot hide an actual source table style change");
    badCompact=ComposeXml.Parse(compacted.Document.OuterXml);((XmlElement)badCompact.Document.GetElementsByTagName("BORDERFILLLIST")[0]).SetAttribute("Count","0");
    Fail(()=>TableStylePlan.Verify(realXml,badCompact,real,17),"renumbering cannot hide inconsistent border-list metadata");
    var charCompacted=ComposeXml.Parse(realXml.Document.OuterXml);foreach(var op in real.Changes.Take(18))Apply(charCompacted,op);CompactUnusedBorder(charCompacted,3);TableBorderSimulation.CompactTextStyle(charCompacted.Document,"CHARSHAPELIST","CHARSHAPE","CharShape",9);
    TableStylePlan.Verify(realXml,charCompacted,real,18);Assert(true,"unused header character style can compact after third table first row without changing source style");
    var badChar=ComposeXml.Parse(charCompacted.Document.OuterXml);((XmlElement)At(badChar,0,0,0).GetElementsByTagName("TEXT")[0]).SetAttribute("CharShape","0");
    Fail(()=>TableStylePlan.Verify(realXml,badChar,real,18),"character renumbering cannot hide a changed source font");
    int done=0;
    foreach(var op in real.Changes){Apply(copy,op);
     done++;TableStylePlan.Verify(realXml,copy,real,done);if(done==1){Assert(true,"partial result is checked against one planned cell and its exact reciprocal edges");Assert(TableStylePlan.Build(copy,0).Changes.Length==real.Changes.Length-1,"retry skips an already formatted cell");}
    }
    TableStylePlan.Verify(realXml,copy,real);Assert(true,"all target styles match and unrelated real-HML content is preserved");
    Assert(TableStylePlan.Build(copy,0).Changes.Length==0,"repeating an already-completed format request does not rewrite any cells");
    var tampered=ComposeXml.Parse(copy.Document.OuterXml);var target=(XmlElement)tampered.Document.GetElementsByTagName("TABLE")[1];((XmlElement)target.GetElementsByTagName("CHAR")[0]).InnerText="tampered";
    Fail(()=>TableStylePlan.Verify(realXml,tampered,real),"changed target cell content is rejected");
    tampered=ComposeXml.Parse(copy.Document.OuterXml);target=(XmlElement)tampered.Document.GetElementsByTagName("TABLE")[1];((XmlElement)target.GetElementsByTagName("CELL")[0]).SetAttribute("BorderFill","2");
    Fail(()=>TableStylePlan.Verify(realXml,tampered,real),"wrong target border is rejected");
   }
   Console.WriteLine("PASS "+n+" offline table-style planning checks; no HWP document modified.");
  }catch(Exception e){Console.Error.WriteLine(e.GetType().Name+": "+e.Message);Environment.ExitCode=1;}
 }
}
