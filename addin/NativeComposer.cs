using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Hwp.Addon;

namespace HAddon {
 // Explicit composition only. Serialization and original document contents remain local.
 internal sealed class NativeComposer {
  internal const int MaxParagraphs=128, MaxParagraphText=12000, MaxText=24000;
  readonly IComposeBackend backend;
  ComposeTarget target;
  string targetId,reason;
  bool active;
  public bool RequiresReview {get;private set;}
  public NativeComposer(Func<IHwpObject> getDocument):this(new NativeComposeBackend(getDocument)){}
  internal NativeComposer(IComposeBackend backend){if(backend==null)throw new ArgumentNullException("backend");this.backend=backend;}
  public object BeginRequest(){
   active=true;RequiresReview=false;target=null;targetId=null;reason=null;backend.Reset();
   try{target=backend.Capture();if(target==null)throw new InvalidOperationException("빈 본문 위치를 확인하지 못했습니다.");targetId=Guid.NewGuid().ToString("N");}
   catch(Exception ex){reason=ex.Message;target=null;}
   return State();
  }
  public void EndRequest(){active=false;target=null;targetId=null;}
  public object Invoke(string method,Dictionary<string,object> args){
   if(!active)throw new InvalidOperationException("조판 요청이 끝났습니다. 빈 본문 문단에서 새 요청을 시작하세요.");
   args=args??new Dictionary<string,object>();
   if(method=="hwp_editor_compose_state"){Keys(args,new string[0]);if(target!=null)Verify();return State();}
   if(method!="hwp_editor_compose_apply")throw new InvalidOperationException("지원하지 않는 한글 조판 도구입니다.");
   Keys(args,new string[]{"expectedTargetId","paragraphs"});object expected,raw;
   if(target==null||!args.TryGetValue("expectedTargetId",out expected)||!(expected is string)||(string)expected!=targetId)
    throw new InvalidOperationException("현재 고정 조판 위치의 targetId가 필요합니다.");
   if(!args.TryGetValue("paragraphs",out raw))throw new InvalidOperationException("조판할 paragraphs 배열이 필요합니다.");
   ComposeParagraph[] plan=ValidateParagraphs(raw);
   Verify();backend.Prepare(target,plan);Verify();
   string appliedTargetId=targetId;bool returned=false;
   try{
    int count=backend.Apply(target,plan);returned=true;
    if(count!=plan.Length)throw new InvalidOperationException("적용된 문단 수의 확인 결과가 조판 계획과 다릅니다.");
    target=null;targetId=null;reason="조판을 적용했습니다. 추가 조판은 빈 문단에서 새 요청을 시작하세요.";
    return new Dictionary<string,object>{{"applied",true},{"changed",true},{"paragraphCount",count},{"targetId",appliedTargetId},{"typography","blueTypography"}};
   }catch(Exception ex){
    RequiresReview=returned||backend.RequiresReview;target=null;targetId=null;
    reason=RequiresReview?"한글 조판 실행 후 확인을 마치지 못했습니다. 본문을 직접 확인하세요. 자동 실행 취소는 하지 않았습니다.":ex.Message;
    throw new InvalidOperationException(reason+(RequiresReview?" "+ex.Message:""),ex);
   }
  }
  void Verify(){
   if(target==null)throw new InvalidOperationException("사용할 수 있는 빈 본문 조판 위치가 없습니다.");
   try{if(!backend.Same(target,backend.Capture()))throw new InvalidOperationException("문서·커서·빈 문단 또는 기존 서식이 바뀌어 조판하지 않았습니다. 다시 요청하세요.");}
   catch{target=null;targetId=null;throw;}
  }
  object State(){
   bool supported=target!=null&&targetId!=null;
   return new Dictionary<string,object>{{"targetId",supported?targetId:""},{"capabilities",new Dictionary<string,object>{
    {"compose",supported},{"maxParagraphs",MaxParagraphs},{"maxText",MaxText},{"tables",false},{"images",false},{"singleSection",true},{"typography","blueTypography"}}},
    {"context",new Dictionary<string,object>{{"scope","emptyBodyParagraph"},{"selectionPresent",false},{"empty",supported},{"reason",reason??""},
     {"paragraphIndex",supported?target.Paragraph:-1},{"typography","blueTypography"},{"bulletStyle","bulletGlyphWithHangingIndent"},
     {"diagnostics",supported?target.Diagnostics:new Dictionary<string,object>()}}}};
  }
  static void Keys(Dictionary<string,object> value,string[] allowed){foreach(string key in value.Keys)if(!allowed.Contains(key))throw new InvalidOperationException("허용하지 않는 조판 인자: "+key);}
  internal static ComposeParagraph[] ValidateParagraphs(object raw){
   IEnumerable sequence=raw as IEnumerable;
   if(sequence==null||raw is string||raw is IDictionary)throw new InvalidOperationException("paragraphs에는 문단 배열을 넣으세요.");
   var result=new List<ComposeParagraph>();int length=0;
   foreach(object entry in sequence){
    if(result.Count>=MaxParagraphs)throw new InvalidOperationException("한 번에 128문단까지 조판할 수 있습니다.");
    var row=entry as Dictionary<string,object>;object role,text;
    if(row==null)throw new InvalidOperationException("각 문단에는 role과 text가 필요합니다.");
    Keys(row,new string[]{"role","text"});
    if(!row.TryGetValue("role",out role)||!(role is string)||!new string[]{"title","heading","body","bullet"}.Contains((string)role))
     throw new InvalidOperationException("문단 role은 title, heading, body, bullet 중 하나여야 합니다.");
    if(!row.TryGetValue("text",out text)||!(text is string)||String.IsNullOrEmpty((string)text)||((string)text).Length>MaxParagraphText||!PlainText((string)text))
     throw new InvalidOperationException("각 문단은 줄바꿈·탭·제어문자·개체·보충 유니코드 문자가 없는 1~12,000자여야 합니다.");
    length+=((string)text).Length;if(length>MaxText)throw new InvalidOperationException("조판 본문 전체는 24,000자 이하여야 합니다.");
    result.Add(new ComposeParagraph{Role=(string)role,Text=(string)text});
   }
   if(result.Count==0)throw new InvalidOperationException("조판할 문단이 없습니다.");
   return result.ToArray();
  }
  internal static bool PlainText(string value){return value!=null&&!value.Any(c=>Char.IsControl(c)||Char.IsSurrogate(c)||c=='\u2028'||c=='\u2029'||c=='\uFFFC'||!XmlConvert.IsXmlChar(c));}
  // Hancom ParameterSetObject.pdf: HWPUNIT=100/pt; TextColor=COLORREF 0x00bbggrr;
  // LineSpacingType=0 means percentage, AlignType 1=left/3=center; URC=HWPUNIT*2.
  // https://forum.developer.hancom.com/t/topic/2209
  // https://raw.githubusercontent.com/hancom-io/devcenter-archive/main/hwp-automation/ParameterSetObject.pdf
  // https://forum.developer.hancom.com/t/vba-hwpctrl/2156
  internal static ComposeStyle StyleFor(string role){
   bool title=role=="title",heading=role=="heading",bullet=role=="bullet";
   return new ComposeStyle{
    Char=new Dictionary<string,int>{{"Height",title?1800:heading?1200:1000},{"Bold",title||heading?1:0},{"TextColor",title||heading?0x00A65824:0}},
    Para=new Dictionary<string,int>{{"AlignType",title?3:1},{"LineSpacingType",0},{"LineSpacing",title?120:heading?130:160},
     {"PrevSpacing",heading?1800:0},{"NextSpacing",title?2000:heading?800:bullet?600:1000},{"HeadingType",0},
     {"LeftMargin",bullet?2400:0},{"Indentation",bullet?-1200:0}}
   };
  }
 }
 internal sealed class ComposeParagraph{public string Role,Text;public string Rendered{get{return Role=="bullet"?"\u2022 "+Text:Text;}}}
 internal sealed class ComposeStyle{public Dictionary<string,int> Char,Para;}
 internal sealed class ComposeTarget{
  public int DocumentId,List,Paragraph,Position;
  public string Path,Hash;
  public object Native;
  public Dictionary<string,object> Diagnostics=new Dictionary<string,object>();
 }
 internal interface IComposeBackend{
  bool RequiresReview{get;}
  void Reset();ComposeTarget Capture();bool Same(ComposeTarget expected,ComposeTarget observed);
  void Prepare(ComposeTarget target,ComposeParagraph[] plan);int Apply(ComposeTarget target,ComposeParagraph[] plan);
 }
 internal sealed class NativeComposeBackend:IComposeBackend{
  const int MaxXml=32*1024*1024;
  readonly Func<IHwpObject> getDocument;
  Prepared prepared;
  public bool RequiresReview{get;private set;}
  sealed class Source{public ComposeXml Xml;public IHwpParameterSet Char,Para;}
  sealed class Prepared{public string Hash;public IHwpAction Insert;public IHwpParameterSet Parameters;public IHwpParameterSet[] Char,Para;public ComposeStyle[] Styles;}
  public NativeComposeBackend(Func<IHwpObject> getDocument){if(getDocument==null)throw new ArgumentNullException("getDocument");this.getDocument=getDocument;}
  public void Reset(){RequiresReview=false;prepared=null;}
  static string Hash(string text){using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));}
  static string ReadXml(IHwpObject doc){string raw=doc.GetTextFile("HWPML2X","");if(String.IsNullOrEmpty(raw)||raw.Length>MaxXml)throw new InvalidOperationException("문서 XML을 안전한 크기로 읽지 못했습니다.");return raw;}
  static void Position(IHwpObject doc,out int list,out int paragraph,out int position){list=paragraph=position=0;if(!doc.GetPos(ref list,ref paragraph,ref position))throw new InvalidOperationException("한글 커서 위치를 읽지 못했습니다.");}
  static bool NoSelection(IHwpObject doc){
   if((doc.GetSelectionMode()&15)!=0||doc.GetCurSelectedCtrl()!=null)return false;
   int sl=0,sp=0,so=0,el=0,ep=0,eo=0;
   return !doc.GetSelectedPos(ref sl,ref sp,ref so,ref el,ref ep,ref eo)||(sl==el&&sp==ep&&so==eo);
  }
  static void Caret(IHwpObject doc,int list,int paragraph,int position){
   int l,p,o;if(!doc.SetPos(list,paragraph,position))throw new InvalidOperationException("조판 커서를 복원하지 못했습니다.");
   Position(doc,out l,out p,out o);if(l!=list||p!=paragraph||o!=position||!NoSelection(doc))throw new InvalidOperationException("조판 커서가 예상한 빈 문단에 있지 않습니다.");
  }
  public ComposeTarget Capture(){
   IHwpObject doc=getDocument();if(doc==null)throw new InvalidOperationException("열린 한글 문서가 없습니다.");
   if(doc.IsReadOnlyMode())throw new InvalidOperationException("읽기 전용 문서에는 조판할 수 없습니다.");
   if(!NoSelection(doc))throw new InvalidOperationException("조판은 선택이 없는 빈 본문 문단에서 시작하세요. 표·셀 블록·개체 선택은 지원하지 않습니다.");
   int list,paragraph,position;Position(doc,out list,out paragraph,out position);
   if(list!=0||paragraph<0||position<0)throw new InvalidOperationException("표·글상자·특수 영역에는 조판하지 않습니다. 빈 본문 문단으로 커서를 옮기세요.");
   var target=new ComposeTarget{DocumentId=doc.GetID(),Path=doc.GetPath()??"",List=list,Paragraph=paragraph,Position=position};
   IHwpParameterSet cs=doc.GetCharShape(),ps=doc.GetParaShape();
   if(cs==null||ps==null||(cs=cs.Clone())==null||(ps=ps.Clone())==null)throw new InvalidOperationException("기존 글꼴과 문단 서식을 보존할 수 없습니다.");
   ValidateShape(cs,NativeComposer.StyleFor("title").Char);ValidateShape(ps,NativeComposer.StyleFor("bullet").Para);
   if(Integer(ps,"HeadingType")!=0)throw new InvalidOperationException("개요·번호·자동 글머리표 문단은 지원하지 않습니다. 보통 빈 본문 문단을 사용하세요.");
   string raw=ReadXml(doc);ComposeXml xml=ComposeXml.Parse(raw);
   if(paragraph>=xml.Paragraphs.Length)throw new InvalidOperationException("본문 문단 인덱스와 한글 XML을 대응하지 못했습니다.");
   ComposeXml.ParagraphContent original=ComposeXml.ReadSimpleParagraph(xml.Paragraphs[paragraph]);
   if(original.Text.Length!=0)throw new InvalidOperationException("기존 글을 덮어쓰지 않습니다. 조판할 빈 본문 문단에 커서를 놓으세요.");
   NativeEditor.ParagraphInfo info;
   try{info=NativeEditor.InspectParagraph(doc,list,paragraph);}
   finally{Caret(doc,list,paragraph,position);}
   if(info.Text!=""||info.NativeStart!=info.NativeEnd||position!=info.NativeEnd)
    throw new InvalidOperationException("빈 본문 문단의 native 경계를 정확히 확인하지 못했습니다.");
   if(doc.GetID()!=target.DocumentId||(doc.GetPath()??"")!=target.Path||Hash(ReadXml(doc))!=Hash(raw))
    throw new InvalidOperationException("조판 위치를 확인하는 동안 문서가 바뀌었습니다.");
   target.Hash=Hash(raw);target.Native=new Source{Xml=xml,Char=cs,Para=ps};
   target.Diagnostics=new Dictionary<string,object>{{"xmlInputLength",raw.Length},{"xmlFirst8CodeUnits",ComposeXml.Prefix(raw)},
    {"xmlLeadingBomRemoved",raw.Length>0&&raw[0]=='\uFEFF'},{"sectionCount",1},{"paragraphCount",xml.Paragraphs.Length},{"nativeEmptyPosition",position}};
   return target;
  }
  public bool Same(ComposeTarget a,ComposeTarget b){
   if(a==null||b==null||a.DocumentId!=b.DocumentId||a.Path!=b.Path||a.List!=b.List||a.Paragraph!=b.Paragraph||a.Position!=b.Position||a.Hash!=b.Hash)return false;
   Source x=a.Native as Source,y=b.Native as Source;
   return x!=null&&y!=null&&x.Char.IsEquivalent(y.Char)&&x.Para.IsEquivalent(y.Para);
  }
  static int Integer(IHwpParameterSet shape,string key){
   try{if(shape==null||!shape.ItemExist(key))throw new InvalidOperationException();return Convert.ToInt32(shape.GetItem(key),CultureInfo.InvariantCulture);}
   catch{throw new InvalidOperationException("한글의 "+key+" 서식 값을 확인할 수 없습니다.");}
  }
  static void ValidateShape(IHwpParameterSet shape,Dictionary<string,int> fields){foreach(string key in fields.Keys)Integer(shape,key);}
  static IHwpParameterSet Patch(IHwpParameterSet original,Dictionary<string,int> fields){
   var clone=original.Clone();if(clone==null)throw new InvalidOperationException("기존 서식의 복제에 실패했습니다.");ValidateShape(clone,fields);
   foreach(var field in fields)clone.SetItem(field.Key,field.Value);return clone;
  }
  static void CheckShape(IHwpParameterSet expected,IHwpParameterSet actual,Dictionary<string,int> fields){
   if(actual==null||!expected.IsEquivalent(actual))throw new InvalidOperationException("기존 글꼴 속성과 요청한 조판 서식의 읽기 검증에 실패했습니다.");
   foreach(var field in fields)if(Integer(actual,field.Key)!=field.Value)throw new InvalidOperationException("요청한 "+field.Key+" 조판 서식이 반영되지 않았습니다.");
  }
  public void Prepare(ComposeTarget target,ComposeParagraph[] plan){
   Source source=target.Native as Source;if(source==null)throw new InvalidOperationException("고정 조판 원본이 없습니다.");
   IHwpObject doc=getDocument();var p=new Prepared{Hash=target.Hash,Char=new IHwpParameterSet[plan.Length],Para=new IHwpParameterSet[plan.Length],Styles=new ComposeStyle[plan.Length]};
   p.Insert=doc.CreateAction("InsertText");
   if(p.Insert==null||(p.Parameters=p.Insert.CreateSet())==null||!p.Insert.GetDefault(p.Parameters))throw new InvalidOperationException("한글 본문 입력 액션을 준비하지 못했습니다.");
   if(plan.Length>1&&doc.CreateAction("BreakPara")==null)throw new InvalidOperationException("한글 문단 나누기 액션을 준비하지 못했습니다.");
   for(int i=0;i<plan.Length;i++){p.Styles[i]=NativeComposer.StyleFor(plan[i].Role);p.Char[i]=Patch(source.Char,p.Styles[i].Char);p.Para[i]=Patch(source.Para,p.Styles[i].Para);}
   prepared=p;
  }
  void Identity(IHwpObject doc,ComposeTarget target){
   IHwpObject active=getDocument();
   if(doc==null||active==null||doc.GetID()!=target.DocumentId||active.GetID()!=target.DocumentId||
      (doc.GetPath()??"")!=target.Path||(active.GetPath()??"")!=target.Path||doc.IsReadOnlyMode())
    throw new InvalidOperationException("조판 중 활성 문서가 바뀌었거나 읽기 전용 상태입니다.");
  }
  public int Apply(ComposeTarget target,ComposeParagraph[] plan){
   Prepared p=prepared;prepared=null;
   if(p==null||p.Hash!=target.Hash||p.Char.Length!=plan.Length)throw new InvalidOperationException("조판 서식 준비 정보가 없습니다.");
   if(!Same(target,Capture()))throw new InvalidOperationException("조판 직전 문서 또는 커서가 바뀌어 적용하지 않았습니다.");
   IHwpObject doc=getDocument();Source source=(Source)target.Native;
   Identity(doc,target);if(Hash(ReadXml(doc))!=target.Hash)throw new InvalidOperationException("조판 직전 문서 내용이 바뀌었습니다.");
   try{
    Caret(doc,0,target.Paragraph,target.Position);
    for(int i=0;i<plan.Length;i++){
     int list,paragraph,position;Position(doc,out list,out paragraph,out position);
     if(list!=0||paragraph!=target.Paragraph+i||!NoSelection(doc))throw new InvalidOperationException("입력할 새 본문 문단 위치가 예상과 다릅니다.");
     NativeEditor.ParagraphInfo blank;
     try{blank=NativeEditor.InspectParagraph(doc,0,paragraph);}finally{Caret(doc,list,paragraph,position);}
     if(blank.Text!=""||blank.NativeStart!=blank.NativeEnd||position!=blank.NativeEnd)throw new InvalidOperationException("입력 대상에 기존 내용이 있어 조판을 중단했습니다.");
     p.Parameters.SetItem("Text",plan[i].Rendered);Identity(doc,target);
     RequiresReview=true; // Native calls may change the document before reporting an error.
     p.Insert.Execute(p.Parameters);
     NativeEditor.ParagraphInfo actual=NativeEditor.InspectParagraph(doc,0,paragraph);
     if(actual.Text!=plan[i].Rendered||actual.NativeEnd-actual.NativeStart!=plan[i].Rendered.Length||
        !NativeEditor.SelectRange(doc,0,paragraph,actual.NativeStart,actual.NativeEnd))
      throw new InvalidOperationException("입력된 문단 전체가 조판 계획과 일치하지 않습니다.");
     Identity(doc,target);doc.SetCharShape(p.Char[i].Clone());Identity(doc,target);doc.SetParaShape(p.Para[i].Clone());
     CheckShape(p.Char[i],doc.GetCharShape(),p.Styles[i].Char);CheckShape(p.Para[i],doc.GetParaShape(),p.Styles[i].Para);
     Caret(doc,0,paragraph,actual.NativeEnd);
     if(i+1<plan.Length){
      Identity(doc,target);if(!doc.Run("BreakPara"))throw new InvalidOperationException("한글이 문단 나누기를 완료하지 못했습니다.");
      Position(doc,out list,out paragraph,out position);
      if(list!=0||paragraph!=target.Paragraph+i+1||!NoSelection(doc))throw new InvalidOperationException("문단 나누기 후 본문 위치가 예상과 다릅니다.");
     }
    }
    // Inspect every generated paragraph again after all paragraph splits and styles.
    int lastEnd=0;
    for(int i=0;i<plan.Length;i++){
     NativeEditor.ParagraphInfo actual=NativeEditor.InspectParagraph(doc,0,target.Paragraph+i);
     if(actual.Text!=plan[i].Rendered||!NativeEditor.SelectRange(doc,0,target.Paragraph+i,actual.NativeStart,actual.NativeEnd))
      throw new InvalidOperationException("조판 후 전체 문단 텍스트 읽기 검증에 실패했습니다.");
     CheckShape(p.Char[i],doc.GetCharShape(),p.Styles[i].Char);CheckShape(p.Para[i],doc.GetParaShape(),p.Styles[i].Para);lastEnd=actual.NativeEnd;
    }
    Caret(doc,0,target.Paragraph+plan.Length-1,lastEnd);
    if(doc.GetID()!=target.DocumentId||(doc.GetPath()??"")!=target.Path)throw new InvalidOperationException("조판 도중 활성 문서가 바뀌었습니다.");
    ComposeXml after=ComposeXml.Parse(ReadXml(doc));
    ComposeXml.VerifyInsertion(source.Xml,after,target.Paragraph,plan.Select(x=>x.Rendered).ToArray());
    RequiresReview=false;return plan.Length;
   }catch{throw;}
  }
 }
 // Conservative structural readback. Existing mapping records must retain their
 // IDs and values; native styling is allowed to append new records.
 internal sealed class ComposeXml{
  public XmlDocument Document;
  public XmlElement Body,Section,Mapping;
  public XmlElement[] Paragraphs;
  internal sealed class ParagraphContent{public string Text;public string[] Metadata;}
  internal static string Prefix(string value){return value==null?"(null)":String.Join(" ",value.Take(8).Select(c=>((int)c).ToString("X4",CultureInfo.InvariantCulture)));}
  static XmlElement[] Elements(XmlNode node){return node.ChildNodes.Cast<XmlNode>().OfType<XmlElement>().ToArray();}
  static XmlElement One(XmlNode node,string name){var found=Elements(node).Where(e=>e.LocalName==name).ToArray();if(found.Length!=1)throw new InvalidOperationException("한글 XML의 "+name+" 구조를 확인하지 못했습니다.");return found[0];}
  public static ComposeXml Parse(string raw){
   try{
    if(String.IsNullOrEmpty(raw)||raw.Length>32*1024*1024)throw new InvalidOperationException("XML 크기가 허용 범위를 벗어납니다.");
    string value=raw[0]=='\uFEFF'?raw.Substring(1):raw;
    var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=32*1024*1024,IgnoreWhitespace=false};
    var doc=new XmlDocument{XmlResolver=null,PreserveWhitespace=true};
    using(var reader=XmlReader.Create(new StringReader(value),settings))doc.Load(reader);
    if(doc.DocumentElement==null||doc.DocumentElement.LocalName!="HWPML")throw new InvalidOperationException("HWPML 루트가 아닙니다.");
    XmlElement body=One(doc.DocumentElement,"BODY");var sections=Elements(body);
    if(sections.Length!=1||sections[0].LocalName!="SECTION")throw new InvalidOperationException("초기 조판은 구역 1개의 본문 문서만 지원합니다.");
    var paragraphs=Elements(sections[0]);if(paragraphs.Length==0||paragraphs.Any(e=>e.LocalName!="P"))throw new InvalidOperationException("단순한 본문 문단 구조가 아닙니다.");
    return new ComposeXml{Document=doc,Body=body,Section=sections[0],Paragraphs=paragraphs,Mapping=One(One(doc.DocumentElement,"HEAD"),"MAPPINGTABLE")};
   }catch(Exception ex){throw new InvalidOperationException("조판용 한글 XML을 안전하게 읽지 못했습니다. length="+(raw==null?-1:raw.Length)+", first8="+Prefix(raw)+": "+ex.Message,ex);}
  }
  public static ParagraphContent ReadSimpleParagraph(XmlElement paragraph){
   var text=new StringBuilder();var metadata=new List<string>();bool content=false;
   foreach(XmlNode node in paragraph.ChildNodes){
    XmlElement item=node as XmlElement;
    if(item==null){if(node.NodeType==XmlNodeType.Comment||String.IsNullOrWhiteSpace(node.Value))continue;throw new InvalidOperationException("본문 문단에 해석하지 못한 XML 데이터가 있습니다.");}
    if(item.LocalName!="TEXT")throw new InvalidOperationException("대상 문단에 표·그림·필드·특수 컨트롤이 있습니다.");
    foreach(XmlNode part in item.ChildNodes){
     XmlElement child=part as XmlElement;
     if(child==null){if(part.NodeType==XmlNodeType.Comment||String.IsNullOrWhiteSpace(part.Value))continue;throw new InvalidOperationException("본문 TEXT 구조가 예상과 다릅니다.");}
     if((child.LocalName=="SECDEF"||child.LocalName=="COLDEF")&&!content){metadata.Add(Canonical(child));continue;}
     if(child.LocalName!="CHAR"||Elements(child).Length!=0)throw new InvalidOperationException("대상 문단의 숨은 컨트롤·개체를 안전하게 보존할 수 없습니다.");
     string value=child.InnerText;if(!NativeComposer.PlainText(value))throw new InvalidOperationException("대상 문단에 특수 문자나 개체가 있습니다.");
     text.Append(value);if(value.Length>0)content=true;
    }
   }
   return new ParagraphContent{Text=text.ToString(),Metadata=metadata.ToArray()};
  }
  public static void VerifyInsertion(ComposeXml before,ComposeXml after,int target,string[] rendered){
   if(before==null||after==null||target<0||target>=before.Paragraphs.Length||rendered==null||rendered.Length==0)throw new InvalidOperationException("조판 검증 범위가 올바르지 않습니다.");
   if(!SameAttributes(before.Document.DocumentElement,after.Document.DocumentElement,false)||
      !Elements(before.Document.DocumentElement).Where(e=>e.LocalName!="HEAD"&&e.LocalName!="BODY").Select(Canonical).SequenceEqual(
       Elements(after.Document.DocumentElement).Where(e=>e.LocalName!="HEAD"&&e.LocalName!="BODY").Select(Canonical)))
    throw new InvalidOperationException("기존 문서의 부가 XML·바이너리 데이터가 변경되었습니다.");
   if(!SameAttributes(before.Body,after.Body,false)||!SameAttributes(before.Section,after.Section,false)||after.Paragraphs.Length!=before.Paragraphs.Length+rendered.Length-1)
    throw new InvalidOperationException("조판 후 구역·본문 문단 수가 예상과 다릅니다.");
   for(int i=0;i<target;i++)if(Canonical(before.Paragraphs[i])!=Canonical(after.Paragraphs[i]))throw new InvalidOperationException("조판 위치 앞의 기존 본문이 바뀌었습니다.");
   for(int i=target+1;i<before.Paragraphs.Length;i++)if(Canonical(before.Paragraphs[i])!=Canonical(after.Paragraphs[i+rendered.Length-1]))throw new InvalidOperationException("조판 위치 뒤의 기존 본문이 바뀌었습니다.");
   ParagraphContent original=ReadSimpleParagraph(before.Paragraphs[target]);if(original.Text!="")throw new InvalidOperationException("조판 원본 문단이 비어 있지 않습니다.");
   for(int i=0;i<rendered.Length;i++){
    ParagraphContent current=ReadSimpleParagraph(after.Paragraphs[target+i]);
    if(current.Text!=rendered[i]||!(i==0?original.Metadata:new string[0]).SequenceEqual(current.Metadata))
     throw new InvalidOperationException("조판 본문 또는 원래 구역·단 메타데이터가 예상과 다릅니다.");
   }
   PreserveRecords(before.Mapping,after.Mapping);
  }
  static bool SameAttributes(XmlElement before,XmlElement after,bool countsMayGrow){
   if(countsMayGrow){
    foreach(XmlAttribute count in before.Attributes.Cast<XmlAttribute>().Where(countAttribute=>String.Equals(countAttribute.LocalName,"Count",StringComparison.OrdinalIgnoreCase))){
     XmlAttribute next=after.Attributes[count.Name];int oldValue,newValue;
     if(next==null||!Int32.TryParse(count.Value,NumberStyles.Integer,CultureInfo.InvariantCulture,out oldValue)||
        !Int32.TryParse(next.Value,NumberStyles.Integer,CultureInfo.InvariantCulture,out newValue)||oldValue<0||newValue<oldValue)return false;
    }
   }
   var a=before.Attributes.Cast<XmlAttribute>().Where(x=>!countsMayGrow||!String.Equals(x.LocalName,"Count",StringComparison.OrdinalIgnoreCase)).OrderBy(x=>x.Name).Select(x=>x.Name+"="+x.Value);
   var b=after.Attributes.Cast<XmlAttribute>().Where(x=>!countsMayGrow||!String.Equals(x.LocalName,"Count",StringComparison.OrdinalIgnoreCase)).OrderBy(x=>x.Name).Select(x=>x.Name+"="+x.Value);
   return before.Name==after.Name&&a.SequenceEqual(b);
  }
  static bool Container(XmlElement item){return item.LocalName=="MAPPINGTABLE"||item.LocalName.EndsWith("LIST",StringComparison.Ordinal)||item.LocalName=="FONTFACE";}
  static string RecordKey(XmlElement item){
   var keys=item.Attributes.Cast<XmlAttribute>().Where(a=>String.Equals(a.LocalName,"Id",StringComparison.OrdinalIgnoreCase)||String.Equals(a.LocalName,"Lang",StringComparison.OrdinalIgnoreCase)).OrderBy(a=>a.Name).Select(a=>a.Name+"="+a.Value).ToArray();
   return item.Name+(keys.Length==0?"":"|"+String.Join("|",keys));
  }
  static void PreserveRecords(XmlElement before,XmlElement after){
   if(!Container(before)){if(Canonical(before)!=Canonical(after))throw new InvalidOperationException("기존 서식 정의가 변경되었습니다.");return;}
   if(!SameAttributes(before,after,true))throw new InvalidOperationException("기존 서식 목록의 속성이 변경되었습니다.");
   var remaining=Elements(after).ToList();
   foreach(XmlElement old in Elements(before)){
    string key=RecordKey(old);var matching=remaining.Where(x=>RecordKey(x)==key).ToArray();
    if(matching.Length!=1)throw new InvalidOperationException("기존 서식 ID의 보존을 확인하지 못했습니다.");
    PreserveRecords(old,matching[0]);remaining.Remove(matching[0]);
   }
  }
  internal static string Canonical(XmlElement element){var b=new StringBuilder();CanonicalNode(element,b,false);return b.ToString();}
  static void Token(StringBuilder output,string value){output.Append(value.Length).Append(':').Append(value);}
  static void CanonicalNode(XmlNode node,StringBuilder b,bool inChar){
   XmlElement element=node as XmlElement;
   if(element!=null){b.Append('<');Token(b,element.Name);foreach(XmlAttribute a in element.Attributes.Cast<XmlAttribute>().OrderBy(x=>x.Name)){Token(b,a.Name);Token(b,a.Value);}b.Append('>');foreach(XmlNode child in element.ChildNodes)CanonicalNode(child,b,inChar||element.LocalName=="CHAR");b.Append("</");Token(b,element.Name);b.Append('>');return;}
   if(node.NodeType==XmlNodeType.Comment)return;
   if(node.NodeType==XmlNodeType.Text||node.NodeType==XmlNodeType.CDATA||node.NodeType==XmlNodeType.Whitespace||node.NodeType==XmlNodeType.SignificantWhitespace){if(inChar||!String.IsNullOrWhiteSpace(node.Value)){b.Append('T');Token(b,node.Value??"");}return;}
   throw new InvalidOperationException("보존 검증이 지원하지 않는 XML 노드입니다.");
  }
 }
}


