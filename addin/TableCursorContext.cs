using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hwp.Addon;

namespace HAddon {
 // Read-only capture for a caret inside a native, top-level body table. It
 // neither selects cells nor changes the user's document or cursor.
 internal static class TableControlIdentity {
  internal static bool Same(IHwpCtrlCode a,IHwpCtrlCode b){
   if(a==null||b==null||a.CtrlID!=b.CtrlID)return false;
   try {
    var aa=a.GetAnchorPos(0);var bb=b.GetAnchorPos(0);
    foreach(string key in new[]{"List","Para","Pos"}){
     if(aa==null||bb==null||!aa.ItemExist(key)||!bb.ItemExist(key))return false;
     if(Convert.ToInt64(aa.GetItem(key),CultureInfo.InvariantCulture)!=Convert.ToInt64(bb.GetItem(key),CultureInfo.InvariantCulture))return false;
    }
    return true;
   }catch{return false;}
  }
 }
 internal sealed class TableCursorContext {
  public readonly int DocumentId,List,Paragraph,Position,SourceIndex,TableCount;
  public readonly string Path,DocumentHash;
  public readonly TableStylePlan Plan;
  internal readonly Dictionary<string,object> DesignSnapshot;
  internal readonly ComposeXml Original;
  internal readonly IHwpCtrlCode[] Controls;
  // Distinguish a real table caret from whether every table in the document
  // is supported by the all-table style planner. No cursor movement.
  internal static bool HasTableCaret(IHwpObject doc){
   if(doc==null)return false;try{int l=0,p=0,x=0;var parent=doc.GetParentCtrl();return doc.GetPos(ref l,ref p,ref x)&&l>0&&parent!=null&&parent.CtrlID=="tbl";}catch{return false;}
  }
  // Routing availability only. Read native control anchors without serializing
  // the document, selecting a cell, or treating an XML table as a native one.
  internal static bool HasBodyTables(IHwpObject doc){
   if(doc==null)return false;var ctrl=doc.GetHeadCtrl();
   for(int steps=0;ctrl!=null;steps++){
    if(steps>=256)throw new InvalidOperationException("문서 개체 목록을 확인하지 못했습니다.");
    if(ctrl.CtrlID=="tbl"&&AnchorItem(ctrl.GetAnchorPos(0),"List")==0)return true;
    ctrl=ctrl.GetNext();
   }
   return false;
  }
  TableCursorContext(int id,string path,int list,int para,int pos,int source,int count,string hash,TableStylePlan plan,Dictionary<string,object> designSnapshot,ComposeXml original,IHwpCtrlCode[] controls){DocumentId=id;Path=path;List=list;Paragraph=para;Position=pos;SourceIndex=source;TableCount=count;DocumentHash=hash;Plan=plan;DesignSnapshot=designSnapshot;Original=original;Controls=controls;}
  internal TableCursorContext WithPlan(TableStylePlan plan){if(plan==null||plan.SourceIndex!=SourceIndex)throw new InvalidOperationException("기준 표와 디자인 계획이 다릅니다.");return new TableCursorContext(DocumentId,Path,List,Paragraph,Position,SourceIndex,TableCount,DocumentHash,plan,DesignSnapshot,Original,Controls);}
  static int AnchorItem(IHwpParameterSet anchor,string name){
   if(anchor==null||!anchor.ItemExist(name))throw new InvalidOperationException("표의 문서 위치를 확인하지 못했습니다.");
   try{return Convert.ToInt32(anchor.GetItem(name),CultureInfo.InvariantCulture);}catch{throw new InvalidOperationException("표의 문서 위치가 올바르지 않습니다.");}
  }
  internal static TableCursorContext Capture(IHwpObject doc){return Capture(doc,false,false);}
  // Experimental analysis capture does not run the old geometry-specific planner.
  internal static TableCursorContext CaptureForDesign(IHwpObject doc){return Capture(doc,true,false);}
  internal static TableCursorContext CaptureFirstForDesign(IHwpObject doc){return Capture(doc,true,true);}
  static TableCursorContext Capture(IHwpObject doc,bool design,bool firstTable){
   if(doc==null||doc.IsReadOnlyMode())throw new InvalidOperationException("편집 가능한 한글 문서를 확인하지 못했습니다.");
   int mode=doc.GetSelectionMode();if((mode&~31)!=0||((mode&15)!=0&&(mode&15)!=1&&(mode&15)!=3))throw new InvalidOperationException("표 안의 글이나 셀을 선택해 주세요.");
   int list=0,para=0,pos=0;if(!doc.GetPos(ref list,ref para,ref pos)||list<0||(!firstTable&&list==0)||para<0||pos<0)throw new InvalidOperationException("문서의 커서 위치를 확인하지 못했습니다.");
   var source=doc.GetParentCtrl();if(!firstTable&&(source==null||source.CtrlID!="tbl"))throw new InvalidOperationException("커서가 속한 표를 확인하지 못했습니다.");
   int id=doc.GetID();string path=doc.GetPath()??"",raw=doc.GetTextFile("HWPML2X","");var xml=ComposeXml.Parse(raw);
   var tables=new List<IHwpCtrlCode>();var anchors=new List<int>();var seen=new List<IHwpCtrlCode>();var ctrl=doc.GetHeadCtrl();
   for(int steps=0;ctrl!=null;steps++){
    if(steps>=256||seen.Any(x=>TableControlIdentity.Same(x,ctrl)))throw new InvalidOperationException("문서 개체 목록이 순환하거나 너무 깁니다.");seen.Add(ctrl);
    if(ctrl.CtrlID=="tbl") {var position=ctrl.GetAnchorPos(0);if(AnchorItem(position,"List")==0){tables.Add(ctrl);anchors.Add(AnchorItem(position,"Para"));}}ctrl=ctrl.GetNext();
   }
   var hits=tables.Select((t,i)=>new {Table=t,Index=i}).Where(x=>TableControlIdentity.Same(x.Table,source)).ToArray();if(!firstTable&&hits.Length!=1)throw new InvalidOperationException("중첩 표 또는 본문 밖 표는 기준으로 사용할 수 없습니다.");
   if(firstTable&&tables.Count==0)throw new InvalidOperationException("본문의 첫 번째 표를 찾지 못했습니다.");
   int sourceIndex=firstTable?0:hits[0].Index;var plan=design?null:TableStylePlan.Build(xml,sourceIndex);var snapshot=design?TableDesignSnapshot.Create(xml,sourceIndex):null;
   var paragraphs=xml.Paragraphs.SelectMany((p,i)=>p.ChildNodes.Cast<System.Xml.XmlNode>().OfType<System.Xml.XmlElement>().Where(x=>x.LocalName=="TEXT").SelectMany(x=>x.ChildNodes.Cast<System.Xml.XmlNode>().OfType<System.Xml.XmlElement>()).Where(x=>x.LocalName=="TABLE").Select(x=>i)).ToArray();int count=paragraphs.Length;
   if(count!=tables.Count||paragraphs.Distinct().Count()!=count||!paragraphs.SequenceEqual(anchors))throw new InvalidOperationException("본문 표 위치가 한글 컨트롤과 일치하지 않습니다.");
   int l=0,pn=0,at=0;string check=doc.GetTextFile("HWPML2X","");var parent=doc.GetParentCtrl();
   if(doc.GetID()!=id||(doc.GetPath()??"")!=path)throw new InvalidOperationException("표를 확인하는 동안 문서가 바뀌었습니다.");
   if(!doc.GetPos(ref l,ref pn,ref at)||l!=list||pn!=para||at!=pos)throw new InvalidOperationException("표를 확인하는 동안 커서 위치가 바뀌었습니다.");
   if(!firstTable&&(parent==null||!TableControlIdentity.Same(parent,source)))throw new InvalidOperationException("표를 확인하는 동안 기준 표가 바뀌었습니다.");
   // HWP may serialize equivalent XML differently, including saved caret
   // metadata. Verify content and definitions semantically, not raw bytes.
   try{if(design){if(TableStableSnapshot.Digest(raw)!=TableStableSnapshot.Digest(check))throw new InvalidOperationException("문서 내용 또는 서식이 바뀌었습니다.");}else TableStylePlan.Verify(xml,ComposeXml.Parse(check),plan,0);}catch(Exception ex){throw new InvalidOperationException("표를 확인하는 동안 문서 검증이 달라졌습니다. "+ex.Message,ex);}
   return new TableCursorContext(id,path,list,para,pos,sourceIndex,count,RichFragmentFile.Digest(System.Text.Encoding.UTF8.GetBytes(raw)),plan,snapshot,xml,tables.ToArray());
  }
 }
}
