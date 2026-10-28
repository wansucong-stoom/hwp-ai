using System;
using System.Collections.Generic;
using System.Linq;
using Hwp.Addon;

namespace HAddon {
 // Experimental bridge between scan locations and inspected whole paragraphs.
 // It does not establish rendered page boundaries or allow editing by itself.
 internal static class NativeDocumentInspection {
  internal static InspectedNativeParagraph[] Inspect(Func<IHwpObject> getDocument,NativePageSnapshot snapshot,NativeScanItem[] scan){
   if(getDocument==null||snapshot==null||scan==null)throw new ArgumentNullException("snapshot");
   var doc=getDocument();Check(doc,snapshot);int list=0,para=0,pos=0,sl=0,sp=0,so=0,el=0,ep=0,eo=0;
   if(!doc.GetPos(ref list,ref para,ref pos))throw new InvalidOperationException("원래 커서를 확인하지 못했습니다.");
   int mode=doc.GetSelectionMode();bool selected=(mode&15)!=0;
   if((mode&~31)!=0||(selected&&(mode&15)!=1))throw new InvalidOperationException("현재 선택 상태는 복원할 수 없습니다.");
   if(selected&&(!doc.GetSelectedPos(ref sl,ref sp,ref so,ref el,ref ep,ref eo)||sl!=el))throw new InvalidOperationException("현재 선택 범위를 복원할 수 없습니다.");
   var pairs=scan.Where(x=>x!=null&&x.List>=0&&x.Paragraph>=0).Select(x=>new{x.List,x.Paragraph}).Distinct().ToArray();
   if(pairs.Length>4096)throw new InvalidOperationException("분석할 한글 문단이 너무 많습니다.");
   var output=new List<InspectedNativeParagraph>();Exception error=null;bool restored=false,userMoved=false;string expected=NativeDocumentScan.CursorState(doc);
   try{
    foreach(var pair in pairs){
     if(NativeDocumentScan.CursorState(doc)!=expected){userMoved=true;throw new InvalidOperationException("문단 확인 중 커서 또는 선택이 바뀌었습니다.");}
     NativeEditor.ParagraphInfo p;
     // InspectParagraph can fail after moving the cursor. Its internal
     // failure state is not proven to be ours, so never restore over it.
     try{p=NativeEditor.InspectParagraph(doc,pair.List,pair.Paragraph);}catch{userMoved=true;throw;}
     int a=0,b=0,c=0,d=0,e=0,f=0,l=0,n=0,x=0;
     bool owned=p.NativeEnd>p.NativeStart
      ?(doc.GetSelectionMode()&15)==1&&doc.GetSelectedPos(ref a,ref b,ref c,ref d,ref e,ref f)&&a==pair.List&&d==pair.List&&b==pair.Paragraph&&e==pair.Paragraph&&Math.Min(c,f)==p.NativeStart&&Math.Max(c,f)==p.NativeEnd
      :(doc.GetSelectionMode()&15)==0&&doc.GetPos(ref l,ref n,ref x)&&l==pair.List&&n==pair.Paragraph&&x==p.NativeEnd;
     if(!owned){userMoved=true;throw new InvalidOperationException("문단 확인 후 커서 또는 선택이 달라졌습니다.");}
     expected=NativeDocumentScan.CursorState(doc);
     if(p.NativeEnd>p.NativeStart&&!String.IsNullOrEmpty(p.Text))output.Add(new InspectedNativeParagraph{List=pair.List,Paragraph=pair.Paragraph,Start=p.NativeStart,End=p.NativeEnd,Text=p.Text});
    }
   }catch(Exception ex){error=ex;}
   finally{try{var current=getDocument();if(!userMoved&&current!=null&&current.GetID()==snapshot.DocumentId&&(current.GetPath()??"")==snapshot.Path){if(NativeDocumentScan.CursorState(current)!=expected)userMoved=true;else restored=NativeDocumentScan.RestoreAndVerify(current,mode,list,para,pos,sl,sp,so,el,ep,eo);}}catch{restored=false;}}
   if(userMoved)throw new InvalidOperationException("문단 확인 중 커서나 선택이 달라져 사용자의 위치를 덮어쓰지 않았습니다.",error);
   if(!restored)throw new InvalidOperationException("문단 확인 후 원래 커서나 선택을 복원하지 못했습니다.",error);
   if(error!=null)throw new InvalidOperationException("문단 위치를 확인하지 못했습니다.",error);
   Check(getDocument(),snapshot);return output.ToArray();
  }
  static void Check(IHwpObject doc,NativePageSnapshot snapshot){
   if(doc==null||doc.GetID()!=snapshot.DocumentId||(doc.GetPath()??"")!=snapshot.Path||TableStableSnapshot.Digest(doc.GetTextFile("HWPML2X",""))!=snapshot.Digest)throw new InvalidOperationException("문서 내용을 읽는 동안 문서가 바뀌었습니다.");
  }
 }
}
