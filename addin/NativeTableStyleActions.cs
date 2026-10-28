using System;
using System.Collections.Generic;
using System.Linq;
using Hwp.Addon;

namespace HAddon {
 // Native adapter for pinned, unmerged body tables.
 // Native behavior still requires verification in the installed HWP application.
 internal sealed class NativeTableStyleActions:ITableStyleActions,ITableStyleCursorHandoff,ITableStyleTextRebase {
  readonly Func<IHwpObject> getDocument;readonly TableCursorContext pinned;
  readonly int[][,] anchorGrids;
  readonly Dictionary<int,string>[] anchorTexts;
  readonly Dictionary<string,NativeTableBorderValue> edgeCache=new Dictionary<string,NativeTableBorderValue>();
  int rowArgument=-1;CursorBookmark restore;
  sealed class CursorBookmark {
   internal int Mode,List,Paragraph,Position,A,B,C,D,E,F;
   internal bool Supported;
   internal static CursorBookmark Capture(IHwpObject doc){
    var value=new CursorBookmark{Mode=doc.GetSelectionMode()};
    if(!doc.GetPos(ref value.List,ref value.Paragraph,ref value.Position))throw new InvalidOperationException("현재 커서 위치를 확인하지 못했습니다.");
    int mode=value.Mode&15;value.Supported=(value.Mode&~31)==0&&(mode==0||mode==1);
    if(mode==1){if(!doc.GetSelectedPos(ref value.A,ref value.B,ref value.C,ref value.D,ref value.E,ref value.F))throw new InvalidOperationException("현재 텍스트 선택을 확인하지 못했습니다.");value.Supported=value.Supported&&value.A==value.D;}
    return value;
   }
   internal string Stamp{get{return List+":"+Paragraph+":"+Position+":"+Mode+((Mode&15)==1?":"+A+":"+B+":"+C+":"+D+":"+E+":"+F:"");}}
  }
  internal NativeTableStyleActions(Func<IHwpObject> getDocument,TableCursorContext pinned){
   this.getDocument=getDocument;this.pinned=pinned;anchorGrids=TableStylePlan.AnchorGrids(pinned.Original,pinned.Plan);anchorTexts=TableStylePlan.AnchorTexts(pinned.Original,pinned.Plan);
   restore=CursorBookmark.Capture(Current());
  }
  IHwpObject Current(){
   var doc=getDocument();if(doc==null||doc.IsReadOnlyMode()||doc.GetID()!=pinned.DocumentId||(doc.GetPath()??"")!=pinned.Path)throw new InvalidOperationException("원래 한글 문서가 바뀌었습니다.");return doc;
  }
  public void CheckIdentity(){Current();}
  public ComposeXml ReadDocument(){return ComposeXml.Parse(Current().GetTextFile("HWPML2X",""));}
  public string ReadRawDocument(){return Current().GetTextFile("HWPML2X","");}
  public string ReadCaret(){return CursorBookmark.Capture(Current()).Stamp;}
  public bool TryAdoptCursor(string expectedCaret){var doc=Current();var next=CursorBookmark.Capture(doc);if(!next.Supported||next.Stamp!=expectedCaret||ReadCaret()!=expectedCaret)return false;restore=next;return true;}
  public void RefreshAnchorTexts(ComposeXml updated){var fresh=TableStylePlan.AnchorTexts(updated,pinned.Plan);if(fresh.Length!=anchorTexts.Length)throw new InvalidOperationException("동시 편집 후 표 위치를 확인하지 못했습니다.");for(int i=0;i<fresh.Length;i++)anchorTexts[i]=fresh[i];}
  void SelectFirst(IHwpObject doc,int table){
   var control=pinned.Controls[table];if(control==null||control.CtrlID!="tbl"||!doc.SelectCtrl(control,1)||!doc.Run("ShapeObjTableSelCell"))throw new InvalidOperationException("표의 첫 셀로 이동하지 못했습니다.");
   var parent=doc.GetParentCtrl();if(parent==null||!TableControlIdentity.Same(parent,control)||doc.GetTableCellAddr(0)!=0||doc.GetTableCellAddr(1)!=0)throw new InvalidOperationException("표 첫 셀의 주소를 확인하지 못했습니다.");
   int selection=doc.GetSelectionMode()&15;if(selection==3){if(!doc.Run("Cancel")||(doc.GetSelectionMode()&15)!=0)throw new InvalidOperationException("표의 첫 셀 선택을 편집 커서로 전환하지 못했습니다.");parent=doc.GetParentCtrl();if(parent==null||!TableControlIdentity.Same(parent,control)||doc.GetTableCellAddr(0)!=0||doc.GetTableCellAddr(1)!=0)throw new InvalidOperationException("선택 해제 후 원래 표의 첫 셀을 확인하지 못했습니다.");}else if(selection!=0)throw new InvalidOperationException("표의 첫 셀 선택 상태를 확인하지 못했습니다. 선택 상태: "+selection);
  }
  bool AxisFits(int table,List<long[]> samples,int rowArg){
   int rows=pinned.Plan.TableRows[table],cols=pinned.Plan.TableColumns[table];var seen=new HashSet<int>();
   foreach(var sample in samples){long r=rowArg==0?sample[0]:sample[1],c=rowArg==0?sample[1]:sample[0];if(r<0||c<0||r>=rows||c>=cols)return false;seen.Add(anchorGrids[table][(int)r,(int)c]);}
   return seen.SetEquals(pinned.Plan.TableAnchors[table]);
  }
  static string NormalizeCellText(string value){return value==null?null:value.Replace("\r\n","\n").Replace("\r","\n").TrimEnd('\n','\t');}
  int ResolveAxisByCellText(IHwpObject doc,int table,int limit){
   SelectFirst(doc,table);int rows=pinned.Plan.TableRows[table],cols=pinned.Plan.TableColumns[table];var seen=new HashSet<string>{"0:0"};
   for(int i=0;i<limit;i++){
    if(!doc.Run("TableRightCell"))break;var parent=doc.GetParentCtrl();if(parent==null||!TableControlIdentity.Same(parent,pinned.Controls[table]))break;
    long a=doc.GetTableCellAddr(0),b=doc.GetTableCellAddr(1);if(!seen.Add(a+":"+b))break;
    if(a<0||b<0||a>=rows||b>=cols||b>=rows||a>=cols)continue;
    int first=anchorGrids[table][(int)a,(int)b],second=anchorGrids[table][(int)b,(int)a];if(first==second)continue;
    string x=NormalizeCellText(anchorTexts[table][first]),y=NormalizeCellText(anchorTexts[table][second]);if(String.IsNullOrEmpty(x)||String.IsNullOrEmpty(y)||x==y)continue;
    if(!doc.Run("TableCellBlock")||(doc.GetSelectionMode()&15)!=3)throw new InvalidOperationException("셀 내용을 대조할 선택 범위를 확인하지 못했습니다.");
    string read;try{read=NormalizeCellText(doc.GetTextFile("TEXT","saveblock"));}finally{if(!doc.Run("Cancel")||(doc.GetSelectionMode()&15)!=0)throw new InvalidOperationException("셀 내용 확인 뒤 선택을 해제하지 못했습니다.");}
    parent=doc.GetParentCtrl();if(parent==null||!TableControlIdentity.Same(parent,pinned.Controls[table])||doc.GetTableCellAddr(0)!=a||doc.GetTableCellAddr(1)!=b)throw new InvalidOperationException("셀 내용 확인 중 위치가 바뀌었습니다.");
    bool match0=read==x,match1=read==y;if(match0!=match1)return match0?0:1;
   }
   return -1;
  }
  void DetermineAxis(IHwpObject doc,int table){
   var samples=new List<long[]>{new long[]{0,0}};var seen=new HashSet<string>{"0:0"};int limit=pinned.Plan.TableRows[table]*pinned.Plan.TableColumns[table]*2+pinned.Plan.TableAnchors[table].Length+1;
   for(int i=0;i<limit;i++){
    if(!doc.Run("TableRightCell"))break;var parent=doc.GetParentCtrl();if(parent==null||!TableControlIdentity.Same(parent,pinned.Controls[table]))break;
    long a=doc.GetTableCellAddr(0),b=doc.GetTableCellAddr(1);string key=a+":"+b;if(!seen.Add(key))break;samples.Add(new[]{a,b});
   }
   bool first=AxisFits(table,samples,0),second=AxisFits(table,samples,1);
   if(first==second){int textAxis=ResolveAxisByCellText(doc,table,limit);if(textAxis>=0){rowArgument=textAxis;return;}}
   if(first==second)throw new InvalidOperationException("한글 셀의 위치와 내용을 대조해 좌표 방향을 확인하지 못했습니다. 현재 동작에서 추가로 적용하지 않았습니다.");
   rowArgument=first?0:1;
  }
  void Navigate(int table,int row,int col){
   var doc=Current();if(table<0||table>=pinned.Controls.Length||row<0||row>=pinned.Plan.TableRows[table]||col<0||col>=pinned.Plan.TableColumns[table])throw new InvalidOperationException("표 셀 좌표가 계획과 다릅니다.");
   var control=pinned.Controls[table];SelectFirst(doc,table);
   int columns=pinned.Plan.TableColumns[table];int[] anchors=pinned.Plan.TableAnchors[table];int steps=Array.IndexOf(anchors,row*columns+col);if(steps<0||anchors.Length==0||anchors[0]!=0)throw new InvalidOperationException("표 셀 이동 순서를 확인하지 못했습니다.");
   if(steps>0&&rowArgument<0){
    int calibration=table;
    // A non-square table can disambiguate swapped axes without depending on
    // traversal order, even when the requested table itself is square.
    if(pinned.Plan.TableRows[calibration]==pinned.Plan.TableColumns[calibration])for(int i=0;i<pinned.Plan.TableRows.Length;i++)if(pinned.Plan.TableRows[i]!=pinned.Plan.TableColumns[i]&&pinned.Plan.TableAnchors[i].Length>1){calibration=i;break;}
    if(calibration!=table)SelectFirst(doc,calibration);DetermineAxis(doc,calibration);SelectFirst(doc,table);
   }
   bool reached=steps==0;int maxSteps=pinned.Plan.TableRows[table]*columns*2+anchors.Length+1;
   for(int i=1;!reached&&i<=maxSteps;i++){
    if(!doc.Run("TableRightCell"))throw new InvalidOperationException("요청한 셀에 도달하기 전에 한글 셀 이동이 끝났습니다. [목표="+(row+1)+","+(col+1)+"]");
    var parent=doc.GetParentCtrl();if(parent==null||!TableControlIdentity.Same(parent,control))throw new InvalidOperationException("셀 이동 중 다른 표로 이동했습니다.");
    long a=doc.GetTableCellAddr(0),b=doc.GetTableCellAddr(1);
    long rr=rowArgument==0?a:b,cc=rowArgument==0?b:a;
    if(rr<0||cc<0||rr>=pinned.Plan.TableRows[table]||cc>=columns)throw new InvalidOperationException("한글 셀 주소가 표 범위를 벗어났습니다.");
    reached=anchorGrids[table][(int)rr,(int)cc]==row*columns+col;
   }
   if(!reached)throw new InvalidOperationException("요청한 표 셀을 확인하지 못했습니다. 셀 이동을 중단했습니다.");
   RequireCell(Current(),table,row,col,0);
  }
  void RequireCell(IHwpObject doc,int table,int row,int col,int requiredMode=-1){
   var parent=doc.GetParentCtrl();if(parent==null||!TableControlIdentity.Same(parent,pinned.Controls[table]))throw new InvalidOperationException("적용 직전의 표가 고정한 대상과 다릅니다.");
   long a=doc.GetTableCellAddr(0),b=doc.GetTableCellAddr(1);
   bool first=row==0&&col==0&&a==0&&b==0;
   if(!first){long rr=rowArgument==0?a:b,cc=rowArgument==0?b:a;if(rowArgument<0||rr<0||cc<0||rr>=pinned.Plan.TableRows[table]||cc>=pinned.Plan.TableColumns[table]||anchorGrids[table][(int)rr,(int)cc]!=row*pinned.Plan.TableColumns[table]+col)throw new InvalidOperationException("적용 직전의 셀 주소가 고정한 대상과 다릅니다.");}
   int mode=doc.GetSelectionMode()&15;if(requiredMode>=0&&mode!=requiredMode)throw new InvalidOperationException("적용 직전의 셀 선택 상태가 달라졌습니다.");
   if(requiredMode<0&&mode!=0&&mode!=3)throw new InvalidOperationException("셀 서식을 적용할 선택 상태가 아닙니다.");
  }
  static void Shape(IHwpObject doc){
   var action=doc.CreateAction("ShapeCopyPaste");var set=action==null?null:action.CreateSet();
   if(action==null||set==null||!action.GetDefault(set))throw new InvalidOperationException("표 서식 복사 명령을 준비하지 못했습니다.");
   string type=set.ItemExist("Type")?"Type":set.ItemExist("type")?"type":null;
   if(type==null)throw new InvalidOperationException("한글 표 서식 복사 옵션을 확인하지 못했습니다.");
   foreach(string name in new[]{"CellAttr","CellBorder","CellFill","TypeBodyAndCellOnly"})if(!set.ItemExist(name))throw new InvalidOperationException("한글 표 서식 복사 옵션을 확인하지 못했습니다: "+name);
   set.SetItem(type,2);set.SetItem("CellAttr",0);set.SetItem("CellBorder",1);set.SetItem("CellFill",1);set.SetItem("TypeBodyAndCellOnly",0);
   if(action.Execute(set)==0)throw new InvalidOperationException("한글이 표 서식 복사 명령을 완료하지 못했습니다.");
  }
  public void CopyCellStyle(int table,int row,int column){Navigate(table,row,column);var doc=Current();RequireCell(doc,table,row,column,0);Shape(doc);}
  public void ApplyCellStyle(int table,int row,int column){
   var change=pinned.Plan.Changes.Single(x=>x.Table==table&&x.Row==row&&x.Column==column);Dictionary<string,NativeTableBorderValue> edges=null;
   if(change.EdgeRows!=null){edges=new Dictionary<string,NativeTableBorderValue>();string[] sides={"Left","Right","Top","Bottom"};for(int i=0;i<4;i++){string key=change.EdgeRows[i]+":"+change.EdgeColumns[i]+":"+sides[i];NativeTableBorderValue value;if(!edgeCache.TryGetValue(key,out value)){Navigate(pinned.Plan.SourceIndex,change.EdgeRows[i],change.EdgeColumns[i]);value=NativeTableBorderParameters.Read(Current(),sides[i]);edgeCache.Add(key,value);}edges.Add(sides[i],value);}}
   Navigate(table,row,column);var doc=Current();RequireCell(doc,table,row,column,0);if(!doc.Run("TableCellBlock")||(doc.GetSelectionMode()&15)!=3)throw new InvalidOperationException("대상 셀을 선택하지 못했습니다.");RequireCell(doc,table,row,column,3);Shape(doc);if(edges!=null){RequireCell(doc,table,row,column);NativeTableBorderParameters.Apply(doc,edges);}if(!doc.Run("Cancel"))throw new InvalidOperationException("셀 선택을 종료하지 못했습니다.");
  }
  public void RestoreCursor(){var doc=Current();if(restore==null||!restore.Supported)throw new InvalidOperationException("기존 개체·셀 블록 선택을 안전하게 복원할 수 없습니다.");if(!NativeDocumentScan.RestoreAndVerify(doc,restore.Mode,restore.List,restore.Paragraph,restore.Position,restore.A,restore.B,restore.C,restore.D,restore.E,restore.F))throw new InvalidOperationException("최근 사용자 커서 또는 텍스트 선택을 복원하지 못했습니다.");}
 }
}
