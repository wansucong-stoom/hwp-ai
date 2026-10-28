using System;
using System.Collections.Generic;
using Hwp.Addon;

namespace HAddon {
 // Experimental read-only HWP scan. Scan positions are native positions, but
 // their relation to returned text and rendered pages has NOT been validated
 // on desktop HWP 2024. This does not authorize edits at those positions.
 internal sealed class NativeScanItem {internal long Kind;internal string Text;internal int List,Paragraph,Position;}
 internal static class NativeDocumentScan {
  const int MaxSteps=100000,MaxChars=400000;
  internal static string CursorState(IHwpObject doc){
   int l=0,p=0,x=0,mode=doc.GetSelectionMode();if(!doc.GetPos(ref l,ref p,ref x))throw new InvalidOperationException("현재 커서를 확인하지 못했습니다.");
   string result=l+":"+p+":"+x+":"+mode;
   if((mode&15)==1){int a=0,b=0,c=0,d=0,e=0,f=0;if(!doc.GetSelectedPos(ref a,ref b,ref c,ref d,ref e,ref f))throw new InvalidOperationException("현재 선택을 확인하지 못했습니다.");result+=":"+a+":"+b+":"+c+":"+d+":"+e+":"+f;}
   return result;
  }
  // A successful SetPos/SelectText return does not prove that HWP restored
  // the requested caret or selection. Read the actual state before reporting
  // success; in particular never mistake a collapsed selection for a restore.
  internal static bool RestoreAndVerify(IHwpObject doc,int mode,int list,int para,int pos,int sl,int sp,int so,int el,int ep,int eo){
   if(doc==null)return false;
   if((mode&15)==1){
    if(!doc.SetPos(sl,sp,so)||!doc.SelectText(sp,so,ep,eo))return false;
    int a=0,b=0,c=0,d=0,e=0,f=0;
    return doc.GetSelectionMode()==mode&&doc.GetSelectedPos(ref a,ref b,ref c,ref d,ref e,ref f)&&a==sl&&b==sp&&c==so&&d==el&&e==ep&&f==eo;
   }
   int l=0,p=0,x=0;
   return doc.SetPos(list,para,pos)&&doc.GetSelectionMode()==mode&&doc.GetPos(ref l,ref p,ref x)&&l==list&&p==para&&x==pos;
  }
  internal static NativeScanItem[] Read(Func<IHwpObject> getDocument){
   if(getDocument==null)throw new ArgumentNullException("getDocument");
   var doc=getDocument();if(doc==null)throw new InvalidOperationException("한글 문서를 확인하지 못했습니다.");
   int id=doc.GetID();string path=doc.GetPath()??"",before=TableStableSnapshot.Digest(doc.GetTextFile("HWPML2X",""));
   int list=0,para=0,pos=0,sl=0,sp=0,so=0,el=0,ep=0,eo=0;
   if(!doc.GetPos(ref list,ref para,ref pos))throw new InvalidOperationException("원래 커서를 확인하지 못했습니다.");
   int mode=doc.GetSelectionMode();bool selected=(mode&15)!=0;
   if((mode&~31)!=0||(selected&&(mode&15)!=1))throw new InvalidOperationException("현재 개체 또는 셀 선택 상태는 복원할 수 없습니다.");
   if(selected&&(!doc.GetSelectedPos(ref sl,ref sp,ref so,ref el,ref ep,ref eo)||sl!=el))throw new InvalidOperationException("현재 선택 범위를 복원할 수 없습니다.");
   var result=new List<NativeScanItem>();int chars=0;bool initialized=false,restoreOk=false,userMoved=false;Exception scanError=null;
   string expectedState=CursorState(doc);
   try {
    // HWP 2024 COM synthetic test: option 15, range 0x77 and -1 end
    // coordinates traversed the body and four table cell lists. Add-on
    // IHwpObject behavior and complete control coverage remain unverified.
    if(!doc.InitScan(15,119,0,0,-1,-1))throw new InvalidOperationException("한글 문서 탐색을 시작하지 못했습니다.");initialized=true;
    for(int step=0;;step++){
     if(step>=MaxSteps)throw new InvalidOperationException("문서 탐색 범위가 너무 큽니다.");
     if(CursorState(doc)!=expectedState){userMoved=true;throw new InvalidOperationException("탐색 중 커서 또는 선택이 변경되었습니다.");}
     string text="";long kind=doc.GetText(ref text);text=text??"";
     if(CursorState(doc)!=expectedState){userMoved=true;throw new InvalidOperationException("탐색 중 커서 또는 선택이 변경되었습니다.");}
     if(kind==1)break;
     if(kind==0)throw new InvalidOperationException("문서 탐색이 끝나기 전에 텍스트를 읽지 못했습니다.");
     if(kind<2||kind>5)throw new InvalidOperationException("한글 문서 탐색에서 오류가 발생했습니다. [상태="+kind+"]");
     if((chars+=text.Length)>MaxChars)throw new InvalidOperationException("문서 탐색 내용이 너무 깁니다.");
     // In the HWP 2024 COM experiment kind 4 (enter control) carried the
     // first cell's text. Exit-control events can also carry text on other
     // documents, so retain every nonempty event from 2 through 5 as a
     // candidate; its position still needs independent inspection.
     if(text.Length>0){
      int l=0,p=0,x=0;if(!doc.MovePos(201,0,0)||!doc.GetPos(ref l,ref p,ref x)||l<0||p<0||x<0)throw new InvalidOperationException("탐색한 문서 위치를 확인하지 못했습니다.");
      expectedState=CursorState(doc);
      result.Add(new NativeScanItem{Kind=kind,Text=text,List=l,Paragraph=p,Position=x});
     }
    }
   }catch(Exception ex){scanError=ex;}
   finally{
    if(initialized)try{if(!doc.ReleaseScan()&&scanError==null)scanError=new InvalidOperationException("문서 탐색 종료를 확인하지 못했습니다.");}catch(Exception ex){if(scanError==null)scanError=ex;}
    try{var now=getDocument();if(!userMoved&&now!=null&&now.GetID()==id&&(now.GetPath()??"")==path){
      if(CursorState(now)!=expectedState){userMoved=true;}
      else
      restoreOk=RestoreAndVerify(now,mode,list,para,pos,sl,sp,so,el,ep,eo);
     }}catch{restoreOk=false;}
   }
   if(userMoved)throw new InvalidOperationException("문서 탐색 중 사용자의 커서나 선택이 바뀌어 원래 위치로 덮어쓰지 않았습니다.",scanError);
   if(!restoreOk)throw new InvalidOperationException("문서 탐색 후 원래 커서나 선택을 복원하지 못했습니다.",scanError);
   if(scanError!=null)throw new InvalidOperationException("문서 탐색을 완료하지 못했습니다. "+scanError.Message,scanError);
   var final=getDocument();if(final==null||final.GetID()!=id||(final.GetPath()??"")!=path||TableStableSnapshot.Digest(final.GetTextFile("HWPML2X",""))!=before)throw new InvalidOperationException("문서 탐색 중 내용이나 서식이 변경되었습니다.");
   return result.ToArray();
  }
 }
}
