using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Hwp.Addon;

namespace HAddon {
 internal sealed class SelectionEditor {
  internal const int MaxParagraphs = 24, MaxText = 12000;
  readonly ISelectionBackend backend;
  readonly List<SelectionSpan> spans = new List<SelectionSpan>();
  SelectionRange range;
  string targetId, documentHash, reason;
  bool active, selectionPresent;
  int selectionMode;
  public bool RequiresReview { get; private set; }
  public SelectionEditor(Func<IHwpObject> getDocument) : this(new NativeSelectionBackend(getDocument)) {}
  internal SelectionEditor(ISelectionBackend backend) {
   if (backend == null) throw new ArgumentNullException("backend");
   this.backend = backend;
  }
  public object BeginRequest() {
   active = true; RequiresReview = false; selectionPresent = true;
   spans.Clear(); range = null; targetId = null; reason = null; documentHash = null;
   backend.Reset();
   try {
    SelectionInfo current = backend.ReadSelection();
    selectionPresent = current.Present; selectionMode = current.Mode;
    if (!selectionPresent) { reason = ""; return State(); }
    int mode = current.Mode & 15;
    if (mode != 1 || current.HasObject) {
     reason = mode == 3 ? "셀 블록·여러 셀 선택은 지원하지 않습니다. 같은 셀 안의 글을 드래그하세요."
      : mode == 2 ? "사각형 글자 블록은 지원하지 않습니다. 일반 텍스트 범위를 드래그하세요."
      : "표·그림·개체 선택은 이 텍스트 편집 경로에서 지원하지 않습니다.";
     return State();
    }
    if (current.Range == null) { reason = "선택이 서로 다른 셀·영역에 걸쳐 있습니다. 같은 영역 안의 글을 선택하세요."; return State(); }
    range = current.Range.Copy();
    if (range.EndParagraph - range.StartParagraph + 1 > MaxParagraphs)
     throw new InvalidOperationException("한 번에 최대 24문단까지만 선택할 수 있습니다.");
    try {
     for (int paragraph = range.StartParagraph; paragraph <= range.EndParagraph; paragraph++) {
      ParagraphBounds bounds = backend.Inspect(range.List, paragraph);
      int start = paragraph == range.StartParagraph ? range.Start : bounds.Start;
      int end = paragraph == range.EndParagraph ? range.End : bounds.End;
      if (start < bounds.Start || end > bounds.End || start > end)
       throw new InvalidOperationException("선택 범위에 숨은 컨트롤이나 문단 경계가 포함되어 정확히 대응할 수 없습니다.");
      if (paragraph > range.StartParagraph && paragraph < range.EndParagraph && bounds.Start != 0)
       throw new InvalidOperationException("선택 중간에 구역·단 정의가 있어 이 범위는 편집하지 않습니다.");
      var partRange = new SelectionRange {List=range.List,StartParagraph=paragraph,EndParagraph=paragraph,Start=start,End=end};
      SelectionSpan part = backend.Capture(partRange);
      if (part.ParagraphText != bounds.Text || part.ContentStart != bounds.Start || part.ContentEnd != bounds.End)
       throw new InvalidOperationException("선택을 읽는 동안 문단이 바뀌었습니다.");
      spans.Add(part);
     }
     if (String.Join("\n",spans.Select(p=>p.Text)).Length > MaxText)
      throw new InvalidOperationException("선택 본문은 문단 구분을 포함하여 12,000자 이하여야 합니다.");
    } finally { Restore(); }
    documentHash=backend.DocumentHash(); VerifyAll(false); documentHash=backend.DocumentHash(); targetId=NewId();
   } catch(Exception ex) { Invalidate(ex.Message); }
   return State();
  }
  public void EndRequest() {active=false;spans.Clear();targetId=null;range=null;documentHash=null;}
  public object Invoke(string method,Dictionary<string,object> args) {
   if(!active)throw new InvalidOperationException("편집 요청이 끝났습니다. 새 요청을 시작하세요.");
   args=args??new Dictionary<string,object>();
   if(method=="hwp_editor_state") {Keys(args,new string[0]);if(targetId!=null)VerifyAll(true);return State();}
   if(method!="hwp_editor_rewrite"&&method!="hwp_editor_format")throw new InvalidOperationException("지원하지 않는 편집 도구입니다.");
   Keys(args,method=="hwp_editor_rewrite"?new string[]{"expectedTargetId","replacement"}:new string[]{"expectedTargetId","patch"});
   object expected;
   if(targetId==null||!args.TryGetValue("expectedTargetId",out expected)||!(expected is string)||(string)expected!=targetId)
    throw new InvalidOperationException("현재 고정 선택의 targetId가 필요합니다.");
   string[] replacements=null;Dictionary<string,object> patch=null;object raw;
   if(method=="hwp_editor_rewrite") {
    if(!args.TryGetValue("replacement",out raw)||!(raw is string))throw new InvalidOperationException("수정할 본문 문자열이 필요합니다.");
    replacements=ValidateReplacement((string)raw,spans.Select(p=>p.Text).ToArray());
   } else {
    if(!args.TryGetValue("patch",out raw)||!(raw is Dictionary<string,object>))throw new InvalidOperationException("서식 patch가 필요합니다.");
    patch=(Dictionary<string,object>)raw;ValidatePatch(patch,FormatProperties());
   }
   VerifyAll(true);int changed=0;
   try {
    for(int i=spans.Count-1;i>=0;i--) {
     if(spans[i].Empty)continue;
     var operation=replacements!=null?new Dictionary<string,object>{{"replacement",replacements[i]}}
      :new Dictionary<string,object>{{"patch",new Dictionary<string,object>(patch)}};
     SpanApplyResult applied=backend.Apply(spans[i],method,operation);
     if(applied==null||applied.Span==null)throw new InvalidOperationException("문단 적용 결과를 확인하지 못했습니다.");
     spans[i]=applied.Span;if(applied.Changed)changed++;
     if(i==spans.Count-1)range.End=applied.Span.Range.End;
     Restore();VerifyAll(false);documentHash=backend.DocumentHash();
    }
    Restore();VerifyAll(false);documentHash=backend.DocumentHash();targetId=NewId();
    return new Dictionary<string,object>{{"applied",true},{"changed",changed>0},{"changedParagraphs",changed},{"targetId",targetId},{"context",Context()}};
   } catch(Exception ex) {
    RequiresReview=changed>0||backend.RequiresReview;
    if(!RequiresReview){try{Restore();}catch{}}
    Invalidate(RequiresReview?"일부 문단의 편집이 실행되었습니다. 본문을 직접 확인하세요. 자동 실행 취소는 하지 않았습니다.":ex.Message);
    throw new InvalidOperationException(RequiresReview?reason+" "+ex.Message:ex.Message,ex);
   }
  }
  void VerifyAll(bool requireHash) {
   if(range==null||spans.Count==0)throw new InvalidOperationException("고정 선택 범위가 없습니다.");
   try {
    SelectionInfo current=backend.ReadSelection();
    if(!current.Present||(current.Mode&15)!=1||current.HasObject||!SelectionRange.Equal(range,current.Range))
     throw new InvalidOperationException("사용자의 선택 범위가 바뀌었습니다. 다시 선택하고 요청하세요.");
    if(requireHash&&backend.DocumentHash()!=documentHash)throw new InvalidOperationException("문서가 변경되어 적용하지 않았습니다.");
    try {
     foreach(SelectionSpan expected in spans) {
      SelectionSpan observed=backend.Capture(expected.Range);
      if(!backend.Equivalent(expected,observed))throw new InvalidOperationException("선택 문단의 원문·경계·서식이 변경되었습니다.");
     }
    } finally {Restore();}
    if(requireHash&&backend.DocumentHash()!=documentHash)throw new InvalidOperationException("선택을 확인하는 동안 문서가 변경되었습니다.");
   } catch {targetId=null;throw;}
  }
  void Restore(){if(range!=null&&!backend.Restore(range))throw new InvalidOperationException("원래 드래그 선택을 복원하지 못했습니다.");}
  string[] FormatProperties(){
   var nonempty=spans.Where(p=>!p.Empty).ToArray();
   if(nonempty.Length==0)return new string[0];
   return new string[]{"bold","fontSizePt","alignment"}.Where(key=>nonempty.All(p=>p.FormatProperties.Contains(key))).ToArray();
  }
  Dictionary<string,object> Context(){
   string paragraphContext=String.Join("\n",spans.Where(p=>!p.Empty).Select(p=>p.ParagraphText));
   if(paragraphContext.Length>50000)paragraphContext=paragraphContext.Substring(0,50000);
   return new Dictionary<string,object>{
    {"selectionPresent",selectionPresent},{"selectionMode",selectionMode},{"scope","sameListParagraphSelection"},{"reason",reason??""},
    {"paragraphCount",spans.Count},{"paragraphText",paragraphContext},{"formatScope","entireSelectedText"},
    {"paragraphs",spans.Select((p,i)=>new Dictionary<string,object>{{"index",i},{"selectedLength",p.Text.Length},{"empty",p.Empty},{"formatProperties",p.FormatProperties}}).ToArray()},
    {"alignmentScope",FormatProperties().Contains("alignment")?"selectedWholeParagraphs":"unsupportedForPartialSelection"}
   };
  }
  object State(){
   bool supported=targetId!=null&&spans.Any(p=>!p.Empty);string[] properties=supported?FormatProperties():new string[0];
   return new Dictionary<string,object>{{"targetId",supported?targetId:""},{"text",supported?String.Join("\n",spans.Select(p=>p.Text)):""},
    {"capabilities",new Dictionary<string,object>{{"rewrite",supported},{"format",supported&&properties.Length>0},{"formatProperties",properties},{"paragraphCount",spans.Count}}},{"context",Context()}};
  }
  void Invalidate(string message){reason=message;targetId=null;}
  static string NewId(){return Guid.NewGuid().ToString("N");}
  static void Keys(Dictionary<string,object> value,string[] allowed){foreach(string key in value.Keys)if(!allowed.Contains(key))throw new InvalidOperationException("허용하지 않는 편집 인자: "+key);}
  internal static string[] ValidateReplacement(string replacement,string[] original){
   if(replacement==null||replacement.Length>MaxText||original==null||original.Length==0||original.Length>MaxParagraphs||
    replacement.Any(c=>c!='\n'&&(Char.IsControl(c)||Char.IsSurrogate(c)||c=='\u2028'||c=='\u2029'||c=='\uFFFC')))
    throw new InvalidOperationException("본문은 제어문자 없이 12,000자 이하로 작성하세요.");
   string[] lines=replacement.Split(new char[]{'\n'},StringSplitOptions.None);
   if(lines.Length!=original.Length)throw new InvalidOperationException("기존 문단 수와 줄바꿈 수를 유지해야 합니다.");
   for(int i=0;i<lines.Length;i++)if(original[i].Length==0?lines[i].Length!=0:lines[i].Length==0)
    throw new InvalidOperationException("빈 문단·선택 경계는 그대로 유지하고, 선택된 문장을 빈 문단으로 바꾸지 마세요.");
   return lines;
  }
  internal static void ValidatePatch(Dictionary<string,object> patch,string[] allowed){
   if(patch==null||patch.Count==0)throw new InvalidOperationException("변경할 서식을 지정하세요.");
   Keys(patch,new string[]{"bold","fontSizePt","alignment"});
   if(patch.Keys.Any(k=>!allowed.Contains(k)))throw new InvalidOperationException("선택 범위에서 지원하지 않는 서식입니다. 문단 정렬은 해당 문단 전체를 선택해야 합니다.");
   if(patch.ContainsKey("alignment")&&patch.Count!=1)throw new InvalidOperationException("문단 정렬과 글자 서식은 나누어 적용하세요.");
   object value;
   if(patch.TryGetValue("bold",out value)&&!(value is bool))throw new InvalidOperationException("bold는 true/false여야 합니다.");
   if(patch.TryGetValue("alignment",out value)&&(!(value is string)||!new string[]{"left","right","center","justify"}.Contains((string)value)))throw new InvalidOperationException("문단 정렬 값이 올바르지 않습니다.");
   if(patch.TryGetValue("fontSizePt",out value)){
    if(!(value is byte||value is short||value is int||value is long||value is float||value is double||value is decimal))throw new InvalidOperationException("글자 크기는 숫자여야 합니다.");
    double size=Convert.ToDouble(value,CultureInfo.InvariantCulture);
    if(Double.IsNaN(size)||Double.IsInfinity(size)||size<1||size>400||Math.Abs(size*100-Math.Round(size*100))>0.00001)
     throw new InvalidOperationException("글자 크기는 1~400pt, 소수점 둘째 자리까지 지정하세요.");
   }
  }
 }
 internal sealed class SelectionRange {
  public int List,StartParagraph,Start,EndParagraph,End;
  public SelectionRange Copy(){return (SelectionRange)MemberwiseClone();}
  public static bool Equal(SelectionRange a,SelectionRange b){return a!=null&&b!=null&&a.List==b.List&&a.StartParagraph==b.StartParagraph&&a.Start==b.Start&&a.EndParagraph==b.EndParagraph&&a.End==b.End;}
  internal static SelectionRange Normalize(int sl,int sp,int so,int el,int ep,int eo){
   if(sl<0||sl!=el||sp<0||ep<0||so<0||eo<0||(sp==ep&&so==eo))return null;
   if(sp>ep||(sp==ep&&so>eo)){int p=sp;sp=ep;ep=p;p=so;so=eo;eo=p;}
   return new SelectionRange{List=sl,StartParagraph=sp,Start=so,EndParagraph=ep,End=eo};
  }
 }
 internal sealed class SelectionInfo{public bool Present,HasObject;public int Mode;public SelectionRange Range;}
 internal sealed class ParagraphBounds{public int Start,End;public string Text;}
 internal sealed class SelectionSpan{
  public SelectionRange Range;public string Text,ParagraphText;public int ContentStart,ContentEnd;public bool Empty;
  public string[] FormatProperties;public Dictionary<string,object> Context;public object Token;
 }
 internal sealed class SpanApplyResult{public SelectionSpan Span;public bool Changed;}
 internal interface ISelectionBackend{
  void Reset();SelectionInfo ReadSelection();ParagraphBounds Inspect(int list,int paragraph);
  SelectionSpan Capture(SelectionRange range);bool Equivalent(SelectionSpan expected,SelectionSpan observed);
  SpanApplyResult Apply(SelectionSpan expected,string method,Dictionary<string,object> args);
  bool Restore(SelectionRange range);string DocumentHash();bool RequiresReview{get;}
 }
 internal sealed class NativeSelectionBackend:ISelectionBackend{
  readonly Func<IHwpObject> getDocument;bool bound;int documentId;string path;
  public bool RequiresReview{get;private set;}
  public NativeSelectionBackend(Func<IHwpObject> getDocument){if(getDocument==null)throw new ArgumentNullException("getDocument");this.getDocument=getDocument;}
  public void Reset(){bound=false;RequiresReview=false;}
  IHwpObject Current(){
   IHwpObject doc=getDocument();if(doc==null)throw new InvalidOperationException("열린 한글 문서가 없습니다.");
   int id=doc.GetID();string currentPath=doc.GetPath()??"";
   if(!bound){bound=true;documentId=id;path=currentPath;}
   if(id!=documentId||currentPath!=path)throw new InvalidOperationException("활성 문서가 바뀌었습니다.");
   return doc;
  }
  public SelectionInfo ReadSelection(){
   IHwpObject doc=Current();int mode=doc.GetSelectionMode();bool hasObject=doc.GetCurSelectedCtrl()!=null;
   int sl=0,sp=0,so=0,el=0,ep=0,eo=0;
   bool positioned=doc.GetSelectedPos(ref sl,ref sp,ref so,ref el,ref ep,ref eo);
   bool nonempty=positioned&&(sl!=el||sp!=ep||so!=eo);
   return new SelectionInfo{Mode=mode,HasObject=hasObject,Present=(mode&15)!=0||(mode&~31)!=0||hasObject||nonempty,
    Range=nonempty?SelectionRange.Normalize(sl,sp,so,el,ep,eo):null};
  }
  public ParagraphBounds Inspect(int list,int paragraph){
   NativeEditor.ParagraphInfo info=NativeEditor.InspectParagraph(Current(),list,paragraph);
   return new ParagraphBounds{Start=info.NativeStart,End=info.NativeEnd,Text=info.Text};
  }
  public SelectionSpan Capture(SelectionRange range){
   IHwpObject doc=Current();NativeEditor.Snapshot snapshot;
   if(range.Start==range.End){
    NativeEditor.ParagraphInfo paragraph=NativeEditor.InspectParagraph(doc,range.List,range.StartParagraph);
    int l=0,p=0,at=0;
    if(range.Start<paragraph.NativeStart||range.End>paragraph.NativeEnd||!doc.SetPos(range.List,range.StartParagraph,range.Start)||
     !doc.GetPos(ref l,ref p,ref at)||l!=range.List||p!=range.StartParagraph||at!=range.Start)
     throw new InvalidOperationException("빈 문단 경계를 확인하지 못했습니다.");
    IHwpParameterSet chars=doc.GetCharShape(),paras=doc.GetParaShape();
    if(chars==null||paras==null)throw new InvalidOperationException("빈 문단 서식을 확인하지 못했습니다.");
    snapshot=new NativeEditor.Snapshot{DocumentId=documentId,Path=path,List=range.List,Paragraph=range.StartParagraph,Start=range.Start,End=range.End,
     Text="",ParagraphText=paragraph.Text,ParagraphNativeStart=paragraph.NativeStart,ParagraphNativeEnd=paragraph.NativeEnd,
     PlainStart=range.Start-paragraph.NativeStart,PlainEnd=range.End-paragraph.NativeStart,CharShape=chars.Clone(),ParaShape=paras.Clone(),Diagnostics=new Dictionary<string,object>()};
    if(snapshot.CharShape==null||snapshot.ParaShape==null)throw new InvalidOperationException("빈 문단 서식을 보존하지 못했습니다.");
   }else{
    if(!NativeEditor.SelectRange(doc,range.List,range.StartParagraph,range.Start,range.End))throw new InvalidOperationException("고정 문단 범위를 선택하지 못했습니다.");
    snapshot=NativeEditor.Capture(doc);if(!snapshot.Supported)throw new InvalidOperationException(snapshot.Reason);
   }
   return FromSnapshot(snapshot);
  }
  static bool ReadableInt(IHwpParameterSet shape,string name){
   try{if(shape==null||!shape.ItemExist(name))return false;Convert.ToInt32(shape.GetItem(name),CultureInfo.InvariantCulture);return true;}catch{return false;}
  }
  static SelectionSpan FromSnapshot(NativeEditor.Snapshot snapshot){
   var properties=new List<string>();
   if(snapshot.Start!=snapshot.End){
    if(ReadableInt(snapshot.CharShape,"Bold"))properties.Add("bold");
    if(ReadableInt(snapshot.CharShape,"Height"))properties.Add("fontSizePt");
    if(snapshot.WholeParagraph&&ReadableInt(snapshot.ParaShape,"AlignType"))properties.Add("alignment");
   }
   return new SelectionSpan{Range=new SelectionRange{List=snapshot.List,StartParagraph=snapshot.Paragraph,EndParagraph=snapshot.Paragraph,Start=snapshot.Start,End=snapshot.End},
    Text=snapshot.Text,ParagraphText=snapshot.ParagraphText,ContentStart=snapshot.ParagraphNativeStart,ContentEnd=snapshot.ParagraphNativeEnd,Empty=snapshot.Start==snapshot.End,
    FormatProperties=properties.ToArray(),Context=NativeEditor.DescribeSnapshot(snapshot),Token=snapshot};
  }
  public bool Equivalent(SelectionSpan expected,SelectionSpan observed){
   return expected!=null&&observed!=null&&expected.Empty==observed.Empty&&NativeEditor.SameContentSnapshot(expected.Token as NativeEditor.Snapshot,observed.Token as NativeEditor.Snapshot);
  }
  public SpanApplyResult Apply(SelectionSpan expected,string method,Dictionary<string,object> args){
   IHwpObject doc=Current();
   if(expected.Empty||!NativeEditor.SelectRange(doc,expected.Range.List,expected.Range.StartParagraph,expected.Range.Start,expected.Range.End))
    throw new InvalidOperationException("적용할 선택 범위를 확인하지 못했습니다.");
   var editor=new NativeEditor(Current);bool returned=false;
   try{
    editor.BeginRequest();
    if(!NativeEditor.SameContentSnapshot(expected.Token as NativeEditor.Snapshot,editor.CurrentSnapshot))throw new InvalidOperationException("적용할 문단이 변경되었습니다.");
    var nativeArgs=new Dictionary<string,object>(args);nativeArgs["expectedTargetId"]=editor.CurrentSnapshot.TargetId;
    var result=editor.Invoke(method,nativeArgs) as Dictionary<string,object>;returned=true;object applied,changed;
    if(result==null||!result.TryGetValue("applied",out applied)||!(applied is bool)||!(bool)applied||editor.CurrentSnapshot==null)
     throw new InvalidOperationException("한글이 문단 적용을 확인하지 못했습니다.");
    bool didChange=!result.TryGetValue("changed",out changed)||!(changed is bool)||(bool)changed;
    return new SpanApplyResult{Span=FromSnapshot(editor.CurrentSnapshot),Changed=didChange};
   }catch{RequiresReview=RequiresReview||returned||editor.RequiresReview;throw;}finally{editor.EndRequest();}
  }
  public bool Restore(SelectionRange range){
   IHwpObject doc=Current();
   if(!doc.SetPos(range.List,range.StartParagraph,range.Start)||!doc.SelectText(range.StartParagraph,range.Start,range.EndParagraph,range.End))return false;
   SelectionInfo current=ReadSelection();
   return (current.Mode&15)==1&&!current.HasObject&&SelectionRange.Equal(range,current.Range);
  }
  public string DocumentHash(){return NativeEditor.ReadDocumentHash(Current());}
 }
}
