using System;
using System.Collections.Generic;
using System.Linq;
using Hwp.Addon;

namespace HAddon {
 // Experimental read-only page evidence. A matching KeyIndicator number at
 // both ends is NOT yet proof of a physical page: indexing, page-number
 // resets, controls and split/merged cells require native-HWP verification.
 // Never use this component on its own to authorize a document edit.
 internal interface IPagePositionBackend {
  string CursorState();
  void CheckDocument();
  bool Move(int list,int paragraph,int position);
  bool IsAt(int list,int paragraph,int position);
  bool TryPage(out int rawPage);
  bool Restore();
 }
 internal static class NativePagePositionEvidence {
  const int MaxTargets=4096;
  // Accept only previously inspected native ranges. Distinct list/paragraph
  // pairs and XML paths prevent two IDs from silently targeting one paragraph.
  internal static PagePositionEvidence[] Collect(IPagePositionBackend backend,DocumentAddressCandidate[] candidates){
   if(backend==null||candidates==null)throw new ArgumentNullException("backend");
   if(candidates.Length>MaxTargets)throw new InvalidOperationException("쪽 위치 확인 대상이 너무 많습니다.");
   var frozen=new List<DocumentAddressCandidate>();var paths=new HashSet<string>(StringComparer.Ordinal);var positions=new HashSet<string>(StringComparer.Ordinal);
   foreach(var c in candidates){
    if(c==null||String.IsNullOrEmpty(c.XmlPath)||c.List<0||c.Paragraph<0||c.Start<0||c.End<=c.Start||!paths.Add(c.XmlPath)||!positions.Add(c.List+":"+c.Paragraph))throw new InvalidOperationException("문단 위치가 중복되거나 올바르지 않습니다.");
    frozen.Add(new DocumentAddressCandidate{XmlPath=c.XmlPath,List=c.List,Paragraph=c.Paragraph,Start=c.Start,End=c.End,Text=c.Text,Page=c.Page});
   }
   backend.CheckDocument();string expected=backend.CursorState();bool owned=false,userMoved=false;Exception failure=null;var output=new List<PagePositionEvidence>();
   try{
    foreach(var c in frozen){
     int first,last;
     if(backend.CursorState()!=expected){userMoved=true;throw new InvalidOperationException("쪽 위치 확인 중 커서 또는 선택이 바뀌었습니다.");}
     if(!backend.Move(c.List,c.Paragraph,c.Start)){owned=true;throw new InvalidOperationException("문단 시작 위치로 이동하지 못했습니다.");}
     owned=true;
     if(!backend.IsAt(c.List,c.Paragraph,c.Start)){userMoved=true;throw new InvalidOperationException("문단 시작 위치가 바뀌었습니다.");}
     expected=backend.CursorState();if(!backend.IsAt(c.List,c.Paragraph,c.Start)){userMoved=true;throw new InvalidOperationException("문단 시작 위치가 바뀌었습니다.");}
     if(!backend.TryPage(out first))throw new InvalidOperationException("문단 시작의 쪽 정보를 읽지 못했습니다.");
     if(backend.CursorState()!=expected||!backend.IsAt(c.List,c.Paragraph,c.Start)){userMoved=true;throw new InvalidOperationException("쪽 읽기 중 커서 또는 선택이 바뀌었습니다.");}
     // The half-open end itself can belong to the next page. The last
     // selected character is at End - 1; controls and surrogate text need
     // separate native verification before this can be relied on.
     if(!backend.Move(c.List,c.Paragraph,c.End-1))throw new InvalidOperationException("문단 끝 위치로 이동하지 못했습니다.");
     if(!backend.IsAt(c.List,c.Paragraph,c.End-1)){userMoved=true;throw new InvalidOperationException("문단 끝 위치가 바뀌었습니다.");}
     expected=backend.CursorState();if(!backend.IsAt(c.List,c.Paragraph,c.End-1)){userMoved=true;throw new InvalidOperationException("문단 끝 위치가 바뀌었습니다.");}
     if(!backend.TryPage(out last))throw new InvalidOperationException("문단 끝의 쪽 정보를 읽지 못했습니다.");
     if(backend.CursorState()!=expected||!backend.IsAt(c.List,c.Paragraph,c.End-1)){userMoved=true;throw new InvalidOperationException("쪽 읽기 중 커서 또는 선택이 바뀌었습니다.");}
     // A spanning paragraph cannot be pinned to one page. Leave it out; it
     // is not safe to truncate or rewrite it based on one page alone.
     if(first==last&&first>0)output.Add(new PagePositionEvidence{XmlPath=c.XmlPath,List=c.List,Paragraph=c.Paragraph,Start=c.Start,End=c.End,RawPage=first});
    }
   }catch(Exception ex){failure=ex;}
   finally{
    if(owned&&!userMoved){try{if(backend.CursorState()!=expected)userMoved=true;else if(!backend.Restore())throw new InvalidOperationException("원래 커서나 선택을 복원하지 못했습니다.");}catch(Exception ex){if(failure==null)failure=ex;}}
   }
   if(userMoved)throw new InvalidOperationException("사용자의 커서나 선택이 바뀌어 원래 위치로 덮어쓰지 않았습니다.",failure);
   if(failure!=null)throw new InvalidOperationException("쪽 위치 확인을 완료하지 못했습니다. "+failure.Message,failure);
   backend.CheckDocument();return output.ToArray();
  }
 }
 // Native adapter retains the originating document and original caret/selection
 // and rechecks both identity and the full document snapshot. Calls must all
 // run on the HWP UI thread; this synchronous experiment is not UI-wired.
 internal sealed class NativePagePositionBackend:IPagePositionBackend {
  readonly Func<IHwpObject> getDocument;readonly NativePageSnapshot snapshot;
  readonly int list,para,pos,mode,sl,sp,so,el,ep,eo;
  internal NativePagePositionBackend(Func<IHwpObject> getDocument,NativePageSnapshot snapshot){
   if(getDocument==null||snapshot==null)throw new ArgumentNullException("snapshot");
   if(snapshot.Pages==null||snapshot.Pages.Length==0||String.IsNullOrEmpty(snapshot.Digest))throw new InvalidOperationException("원래 쪽 스냅샷이 없습니다.");
   this.getDocument=getDocument;this.snapshot=snapshot;
   var doc=Current();if(!doc.GetPos(ref list,ref para,ref pos))throw new InvalidOperationException("원래 커서를 확인하지 못했습니다.");
   mode=doc.GetSelectionMode();if((mode&~31)!=0||((mode&15)!=0&&(mode&15)!=1))throw new InvalidOperationException("현재 선택 상태를 복원할 수 없습니다.");
   if((mode&15)==1&&(!doc.GetSelectedPos(ref sl,ref sp,ref so,ref el,ref ep,ref eo)||sl!=el))throw new InvalidOperationException("현재 선택을 복원할 수 없습니다.");
  }
  IHwpObject Current(){var doc=getDocument();if(doc==null||doc.GetID()!=snapshot.DocumentId||(doc.GetPath()??"")!=snapshot.Path)throw new InvalidOperationException("원래 한글 문서가 바뀌었습니다.");return doc;}
  public void CheckDocument(){var doc=Current();if(doc.GetPageCount()!=snapshot.Pages.Length||TableStableSnapshot.Digest(doc.GetTextFile("HWPML2X",""))!=snapshot.Digest)throw new InvalidOperationException("쪽 확인 중 문서가 바뀌었습니다.");Current();}
  public string CursorState(){var doc=Current();int l=0,p=0,x=0,m=doc.GetSelectionMode();if(!doc.GetPos(ref l,ref p,ref x))throw new InvalidOperationException("현재 커서를 확인하지 못했습니다.");string result=l+":"+p+":"+x+":"+m;
   if((m&15)==1){int a=0,b=0,c=0,d=0,e=0,f=0;if(!doc.GetSelectedPos(ref a,ref b,ref c,ref d,ref e,ref f))throw new InvalidOperationException("현재 선택을 확인하지 못했습니다.");result+=":"+a+":"+b+":"+c+":"+d+":"+e+":"+f;}return result;}
  public bool Move(int l,int p,int x){return Current().SetPos(l,p,x);}
  public bool IsAt(int l,int p,int x){var doc=Current();int a=0,b=0,c=0;return doc.GetSelectionMode()==0&&doc.GetPos(ref a,ref b,ref c)&&a==l&&b==p&&c==x;}
  public bool TryPage(out int page){return NativePageIndicator.TryReadRaw(Current(),out page);}
  public bool Restore(){return NativeDocumentScan.RestoreAndVerify(Current(),mode,list,para,pos,sl,sp,so,el,ep,eo);}
 }
}
