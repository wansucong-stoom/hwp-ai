using System;
using Hwp.Addon;

namespace HAddon {
 // A popup without a selection owns the original document and caret position.
 // Reading the document is unnecessary here; native composition snapshots the
 // full document immediately after this point and verifies it before mutation.
 internal sealed class CaretAnchor {
  readonly int documentId,list,paragraph,position;
  readonly string path;
  CaretAnchor(int id,string path,int list,int paragraph,int position){
   documentId=id;this.path=path;this.list=list;this.paragraph=paragraph;this.position=position;
  }
  static bool NoSelection(IHwpObject doc){
   int mode=doc.GetSelectionMode();if((mode&15)!=0||(mode&~31)!=0||doc.GetCurSelectedCtrl()!=null)return false;
   int sl=0,sp=0,so=0,el=0,ep=0,eo=0;
   return !doc.GetSelectedPos(ref sl,ref sp,ref so,ref el,ref ep,ref eo)||(sl==el&&sp==ep&&so==eo);
  }
  internal static CaretAnchor Capture(IHwpObject doc){
   if(doc==null)throw new InvalidOperationException("요청할 한글 문서를 확인할 수 없습니다.");
   if(!NoSelection(doc))throw new InvalidOperationException("본문 선택이 바뀌었습니다. 다시 요청하세요.");
   int id=doc.GetID(),list=0,paragraph=0,position=0;string path=doc.GetPath()??"";
   if(!doc.GetPos(ref list,ref paragraph,ref position)||list<0||paragraph<0||position<0)throw new InvalidOperationException("요청할 본문 커서를 확인할 수 없습니다.");
   var anchor=new CaretAnchor(id,path,list,paragraph,position);anchor.RequireCurrent(doc);return anchor;
  }
  internal void RequireCurrent(IHwpObject doc){
   if(doc==null||!NoSelection(doc)||doc.GetID()!=documentId||(doc.GetPath()??"")!=path)
    throw new InvalidOperationException("요청을 연 뒤 문서 또는 선택이 바뀌었습니다. 원래 위치에서 다시 요청하세요.");
   int observedList=0,observedParagraph=0,observedPosition=0;
   if(!doc.GetPos(ref observedList,ref observedParagraph,ref observedPosition)||observedList!=list||observedParagraph!=paragraph||observedPosition!=position)
    throw new InvalidOperationException("요청을 연 뒤 본문 커서가 바뀌었습니다. 원래 위치에서 다시 요청하세요.");
  }
 }
}
