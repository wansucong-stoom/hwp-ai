using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;

namespace HAddon {
 // Pure planning/verification only: no cursor movement or native writes.
 // The executable backend must pin the live table and recheck the HWPML hash.
 internal sealed class TableStyleCell {
  public int Table,Row,Column,SourceRow,SourceColumn,RowSpan,ColSpan; public string Border,Paragraph,Character;public string[] ParagraphStyles,CharacterStyles;
  // Optional independently sourced border sides in left/right/top/bottom order.
  public int[] EdgeRows,EdgeColumns;
 }
 internal sealed class TableStylePlan {
  public int SourceIndex;public int Rows,Columns;public int[] TableRows,TableColumns;public int[][] TableAnchors;public TableStyleCell[] Changes;
  static XmlElement[] Children(XmlNode n){return n.ChildNodes.Cast<XmlNode>().OfType<XmlElement>().ToArray();}
  static int Number(XmlElement e,string name,int max){int result;if(!Int32.TryParse(e.GetAttribute(name),NumberStyles.None,CultureInfo.InvariantCulture,out result)||result<0||result>max)throw new InvalidOperationException("표의 행·열 정보를 확인하지 못했습니다.");return result;}
  static XmlElement Single(XmlNode n,string name){var list=Children(n).Where(x=>x.LocalName==name).ToArray();if(list.Length!=1)throw new InvalidOperationException("표 셀의 "+name+" 구조를 확인하지 못했습니다.");return list[0];}
  static string Ref(XmlElement e,string name){string value=e.GetAttribute(name);int n;if(!Int32.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out n)||n<0)throw new InvalidOperationException("표 서식 참조를 확인하지 못했습니다.");return value;}
  static XmlElement[] Tables(ComposeXml xml){return xml.Paragraphs.SelectMany(p=>Children(p).Where(x=>x.LocalName=="TEXT").SelectMany(Children).Where(x=>x.LocalName=="TABLE")).ToArray();}
  static TableStyleCell[][] Read(XmlElement table,int index){
   int rows=Number(table,"RowCount",100),columns=Number(table,"ColCount",30);if(rows<1||columns<1||rows*columns>500)throw new InvalidOperationException("표가 지원 크기를 벗어났습니다.");
   var elements=Children(table).Where(x=>x.LocalName=="ROW").ToArray();if(elements.Length!=rows)throw new InvalidOperationException("표 행 구성이 예상과 다릅니다.");
   var cells=new TableStyleCell[rows][];for(int row=0;row<rows;row++)cells[row]=new TableStyleCell[columns];
   foreach(var row in elements)foreach(var cell in Children(row)){
    if(cell.LocalName!="CELL")throw new InvalidOperationException("지원하지 않는 표 행 구조입니다.");
    int r=Number(cell,"RowAddr",rows-1),c=Number(cell,"ColAddr",columns-1);
    int rs=cell.HasAttribute("RowSpan")?Number(cell,"RowSpan",100):1,cs=cell.HasAttribute("ColSpan")?Number(cell,"ColSpan",30):1;
    if(rs<1||cs<1||r+rs>rows||c+cs>columns)throw new InvalidOperationException("표 셀 병합 범위가 올바르지 않습니다.");
    var paragraphs=Children(Single(cell,"PARALIST"));if(paragraphs.Length<1||paragraphs.Any(p=>p.LocalName!="P"))throw new InvalidOperationException("표 셀 문단을 확인하지 못했습니다.");
    var texts=paragraphs.SelectMany(Children).ToArray();if(texts.Length<1||texts.Any(t=>t.LocalName!="TEXT")||paragraphs.Any(p=>Children(p).Length<1)||texts.Any(t=>Children(t).Any(x=>x.LocalName!="CHAR"||Children(x).Length>0)))throw new InvalidOperationException("표 셀의 그림·필드·중첩 표는 지원하지 않습니다.");
    var ps=paragraphs.Select(p=>Ref(p,"ParaShape")).ToArray();var chars=texts.Select(t=>Ref(t,"CharShape")).ToArray();
    var item=new TableStyleCell{Table=index,Row=r,Column=c,RowSpan=rs,ColSpan=cs,Border=Ref(cell,"BorderFill"),Paragraph=ps[0],Character=chars[0],ParagraphStyles=ps,CharacterStyles=chars};
    for(int rr=r;rr<r+rs;rr++)for(int cc=c;cc<c+cs;cc++){if(cells[rr][cc]!=null)throw new InvalidOperationException("병합 셀이 서로 겹칩니다.");cells[rr][cc]=item;}
   }
   if(cells.Any(row=>row.Any(x=>x==null)))throw new InvalidOperationException("표의 모든 셀을 확인하지 못했습니다.");return cells;
  }
  static int SourceRow(int r,int targetRows,int sourceRows){return r==0?0:sourceRows==1?0:r==targetRows-1?sourceRows-1:1+(r-1)%Math.Max(1,sourceRows-1);}
  static int SourceColumn(int c,int targetColumns,int sourceColumns){return c==0?0:sourceColumns==1?0:c==targetColumns-1?sourceColumns-1:Math.Min(c,sourceColumns-2);}
  static int TargetSourceRow(int r,int span,int targetRows,int sourceRows){return SourceRow(r>0&&r+span==targetRows?targetRows-1:r,targetRows,sourceRows);}
  static int TargetSourceColumn(int c,int span,int targetColumns,int sourceColumns){return SourceColumn(c>0&&c+span==targetColumns?targetColumns-1:c,targetColumns,sourceColumns);}
  internal static TableStylePlan Build(ComposeXml xml,int sourceIndex){
   if(xml==null)throw new ArgumentNullException("xml");
   var tables=Tables(xml);
   if(tables.Length<2||tables.Length>32||sourceIndex<0||sourceIndex>=tables.Length)throw new InvalidOperationException("기준 표와 나머지 본문 표를 확인하지 못했습니다.");
   var grids=tables.Select((t,i)=>Read(t,i)).ToArray();var source=grids[sourceIndex];int rows=source.Length,cols=source[0].Length;
   if(grids.Any(g=>(g[0].Length==1)!=(cols==1)))throw new InvalidOperationException("한 열짜리 표와 여러 열짜리 표는 양쪽 테두리 보존을 확인할 수 없어 아직 함께 적용하지 않습니다.");
   if(source.SelectMany(x=>x).Distinct().Any(x=>x.ParagraphStyles.Any(v=>v!=x.Paragraph)||x.CharacterStyles.Any(v=>v!=x.Character)))throw new InvalidOperationException("기준 표의 한 셀 안에 여러 글자·문단 서식이 있어 자동 복사할 수 없습니다.");
   var changes=new List<TableStyleCell>();for(int i=0;i<grids.Length;i++)if(i!=sourceIndex)for(int r=0;r<grids[i].Length;r++)for(int c=0;c<grids[i][0].Length;c++){
    int tr=grids[i].Length,tc=grids[i][0].Length;var existing=grids[i][r][c];if(existing.Row!=r||existing.Column!=c)continue;
    if(r==0&&existing.RowSpan>1&&rows>1)throw new InvalidOperationException("표 "+(i+1)+"의 "+(r+1)+"행 "+(c+1)+"열이 머리행과 본문에 걸쳐 있어 기준 역할을 안전하게 구분할 수 없습니다.");
    if(c==0&&existing.ColSpan==tc&&cols>1)throw new InvalidOperationException("표 "+(i+1)+"의 "+(r+1)+"행 "+(c+1)+"열이 양쪽 가장자리를 모두 포함해 테두리를 안전하게 복사할 수 없습니다.");
    int sr=TargetSourceRow(r,existing.RowSpan,tr,rows),sc=TargetSourceColumn(c,existing.ColSpan,tc,cols);var s=source[sr][sc];
    if(s.Border==existing.Border&&existing.ParagraphStyles.All(v=>v==s.Paragraph)&&existing.CharacterStyles.All(v=>v==s.Character))continue;
    changes.Add(new TableStyleCell{Table=i,Row=r,Column=c,RowSpan=existing.RowSpan,ColSpan=existing.ColSpan,SourceRow=s.Row,SourceColumn=s.Column,Border=s.Border,Paragraph=s.Paragraph,Character=s.Character});
   }
   return new TableStylePlan{SourceIndex=sourceIndex,Rows=rows,Columns=cols,TableRows=grids.Select(g=>g.Length).ToArray(),TableColumns=grids.Select(g=>g[0].Length).ToArray(),TableAnchors=grids.Select(g=>g.SelectMany(x=>x).Distinct().Select(x=>x.Row*g[0].Length+x.Column).OrderBy(x=>x).ToArray()).ToArray(),Changes=changes.ToArray()};
  }
  internal static TableStylePlan BuildFromDesign(ComposeXml xml,int sourceIndex,TableDesignChange[] design){
   if(xml==null||design==null)throw new ArgumentNullException("design");var tables=Tables(xml);
   if(sourceIndex<0||sourceIndex>=tables.Length)throw new InvalidOperationException("기준 표를 확인하지 못했습니다.");
   var grids=tables.Select((t,i)=>Read(t,i)).ToArray();var source=grids[sourceIndex];var changes=new List<TableStyleCell>();var seen=new HashSet<string>();
   foreach(var d in design){
    if(d==null||d.Target==null||d.Fill==null||d.Paragraph==null||d.Character==null)throw new InvalidOperationException("표 디자인 계획을 확인하지 못했습니다.");
    var p=d.Target;if(p.Table<0||p.Table>=grids.Length||p.Table==sourceIndex||p.Row<0||p.Row>=grids[p.Table].Length||p.Column<0||p.Column>=grids[p.Table][0].Length)throw new InvalidOperationException("대상 셀 위치가 올바르지 않습니다.");
    var target=grids[p.Table][p.Row][p.Column];if(target.Row!=p.Row||target.Column!=p.Column||!seen.Add(CellKey(p.Table,p.Row,p.Column)))throw new InvalidOperationException("대상 셀 위치가 중복되거나 병합 셀의 시작이 아닙니다.");
    var refs=new[]{d.Fill,d.Paragraph,d.Character,d.Left,d.Right,d.Top,d.Bottom};
    foreach(var s in refs)if(s==null||s.Table!=sourceIndex||s.Row<0||s.Row>=source.Length||s.Column<0||s.Column>=source[0].Length||source[s.Row][s.Column].Row!=s.Row||source[s.Row][s.Column].Column!=s.Column)throw new InvalidOperationException("기준 셀 위치가 올바르지 않습니다.");
    if(d.Fill.Row!=d.Paragraph.Row||d.Fill.Column!=d.Paragraph.Column||d.Fill.Row!=d.Character.Row||d.Fill.Column!=d.Character.Column)throw new InvalidOperationException("글자·문단·배경의 기준 셀이 서로 달라 현재 한글 명령으로 함께 적용할 수 없습니다.");
    var selected=source[d.Fill.Row][d.Fill.Column];if(selected.ParagraphStyles.Any(v=>v!=selected.Paragraph)||selected.CharacterStyles.Any(v=>v!=selected.Character))throw new InvalidOperationException("기준 셀에 서로 다른 글자·문단 서식이 있어 적용 결과를 확인할 수 없습니다.");
    changes.Add(new TableStyleCell{Table=p.Table,Row=p.Row,Column=p.Column,RowSpan=target.RowSpan,ColSpan=target.ColSpan,SourceRow=selected.Row,SourceColumn=selected.Column,Border=selected.Border,Paragraph=selected.Paragraph,Character=selected.Character,EdgeRows=refs.Skip(3).Select(x=>x.Row).ToArray(),EdgeColumns=refs.Skip(3).Select(x=>x.Column).ToArray()});
   }
   int expected=grids.Select((g,i)=>i==sourceIndex?0:g.SelectMany(x=>x).Distinct().Count()).Sum();if(changes.Count!=expected)throw new InvalidOperationException("일부 대상 셀의 디자인 계획이 없습니다.");
   // Model response order is not part of the design. Traverse physical cells
   // in stable document order so validation and progress are reproducible.
   return new TableStylePlan{SourceIndex=sourceIndex,Rows=source.Length,Columns=source[0].Length,TableRows=grids.Select(g=>g.Length).ToArray(),TableColumns=grids.Select(g=>g[0].Length).ToArray(),TableAnchors=grids.Select(g=>g.SelectMany(x=>x).Distinct().Select(x=>x.Row*g[0].Length+x.Column).OrderBy(x=>x).ToArray()).ToArray(),Changes=changes.OrderBy(x=>x.Table).ThenBy(x=>x.Row).ThenBy(x=>x.Column).ToArray()};
  }
  static XmlElement DefinitionElement(XmlElement mapping,string list,string type,string id){
   var matches=Children(Single(mapping,list)).Where(x=>x.LocalName==type&&x.GetAttribute("Id")==id).ToArray();
   if(matches.Length!=1)throw new InvalidOperationException("표 서식 정의를 확인하지 못했습니다.");
   var copy=(XmlElement)matches[0].CloneNode(true);copy.RemoveAttribute("Id");return copy;
  }
  static string Definition(XmlElement mapping,string list,string type,string id){return ComposeXml.Canonical(DefinitionElement(mapping,list,type,id));}
  static string BorderKey(XmlElement border){var copy=(XmlElement)border.CloneNode(true);copy.RemoveAttribute("Id");return ComposeXml.Canonical(copy);}
  // Match character and paragraph definitions by their entire content, and
  // remap all known references on a private comparison clone. A removed
  // definition must have been referenced only by directly edited cell text
  // or paragraphs; other consumers remain protected.
  static ComposeXml NormalizeTextStyleIds(ComposeXml before,ComposeXml after,TableStylePlan plan,int count,string listName,string elementName,string reference){
   var left=Children(before.Mapping).Where(x=>x.LocalName==listName).ToArray();var right=Children(after.Mapping).Where(x=>x.LocalName==listName).ToArray();
   if(left.Length==0&&right.Length==0)return after;if(left.Length!=1||right.Length!=1)throw new InvalidOperationException("기존 글자·문단 서식 목록을 확인하지 못했습니다. [종류="+elementName+"]");
   var old=Children(left[0]);var fresh=Children(right[0]);int declared,n,max=0;
   if(old.Any(x=>x.LocalName!=elementName)||fresh.Any(x=>x.LocalName!=elementName)||!Int32.TryParse(left[0].GetAttribute("Count"),NumberStyles.None,CultureInfo.InvariantCulture,out declared)||declared!=old.Length||!Int32.TryParse(right[0].GetAttribute("Count"),NumberStyles.None,CultureInfo.InvariantCulture,out declared)||declared!=fresh.Length)throw new InvalidOperationException("글자·문단 서식 목록 구조를 확인하지 못했습니다. [종류="+elementName+"]");
   var oldIds=new HashSet<string>();var newIds=new HashSet<string>();foreach(var e in old){string id=e.GetAttribute("Id");if(!Int32.TryParse(id,NumberStyles.None,CultureInfo.InvariantCulture,out n)||n<0||!oldIds.Add(id))throw new InvalidOperationException("기존 서식 번호를 확인하지 못했습니다.");max=Math.Max(max,n);}
   foreach(var e in fresh){string id=e.GetAttribute("Id");if(!Int32.TryParse(id,NumberStyles.None,CultureInfo.InvariantCulture,out n)||n<0||!newIds.Add(id))throw new InvalidOperationException("변경된 서식 번호를 확인하지 못했습니다.");}
   if(old.All(o=>fresh.Any(f=>f.GetAttribute("Id")==o.GetAttribute("Id")&&BorderKey(f)==BorderKey(o))))return after;
   var clone=ComposeXml.Parse(after.Document.OuterXml);var list=Single(clone.Mapping,listName);var entries=Children(list);var remap=new Dictionary<string,string>();var used=new HashSet<string>();
   foreach(var e in entries){string id=e.GetAttribute("Id");var same=old.FirstOrDefault(x=>x.GetAttribute("Id")==id&&BorderKey(x)==BorderKey(e));if(same!=null){remap[id]=id;used.Add(id);}}
   foreach(var e in entries){string id=e.GetAttribute("Id");if(remap.ContainsKey(id))continue;string key=BorderKey(e);var match=old.FirstOrDefault(x=>!used.Contains(x.GetAttribute("Id"))&&BorderKey(x)==key);
    if(match!=null){string target=match.GetAttribute("Id");remap[id]=target;used.Add(target);}else{if(max==Int32.MaxValue)throw new InvalidOperationException("서식 번호가 너무 큽니다.");remap[id]=(++max).ToString(CultureInfo.InvariantCulture);}}
   var missing=old.Where(x=>!used.Contains(x.GetAttribute("Id"))).ToArray();
   if(missing.Length>0){
    var tables=Tables(before);var grids=tables.Select((t,i)=>Cells(t,plan.TableRows[i],plan.TableColumns[i])).ToArray();var allowed=new HashSet<XmlElement>();
    foreach(var change in plan.Changes.Take(count))foreach(var p in Children(Single(grids[change.Table][change.Row,change.Column],"PARALIST"))){if(reference=="CharShape")foreach(var text in Children(p))allowed.Add(text);else allowed.Add(p);}
    var missingIds=new HashSet<string>(missing.Select(x=>x.GetAttribute("Id")));
    foreach(XmlElement e in before.Document.SelectNodes("//*"))foreach(XmlAttribute a in e.Attributes)if(a.LocalName==reference&&missingIds.Contains(a.Value)&&!allowed.Contains(e))throw new InvalidOperationException("사용 중인 기존 서식이 변경되거나 사라졌습니다. [종류="+elementName+"; ID="+a.Value+"]");
   }
   foreach(XmlElement e in clone.Document.SelectNodes("//*"))foreach(XmlAttribute a in e.Attributes){
    if(a.LocalName==reference){string mapped;if(!remap.TryGetValue(a.Value,out mapped))throw new InvalidOperationException("글자·문단 서식 참조를 확인하지 못했습니다. [종류="+elementName+"]");a.Value=mapped;}
    else if(a.LocalName.IndexOf(reference,StringComparison.OrdinalIgnoreCase)>=0&&Int32.TryParse(a.Value,NumberStyles.None,CultureInfo.InvariantCulture,out n))throw new InvalidOperationException("지원하지 않는 글자·문단 서식 참조가 있습니다.");
   }
   foreach(var e in entries)e.SetAttribute("Id",remap[e.GetAttribute("Id")]);foreach(var e in missing)list.AppendChild(clone.Document.ImportNode(e,true));list.SetAttribute("Count",Children(list).Length.ToString(CultureInfo.InvariantCulture));return clone;
  }
  // HWP may drop no-longer-used border definitions and renumber the list.
  // Normalize a private readback clone by full definition value. An old
  // definition can be absent only when every previous reference was in a
  // cell affected by this exact applied prefix. Never mutate live document.
  static ComposeXml NormalizeBorderIds(ComposeXml before,ComposeXml after,TableStylePlan plan,int count){
   var leftLists=Children(before.Mapping).Where(x=>x.LocalName=="BORDERFILLLIST").ToArray();var rightLists=Children(after.Mapping).Where(x=>x.LocalName=="BORDERFILLLIST").ToArray();
   if(leftLists.Length==0&&rightLists.Length==0)return after;
   if(leftLists.Length!=1||rightLists.Length!=1)throw new InvalidOperationException("테두리 서식 목록을 확인하지 못했습니다.");
   var oldList=leftLists[0];var afterList=rightLists[0];
   var old=Children(oldList);var fresh=Children(afterList);if(old.Any(x=>x.LocalName!="BORDERFILL")||fresh.Any(x=>x.LocalName!="BORDERFILL"))throw new InvalidOperationException("테두리 서식 목록 구조를 확인하지 못했습니다.");
   int declared;if(!oldList.HasAttribute("Count")||!afterList.HasAttribute("Count")||!Int32.TryParse(oldList.GetAttribute("Count"),NumberStyles.None,CultureInfo.InvariantCulture,out declared)||declared!=old.Length||!Int32.TryParse(afterList.GetAttribute("Count"),NumberStyles.None,CultureInfo.InvariantCulture,out declared)||declared!=fresh.Length)throw new InvalidOperationException("테두리 서식 개수를 확인하지 못했습니다.");
   var oldIds=new HashSet<string>();var newIds=new HashSet<string>();int max=0,n;
   foreach(var e in old){string id=e.GetAttribute("Id");if(!Int32.TryParse(id,NumberStyles.None,CultureInfo.InvariantCulture,out n)||n<0||!oldIds.Add(id))throw new InvalidOperationException("기존 테두리 서식 번호가 올바르지 않습니다.");max=Math.Max(max,n);}
   foreach(var e in fresh){string id=e.GetAttribute("Id");if(!Int32.TryParse(id,NumberStyles.None,CultureInfo.InvariantCulture,out n)||n<0||!newIds.Add(id))throw new InvalidOperationException("변경된 테두리 서식 번호가 올바르지 않습니다.");}
   if(old.All(o=>fresh.Any(f=>f.GetAttribute("Id")==o.GetAttribute("Id")&&BorderKey(f)==BorderKey(o))))return after;
   var clone=ComposeXml.Parse(after.Document.OuterXml);var list=Single(clone.Mapping,"BORDERFILLLIST");var entries=Children(list);var remap=new Dictionary<string,string>();var used=new HashSet<string>();
   // Prefer identical same-number records, then match only exact definitions.
   foreach(var e in entries){string id=e.GetAttribute("Id");var same=old.FirstOrDefault(x=>x.GetAttribute("Id")==id&&BorderKey(x)==BorderKey(e));if(same!=null){remap[id]=id;used.Add(id);}}
   foreach(var e in entries){string id=e.GetAttribute("Id");if(remap.ContainsKey(id))continue;string key=BorderKey(e);var match=old.FirstOrDefault(x=>!used.Contains(x.GetAttribute("Id"))&&BorderKey(x)==key);
    if(match!=null){string target=match.GetAttribute("Id");remap[id]=target;used.Add(target);}else{if(max==Int32.MaxValue)throw new InvalidOperationException("테두리 서식 번호가 너무 큽니다.");remap[id]=(++max).ToString(CultureInfo.InvariantCulture);}}
   var missing=old.Where(x=>!used.Contains(x.GetAttribute("Id"))).ToArray();
   if(missing.Length>0){
    var beforeTables=Tables(before);var beforeGrids=beforeTables.Select((t,i)=>Cells(t,plan.TableRows[i],plan.TableColumns[i])).ToArray();var source=Read(beforeTables[plan.SourceIndex],plan.SourceIndex);
    var affected=new HashSet<XmlElement>(ExpectedBorders(before,plan,source,beforeGrids,count).Values.Select(x=>beforeGrids[x.Table][x.Row,x.Column]));
    var missingIds=new HashSet<string>(missing.Select(x=>x.GetAttribute("Id")));
    foreach(XmlElement e in before.Document.SelectNodes("//*"))foreach(XmlAttribute a in e.Attributes){
     if((a.LocalName=="BorderFill"||a.LocalName=="BorderFillId")&&missingIds.Contains(a.Value)&&!(e.LocalName=="CELL"&&affected.Contains(e)))
      throw new InvalidOperationException("사용 중인 기존 테두리 서식이 변경되거나 사라졌습니다. [종류=BORDERFILL; ID="+a.Value+"]");
    }
   }
   // All recognised border references are remapped consistently, including
   // table, paragraph and character references; unknown numeric border-ref
   // names fail closed rather than guessing their semantics.
   foreach(XmlElement e in clone.Document.SelectNodes("//*"))foreach(XmlAttribute a in e.Attributes){
    if(a.LocalName=="BorderFill"||a.LocalName=="BorderFillId"){
     string mapped;if(remap.TryGetValue(a.Value,out mapped))a.Value=mapped;else if(a.Value!="0")throw new InvalidOperationException("테두리 서식 참조를 확인하지 못했습니다.");
    }else if(a.LocalName.IndexOf("BorderFill",StringComparison.OrdinalIgnoreCase)>=0&&Int32.TryParse(a.Value,NumberStyles.None,CultureInfo.InvariantCulture,out n))throw new InvalidOperationException("지원하지 않는 테두리 서식 참조가 있습니다.");
   }
   foreach(var e in entries)e.SetAttribute("Id",remap[e.GetAttribute("Id")]);
   // Add vanished definitions only to this comparison clone; they are never
   // restored to the user document. The reference guard above limits this.
   foreach(var e in missing)list.AppendChild(clone.Document.ImportNode(e,true));
   list.SetAttribute("Count",Children(list).Length.ToString(CultureInfo.InvariantCulture));
   return clone;
  }
  static string CellKey(int table,int row,int col){return table+":"+row+":"+col;}
  sealed class ExpectedBorder {internal int Table,Row,Column;internal XmlElement Definition;}
  static XmlElement PlannedBorder(ComposeXml original,TableStyleCell change,TableStyleCell[][] source){
   var result=DefinitionElement(original.Mapping,"BORDERFILLLIST","BORDERFILL",source[change.SourceRow][change.SourceColumn].Border);
   if(change.EdgeRows==null&&change.EdgeColumns==null)return result;
   if(change.EdgeRows==null||change.EdgeColumns==null||change.EdgeRows.Length!=4||change.EdgeColumns.Length!=4)throw new InvalidOperationException("표 테두리 계획이 올바르지 않습니다.");
   string[] sides={"LEFTBORDER","RIGHTBORDER","TOPBORDER","BOTTOMBORDER"};
   for(int i=0;i<4;i++){
    var desired=DefinitionElement(original.Mapping,"BORDERFILLLIST","BORDERFILL",source[change.EdgeRows[i]][change.EdgeColumns[i]].Border);var edge=Single(desired,sides[i]);var old=Single(result,sides[i]);
    var replacement=result.OwnerDocument.CreateElement(old.Prefix,old.LocalName,old.NamespaceURI);
    foreach(XmlAttribute attr in edge.Attributes)replacement.Attributes.Append((XmlAttribute)replacement.OwnerDocument.ImportNode(attr,true));foreach(XmlNode child in edge.ChildNodes)replacement.AppendChild(replacement.OwnerDocument.ImportNode(child,true));result.ReplaceChild(replacement,old);
   }
   return result;
  }
  // A native cell border is a shared edge. ShapeCopyPaste also updates only
  // the opposing edge of the adjacent cell. Replay the confirmed operation
  // prefix, so later adjacent writes can supersede earlier shared edges.
  // No fill, diagonal, other edge, or unrelated cell is normalized away.
  static Dictionary<string,ExpectedBorder> ExpectedBorders(ComposeXml before,TableStylePlan plan,TableStyleCell[][] source,XmlElement[][,] grids,int count){
   var expected=new Dictionary<string,ExpectedBorder>();
   Func<int,int,int,ExpectedBorder> get=(t,r,c)=>{
    XmlElement anchor=grids[t][r,c];r=Number(anchor,"RowAddr",plan.TableRows[t]-1);c=Number(anchor,"ColAddr",plan.TableColumns[t]-1);string key=CellKey(t,r,c);ExpectedBorder value;
    if(!expected.TryGetValue(key,out value)){
     value=new ExpectedBorder{Table=t,Row=r,Column=c,Definition=DefinitionElement(before.Mapping,"BORDERFILLLIST","BORDERFILL",grids[t][r,c].GetAttribute("BorderFill"))};expected.Add(key,value);
    }
    return value;
   };
   string[] own={"LEFTBORDER","RIGHTBORDER","TOPBORDER","BOTTOMBORDER"},opposite={"RIGHTBORDER","LEFTBORDER","BOTTOMBORDER","TOPBORDER"};
   foreach(var change in plan.Changes.Take(count)){
    var current=get(change.Table,change.Row,change.Column);
    current.Definition=PlannedBorder(before,change,source);
    for(int side=0;side<4;side++){
     var edge=Single(current.Definition,own[side]);int length=side<2?change.RowSpan:change.ColSpan;
     for(int offset=0;offset<length;offset++){int r=side<2?change.Row+offset:side==2?change.Row-1:change.Row+change.RowSpan;int c=side<2?(side==0?change.Column-1:change.Column+change.ColSpan):change.Column+offset;
     if(r<0||r>=plan.TableRows[change.Table]||c<0||c>=plan.TableColumns[change.Table])continue;
     var neighbor=get(change.Table,r,c);var old=Single(neighbor.Definition,opposite[side]);
     var replacement=neighbor.Definition.OwnerDocument.CreateElement(old.Prefix,old.LocalName,old.NamespaceURI);
     foreach(XmlAttribute attr in edge.Attributes)replacement.Attributes.Append((XmlAttribute)replacement.OwnerDocument.ImportNode(attr,true));
     foreach(XmlNode child in edge.ChildNodes)replacement.AppendChild(replacement.OwnerDocument.ImportNode(child,true));
     neighbor.Definition.ReplaceChild(replacement,old);}
    }
   }
   return expected;
  }
  static XmlElement[,] Cells(XmlElement table,int rows,int cols){
   var result=new XmlElement[rows,cols];foreach(var row in Children(table).Where(x=>x.LocalName=="ROW"))foreach(var cell in Children(row)){int r=Number(cell,"RowAddr",rows-1),c=Number(cell,"ColAddr",cols-1),rs=cell.HasAttribute("RowSpan")?Number(cell,"RowSpan",100):1,cs=cell.HasAttribute("ColSpan")?Number(cell,"ColSpan",30):1;for(int rr=r;rr<r+rs;rr++)for(int cc=c;cc<c+cs;cc++)result[rr,cc]=cell;}return result;
  }
  // Convert every logical coordinate covered by a merged cell to its physical
  // anchor; this map is immutable and never changes the native document.
  internal static int[][,] AnchorGrids(ComposeXml xml,TableStylePlan plan){
   var tables=Tables(xml);if(tables.Length!=plan.TableRows.Length)throw new InvalidOperationException("표 위치를 확인하지 못했습니다.");
   var result=new int[tables.Length][,];for(int i=0;i<tables.Length;i++){
    int rows=plan.TableRows[i],cols=plan.TableColumns[i];var cells=Cells(tables[i],rows,cols);var map=new int[rows,cols];
    for(int r=0;r<rows;r++)for(int c=0;c<cols;c++){var cell=cells[r,c];if(cell==null)throw new InvalidOperationException("표 셀 범위를 확인하지 못했습니다.");map[r,c]=Number(cell,"RowAddr",rows-1)*cols+Number(cell,"ColAddr",cols-1);}
    result[i]=map;
   }return result;
  }
  internal static Dictionary<int,string>[] AnchorTexts(ComposeXml xml,TableStylePlan plan){
   var tables=Tables(xml);var result=new Dictionary<int,string>[tables.Length];
   for(int i=0;i<tables.Length;i++){var map=new Dictionary<int,string>();int cols=plan.TableColumns[i];
    foreach(var row in Children(tables[i]).Where(x=>x.LocalName=="ROW"))foreach(var cell in Children(row).Where(x=>x.LocalName=="CELL")){
     int r=Number(cell,"RowAddr",plan.TableRows[i]-1),c=Number(cell,"ColAddr",cols-1);var paragraphs=Children(Single(cell,"PARALIST"));
     string value=String.Join("\n",paragraphs.Select(p=>String.Concat(Children(p).SelectMany(Children).Where(x=>x.LocalName=="CHAR").Select(x=>x.InnerText))).ToArray());map.Add(r*cols+c,value);
    }result[i]=map;
   }return result;
  }
  // Report only limited border properties, never cell text or document data.
  static string BorderDifference(XmlElement expected,XmlElement actual){
   string[] names={"LEFTBORDER","RIGHTBORDER","TOPBORDER","BOTTOMBORDER"},labels={"왼쪽","오른쪽","위","아래"};
   for(int i=0;i<names.Length;i++){
    var a=Children(expected).FirstOrDefault(x=>x.LocalName==names[i]);var b=Children(actual).FirstOrDefault(x=>x.LocalName==names[i]);
    if(a==null||b==null)return labels[i]+" 선 구조";
    foreach(string key in new[]{"Type","Width","Color"})if(a.GetAttribute(key)!=b.GetAttribute(key)){
     Func<string,string> safe=s=>System.Text.RegularExpressions.Regex.IsMatch(s??"",@"^[A-Za-z0-9#., -]{0,32}$")?(String.IsNullOrEmpty(s)?"(없음)":s):"(확인 불가)";
     return labels[i]+" 선 "+key+": 예상="+safe(a.GetAttribute(key))+", 실제="+safe(b.GetAttribute(key));
    }
   }
   return "테두리·채우기 정의";
  }
  // While a target is still pending, Hancom may defer synchronization of an
  // adjacent shared edge (especially next to a merged cell). Permit differences
  // only on edges touched by already-applied cells. The complete border is
  // verified when the target is applied and again at transaction completion.
  static HashSet<string> PendingSharedEdges(TableStylePlan plan,int count,int table,int row,int col){
   var result=new HashSet<string>();int index=Array.FindIndex(plan.Changes,x=>x.Table==table&&x.Row==row&&x.Column==col);
   if(index<0||index<count)return result;
   var pending=plan.Changes[index];
   foreach(var done in plan.Changes.Take(count).Where(x=>x.Table==table)){
    bool rows=done.Row<pending.Row+pending.RowSpan&&pending.Row<done.Row+done.RowSpan;
    bool cols=done.Column<pending.Column+pending.ColSpan&&pending.Column<done.Column+done.ColSpan;
    if(rows&&done.Column+done.ColSpan==pending.Column)result.Add("LEFTBORDER");
    if(rows&&pending.Column+pending.ColSpan==done.Column)result.Add("RIGHTBORDER");
    if(cols&&done.Row+done.RowSpan==pending.Row)result.Add("TOPBORDER");
    if(cols&&pending.Row+pending.RowSpan==done.Row)result.Add("BOTTOMBORDER");
   }
   return result;
  }
  static XmlElement NormalizePendingEdges(XmlElement expected,XmlElement actual,HashSet<string> sides){
   var result=(XmlElement)actual.CloneNode(true);
   foreach(string name in sides){var old=Children(result).Single(x=>x.LocalName==name);var replacement=Children(expected).Single(x=>x.LocalName==name);result.ReplaceChild(result.OwnerDocument.ImportNode(replacement,true),old);}
   return result;
  }
  // Before native writes, make sure shared-edge propagation can leave EVERY
  // target cell at its source role, including cells skipped as already equal.
  // Conflicting reference edges otherwise allow a misleading success result.
  internal static void VerifyFinalPlan(ComposeXml original,TableStylePlan plan){
   if(original==null||plan==null)throw new ArgumentNullException("plan");
   if(plan.Changes.Length==0)return;
   var tables=Tables(original);var source=Read(tables[plan.SourceIndex],plan.SourceIndex);
   var grids=tables.Select((t,i)=>Cells(t,plan.TableRows[i],plan.TableColumns[i])).ToArray();
   var expected=ExpectedBorders(original,plan,source,grids,plan.Changes.Length);
   for(int t=0;t<tables.Length;t++)if(t!=plan.SourceIndex)for(int r=0;r<plan.TableRows[t];r++)for(int c=0;c<plan.TableColumns[t];c++){
    if(Number(grids[t][r,c],"RowAddr",plan.TableRows[t]-1)!=r||Number(grids[t][r,c],"ColAddr",plan.TableColumns[t]-1)!=c)continue;
    var cell=grids[t][r,c];int rs=cell.HasAttribute("RowSpan")?Number(cell,"RowSpan",100):1,cs=cell.HasAttribute("ColSpan")?Number(cell,"ColSpan",30):1;
    var design=plan.Changes.FirstOrDefault(x=>x.Table==t&&x.Row==r&&x.Column==c);var desired=source[TargetSourceRow(r,rs,plan.TableRows[t],plan.Rows)][TargetSourceColumn(c,cs,plan.TableColumns[t],plan.Columns)];ExpectedBorder predicted;
    string actual=expected.TryGetValue(CellKey(t,r,c),out predicted)?ComposeXml.Canonical(predicted.Definition):Definition(original.Mapping,"BORDERFILLLIST","BORDERFILL",grids[t][r,c].GetAttribute("BorderFill"));
    string required=design!=null&&design.EdgeRows!=null?ComposeXml.Canonical(PlannedBorder(original,design,source)):Definition(original.Mapping,"BORDERFILLLIST","BORDERFILL",desired.Border);
    if(actual!=required)throw new InvalidOperationException("기준 표의 맞닿은 테두리를 다른 표의 셀 배치에 그대로 적용할 수 없습니다. [표="+(t+1)+"; 행="+(r+1)+"; 열="+(c+1)+"]");
   }
  }
  // Describe only the structural location of a mismatch, never document text
  // or attribute values, so a user can report it without sharing content.
  static string BodyDifference(XmlNode a,XmlNode b,string path,bool inChar){
   var x=a as XmlElement;var y=b as XmlElement;
   if(x==null||y==null)return a.NodeType==b.NodeType&&a.Value==b.Value?null:path+" (텍스트 또는 노드)";
   if(x.Name!=y.Name)return path+" (요소 종류)";
   foreach(string name in x.Attributes.Cast<XmlAttribute>().Select(z=>z.Name).Concat(y.Attributes.Cast<XmlAttribute>().Select(z=>z.Name)).Distinct().OrderBy(z=>z))
    if(x.HasAttribute(name)!=y.HasAttribute(name)||x.GetAttribute(name)!=y.GetAttribute(name))return path+" (속성="+name+")";
   bool inside=inChar||x.LocalName=="CHAR";
   Func<XmlNode,bool> included=n=>n is XmlElement||((n.NodeType==XmlNodeType.Text||n.NodeType==XmlNodeType.CDATA||n.NodeType==XmlNodeType.Whitespace||n.NodeType==XmlNodeType.SignificantWhitespace)&&(inside||!String.IsNullOrWhiteSpace(n.Value)));
   var left=x.ChildNodes.Cast<XmlNode>().Where(included).ToArray();var right=y.ChildNodes.Cast<XmlNode>().Where(included).ToArray();
   if(left.Length!=right.Length)return path+" (자식 요소 수)";
   for(int i=0;i<left.Length;i++){string name=left[i] is XmlElement?((XmlElement)left[i]).LocalName:"text";string next=BodyDifference(left[i],right[i],path+"/"+name+"["+i+"]",inside);if(next!=null)return next;}
   return null;
  }
  internal static void Verify(ComposeXml before,ComposeXml after,TableStylePlan plan){Verify(before,after,plan,plan.Changes.Length);}
  internal static void Verify(ComposeXml before,ComposeXml after,TableStylePlan plan,int appliedCount){
   if(before==null||after==null||plan==null)throw new ArgumentNullException("plan");
   if(appliedCount<0||appliedCount>plan.Changes.Length)throw new InvalidOperationException("표 서식 적용 수가 올바르지 않습니다.");
   after=NormalizeBorderIds(before,after,plan,appliedCount);
   after=NormalizeTextStyleIds(before,after,plan,appliedCount,"CHARSHAPELIST","CHARSHAPE","CharShape");
   after=NormalizeTextStyleIds(before,after,plan,appliedCount,"PARASHAPELIST","PARASHAPE","ParaShape");
   var oldTables=Tables(before);var newTables=Tables(after);if(oldTables.Length!=newTables.Length||after.Paragraphs.Length!=before.Paragraphs.Length)throw new InvalidOperationException("표 서식 적용 후 본문 구조가 바뀌었습니다.");
   // Read physical grids directly: rebuilding the legacy style plan here
   // incorrectly applies its unrelated shape restrictions during verification.
   var afterGrids=newTables.Select((t,i)=>Read(t,i)).ToArray();
   if(!afterGrids.Select(g=>g.Length).SequenceEqual(plan.TableRows)||!afterGrids.Select(g=>g[0].Length).SequenceEqual(plan.TableColumns)||
      !afterGrids.Select((g,i)=>g.SelectMany(row=>row).Distinct().Select(c=>c.Row*g[0].Length+c.Column).OrderBy(x=>x).SequenceEqual(plan.TableAnchors[i])).All(x=>x))throw new InvalidOperationException("표 행·열 또는 셀 배치가 바뀌었습니다.");
   var source=Read(oldTables[plan.SourceIndex],plan.SourceIndex);var normalized=(XmlDocument)after.Document.CloneNode(true);var normalizedAfter=ComposeXml.Parse(normalized.OuterXml);var normalizedTables=Tables(normalizedAfter);
   var oldGrids=oldTables.Select((t,i)=>Cells(t,plan.TableRows[i],plan.TableColumns[i])).ToArray();var normalizedGrids=normalizedTables.Select((t,i)=>Cells(t,plan.TableRows[i],plan.TableColumns[i])).ToArray();
   foreach(var border in ExpectedBorders(before,plan,source,oldGrids,appliedCount).Values){
    var actual=normalizedGrids[border.Table][border.Row,border.Column];
    var actualDefinition=DefinitionElement(after.Mapping,"BORDERFILLLIST","BORDERFILL",actual.GetAttribute("BorderFill"));
    var pendingSides=PendingSharedEdges(plan,appliedCount,border.Table,border.Row,border.Column);
    var compared=pendingSides.Count==0?actualDefinition:NormalizePendingEdges(border.Definition,actualDefinition,pendingSides);
    if(ComposeXml.Canonical(border.Definition)!=ComposeXml.Canonical(compared))
     throw new InvalidOperationException("표 테두리 또는 인접 셀의 공유 경계가 적용 계획과 다릅니다. [표="+(border.Table+1)+"; 행="+(border.Row+1)+"; 열="+(border.Column+1)+"; "+BorderDifference(border.Definition,compared)+"]");
    actual.SetAttribute("BorderFill",oldGrids[border.Table][border.Row,border.Column].GetAttribute("BorderFill"));
   }
   // HWP can assign a different ID to an otherwise identical border record.
   // Only normalize a cell reference when the full referenced definitions
   // match exactly. Preserve() below still checks every original record.
   for(int t=0;t<oldGrids.Length;t++)for(int r=0;r<plan.TableRows[t];r++)for(int c=0;c<plan.TableColumns[t];c++){
    var old=oldGrids[t][r,c];var current=normalizedGrids[t][r,c];string oldId=old.GetAttribute("BorderFill"),newId=current.GetAttribute("BorderFill");
    if(oldId!=newId&&Definition(before.Mapping,"BORDERFILLLIST","BORDERFILL",oldId)==Definition(after.Mapping,"BORDERFILLLIST","BORDERFILL",newId))current.SetAttribute("BorderFill",oldId);
   }
   foreach(var change in plan.Changes.Take(appliedCount)){
    var desired=source[change.SourceRow][change.SourceColumn];var actual=Read(newTables[change.Table],change.Table)[change.Row][change.Column];
    if(actual.ParagraphStyles.Any(v=>Definition(before.Mapping,"PARASHAPELIST","PARASHAPE",desired.Paragraph)!=Definition(after.Mapping,"PARASHAPELIST","PARASHAPE",v))||
       actual.CharacterStyles.Any(v=>Definition(before.Mapping,"CHARSHAPELIST","CHARSHAPE",desired.Character)!=Definition(after.Mapping,"CHARSHAPELIST","CHARSHAPE",v)))
     throw new InvalidOperationException("표 셀 서식이 기준 표와 일치하지 않습니다.");
    XmlElement old=oldGrids[change.Table][change.Row,change.Column], target=normalizedGrids[change.Table][change.Row,change.Column];
    target.SetAttribute("BorderFill",old.GetAttribute("BorderFill"));var oldPs=Children(Single(old,"PARALIST"));var newPs=Children(Single(target,"PARALIST"));if(oldPs.Length!=newPs.Length)throw new InvalidOperationException("표 셀 문단 구조가 바뀌었습니다.");
    for(int p=0;p<oldPs.Length;p++){newPs[p].SetAttribute("ParaShape",oldPs[p].GetAttribute("ParaShape"));var oldText=Children(oldPs[p]);var newText=Children(newPs[p]);if(oldText.Length!=newText.Length)throw new InvalidOperationException("표 셀 글자 구조가 바뀌었습니다.");for(int i=0;i<oldText.Length;i++)newText[i].SetAttribute("CharShape",oldText[i].GetAttribute("CharShape"));}
   }
   if(!ComposeXml.Canonical(before.Body).Equals(ComposeXml.Canonical(normalizedAfter.Body),StringComparison.Ordinal)){
    string location=BodyDifference(before.Body,normalizedAfter.Body,"BODY",false);
    throw new InvalidOperationException("표 서식 이외의 본문·셀 내용 또는 구조가 변경되었습니다."+(String.IsNullOrEmpty(location)?"":" [위치="+location+"]"));
   }
   var beforeHead=(XmlElement)Single(before.Document.DocumentElement,"HEAD").CloneNode(true);var afterHead=(XmlElement)Single(after.Document.DocumentElement,"HEAD").CloneNode(true);
   // Navigation changes the saved caret without changing document content.
   foreach(XmlElement e in beforeHead.GetElementsByTagName("CARETPOS").Cast<XmlElement>().ToArray())e.ParentNode.RemoveChild(e);
   foreach(XmlElement e in afterHead.GetElementsByTagName("CARETPOS").Cast<XmlElement>().ToArray())e.ParentNode.RemoveChild(e);
   if(!Children(beforeHead).Where(x=>x.LocalName!="MAPPINGTABLE").Select(ComposeXml.Canonical).SequenceEqual(Children(afterHead).Where(x=>x.LocalName!="MAPPINGTABLE").Select(ComposeXml.Canonical)))throw new InvalidOperationException("표 서식 이외의 문서 정보가 변경되었습니다.");
   RichXml.Preserve(before.Mapping,after.Mapping);RichXml.PreserveTail(before.Document.DocumentElement,after.Document.DocumentElement);
  }
 }
}
