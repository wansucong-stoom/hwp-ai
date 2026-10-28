using System;
using Hwp.Addon;

namespace HAddon {
 // Read-only adapter for an experimental selection-block scan. Never use
 // candidate locations as edit targets until native block coverage, page
 // boundaries and cross-list behavior are independently verified.
 internal sealed class NativeBoundedPageScan:IBoundedPageScanBackend {
  readonly Func<IHwpObject> getDocument;readonly NativePageSnapshot snapshot;
  readonly int list,para,pos,mode,sl,sp,so,el,ep,eo;
  internal NativeBoundedPageScan(Func<IHwpObject> getDocument,NativePageSnapshot snapshot){
   if(getDocument==null||snapshot==null)throw new ArgumentNullException("snapshot");this.getDocument=getDocument;this.snapshot=snapshot;
   var doc=Current();if(!doc.GetPos(ref list,ref para,ref pos))throw new InvalidOperationException("원래 커서 위치를 확인하지 못했습니다.");
   mode=doc.GetSelectionMode();if((mode&~31)!=0||((mode&15)!=0&&(mode&15)!=1))throw new InvalidOperationException("원래 선택 상태를 복원할 수 없습니다.");
   if((mode&15)==1&&(!doc.GetSelectedPos(ref sl,ref sp,ref so,ref el,ref ep,ref eo)||sl!=el))throw new InvalidOperationException("원래 선택 범위를 복원할 수 없습니다.");
  }
  IHwpObject Current(){var d=getDocument();if(d==null||d.GetID()!=snapshot.DocumentId||(d.GetPath()??"")!=(snapshot.Path??""))throw new InvalidOperationException("한글 문서가 바뀌었습니다.");return d;}
  public void CheckDocument(){var d=Current();if(snapshot.Pages==null||d.GetPageCount()!=snapshot.Pages.Length||TableStableSnapshot.Digest(d.GetTextFile("HWPML2X",""))!=snapshot.Digest)throw new InvalidOperationException("쪽 탐색 중 문서가 바뀌었습니다.");Current();}
  public string CursorState(){return NativeDocumentScan.CursorState(Current());}
  public void GotoPage(int page){
   var d=Current();var action=d.CreateAction("Goto");var set=action==null?null:action.CreateSet();
   if(action==null||set==null||!action.GetDefault(set))throw new InvalidOperationException("쪽 이동 명령을 준비하지 못했습니다.");
   set.SetItem("SetSelectionIndex",1);set.SetItem("DialogResult",page);
   if(action.Execute(set)==0)throw new InvalidOperationException("쪽 이동 명령이 실패했습니다.");
   int raw;if(!NativePageIndicator.TryReadRaw(Current(),out raw)||raw!=page)throw new InvalidOperationException("쪽 이동 결과를 확인하지 못했습니다.");
  }
  public void BeginSelection(){if(!Current().Run("Select"))throw new InvalidOperationException("쪽 범위 선택을 시작하지 못했습니다.");}
  public bool HasSelection(){var d=Current();int a=0,b=0,c=0,e=0,f=0,g=0;return (d.GetSelectionMode()&15)==1&&d.GetSelectedPos(ref a,ref b,ref c,ref e,ref f,ref g)&&(a!=e||b!=f||c!=g);}
  public bool InitSelectionScan(){return Current().InitScan(7,255,0,0,0,0);}
  public long ReadText(out string text){text="";return Current().GetText(ref text);}
  public NativeScanItem CurrentScanPosition(long kind,string text){var d=Current();int l=0,p=0,x=0;if(!d.MovePos(201,0,0)||!d.GetPos(ref l,ref p,ref x))throw new InvalidOperationException("선택 내 탐색 위치를 확인하지 못했습니다.");return new NativeScanItem{Kind=kind,Text=text,List=l,Paragraph=p,Position=x};}
  public bool EndScan(){return Current().ReleaseScan();}
  public bool Restore(){return NativeDocumentScan.RestoreAndVerify(Current(),mode,list,para,pos,sl,sp,so,el,ep,eo);}
 }
}
