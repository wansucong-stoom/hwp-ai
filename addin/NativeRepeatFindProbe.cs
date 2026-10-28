using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hwp.Addon;

namespace HAddon {
 // Native read-only locator. It changes the caret temporarily and may update
 // HWP's remembered Find settings, but never issues a document write command.
 // Physical-page and XML/native paragraph-order semantics are separate facts
 // supplied by the caller after native verification, not inferred here.
 internal sealed class NativeRepeatFindProbe:IOnDemandNativeProbe {
  readonly Func<IHwpObject> getDocument;readonly NativePageSnapshot snapshot;readonly HwpParagraphProofs proofs;readonly int uiThread;
  readonly bool physicalVerified,relativeVerified,coordinatedRead;
  readonly Func<IHwpObject,int,int,NativeEditor.ParagraphInfo> inspect;
  readonly Cursor original;
  readonly Dictionary<string,string> anchors=new Dictionary<string,string>(StringComparer.Ordinal);
  internal bool SearchSettingsMayHaveChanged{get;private set;}
  internal int SearchCalls{get;private set;}
  sealed class Cursor {
   internal int List,Para,Pos,Mode,A,B,C,D,E,F;
   internal string Key{get{return List+":"+Para+":"+Pos+":"+Mode+":"+A+":"+B+":"+C+":"+D+":"+E+":"+F;}}
  }
  internal NativeRepeatFindProbe(Func<IHwpObject> getDocument,NativePageSnapshot snapshot,HwpParagraphProofs proofs,bool physicalPageSemanticsVerified,bool relativeParagraphOrderVerified=false)
   :this(getDocument,snapshot,proofs,physicalPageSemanticsVerified,relativeParagraphOrderVerified,NativeEditor.InspectParagraph){}
  // The automatic coordinator independently verifies the complete revision
  // immediately before and after Locate. Only that synchronous read phase may
  // use this factory; it must never span an await or include document writes.
  internal static NativeRepeatFindProbe CreateCoordinatedRead(Func<IHwpObject> getDocument,NativePageSnapshot snapshot,HwpParagraphProofs proofs,bool physicalPageSemanticsVerified,bool relativeParagraphOrderVerified=false){return new NativeRepeatFindProbe(getDocument,snapshot,proofs,physicalPageSemanticsVerified,relativeParagraphOrderVerified,NativeEditor.InspectParagraph,true);}
  internal NativeRepeatFindProbe(Func<IHwpObject> getDocument,NativePageSnapshot snapshot,HwpParagraphProofs proofs,bool physicalPageSemanticsVerified,bool relativeParagraphOrderVerified,Func<IHwpObject,int,int,NativeEditor.ParagraphInfo> inspect,bool coordinatedRead=false){
   if(getDocument==null||snapshot==null||proofs==null||inspect==null)throw new ArgumentNullException("snapshot");
   this.getDocument=getDocument;uiThread=System.Threading.Thread.CurrentThread.ManagedThreadId;this.snapshot=new NativePageSnapshot{DocumentId=snapshot.DocumentId,Path=snapshot.Path,Digest=snapshot.Digest,Pages=snapshot.Pages==null?null:(string[])snapshot.Pages.Clone()};
   this.proofs=proofs;this.inspect=inspect;physicalVerified=physicalPageSemanticsVerified;relativeVerified=relativeParagraphOrderVerified;this.coordinatedRead=coordinatedRead;
   if(this.snapshot.Pages==null||this.snapshot.Pages.Length==0||String.IsNullOrEmpty(this.snapshot.Digest))throw new InvalidOperationException("문서 기준 상태가 없습니다.");
   CheckSnapshot();original=ReadCursor(Current());if((original.Mode&~31)!=0||((original.Mode&15)!=0&&(original.Mode&15)!=1))throw new InvalidOperationException("현재 선택은 안전하게 복원할 수 없습니다.");
   if((original.Mode&15)==1&&original.A!=original.D)throw new InvalidOperationException("현재 선택은 여러 영역에 걸쳐 있습니다.");
  }
  IHwpObject Current(){if(System.Threading.Thread.CurrentThread.ManagedThreadId!=uiThread)throw new InvalidOperationException("한글 문단 찾기 스레드가 변경되었습니다.");var d=getDocument();if(d==null||d.GetID()!=snapshot.DocumentId||(d.GetPath()??"")!=(snapshot.Path??""))throw new InvalidOperationException("원래 한글 문서가 바뀌었습니다.");return d;}
  public void CheckSnapshot(){var d=Current();if(!coordinatedRead&&TableStableSnapshot.Digest(d.GetTextFile("HWPML2X",""))!=snapshot.Digest)throw new InvalidOperationException("문단을 찾는 동안 문서가 바뀌었습니다.");Current();}
  static Cursor ReadCursor(IHwpObject d){var c=new Cursor();c.Mode=d.GetSelectionMode();if(!d.GetPos(ref c.List,ref c.Para,ref c.Pos))throw new InvalidOperationException("현재 커서를 읽지 못했습니다.");if((c.Mode&15)==1&&!d.GetSelectedPos(ref c.A,ref c.B,ref c.C,ref c.D,ref c.E,ref c.F))throw new InvalidOperationException("현재 선택을 읽지 못했습니다.");return c;}
  void RequireOriginal(){if(ReadCursor(Current()).Key!=original.Key)throw new InvalidOperationException("사용자의 커서나 선택이 바뀌었습니다.");}
  bool Restore(Cursor owned){
   var d=Current();if(owned==null||ReadCursor(d).Key!=owned.Key)return false;
   if(!NativeDocumentScan.RestoreAndVerify(d,original.Mode,original.List,original.Para,original.Pos,original.A,original.B,original.C,original.D,original.E,original.F))return false;
   return ReadCursor(d).Key==original.Key;
  }
  static bool SameHit(Cursor c,int list,int para,int start,int end){return (c.Mode&15)==1&&c.A==list&&c.D==list&&c.B==para&&c.E==para&&Math.Min(c.C,c.F)==start&&Math.Max(c.C,c.F)==end;}
  Cursor Select(IHwpObject d,int list,int para,int start,int end){if(!NativeEditor.SelectRange(d,list,para,start,end))throw new InvalidOperationException("고정 문단의 선택을 확인하지 못했습니다.");var c=ReadCursor(d);if(!SameHit(c,list,para,start,end))throw new InvalidOperationException("선택한 문단 위치가 다릅니다.");return c;}
  static bool Plain(string text){return !String.IsNullOrEmpty(text)&&text.Length<=12000&&!text.Any(c=>Char.IsControl(c)||Char.IsSurrogate(c)||c=='\u2028'||c=='\u2029'||c=='\uFFFC');}
  IHwpParameterSet FindSet(IHwpAction action,string text){
   var s=action.CreateSet();if(s==null)throw new InvalidOperationException("한글 찾기 조건을 준비하지 못했습니다.");
   // Deliberately do not GetDefault: it can reintroduce remembered font/para
   // filters. A fresh set and explicit literal-search flags are independent.
   s.SetItem("FindString",text);s.SetItem("Direction",0);s.SetItem("FindType",1);s.SetItem("MatchCase",1);s.SetItem("WholeWordOnly",0);s.SetItem("UseWildCards",0);s.SetItem("SeveralWords",0);s.SetItem("AutoSpell",0);s.SetItem("HanjaFromHangul",0);s.SetItem("AllWordForms",0);s.SetItem("FindJaso",0);s.SetItem("FindRegExp",0);s.SetItem("FindStyle","");s.SetItem("ReplaceStyle","");s.SetItem("IgnoreMessage",1);
   return s;
  }
  public OnDemandAnchorSearch FindAnchor(string exactText){
   if(!Plain(exactText))throw new InvalidOperationException("찾을 문단 원문이 올바르지 않습니다.");CheckSnapshot();RequireOriginal();var d=Current();Cursor owned=null,firstHit=null,startCursor=null;bool moved=false;long oldMask=d.GetMessageBoxMode();const uint mask=0x00020000;var found=new List<OnDemandObservedParagraph>();var seen=new HashSet<string>(StringComparer.Ordinal);bool complete=false;Exception failure=null;
   try{
    d.SetMessageBoxMode(mask);
    if(!d.MovePos(2,0,0))throw new InvalidOperationException("문서 시작으로 이동하지 못했습니다.");moved=true;owned=ReadCursor(d);startCursor=owned;if(owned.List!=0||owned.Para!=0||(owned.Mode&15)!=0)throw new InvalidOperationException("문서 시작 위치를 확인하지 못했습니다.");
    for(int step=0;step<512;step++){
     if(ReadCursor(d).Key!=owned.Key)throw new InvalidOperationException("검색 중 사용자의 커서나 선택이 바뀌었습니다.");CheckSnapshot();
     var action=d.CreateAction("RepeatFind");if(action==null)throw new InvalidOperationException("한글 찾기 명령을 만들지 못했습니다.");var set=FindSet(action,exactText);
     if(ReadCursor(d).Key!=owned.Key)throw new InvalidOperationException("검색 준비 중 커서나 선택이 바뀌었습니다.");
     SearchSettingsMayHaveChanged=true;SearchCalls++;long result=action.Execute(set);
     if(result==0){var ended=ReadCursor(d);
      // HWP 2024's forward search can report no further match after it
      // collapses to the first search hit. Accept only this observed exact
      // engine landing point (or unchanged/document-start state), not an
      // arbitrary new caret position that could be the user's input.
      bool firstStart=firstHit!=null&&(ended.Mode&15)==0&&ended.List==firstHit.A&&ended.Para==firstHit.B&&ended.Pos==Math.Min(firstHit.C,firstHit.F);
      if(ended.Key!=owned.Key&&ended.Key!=startCursor.Key&&!firstStart)throw new InvalidOperationException("검색 종료 후 커서나 선택이 달라졌습니다.");owned=ended;complete=true;break;}
     var hit=ReadCursor(d);if((hit.Mode&15)!=1||hit.A!=hit.D||hit.B!=hit.E)throw new InvalidOperationException("찾은 문단의 선택 범위를 확인하지 못했습니다.");int start=Math.Min(hit.C,hit.F),end=Math.Max(hit.C,hit.F);
     string selected=NativeEditor.NormalizeSelectedText(d.GetSelectText(),null,exactText,end-start);
     if(selected!=exactText||end-start!=exactText.Length)throw new InvalidOperationException("찾은 원문이 요청 문단과 다릅니다.");owned=hit;if(firstHit==null)firstHit=hit;string key=hit.A+":"+hit.B+":"+start+":"+end;
     if(!seen.Add(key)){complete=true;break;}
     // Inspecting the whole paragraph rejects a substring-only hit. Return to
     // the exact hit before continuing so search traversal does not skip text.
     var observed=InspectCore(d,hit.A,hit.B,ref owned);
     if(observed.Text==exactText&&observed.WholeParagraph&&!observed.HasObjects){found.Add(observed);var unique=proofs.MatchSelected(d.GetTextFile("HWPML2X","saveblock"));if(unique.Unique)anchors[hit.A+":"+hit.B]=unique.XmlPath;}
     owned=Select(d,hit.A,hit.B,start,end);
    }
    CheckSnapshot();return new OnDemandAnchorSearch{Complete=complete,Matches=found.ToArray()};
   }catch(Exception error){failure=error;throw;}finally{
    try{if(Current().GetMessageBoxMode()==mask)Current().SetMessageBoxMode(unchecked((uint)oldMask));}catch{}
    if(moved&&!Restore(owned))throw new InvalidOperationException("검색 후 사용자 위치가 달라져 원래 커서로 덮어쓰지 않았습니다."+(failure==null?"":" 원인: "+failure.Message),failure);
   }
  }
  public OnDemandObservedParagraph InspectParagraph(int list,int paragraph){
   if(list<0||paragraph<0)throw new InvalidOperationException("문단 위치가 올바르지 않습니다.");CheckSnapshot();RequireOriginal();var d=Current();Cursor owned=null;bool moved=false;Exception failure=null;
   try{moved=true;var observed=InspectCore(d,list,paragraph,ref owned);CheckSnapshot();return observed;}
   catch(Exception error){failure=error;throw;}
   finally{if(moved&&!Restore(owned))throw new InvalidOperationException("문단 확인 후 사용자의 커서나 선택을 덮어쓰지 않았습니다."+(failure==null?"":" 원인: "+failure.Message),failure);}
  }
  OnDemandObservedParagraph InspectCore(IHwpObject d,int list,int paragraph,ref Cursor owned){
   // Do not assume ownership of an unknown exception state from the native
   // inspector. On failure the outer finally refuses to restore over a user.
   owned=null;var p=inspect(d,list,paragraph);owned=ReadCursor(d);
   if(p==null||p.NativeStart<0||p.NativeEnd<=p.NativeStart||!Plain(p.Text)||p.NativeEnd-p.NativeStart!=p.Text.Length||!SameHit(owned,list,paragraph,p.NativeStart,p.NativeEnd))throw new InvalidOperationException("문단 원문과 한글 위치를 대조하지 못했습니다.");
   string block=d.GetTextFile("HWPML2X","saveblock");if(ReadCursor(d).Key!=owned.Key)throw new InvalidOperationException("원문을 읽는 동안 선택이 바뀌었습니다.");var proof=proofs.MatchSelected(block);
   OnDemandExpectedTarget contextual=null;
   if(!proof.Unique&&relativeVerified){
    foreach(var entry in anchors){var parts=entry.Key.Split(':');if(Int32.Parse(parts[0],CultureInfo.InvariantCulture)!=list)continue;int anchorParagraph=Int32.Parse(parts[1],CultureInfo.InvariantCulture);string path=Sibling(entry.Value,paragraph-anchorParagraph);if(path!=null&&proofs.MatchesExpected(path,block)){var candidate=proofs.CreateExpected(path);if(contextual!=null&&contextual.XmlPath!=candidate.XmlPath){contextual=null;break;}contextual=candidate;}}
   }
   int first,last;owned=At(d,list,paragraph,p.NativeStart);if(!NativePageIndicator.TryReadRaw(d,out first))throw new InvalidOperationException("문단 시작 쪽을 읽지 못했습니다.");if(ReadCursor(d).Key!=owned.Key)throw new InvalidOperationException("쪽 확인 중 커서가 바뀌었습니다.");owned=At(d,list,paragraph,p.NativeEnd-1);if(!NativePageIndicator.TryReadRaw(d,out last))throw new InvalidOperationException("문단 끝 쪽을 읽지 못했습니다.");if(ReadCursor(d).Key!=owned.Key)throw new InvalidOperationException("쪽 확인 중 커서가 바뀌었습니다.");owned=Select(d,list,paragraph,p.NativeStart,p.NativeEnd);
   bool mapped=proof.Unique||contextual!=null;
   return new OnDemandObservedParagraph{List=list,Paragraph=paragraph,Start=p.NativeStart,End=p.NativeEnd,Text=p.Text,WholeParagraph=true,TextVerified=proof.Text==p.Text,HasObjects=false,
    FirstPhysicalPage=first,LastPhysicalPage=last,PhysicalPageVerified=physicalVerified&&first>=1&&last>=1&&first<=snapshot.Pages.Length&&last<=snapshot.Pages.Length&&first==last&&PageContains(snapshot.Pages[first-1],p.Text),
    ContainerVerified=mapped,StyleVerified=mapped,StructureVerified=mapped,ContainerToken=proof.Unique?proof.ContainerToken:contextual==null?null:contextual.ContainerToken,StyleToken=proof.Unique?proof.StyleToken:contextual==null?null:contextual.StyleToken,StructureToken=proof.Unique?proof.StructureToken:contextual==null?null:contextual.StructureToken};
  }
  static bool PageContains(string page,string text){if(page==null||text==null)return false;string expected=Compact(text);return expected.Length>0&&Compact(page).IndexOf(expected,StringComparison.Ordinal)>=0;}
  static string Compact(string value){return new string(value.Where(c=>!Char.IsWhiteSpace(c)).ToArray());}
  static string Sibling(string path,int delta){int bracket=path.LastIndexOf("/P[",StringComparison.Ordinal);if(bracket<0||!path.EndsWith("]"))return null;int index;if(!Int32.TryParse(path.Substring(bracket+3,path.Length-bracket-4),out index))return null;long next=(long)index+delta;return next<0||next>Int32.MaxValue?null:path.Substring(0,bracket)+"/P["+next+"]";}
  Cursor At(IHwpObject d,int list,int paragraph,int pos){if(!d.SetPos(list,paragraph,pos))throw new InvalidOperationException("문단 위치로 이동하지 못했습니다.");var c=ReadCursor(d);if(c.List!=list||c.Para!=paragraph||c.Pos!=pos||(c.Mode&15)!=0)throw new InvalidOperationException("이동한 문단 위치가 다릅니다.");return c;}
 }
}
