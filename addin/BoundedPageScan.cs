using System;
using System.Collections.Generic;

namespace HAddon {
 // Research only: no claim that page blocks include every nested/floating
 // object or that a scan address authorizes an edit. Native adapter and UI
 // integration must be validated on isolated HWP documents first.
 internal interface IBoundedPageScanBackend {
  void CheckDocument();
  string CursorState();
  void GotoPage(int page);
  void BeginSelection();
  bool HasSelection();
  bool InitSelectionScan();
  long ReadText(out string text);
  NativeScanItem CurrentScanPosition(long kind,string text);
  bool EndScan();
  bool Restore();
 }
 internal sealed class BoundedPageScanResult {
  internal NativeScanItem[] Items;
  // Block selection, not proof of total paragraph/control coverage.
  internal bool CoverageProven;
 }
 internal static class BoundedPageScan {
  const int MaxSteps=100000,MaxChars=400000;
  internal static BoundedPageScanResult Read(IBoundedPageScanBackend backend,int firstPage,int afterLastPage,int pageCount){
   if(backend==null)throw new ArgumentNullException("backend");
   if(firstPage<1||afterLastPage<=firstPage||afterLastPage>pageCount||pageCount>256)throw new InvalidOperationException("쪽 범위가 올바르지 않습니다.");
   backend.CheckDocument();string expected=backend.CursorState();bool owned=false,unknown=false,initialized=false;Exception error=null;var items=new List<NativeScanItem>();
   try{
    if(backend.CursorState()!=expected){unknown=true;throw new InvalidOperationException("사용자 커서가 바뀌었습니다.");}
    // If any command throws after being invoked, its caret effect is unknown.
    unknown=true;backend.GotoPage(firstPage);owned=true;expected=backend.CursorState();unknown=false;
    if(backend.CursorState()!=expected){unknown=true;throw new InvalidOperationException("쪽 시작 위치가 바뀌었습니다.");}
    unknown=true;backend.BeginSelection();expected=backend.CursorState();unknown=false;
    if(backend.CursorState()!=expected){unknown=true;throw new InvalidOperationException("선택 상태가 바뀌었습니다.");}
    unknown=true;backend.GotoPage(afterLastPage);expected=backend.CursorState();unknown=false;
    if(!backend.HasSelection()||backend.CursorState()!=expected)throw new InvalidOperationException("요청한 쪽 범위 선택을 확인하지 못했습니다.");
    if(!backend.InitSelectionScan())throw new InvalidOperationException("선택 범위 탐색을 시작하지 못했습니다.");initialized=true;
    int total=0;
    for(int step=0;;step++){
     if(step>=MaxSteps)throw new InvalidOperationException("쪽 탐색 단계가 너무 많습니다.");
     if(backend.CursorState()!=expected){unknown=true;throw new InvalidOperationException("탐색 중 사용자 커서가 바뀌었습니다.");}
     string text;long kind=backend.ReadText(out text);text=text??"";
     if(backend.CursorState()!=expected){unknown=true;throw new InvalidOperationException("읽기 중 사용자 커서가 바뀌었습니다.");}
     if(kind==1)break;if(kind==0||kind<2||kind>5)throw new InvalidOperationException("선택 범위 탐색이 완료되지 않았습니다.");
     if((total+=text.Length)>MaxChars)throw new InvalidOperationException("선택 범위가 너무 큽니다.");
     if(text.Length>0){unknown=true;var item=backend.CurrentScanPosition(kind,text);expected=backend.CursorState();unknown=false;if(item==null||item.List<0||item.Paragraph<0||item.Position<0)throw new InvalidOperationException("탐색 위치를 확인하지 못했습니다.");items.Add(item);}
    }
   }catch(Exception ex){error=ex;}
   finally{
    if(initialized)try{if(!backend.EndScan()&&error==null)error=new InvalidOperationException("문서 탐색 종료를 확인하지 못했습니다.");}catch(Exception ex){if(error==null)error=ex;}
    if(owned&&!unknown)try{if(backend.CursorState()!=expected)unknown=true;else if(!backend.Restore())throw new InvalidOperationException("원래 선택을 복원하지 못했습니다.");}catch(Exception ex){if(error==null)error=ex;}
   }
   if(unknown)throw new InvalidOperationException("사용자 커서나 선택이 달라졌을 수 있어 복원 명령을 실행하지 않았습니다.",error);
   if(error!=null)throw new InvalidOperationException("쪽 범위 탐색을 완료하지 못했습니다. "+error.Message,error);
   backend.CheckDocument();return new BoundedPageScanResult{Items=items.ToArray(),CoverageProven=false};
  }
 }
}
