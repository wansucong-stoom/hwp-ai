using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Hwp.Addon;

namespace HAddon {
 internal sealed class NativeRichComposer {
  readonly IRichComposeBackend backend;
  RichComposeTarget target;
  string targetId,reason,requestDirectory;
  bool active,applying,cleanupPending;
  public bool RequiresReview{get;private set;}
  public NativeRichComposer(Func<IHwpObject> getDocument):this(new NativeRichComposeBackend(getDocument)){}
  internal NativeRichComposer(IRichComposeBackend backend){if(backend==null)throw new ArgumentNullException("backend");this.backend=backend;}
  public object BeginRequest(){
   EndRequest();active=true;RequiresReview=false;reason=null;backend.Reset();
   try{target=backend.Capture();if(target==null||target.ContentWidth<7200)throw new InvalidOperationException("본문 폭을 확인하지 못했습니다.");
    targetId=Guid.NewGuid().ToString("N");requestDirectory=Path.Combine(RichFragmentFile.OwnedRoot,targetId);RichFragmentFile.CreateRequest(requestDirectory);
   }catch(Exception ex){target=null;targetId=null;reason=ex.Message;Cleanup();}
   return State();
  }
  public void EndRequest(){active=false;target=null;targetId=null;if(applying){cleanupPending=true;return;}Cleanup();}
  void Cleanup(){if(requestDirectory!=null)RichFragmentFile.Cleanup(requestDirectory);requestDirectory=null;cleanupPending=false;}
  public object Invoke(string method,Dictionary<string,object> args){
   if(!active)throw new InvalidOperationException("표·인포그래픽 조판 요청이 끝났습니다. 빈 본문 문단에서 새 요청을 시작하세요.");
   args=args??new Dictionary<string,object>();
   if(method=="hwp_editor_rich_state"){RichExpected.Keys(args,new string[0]);if(target!=null)Verify();return State();}
   if(method!="hwp_editor_rich_apply")throw new InvalidOperationException("지원하지 않는 표·인포그래픽 도구입니다.");
   RichExpected.Keys(args,new string[]{"expectedTargetId","fragmentPath","fragmentSha256","expected"});object id,raw,path,sha;
   if(target==null||!args.TryGetValue("expectedTargetId",out id)||!(id is string)||(string)id!=targetId)throw new InvalidOperationException("현재 고정 조판 위치의 targetId가 필요합니다.");
   if(!args.TryGetValue("fragmentPath",out path)||!(path is string)||!args.TryGetValue("fragmentSha256",out sha)||!(sha is string)||!args.TryGetValue("expected",out raw))
    throw new InvalidOperationException("조판 fragment 경로·SHA256·expected가 필요합니다.");
   RichExpected expected=RichExpected.Parse(raw,target.ContentWidth);Verify();string appliedId=targetId;
   using(FileStream file=RichFragmentFile.Open(requestDirectory,(string)path,(string)sha)){
    RichImportFile import=null;
    try{
     RichFragmentPackage.Validate(file,expected);RichFragmentPackage.ValidateMemo(file,target.MemoDefinition);import=RichFragmentFile.CreateImportCopy(file,requestDirectory,(string)sha);expected.ImportFile=import;
     RichFragmentFile.VerifyImportCopy(import);Verify();bool returned=false;applying=true;
     try{
      // Only the verified original remains open. The importer copy has no open handle.
      RichComposeReceipt result=backend.Apply(target,import.Path,expected);returned=true;
      RichFragmentFile.VerifyImportCopy(import);
      if(result==null||result.ParagraphCount!=expected.ParagraphTexts.Length||result.TableCount!=expected.Tables.Length||result.ImageCount!=expected.Images.Length)
       throw new InvalidOperationException("표·그림의 읽기 검증 결과가 계획과 다릅니다.");
      target=null;targetId=null;reason="표·인포그래픽을 본문에 삽입하고 구조를 확인했습니다.";
      return new Dictionary<string,object>{{"applied",true},{"changed",true},{"targetId",appliedId},{"paragraphCount",result.ParagraphCount},{"tableCount",result.TableCount},{"imageCount",result.ImageCount},{"imageBytesVerified",true},{"nativeExecuteResult",result.ExecuteResult}};
     }catch(Exception ex){
      RequiresReview=returned||backend.RequiresReview;target=null;targetId=null;
      reason=RequiresReview?"본문 삽입이 실행된 뒤 검증을 마치지 못했습니다. 문서를 직접 확인하세요. 자동 실행 취소는 하지 않았습니다.":ex.Message;
      throw new InvalidOperationException(reason+(RequiresReview?" "+ex.Message:""),ex);
     }finally{applying=false;}
    }finally{RichFragmentFile.RemoveImportCopy(import);file.Dispose();if(cleanupPending)Cleanup();}
   }
  }
  void Verify(){try{if(target==null||!backend.Same(target,backend.Capture()))throw new InvalidOperationException("문서·커서·빈 문단이 바뀌었습니다. 다시 요청하세요.");}catch{target=null;targetId=null;throw;}}
  object State(){bool supported=target!=null&&targetId!=null;string scope=supported?(target.Scope??"emptyBodyParagraph"):(backend.SelectionPresent?"wholeBodyParagraphSelection":"emptyBodyParagraph");return new Dictionary<string,object>{{"targetId",supported?targetId:""},{"source_scope",scope},{"text",supported?(target.SourceText??""):""},
   {"capabilities",new Dictionary<string,object>{{"compose",supported},{"richCompose",supported},{"tables",supported},{"images",supported},{"maxTables",3},{"maxImages",3},{"singleSection",true},{"nativeEditableTables",true},{"infographicKind","embeddedPng"}}},
   {"context",new Dictionary<string,object>{{"scope",scope},{"source_scope",scope},{"selectionPresent",backend.SelectionPresent},{"paragraphRange",supported?(object)new int[]{target.Paragraph,target.EndParagraph}:new int[0]},{"reason",reason??""},{"fragmentDirectory",supported?requestDirectory:""},
    {"contentWidthHwpUnit",supported?target.ContentWidth:0},{"memoDefinition",supported?target.MemoDefinition:null},{"maxImageHeightHwpUnit",60000},{"diagnostics",supported?target.Diagnostics:new Dictionary<string,object>()}}}};}
 }
 internal sealed class RichExpected {
  public string[] ParagraphTexts;public RichTable[] Tables;public RichImage[] Images;public int ContentWidth;public RichImportFile ImportFile;
  internal static void Keys(Dictionary<string,object> value,string[] allowed){foreach(string key in value.Keys)if(!allowed.Contains(key))throw new InvalidOperationException("허용하지 않는 rich 조판 인자: "+key);}
  internal static object[] ArrayValue(object raw,string label){var list=raw as IEnumerable;if(list==null||raw is string||raw is IDictionary)throw new InvalidOperationException(label+"에는 배열이 필요합니다.");return list.Cast<object>().ToArray();}
  internal static int IntValue(object raw,string label,int min,int max){
   if(!(raw is byte||raw is short||raw is int||raw is long||raw is float||raw is double||raw is decimal))throw new InvalidOperationException(label+"은 정수여야 합니다.");
   double value=Convert.ToDouble(raw,CultureInfo.InvariantCulture);if(Double.IsNaN(value)||Double.IsInfinity(value)||value<min||value>max||value!=Math.Floor(value))throw new InvalidOperationException(label+"의 범위가 올바르지 않습니다.");return(int)value;
  }
  internal static string Text(object raw,bool empty){string value=raw as string;if(value==null||(!empty&&value.Length==0)||value.Length>12000||!NativeComposer.PlainText(value))throw new InvalidOperationException("rich 본문은 제어문자 없는 한 줄 문자열이어야 합니다.");return value;}
  internal static bool Sha(string value){return value!=null&&value.Length==64&&value.All(c=>(c>='0'&&c<='9')||(c>='a'&&c<='f'));}
  public static RichExpected Parse(object raw,int contentWidth){
   var input=raw as Dictionary<string,object>;object paragraphs,tables,images;
   if(input==null)throw new InvalidOperationException("expected 객체가 필요합니다.");Keys(input,new string[]{"paragraphTexts","tables","images"});
   if(!input.TryGetValue("paragraphTexts",out paragraphs)||!input.TryGetValue("tables",out tables)||!input.TryGetValue("images",out images))throw new InvalidOperationException("expected의 문단·표·그림 배열이 필요합니다.");
   var p=ArrayValue(paragraphs,"paragraphTexts");var t=ArrayValue(tables,"tables");var im=ArrayValue(images,"images");
   if(p.Length<1||p.Length>256||t.Length>3||im.Length>3||(t.Length==0&&im.Length==0))throw new InvalidOperationException("rich 조판은 1~256문단, 표·그림 각각 최대 3개를 지원합니다.");
   var result=new RichExpected{ContentWidth=contentWidth,ParagraphTexts=p.Select(x=>Text(x,true)).ToArray(),Tables=new RichTable[t.Length],Images=new RichImage[im.Length]};
   int total=result.ParagraphTexts.Sum(x=>x.Length);
   for(int i=0;i<t.Length;i++){
    var row=t[i] as Dictionary<string,object>;object rows,columns,cells;
    if(row==null)throw new InvalidOperationException("표 expected 형식이 올바르지 않습니다.");Keys(row,new string[]{"rows","columns","cells"});
    if(!row.TryGetValue("rows",out rows)||!row.TryGetValue("columns",out columns)||!row.TryGetValue("cells",out cells))throw new InvalidOperationException("표 행·열·셀 배열이 필요합니다.");
    var table=new RichTable{Rows=IntValue(rows,"rows",2,21),Columns=IntValue(columns,"columns",1,8)};var grid=ArrayValue(cells,"cells");
    if(grid.Length!=table.Rows)throw new InvalidOperationException("표 행 수와 cells 길이가 다릅니다.");table.Cells=new string[table.Rows][];
    for(int r=0;r<table.Rows;r++){var line=ArrayValue(grid[r],"cells row");if(line.Length!=table.Columns)throw new InvalidOperationException("표가 직사각형이 아닙니다.");table.Cells[r]=line.Select(x=>Text(x,true)).ToArray();total+=table.Cells[r].Sum(x=>x.Length);}
    result.Tables[i]=table;
   }
   if(total>48000)throw new InvalidOperationException("rich 조판 텍스트 전체는 48,000자 이하여야 합니다.");
   for(int i=0;i<im.Length;i++){
    var row=im[i] as Dictionary<string,object>;object hash,width,height;
    if(row==null)throw new InvalidOperationException("그림 expected 형식이 올바르지 않습니다.");Keys(row,new string[]{"sha256","width","height"});
    if(!row.TryGetValue("sha256",out hash)||!(hash is string)||!Sha((string)hash)||!row.TryGetValue("width",out width)||!row.TryGetValue("height",out height))throw new InvalidOperationException("그림 SHA256과 HWPUNIT 표시 크기가 필요합니다.");
    result.Images[i]=new RichImage{Sha256=(string)hash,Width=IntValue(width,"image width",1,contentWidth),Height=IntValue(height,"image height",1,60000)};
   }
   return result;
  }
 }
 internal sealed class RichTable{public int Rows,Columns;public string[][] Cells;}
 internal sealed class RichImage{public string Sha256;public int Width,Height;}
 internal sealed class RichComposeTarget{public object Pin;public ComposeXml Source;public string Scope,SourceText;public int Paragraph,EndParagraph,ContentWidth;public Dictionary<string,object> MemoDefinition;public Dictionary<string,object> Diagnostics=new Dictionary<string,object>();}
 internal sealed class RichComposeReceipt{public int ParagraphCount,TableCount,ImageCount;public int? ExecuteResult;}
 internal interface IRichComposeBackend{bool RequiresReview{get;}bool SelectionPresent{get;}void Reset();RichComposeTarget Capture();bool Same(RichComposeTarget a,RichComposeTarget b);RichComposeReceipt Apply(RichComposeTarget target,string path,RichExpected expected);}
 internal sealed class RichNativePin{
  public int DocumentId,StartParagraph,EndParagraph,Start,End;public string Path,Hash;public bool Selected;
 }
 internal sealed class NativeRichComposeBackend:IRichComposeBackend{
  readonly Func<IHwpObject> getDocument;
  public bool RequiresReview{get;private set;}
  public bool SelectionPresent{get;private set;}
  public NativeRichComposeBackend(Func<IHwpObject> getDocument){if(getDocument==null)throw new ArgumentNullException("getDocument");this.getDocument=getDocument;}
  public void Reset(){RequiresReview=false;SelectionPresent=true;}
  internal static string Hash(string raw){return RichFragmentFile.Digest(Encoding.UTF8.GetBytes(raw));}
  static string Read(IHwpObject doc){string raw=doc.GetTextFile("HWPML2X","");if(String.IsNullOrEmpty(raw)||raw.Length>32*1024*1024)throw new InvalidOperationException("조판 읽기 검증 XML을 안전한 크기로 준비하지 못했습니다.");return raw;}
  static bool Range(IHwpObject doc,out int sp,out int so,out int ep,out int eo){
   int sl=0,el=0;sp=so=ep=eo=0;
   if(!doc.GetSelectedPos(ref sl,ref sp,ref so,ref el,ref ep,ref eo)||sl!=0||el!=0||sp<0||ep<0||so<0||eo<0||(sp==ep&&so==eo))return false;
   if(sp>ep||(sp==ep&&so>eo)){int swap=sp;sp=ep;ep=swap;swap=so;so=eo;eo=swap;}return true;
  }
  static void Restore(IHwpObject doc,RichNativePin p){
   if(!doc.SetPos(0,p.StartParagraph,p.Start))throw new InvalidOperationException("고정한 본문 커서를 복원하지 못했습니다.");
   if(p.Selected){int sp,so,ep,eo;if(!doc.SelectText(p.StartParagraph,p.Start,p.EndParagraph,p.End)||!Range(doc,out sp,out so,out ep,out eo)||sp!=p.StartParagraph||so!=p.Start||ep!=p.EndParagraph||eo!=p.End)throw new InvalidOperationException("원래 전체 문단 선택을 복원하지 못했습니다.");}
   else{int l=0,para=0,pos=0;if(!doc.GetPos(ref l,ref para,ref pos)||l!=0||para!=p.StartParagraph||pos!=p.Start||(doc.GetSelectionMode()&15)!=0)throw new InvalidOperationException("원래 빈 문단 커서를 복원하지 못했습니다.");}
  }
  public RichComposeTarget Capture(){
   SelectionPresent=true;IHwpObject doc=getDocument();if(doc==null)throw new InvalidOperationException("열린 한글 문서가 없습니다.");
   int mode=doc.GetSelectionMode()&15;bool selected=mode!=0;SelectionPresent=selected||doc.GetCurSelectedCtrl()!=null;
   if(doc.GetCurSelectedCtrl()!=null||mode!=0&&mode!=1)throw new InvalidOperationException("부분 셀·셀 블록·그림·개체 선택은 표 재조판 경로에서 지원하지 않습니다. 본문 문단 전체를 선택하세요.");
   if(doc.IsReadOnlyMode())throw new InvalidOperationException("읽기 전용 문서에는 조판할 수 없습니다.");
   var p=new RichNativePin{DocumentId=doc.GetID(),Path=doc.GetPath()??"",Selected=selected};
   if(selected){int sp,so,ep,eo;if(!Range(doc,out sp,out so,out ep,out eo))throw new InvalidOperationException("같은 본문 안의 문단 선택을 확인하지 못했습니다.");
    if(ep-sp+1>24)throw new InvalidOperationException("표 재조판은 한 번에 본문 문단 24개까지 지원합니다.");p.StartParagraph=sp;p.EndParagraph=ep;p.Start=so;p.End=eo;
   }else{int l=0,para=0,pos=0,sp,so,ep,eo;if(!doc.GetPos(ref l,ref para,ref pos)||l!=0||para<0||pos<0||Range(doc,out sp,out so,out ep,out eo))throw new InvalidOperationException("표·글상자 밖의 빈 본문 문단이나 전체 본문 문단 선택을 사용하세요.");p.StartParagraph=p.EndParagraph=para;p.Start=p.End=pos;}
   string raw=Read(doc);ComposeXml xml=ComposeXml.Parse(raw);int width=RichXml.ContentWidth(xml);var memo=RichMemo.Read(xml);
   if(p.EndParagraph>=xml.Paragraphs.Length)throw new InvalidOperationException("본문 문단 인덱스와 XML을 대응하지 못했습니다.");
   var texts=new List<string>();
   try{
    for(int i=p.StartParagraph;i<=p.EndParagraph;i++){
     ComposeXml.ParagraphContent model=ComposeXml.ReadSimpleParagraph(xml.Paragraphs[i]);NativeEditor.ParagraphInfo native=NativeEditor.InspectParagraph(doc,0,i);
     if(native.Text!=model.Text)throw new InvalidOperationException("선택 문단의 native 본문과 XML이 다릅니다.");
     if(selected){
      if((i==p.StartParagraph&&p.Start!=native.NativeStart)||(i==p.EndParagraph&&p.End!=native.NativeEnd)||(i>p.StartParagraph&&native.NativeStart!=0))
       throw new InvalidOperationException("표 재조판은 문단 본문 전체만 바꿀 수 있습니다. 부분 선택·문단 밖 숨은 컨트롤을 제외하고 전체 문단을 다시 선택하세요.");
     }else if(native.Text!=""||native.NativeStart!=native.NativeEnd||p.Start!=native.NativeEnd)
      throw new InvalidOperationException("기존 글을 덮어쓰지 않습니다. 빈 본문 문단에 커서를 놓거나 바꿀 본문 문단 전체를 선택하세요.");
     texts.Add(model.Text);
    }
   }finally{Restore(doc,p);}
   string text=selected?String.Join("\n",texts):"";if(text.Length>24000)throw new InvalidOperationException("표 재조판 원문은 24,000자 이하여야 합니다.");
   Identity(doc,p,true);p.Hash=Hash(raw);if(Hash(Read(doc))!=p.Hash)throw new InvalidOperationException("조판 위치를 읽는 동안 문서가 바뀌었습니다.");
   return new RichComposeTarget{Pin=p,Source=xml,Paragraph=p.StartParagraph,EndParagraph=p.EndParagraph,ContentWidth=width,SourceText=text,MemoDefinition=memo,Scope=selected?"wholeBodyParagraphSelection":"emptyBodyParagraph",
    Diagnostics=new Dictionary<string,object>{{"xmlInputLength",raw.Length},{"xmlFirst8CodeUnits",ComposeXml.Prefix(raw)},{"xmlLeadingBomRemoved",raw.Length>0&&raw[0]=='\uFEFF'},{"selectionParagraphCount",p.EndParagraph-p.StartParagraph+1}}};
  }
  public bool Same(RichComposeTarget a,RichComposeTarget b){
   if(a==null||b==null||a.Scope!=b.Scope||a.SourceText!=b.SourceText||a.ContentWidth!=b.ContentWidth)return false;var x=a.Pin as RichNativePin;var y=b.Pin as RichNativePin;
   return x!=null&&y!=null&&x.DocumentId==y.DocumentId&&x.Path==y.Path&&x.Hash==y.Hash&&x.Selected==y.Selected&&x.StartParagraph==y.StartParagraph&&x.EndParagraph==y.EndParagraph&&x.Start==y.Start&&x.End==y.End;
  }
  void Identity(IHwpObject doc,RichNativePin source,bool range){
   IHwpObject current=getDocument();if(doc==null||current==null||doc.GetID()!=source.DocumentId||current.GetID()!=source.DocumentId||(doc.GetPath()??"")!=source.Path||(current.GetPath()??"")!=source.Path||doc.IsReadOnlyMode())throw new InvalidOperationException("조판 대상 문서가 바뀌었거나 읽기 전용입니다.");
   if(!range)return;
   if(source.Selected){int sp,so,ep,eo;if((doc.GetSelectionMode()&15)!=1||doc.GetCurSelectedCtrl()!=null||!Range(doc,out sp,out so,out ep,out eo)||sp!=source.StartParagraph||so!=source.Start||ep!=source.EndParagraph||eo!=source.End)throw new InvalidOperationException("고정한 전체 본문 문단 선택이 바뀌었습니다.");}
   else{int list=0,paragraph=0,position=0;if(!doc.GetPos(ref list,ref paragraph,ref position)||list!=0||paragraph!=source.StartParagraph||position!=source.Start||(doc.GetSelectionMode()&15)!=0||doc.GetCurSelectedCtrl()!=null)throw new InvalidOperationException("고정한 빈 본문 커서가 바뀌었습니다.");}
  }
  public RichComposeReceipt Apply(RichComposeTarget target,string path,RichExpected expected){
   if(!Same(target,Capture()))throw new InvalidOperationException("파일 삽입 직전 문서 또는 고정 본문 범위가 바뀌었습니다.");
   IHwpObject doc=getDocument();RichNativePin original=(RichNativePin)target.Pin;Identity(doc,original,true);
   IHwpAction action=doc.CreateAction("InsertFile");IHwpParameterSet set=action==null?null:action.CreateSet();
   if(action==null||set==null||!action.GetDefault(set))throw new InvalidOperationException("한글 InsertFile 액션을 준비하지 못했습니다.");
   if(original.Selected&&doc.CreateAction("Delete")==null)throw new InvalidOperationException("한글 선택 삭제 액션을 준비하지 못했습니다.");
   // Installed IHwpAction.Execute returns Int32. Its wrapper uses Convert.ToInt32
   // on the native response. No documented numeric success convention is assumed.
   // Official desktop: https://forum.developer.hancom.com/t/topic/1903/2
   // FileFormat HWPX: https://forum.developer.hancom.com/t/settextfile/1511
   set.SetItem("FileName",path);set.SetItem("FileFormat","HWPX");set.SetItem("FileArg","");
   set.SetItem("KeepSection",0);set.SetItem("KeepCharshape",1);set.SetItem("KeepParashape",1);set.SetItem("KeepStyle",1);
   Identity(doc,original,true);if(Hash(Read(doc))!=original.Hash)throw new InvalidOperationException("파일 삽입 직전 문서 내용이 바뀌었습니다.");
   RichNativePin insertion=original;int? executeResult=null;string stage="prepared";bool deleteAttempted=false;
   try{
    RichFragmentFile.VerifyImportCopy(expected.ImportFile);
    if(!String.Equals(Path.GetFullPath(path),expected.ImportFile.Path,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("삽입용 복사본 경로가 바뀌었습니다.");
    if(original.Selected){
     stage="delete";deleteAttempted=true;RequiresReview=true;
     if(!doc.Run("Delete"))throw new InvalidOperationException("전체 본문 문단 선택의 삭제가 성공을 반환하지 않았습니다.");Identity(doc,original,false);
     stage="delete-verify";string deletedRaw=Read(doc);ComposeXml deleted=ComposeXml.Parse(deletedRaw);RichXml.VerifyDeletion(target.Source,deleted,original.StartParagraph,original.EndParagraph);
     NativeEditor.ParagraphInfo blank=NativeEditor.InspectParagraph(doc,0,original.StartParagraph);
     insertion=new RichNativePin{DocumentId=original.DocumentId,Path=original.Path,Hash=Hash(deletedRaw),StartParagraph=original.StartParagraph,EndParagraph=original.StartParagraph,Start=original.Start,End=original.Start,Selected=false};
     if(blank.Text!=""||blank.NativeStart!=blank.NativeEnd||blank.NativeEnd!=insertion.Start)throw new InvalidOperationException("삭제 뒤 빈 문단 경계가 예상과 다릅니다.");Restore(doc,insertion);
    }
    Identity(doc,insertion,true);if(Hash(Read(doc))!=insertion.Hash)throw new InvalidOperationException("삽입 직전 문서가 바뀌었습니다.");
    RichFragmentFile.VerifyImportCopy(expected.ImportFile);
    stage="insert";RequiresReview=true;Exception executionFailure=null;
    try{executeResult=action.Execute(set);}catch(Exception ex){executionFailure=ex;}
    stage="insert-file-verify";
    try{RichFragmentFile.VerifyImportCopy(expected.ImportFile);}
    catch(Exception ex){throw new InvalidOperationException("삽입용 복사본 무결성 확인 실패."+(executionFailure==null?"":" native 실행 오류: "+SafeError(executionFailure.Message)),ex);}
    if(executionFailure!=null)throw new InvalidOperationException("InsertFile 실행 오류: "+SafeError(executionFailure.Message),executionFailure);
    Identity(doc,original,false);
    stage="insert-readback";string afterRaw=Read(doc);
    if(Hash(afterRaw)==insertion.Hash)throw new InvalidOperationException("InsertFile 실행 뒤 문서 XML에 변화가 없습니다. 파일 삽입이 적용되지 않았습니다.");
    ComposeXml after=ComposeXml.Parse(afterRaw);stage="result-verify";
    if(original.Selected)RichXml.VerifyReplacement(target.Source,after,original.StartParagraph,original.EndParagraph,expected);else RichXml.Verify(target.Source,after,target.Paragraph,expected);
    RequiresReview=false;return new RichComposeReceipt{ParagraphCount=expected.ParagraphTexts.Length,TableCount=expected.Tables.Length,ImageCount=expected.Images.Length,ExecuteResult=executeResult};
   }catch(Exception ex){
    string observed=null;bool? changed=null;
    try{Identity(doc,original,false);observed=Read(doc);changed=Hash(observed)!=original.Hash;}catch{}
    // A verified unchanged original is a failed command with no document edit.
    // A prior deletion, any changed bytes, or unavailable readback needs review.
    RequiresReview=changed!=false;
    string status=changed==false?"한글 명령 실패: 원본문서 XML 불변.":changed==true?"한글 실행 후 원본문서 XML 변경: 본문 확인 필요.":"한글 실행 후 원본문서 변경 여부를 확인하지 못했습니다.";
    string message=status+" "+Diagnostics(target,expected,observed,executeResult,stage,deleteAttempted,changed)+" "+SafeError(ex.Message);
    throw new InvalidOperationException(message,ex);
   }
  }
  static string SafeError(string message){
   if(String.IsNullOrEmpty(message))return "실행 결과를 확인하지 못했습니다.";
   string safe=new String(message.Select(c=>Char.IsControl(c)?' ':c).ToArray());return safe.Length>120?safe.Substring(0,120):safe;
  }
  internal static string Diagnostics(RichComposeTarget target,RichExpected expected,string observed,int? executeResult,string stage,bool deleteAttempted,bool? changed){
   string actualParagraphs="?",actualSections="?",actualBodies="?";
   if(!String.IsNullOrEmpty(observed))try{
    string raw=observed[0]=='\uFEFF'?observed.Substring(1):observed;
    var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=32*1024*1024};var xml=new XmlDocument{XmlResolver=null};
    using(var reader=XmlReader.Create(new StringReader(raw),settings))xml.Load(reader);
    if(xml.DocumentElement!=null&&xml.DocumentElement.LocalName=="HWPML"){
     var bodies=RichXml.Elements(xml.DocumentElement).Where(x=>x.LocalName=="BODY").ToArray();var sections=bodies.SelectMany(RichXml.Elements).Where(x=>x.LocalName=="SECTION").ToArray();
     actualBodies=bodies.Length.ToString(CultureInfo.InvariantCulture);actualSections=sections.Length.ToString(CultureInfo.InvariantCulture);actualParagraphs=sections.SelectMany(RichXml.Elements).Count(x=>x.LocalName=="P").ToString(CultureInfo.InvariantCulture);
    }
   }catch{}
   int expectedParagraphs=target.Source.Paragraphs.Length-(target.EndParagraph-target.Paragraph+1)+expected.ParagraphTexts.Length;
   return "[InsertFile="+(executeResult.HasValue?executeResult.Value.ToString(CultureInfo.InvariantCulture):"not-returned")+";stage="+stage+";delete="+(deleteAttempted?"1":"0")+
    ";changed="+(changed.HasValue?(changed.Value?"1":"0"):"?")+";expectedP="+expectedParagraphs.ToString(CultureInfo.InvariantCulture)+";actualP="+actualParagraphs+";expectedS=1;actualS="+actualSections+";actualB="+actualBodies+"]";
  }
 }
 internal sealed class RichImportFile{
  public string Path,Sha256;public uint Volume,IndexHigh,IndexLow;
 }
 internal static class RichFragmentFile{
  public static readonly string OwnedRoot=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"Stoom.Hwp.Codex","rich"));
  const long MaxFile=32L*1024*1024;
  internal static string Digest(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
  internal static string Digest(Stream stream){stream.Position=0;string value;using(var sha=SHA256.Create())value=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();stream.Position=0;return value;}
  static bool Under(string path,string root){return path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}
  internal static string Resolve(string directory,string path){
   string root=Path.GetFullPath(directory),full=Path.GetFullPath(path);Guid id;
   if(!Under(root,OwnedRoot)||!String.Equals(Path.GetDirectoryName(root),OwnedRoot,StringComparison.OrdinalIgnoreCase)||!Guid.TryParseExact(Path.GetFileName(root),"N",out id)||!Under(full,root)||!String.Equals(Path.GetExtension(full),".hwpx",StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("이 요청에 소유된 임시 HWPX fragment 경로만 사용할 수 있습니다.");return full;
  }
  static void NoReparse(string path){
   string part=Path.GetFullPath(path);while(!String.IsNullOrEmpty(part)){
    if(File.Exists(part)||Directory.Exists(part)){FileAttributes attributes=File.GetAttributes(part);if((attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("심볼릭 링크·junction을 통한 조판 파일 접근은 허용하지 않습니다.");}
    string parent=Path.GetDirectoryName(part);if(parent==part)break;part=parent;
   }
  }
  internal static void CreateRequest(string directory){
   string full=Path.GetFullPath(directory);Resolve(full,Path.Combine(full,"fragment.hwpx"));NoReparse(full);Directory.CreateDirectory(full);NoReparse(full);
  }
  [StructLayout(LayoutKind.Sequential)]
  struct FileIdentity{
   public uint Attributes,CreationLow,CreationHigh,AccessLow,AccessHigh,WriteLow,WriteHigh,Volume,SizeHigh,SizeLow,Links,IndexHigh,IndexLow;
  }
  [DllImport("kernel32.dll",SetLastError=true)]
  static extern bool GetFileInformationByHandle(SafeFileHandle file,out FileIdentity information);
  static FileIdentity Identity(FileStream file){
   FileIdentity value;if(!GetFileInformationByHandle(file.SafeFileHandle,out value)||value.Links!=1||(value.IndexHigh==0&&value.IndexLow==0))throw new InvalidOperationException("삽입용 복사본의 파일 ID를 확인하지 못했습니다.");return value;
  }
  static string ImportPath(RichImportFile import){
   if(import==null||String.IsNullOrEmpty(import.Path)||!RichExpected.Sha(import.Sha256))throw new InvalidOperationException("검증된 삽입용 복사본 정보가 없습니다.");
   string full=Path.GetFullPath(import.Path),name=Path.GetFileName(full);const string prefix="native-import-";Guid id;
   if(!name.StartsWith(prefix,StringComparison.Ordinal)||!name.EndsWith(".hwpx",StringComparison.OrdinalIgnoreCase)||name.Length<=prefix.Length+5||!Guid.TryParseExact(name.Substring(prefix.Length,name.Length-prefix.Length-5),"N",out id))throw new InvalidOperationException("삽입용 복사본 파일 이름이 올바르지 않습니다.");
   return Resolve(Path.GetDirectoryName(full),full);
  }
  internal static RichImportFile CreateImportCopy(FileStream original,string directory,string expectedSha){
   if(original==null||!original.CanRead||!RichExpected.Sha(expectedSha))throw new InvalidOperationException("검증된 원본 fragment가 필요합니다.");
   var copy=new RichImportFile{Path=Path.GetFullPath(Path.Combine(directory,"native-import-"+Guid.NewGuid().ToString("N")+".hwpx")),Sha256=expectedSha};
   string path=ImportPath(copy);NoReparse(path);bool created=false;
   try{
    using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None)){
     created=true;original.Position=0;byte[] buffer=new byte[8192];int count;long total=0;
     while((count=original.Read(buffer,0,buffer.Length))>0){total+=count;if(total>MaxFile)throw new InvalidOperationException("삽입용 복사본 크기가 한도를 넘었습니다.");output.Write(buffer,0,count);}
     output.Flush(true);if(total!=original.Length||Digest(output)!=expectedSha)throw new InvalidOperationException("삽입용 복사본 SHA256이 원본과 다릅니다.");
     FileIdentity info=Identity(output);copy.Volume=info.Volume;copy.IndexHigh=info.IndexHigh;copy.IndexLow=info.IndexLow;
    }
    VerifyImportCopy(copy);return copy;
   }catch{if(created)RemoveImportCopy(copy);throw;}
   finally{original.Position=0;}
  }
  internal static void VerifyImportCopy(RichImportFile import){
   string path=ImportPath(import);NoReparse(path);
   try{
    using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
     FileIdentity id=Identity(file);
     if(id.Volume!=import.Volume||id.IndexHigh!=import.IndexHigh||id.IndexLow!=import.IndexLow||file.Length<1||file.Length>MaxFile||Digest(file)!=import.Sha256)
      throw new InvalidOperationException("삽입용 복사본이 교체되었거나 SHA256이 바뀌었습니다.");
    }
    NoReparse(path);
   }catch(InvalidOperationException){throw;}
   catch(Exception ex){throw new InvalidOperationException("삽입용 복사본을 안전하게 읽어 확인하지 못했습니다.",ex);}
  }
  internal static void RemoveImportCopy(RichImportFile import){
   if(import==null)return;
   try{string path=ImportPath(import);NoReparse(path);if(File.Exists(path))File.Delete(path);}catch{/* Only the exact owned import copy is eligible; otherwise leave it for owned request cleanup. */}
  }
  internal static FileStream Open(string directory,string path,string expectedSha){
   if(!RichExpected.Sha(expectedSha))throw new InvalidOperationException("fragment SHA256 형식이 올바르지 않습니다.");string full=Resolve(directory,path);NoReparse(full);
   var stream=new FileStream(full,FileMode.Open,FileAccess.Read,FileShare.Read);
   try{if(stream.Length<1||stream.Length>MaxFile||Digest(stream)!=expectedSha)throw new InvalidOperationException("fragment의 크기 또는 SHA256이 예상과 다릅니다.");NoReparse(full);return stream;}catch{stream.Dispose();throw;}
  }
  internal static void Cleanup(string directory){
   try{
    string full=Path.GetFullPath(directory);Guid id;if(!String.Equals(Path.GetDirectoryName(full),OwnedRoot,StringComparison.OrdinalIgnoreCase)||!Guid.TryParseExact(Path.GetFileName(full),"N",out id)||!Directory.Exists(full))return;
    NoReparse(full);var pending=new Stack<string>();pending.Push(full);
    while(pending.Count>0){string current=pending.Pop();if(current!=full&&!Under(current,full))return;NoReparse(current);
     foreach(string entry in Directory.EnumerateFileSystemEntries(current)){if(!Under(Path.GetFullPath(entry),full)||(File.GetAttributes(entry)&FileAttributes.ReparsePoint)!=0)return;if(Directory.Exists(entry))pending.Push(entry);}}
    // Exact owned GUID root and every descendant were checked above.
    Directory.Delete(full,true);
   }catch{/* Unreadable or still-locked owned temporary files remain for later cleanup. */}
  }
 }
 internal static class RichXml{
  internal static XmlElement[] Elements(XmlNode node){return node.ChildNodes.Cast<XmlNode>().OfType<XmlElement>().ToArray();}
  internal static XmlElement One(XmlNode node,string name){var found=Elements(node).Where(x=>x.LocalName==name).ToArray();if(found.Length!=1)throw new InvalidOperationException("rich XML의 "+name+" 구조가 예상과 다릅니다.");return found[0];}
  internal static int Number(XmlElement item,string key,int min,int max){int n;if(!Int32.TryParse(item.GetAttribute(key),NumberStyles.Integer,CultureInfo.InvariantCulture,out n)||n<min||n>max)throw new InvalidOperationException("rich XML의 "+key+" 좌표·크기가 예상과 다릅니다.");return n;}
  static int Span(XmlElement item,string key){if(!item.HasAttribute(key))return 1;return Number(item,key,1,1);}
  static bool Attrs(XmlElement a,XmlElement b,bool count){
   if(a.Name!=b.Name)return false;
   if(count)foreach(XmlAttribute oldCount in a.Attributes.Cast<XmlAttribute>().Where(x=>x.LocalName=="Count")){
    int oldValue,newValue;XmlAttribute next=b.Attributes[oldCount.Name];
    if(next==null||!Int32.TryParse(oldCount.Value,out oldValue)||!Int32.TryParse(next.Value,out newValue)||oldValue<0||newValue<oldValue)return false;
   }
   var left=a.Attributes.Cast<XmlAttribute>().Where(x=>!count||x.LocalName!="Count").OrderBy(x=>x.Name).Select(x=>x.Name+"="+x.Value);
   var right=b.Attributes.Cast<XmlAttribute>().Where(x=>!count||x.LocalName!="Count").OrderBy(x=>x.Name).Select(x=>x.Name+"="+x.Value);return left.SequenceEqual(right);
  }
  internal static int ContentWidth(ComposeXml xml){
   var definitions=xml.Paragraphs.SelectMany(p=>Elements(p).Where(e=>e.LocalName=="TEXT").SelectMany(Elements)).ToArray();
   var sections=definitions.Where(e=>e.LocalName=="SECDEF").ToArray();var cols=definitions.Where(e=>e.LocalName=="COLDEF").ToArray();
   if(sections.Length!=1||cols.Length>1||(cols.Length==1&&Number(cols[0],"Count",1,1)!=1))throw new InvalidOperationException("표·그림 조판은 단일 구역·단일 단의 본문만 지원합니다.");
   XmlElement page=One(sections[0],"PAGEDEF"),margin=One(page,"PAGEMARGIN");
   int width=Math.Min(Number(page,"Width",7200,200000),Number(page,"Height",7200,200000));
   width-=Number(margin,"Left",0,100000)+Number(margin,"Right",0,100000);
   if(margin.HasAttribute("Gutter"))width-=2*Number(margin,"Gutter",0,100000);
   if(width<7200||width>100000)throw new InvalidOperationException("표·그림을 안전하게 배치할 본문 폭을 확인하지 못했습니다.");return width;
  }
  sealed class BodyParagraph{public string Text;public string[] Metadata;public List<XmlElement> Tables=new List<XmlElement>(),Images=new List<XmlElement>();}
  static void StructuralWhitespace(XmlNode parent){foreach(XmlNode node in parent.ChildNodes)if(!(node is XmlElement)&&node.NodeType!=XmlNodeType.Comment&&!String.IsNullOrWhiteSpace(node.Value))throw new InvalidOperationException("native XML의 태그 밖 본문을 해석할 수 없습니다.");}
  static BodyParagraph Paragraph(XmlElement p,bool objects){
   StructuralWhitespace(p);
   var result=new BodyParagraph();var text=new StringBuilder();var metadata=new List<string>();bool started=false;
   foreach(XmlElement run in Elements(p)){
    if(run.LocalName!="TEXT")throw new InvalidOperationException("native 본문 문단에 지원하지 않는 컨트롤이 있습니다.");
    StructuralWhitespace(run);foreach(XmlElement item in Elements(run)){
     if(item.LocalName=="CHAR"&&Elements(item).Length==0){if(!NativeComposer.PlainText(item.InnerText))throw new InvalidOperationException("native 본문에 특수 문자가 있습니다.");text.Append(item.InnerText);if(item.InnerText.Length>0)started=true;}
     else if((item.LocalName=="SECDEF"||item.LocalName=="COLDEF")&&!started){metadata.Add(ComposeXml.Canonical(item));}
     else if(objects&&item.LocalName=="TABLE"){result.Tables.Add(item);started=true;}
     else if(objects&&item.LocalName=="PICTURE"){result.Images.Add(item);started=true;}
     else throw new InvalidOperationException("native 조판 결과에 표·PNG 이외의 개체가 있습니다.");
    }
   }
   result.Text=text.ToString();result.Metadata=metadata.ToArray();return result;
  }
  public static void Verify(ComposeXml before,ComposeXml after,int target,RichExpected expected){VerifyRange(before,after,target,target,expected,false);}
  public static void VerifyReplacement(ComposeXml before,ComposeXml after,int start,int end,RichExpected expected){VerifyRange(before,after,start,end,expected,true);}
  public static void VerifyDeletion(ComposeXml before,ComposeXml after,int start,int end){VerifyRange(before,after,start,end,new RichExpected{ParagraphTexts=new string[]{""},Tables=new RichTable[0],Images=new RichImage[0]},true);}
  static void VerifyRange(ComposeXml before,ComposeXml after,int target,int end,RichExpected expected,bool replace){
   int count=expected.ParagraphTexts.Length;
   if(target<0||end<target||end>=before.Paragraphs.Length||after.Paragraphs.Length!=before.Paragraphs.Length-(end-target+1)+count||!Attrs(before.Body,after.Body,false)||!Attrs(before.Section,after.Section,false)||!Attrs(before.Document.DocumentElement,after.Document.DocumentElement,false))
    throw new InvalidOperationException("rich 삽입 후 본문 문단 수·구역 구조가 예상과 다릅니다.");
   for(int i=0;i<target;i++)if(ComposeXml.Canonical(before.Paragraphs[i])!=ComposeXml.Canonical(after.Paragraphs[i]))throw new InvalidOperationException("삽입 위치 앞의 기존 본문이 변경되었습니다.");
   for(int i=end+1;i<before.Paragraphs.Length;i++)if(ComposeXml.Canonical(before.Paragraphs[i])!=ComposeXml.Canonical(after.Paragraphs[i-(end-target+1)+count]))throw new InvalidOperationException("삽입 위치 뒤의 기존 본문이 변경되었습니다.");
   BodyParagraph blank=Paragraph(before.Paragraphs[target],false);if(!replace&&blank.Text!="")throw new InvalidOperationException("원래 조판 문단이 비어 있지 않습니다.");
   for(int selected=target+1;selected<=end;selected++)if(Paragraph(before.Paragraphs[selected],false).Metadata.Length!=0)throw new InvalidOperationException("선택 중간에 숨은 구역 컨트롤이 있습니다.");
   var tables=new List<XmlElement>();var images=new List<XmlElement>();
   for(int i=0;i<count;i++){
    BodyParagraph p=Paragraph(after.Paragraphs[target+i],true);
    string[] originalMetadata=i==0?blank.Metadata:new string[0];
    if(p.Text!=expected.ParagraphTexts[i])throw new InvalidOperationException("삽입 결과의 "+(i+1)+"번째 문단 텍스트가 계획과 다릅니다. [expectedLength="+expected.ParagraphTexts[i].Length+";actualLength="+p.Text.Length+"]");
    if(!originalMetadata.SequenceEqual(p.Metadata))throw new InvalidOperationException("삽입 결과의 "+(i+1)+"번째 문단 구역 정보가 원래 문서와 다릅니다. [expectedMetadata="+originalMetadata.Length+";actualMetadata="+p.Metadata.Length+"]");
    tables.AddRange(p.Tables);images.AddRange(p.Images);
   }
   if(tables.Count!=expected.Tables.Length||images.Count!=expected.Images.Length)throw new InvalidOperationException("실제 native 표·그림 개수가 계획과 다릅니다.");
   for(int i=0;i<tables.Count;i++)VerifyTable(tables[i],expected.Tables[i]);
   for(int i=0;i<images.Count;i++)VerifyImage(after,images[i],expected.Images[i]);
   Preserve(before.Mapping,after.Mapping);PreserveTail(before.Document.DocumentElement,after.Document.DocumentElement);
  }
  static void Inline(XmlElement obj){XmlElement shape=One(obj,"SHAPEOBJECT"),pos=One(shape,"POSITION");string value=pos.GetAttribute("TreatAsChar");if(value!="true"&&value!="1")throw new InvalidOperationException("표·그림이 본문 inline 개체로 삽입되지 않았습니다.");}
  static void VerifyTable(XmlElement table,RichTable expected){
   if(table.GetElementsByTagName("P").Count!=expected.Rows*expected.Columns||Elements(table).Any(x=>!new string[]{"SHAPEOBJECT","INSIDEMARGIN","ROW","CELLZONELIST"}.Contains(x.LocalName)))throw new InvalidOperationException("native 표에 예상 밖의 캡션·중첩 개체가 있습니다.");
   Inline(table);if(Number(table,"RowCount",1,21)!=expected.Rows||Number(table,"ColCount",1,8)!=expected.Columns)throw new InvalidOperationException("native 표 행·열 수가 계획과 다릅니다.");
   var rows=Elements(table).Where(x=>x.LocalName=="ROW").ToArray();if(rows.Length!=expected.Rows)throw new InvalidOperationException("native 표 ROW 개수가 다릅니다.");var seen=new HashSet<string>();
   foreach(XmlElement row in rows){var cells=Elements(row);if(cells.Length!=expected.Columns||cells.Any(x=>x.LocalName!="CELL"))throw new InvalidOperationException("native 표가 단순한 직사각형이 아닙니다.");
    foreach(XmlElement cell in cells){int r=Number(cell,"RowAddr",0,expected.Rows-1),c=Number(cell,"ColAddr",0,expected.Columns-1);Span(cell,"RowSpan");Span(cell,"ColSpan");if(!seen.Add(r+":"+c))throw new InvalidOperationException("native 표 셀 주소가 중복됩니다.");
     if(Elements(cell).Any(x=>x.LocalName!="PARALIST"&&x.LocalName!="CELLMARGIN"))throw new InvalidOperationException("native 셀에 예상 밖의 컨트롤이 있습니다.");
     XmlElement list=One(cell,"PARALIST");var ps=Elements(list);if(ps.Length!=1||ps[0].LocalName!="P")throw new InvalidOperationException("native 표 셀의 문단 구조가 다릅니다.");BodyParagraph text=Paragraph(ps[0],false);
     if(text.Metadata.Length!=0||text.Text!=expected.Cells[r][c])throw new InvalidOperationException("native 표 셀 내용이 계획과 다릅니다.");
    }
   }
  }
  static void VerifyImage(ComposeXml xml,XmlElement picture,RichImage expected){
   Inline(picture);XmlElement size=One(One(picture,"SHAPEOBJECT"),"SIZE");
   if(Number(size,"Width",1,100000)!=expected.Width||Number(size,"Height",1,100000)!=expected.Height)throw new InvalidOperationException("native 그림의 HWPUNIT 표시 크기가 다릅니다.");
   var image=picture.GetElementsByTagName("IMAGE").Cast<XmlElement>().ToArray();if(image.Length!=1)throw new InvalidOperationException("native 그림 바이너리 참조가 명확하지 않습니다.");
   XmlElement list=One(xml.Mapping,"BINDATALIST");var items=Elements(list);int index=Number(image[0],"BinItem",1,items.Length);
   // HWPML BINDATALIST is ordered. Unknown/zero references fail closed.
   XmlElement item=items[index-1];if(item.LocalName!="BINITEM"||item.GetAttribute("Type")!="Embedding"||!String.Equals(item.GetAttribute("Format"),"png",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("native 그림이 포함된 PNG 데이터가 아닙니다.");
   string id=item.GetAttribute("BinData");if(id.Length==0)throw new InvalidOperationException("native PNG 저장소 ID가 없습니다.");
   var data=xml.Document.GetElementsByTagName("BINDATA").Cast<XmlElement>().Where(x=>x.GetAttribute("Id")==id).ToArray();
   if(data.Length!=1||data[0].ParentNode.LocalName!="BINDATASTORAGE"||(data[0].HasAttribute("Encoding")&&data[0].GetAttribute("Encoding")!="Base64"))throw new InvalidOperationException("native PNG 저장소 매핑을 확인하지 못했습니다.");
   byte[] bytes;try{bytes=Convert.FromBase64String(data[0].InnerText);}catch{throw new InvalidOperationException("native PNG Base64를 읽지 못했습니다.");}
   if(bytes.Length>16*1024*1024||RichFragmentFile.Digest(bytes)!=expected.Sha256||!RichFragmentPackage.Png(bytes))throw new InvalidOperationException("native 그림 원바이트 SHA256을 확인하지 못했습니다. 압축·재인코딩 형식은 이 버전에서 성공으로 처리하지 않습니다.");
  }
  static bool Container(XmlElement e){return e.LocalName=="MAPPINGTABLE"||e.LocalName.EndsWith("LIST",StringComparison.Ordinal)||e.LocalName=="FONTFACE"||e.LocalName=="BINDATASTORAGE";}
  static string Key(XmlElement e){var attr=e.Attributes.Cast<XmlAttribute>().Where(x=>x.LocalName.Equals("Id",StringComparison.OrdinalIgnoreCase)||x.LocalName.Equals("Lang",StringComparison.OrdinalIgnoreCase)).OrderBy(x=>x.Name).Select(x=>x.Name+"="+x.Value).ToArray();return attr.Length==0?null:e.Name+"|"+String.Join("|",attr);}
  // HWPML export may recalculate Size even for the same payload. Ignore only
  // a valid numeric Size difference, and only after every decoded byte matches.
  // Keep identity, encoding, compression, and all other attributes unchanged.
  static bool EqualBinaryPayload(XmlElement a,XmlElement b){
   if(a.LocalName!="BINDATA"||a.ParentNode==null||b.ParentNode==null||a.ParentNode.LocalName!="BINDATASTORAGE"||b.ParentNode.LocalName!="BINDATASTORAGE")return false;
   if(!Attrs(a,b,false)){
    var names=a.Attributes.Cast<XmlAttribute>().Select(x=>x.Name).Concat(b.Attributes.Cast<XmlAttribute>().Select(x=>x.Name)).Distinct().Where(x=>a.HasAttribute(x)!=b.HasAttribute(x)||a.GetAttribute(x)!=b.GetAttribute(x)).Take(8).ToArray();
    long oldSize,newSize;
    bool sizeOnly=names.Length==1&&names[0]=="Size"&&a.HasAttribute("Size")&&b.HasAttribute("Size")&&Int64.TryParse(a.GetAttribute("Size"),NumberStyles.None,CultureInfo.InvariantCulture,out oldSize)&&oldSize>=0&&Int64.TryParse(b.GetAttribute("Size"),NumberStyles.None,CultureInfo.InvariantCulture,out newSize)&&newSize>=0;
    if(!sizeOnly)throw new InvalidOperationException("기존 바이너리 항목의 속성이 변경되었습니다. [ID="+a.GetAttribute("Id")+"; 속성="+String.Join(",",names)+"]");
   }
   if(Elements(a).Length!=0||Elements(b).Length!=0)throw new InvalidOperationException("기존 바이너리 항목의 구조를 확인하지 못했습니다. [ID="+a.GetAttribute("Id")+"]");
   string encoding=a.GetAttribute("Encoding");if(encoding.Length>0&&!String.Equals(encoding,"Base64",StringComparison.OrdinalIgnoreCase))return false;
   try{if(!Convert.FromBase64String(a.InnerText).SequenceEqual(Convert.FromBase64String(b.InnerText)))throw new InvalidOperationException("기존 바이너리 데이터의 내용이 변경되었습니다. [ID="+a.GetAttribute("Id")+"]");return true;}
   catch(FormatException){throw new InvalidOperationException("기존 바이너리 항목의 Base64 형식을 확인하지 못했습니다. [ID="+a.GetAttribute("Id")+"]");}
  }
  internal static void Preserve(XmlElement before,XmlElement after){
   if(!Container(before)){if(ComposeXml.Canonical(before)!=ComposeXml.Canonical(after)&&!EqualBinaryPayload(before,after)){string id=before.GetAttribute("Id");throw new InvalidOperationException("기존 서식·바이너리 레코드가 변경되었습니다. [종류="+before.LocalName+(String.IsNullOrEmpty(id)?"":"; ID="+id)+"]");}return;}
   if(!Attrs(before,after,true))throw new InvalidOperationException("기존 서식·바이너리 목록 속성이 변경되었습니다.");
   var old=Elements(before);var current=Elements(after);
   for(int i=0;i<old.Length;i++){
    string key=Key(old[i]);XmlElement found;
    if(key!=null){var matching=current.Where(x=>Key(x)==key).ToArray();if(matching.Length!=1)throw new InvalidOperationException("기존 ID 참조를 보존하지 못했습니다.");found=matching[0];}
    else{int ordinal=old.Take(i).Count(x=>x.Name==old[i].Name);var matching=current.Where(x=>x.Name==old[i].Name).ToArray();if(ordinal>=matching.Length)throw new InvalidOperationException("기존 순번 레코드가 사라졌습니다.");found=matching[ordinal];}
    Preserve(old[i],found);
   }
  }
  internal static void PreserveTail(XmlElement before,XmlElement after){
   var old=Elements(before).Where(x=>x.LocalName!="HEAD"&&x.LocalName!="BODY").ToArray();var current=Elements(after).Where(x=>x.LocalName!="HEAD"&&x.LocalName!="BODY").ToArray();
   foreach(XmlElement item in old){var same=current.Where(x=>x.Name==item.Name).ToArray();if(same.Length!=1)throw new InvalidOperationException("기존 부가 데이터가 사라졌습니다.");
    if(item.LocalName!="TAIL"){Preserve(item,same[0]);continue;}if(!Attrs(item,same[0],false))throw new InvalidOperationException("기존 TAIL 속성이 변경되었습니다.");
    if(Elements(same[0]).Any(x=>x.LocalName!="BINDATASTORAGE"&&!Elements(item).Any(o=>o.Name==x.Name)))throw new InvalidOperationException("예상하지 않은 TAIL 스크립트·부가 데이터가 추가되었습니다.");
    foreach(XmlElement part in Elements(item)){var next=Elements(same[0]).Where(x=>x.Name==part.Name).ToArray();if(next.Length!=1)throw new InvalidOperationException("기존 TAIL 데이터가 사라졌습니다.");Preserve(part,next[0]);}
   }
   foreach(XmlElement added in current.Where(x=>!old.Any(o=>o.Name==x.Name)))if(added.LocalName!="TAIL"||Elements(added).Any(x=>x.LocalName!="BINDATASTORAGE"))throw new InvalidOperationException("예상하지 않은 부가 XML이 추가되었습니다.");
  }
 }
 internal static class RichMemo{
  static string Color(XmlElement e,string name){int n;if(!Int32.TryParse(e.GetAttribute(name),NumberStyles.None,CultureInfo.InvariantCulture,out n)||n<0||n>0xffffff)throw new InvalidOperationException("기존 메모 색상을 보존할 수 없습니다.");return String.Format(CultureInfo.InvariantCulture,"#{0:X2}{1:X2}{2:X2}",n&255,(n>>8)&255,(n>>16)&255);}
  internal static Dictionary<string,object> Read(ComposeXml xml){
   var lists=RichXml.Elements(xml.Mapping).Where(x=>x.LocalName=="MEMOSHAPELIST").ToArray();
   var sections=xml.Document.GetElementsByTagName("SECDEF").Cast<XmlElement>().ToArray();if(sections.Length!=1)throw new InvalidOperationException("구역별 메모 설정을 확인하지 못했습니다.");
   // Do not silently lose actual comment controls or unfamiliar memo styles.
   if(xml.Document.GetElementsByTagName("MEMO").Cast<XmlElement>().Any(x=>x.ParentNode==null||x.ParentNode.LocalName!="MEMOSHAPELIST")||xml.Document.GetElementsByTagName("FIELD").Cast<XmlElement>().Any(x=>(x.GetAttribute("Type")??"").IndexOf("memo",StringComparison.OrdinalIgnoreCase)>=0))throw new InvalidOperationException("메모가 포함된 문서는 표·이미지 조판 전에 보존 여부를 확인해야 합니다.");
   if(lists.Length==0){if(sections[0].HasAttribute("MemoShapeId"))throw new InvalidOperationException("구역의 메모 서식 정의를 확인하지 못했습니다.");return null;}
   if(lists.Length!=1)throw new InvalidOperationException("기존 메모 서식 목록이 복잡합니다.");
   var entries=RichXml.Elements(lists[0]);if(entries.Length!=1||entries[0].LocalName!="MEMO"||lists[0].GetAttribute("Count")!="1")throw new InvalidOperationException("여러 메모 서식을 안전하게 보존하는 기능은 아직 지원하지 않습니다.");
   XmlElement shape=entries[0];int width;
   var permitted=new string[]{"ActiveColor","FillColor","Id","LineColor","LineType","LineWidth","MemoType","Width"};
   if(shape.Attributes.Count!=permitted.Length||shape.Attributes.Cast<XmlAttribute>().Any(x=>!permitted.Contains(x.Name))||RichXml.Elements(shape).Length!=0)throw new InvalidOperationException("기존 메모 서식에 지원하지 않는 속성이 있습니다.");
   if(shape.GetAttribute("Id")!="1"||sections[0].GetAttribute("MemoShapeId")!="1"||shape.GetAttribute("LineType")!="1"||shape.GetAttribute("LineWidth")!="1"||shape.GetAttribute("MemoType")!="0"||!Int32.TryParse(shape.GetAttribute("Width"),NumberStyles.None,CultureInfo.InvariantCulture,out width)||width<1||width>100000)throw new InvalidOperationException("기존 메모 서식이 지원 범위를 벗어나 보존할 수 없습니다.");
   return new Dictionary<string,object>{{"id",1},{"width",width},{"lineColor",Color(shape,"LineColor")},{"fillColor",Color(shape,"FillColor")},{"activeColor",Color(shape,"ActiveColor")}};
  }
 }
 internal static class RichFragmentPackage{
  internal static bool Png(byte[] bytes){byte[] signature={137,80,78,71,13,10,26,10};return bytes!=null&&bytes.Length>=24&&signature.SequenceEqual(bytes.Take(8));}
  static byte[] Read(ZipArchiveEntry entry,int limit){if(entry.Length<0||entry.Length>limit)throw new InvalidOperationException("fragment ZIP 항목이 허용 크기를 넘습니다.");using(var input=entry.Open())using(var output=new MemoryStream()){byte[] buffer=new byte[8192];int count;while((count=input.Read(buffer,0,buffer.Length))>0){if(output.Length+count>limit)throw new InvalidOperationException("fragment ZIP 압축 해제 한도를 넘습니다.");output.Write(buffer,0,count);}return output.ToArray();}}
  internal static XmlDocument Xml(byte[] bytes){var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=8*1024*1024};var result=new XmlDocument{XmlResolver=null,PreserveWhitespace=true};using(var input=new MemoryStream(bytes))using(var reader=XmlReader.Create(input,settings))result.Load(reader);return result;}
  static XmlElement[] Desc(XmlElement e,string name){return e.GetElementsByTagName("*").Cast<XmlElement>().Where(x=>x.LocalName==name).ToArray();}
  static int Attr(XmlElement e,string name,int max){return RichXml.Number(e,name,0,max);}
  sealed class PInfo{public string Text;public List<XmlElement> Tables=new List<XmlElement>(),Images=new List<XmlElement>();}
  static PInfo Paragraph(XmlElement p,bool objects){var result=new PInfo();var text=new StringBuilder();
   foreach(XmlElement child in RichXml.Elements(p)){
    if(child.LocalName=="linesegarray")continue;if(child.LocalName!="run")throw new InvalidOperationException("fragment 문단에 알 수 없는 구조가 있습니다.");
    foreach(XmlElement run in RichXml.Elements(child)){
     if(run.LocalName=="t"&&RichXml.Elements(run).Length==0){if(!NativeComposer.PlainText(run.InnerText))throw new InvalidOperationException("fragment 본문에 특수 문자가 있습니다.");text.Append(run.InnerText);}
     else if(objects&&(run.LocalName=="secPr"||run.LocalName=="colPr")){}
     else if(objects&&run.LocalName=="ctrl"&&RichXml.Elements(run).All(x=>x.LocalName=="colPr")){}
     else if(objects&&run.LocalName=="tbl")result.Tables.Add(run);
     else if(objects&&run.LocalName=="pic")result.Images.Add(run);
     else throw new InvalidOperationException("fragment에 허용하지 않은 필드·도형·컨트롤이 있습니다.");
    }
   }result.Text=text.ToString();return result;
  }
  static void Inline(XmlElement item){XmlElement pos=RichXml.One(item,"pos");if(pos.GetAttribute("treatAsChar")!="1"&&pos.GetAttribute("treatAsChar")!="true")throw new InvalidOperationException("fragment 표·그림은 inline 이어야 합니다.");}
  internal static void ValidateMemo(Stream file,Dictionary<string,object> expected){
   file.Position=0;using(var zip=new ZipArchive(file,ZipArchiveMode.Read,true)){
    var h=zip.GetEntry("Contents/header.xml");var s=zip.GetEntry("Contents/section0.xml");if(h==null||s==null)throw new InvalidOperationException("메모 서식 검증에 필요한 조각이 없습니다.");
    var header=Xml(Read(h,8*1024*1024));var section=Xml(Read(s,8*1024*1024));
    var shapes=header.GetElementsByTagName("*").Cast<XmlElement>().Where(x=>x.LocalName=="memoPr").ToArray();var groups=header.GetElementsByTagName("*").Cast<XmlElement>().Where(x=>x.LocalName=="memoProperties").ToArray();
    var sections=section.GetElementsByTagName("*").Cast<XmlElement>().Where(x=>x.LocalName=="secPr").ToArray();
    if(sections.Length!=1)throw new InvalidOperationException("조각 구역 메모 설정을 확인하지 못했습니다.");
    if(expected==null){string reference=sections[0].GetAttribute("memoShapeIDRef");if(shapes.Length!=0||groups.Length!=0||(reference!=""&&reference!="0"))throw new InvalidOperationException("원본에 없는 메모 서식이 조각에 포함되었습니다.");}
    else{
     if(shapes.Length!=1||groups.Length!=1||groups[0].GetAttribute("itemCnt")!="1"||shapes[0].ParentNode!=groups[0]||sections[0].GetAttribute("memoShapeIDRef")!="1")throw new InvalidOperationException("조각에서 원래 메모 서식 참조를 보존하지 못했습니다.");
     XmlElement shape=shapes[0];var attrs=new Dictionary<string,string>{{"id","1"},{"width",Convert.ToString(expected["width"],CultureInfo.InvariantCulture)},{"lineWidth","1"},{"lineType","SOLID"},{"lineColor",(string)expected["lineColor"]},{"fillColor",(string)expected["fillColor"]},{"activeColor",(string)expected["activeColor"]},{"memoType","NOMAL"}};
     if(shape.Attributes.Count!=attrs.Count||attrs.Any(x=>shape.GetAttribute(x.Key)!=x.Value)||shape.ChildNodes.Cast<XmlNode>().Any(x=>x is XmlElement))throw new InvalidOperationException("조각의 메모 서식이 원래 문서와 다릅니다.");
    }
   }file.Position=0;
  }
  internal static void Validate(Stream file,RichExpected expected){
   file.Position=0;using(var zip=new ZipArchive(file,ZipArchiveMode.Read,true)){
    if(zip.Entries.Count>128)throw new InvalidOperationException("fragment ZIP 항목이 너무 많습니다.");var entries=new Dictionary<string,ZipArchiveEntry>(StringComparer.Ordinal);long expanded=0;
    foreach(var entry in zip.Entries){string name=entry.FullName;if(name.Length==0||name.Contains("\\")||name.Contains(":")||name.StartsWith("/")||name.Split('/').Any(x=>x==".."||x==".")||entries.ContainsKey(name))throw new InvalidOperationException("fragment ZIP 경로가 올바르지 않습니다.");if(!new string[]{"mimetype","version.xml","settings.xml","META-INF/container.xml","META-INF/container.rdf","META-INF/manifest.xml","Contents/content.hpf","Contents/header.xml","Contents/section0.xml","Preview/PrvText.txt","Preview/PrvImage.png"}.Contains(name)&&!(name.StartsWith("BinData/",StringComparison.Ordinal)&&name.EndsWith(".png",StringComparison.OrdinalIgnoreCase)))throw new InvalidOperationException("확인하지 않은 HWPX package 항목입니다.");
entries.Add(name,entry);expanded+=entry.Length;if(expanded>64L*1024*1024)throw new InvalidOperationException("fragment ZIP 전체 크기가 너무 큽니다.");
     if(name.IndexOf("script",StringComparison.OrdinalIgnoreCase)>=0&&Read(entry,1024*1024).Any(b=>b!=9&&b!=10&&b!=13&&b!=32))throw new InvalidOperationException("실행 스크립트가 포함된 fragment는 허용하지 않습니다.");
    }
    foreach(string required in new string[]{"mimetype","Contents/content.hpf","Contents/header.xml","Contents/section0.xml"})if(!entries.ContainsKey(required))throw new InvalidOperationException("fragment HWPX 필수 항목이 없습니다.");
    if(Encoding.ASCII.GetString(Read(entries["mimetype"],100)).Trim()!="application/hwp+zip")throw new InvalidOperationException("fragment가 HWPX 형식이 아닙니다.");
    if(entries.Keys.Any(n=>n.StartsWith("Contents/section",StringComparison.Ordinal)&&n.EndsWith(".xml",StringComparison.Ordinal)&&n!="Contents/section0.xml"))throw new InvalidOperationException("fragment는 단일 구역이어야 합니다.");
    XmlDocument section=Xml(Read(entries["Contents/section0.xml"],8*1024*1024));XmlDocument manifest=Xml(Read(entries["Contents/content.hpf"],8*1024*1024));Xml(Read(entries["Contents/header.xml"],8*1024*1024));
    if(section.DocumentElement==null||section.DocumentElement.LocalName!="sec")throw new InvalidOperationException("fragment 구역 루트가 올바르지 않습니다.");
    var paragraphs=RichXml.Elements(section.DocumentElement);if(paragraphs.Length!=expected.ParagraphTexts.Length||paragraphs.Any(x=>x.LocalName!="p"))throw new InvalidOperationException("fragment 최상위 문단 수가 예상과 다릅니다.");
    var tables=new List<XmlElement>();var images=new List<XmlElement>();for(int i=0;i<paragraphs.Length;i++){var p=Paragraph(paragraphs[i],true);if(p.Text!=expected.ParagraphTexts[i])throw new InvalidOperationException("fragment 본문 텍스트가 예상과 다릅니다.");tables.AddRange(p.Tables);images.AddRange(p.Images);}
    if(tables.Count!=expected.Tables.Length||images.Count!=expected.Images.Length)throw new InvalidOperationException("fragment 표·그림 개수가 예상과 다릅니다.");
    for(int i=0;i<tables.Count;i++){
     XmlElement table=tables[i];Inline(table);XmlElement tableSize=RichXml.One(table,"sz");if(RichXml.Number(tableSize,"width",1,expected.ContentWidth)>expected.ContentWidth)throw new InvalidOperationException("fragment 표가 본문 폭을 넘습니다.");RichTable desired=expected.Tables[i];if(Attr(table,"rowCnt",21)!=desired.Rows||Attr(table,"colCnt",8)!=desired.Columns)throw new InvalidOperationException("fragment 표 크기가 다릅니다.");
     var rows=RichXml.Elements(table).Where(x=>x.LocalName=="tr").ToArray();if(rows.Length!=desired.Rows)throw new InvalidOperationException("fragment 표 행이 다릅니다.");var seen=new HashSet<string>();
     foreach(XmlElement row in rows){var cells=RichXml.Elements(row);if(cells.Length!=desired.Columns||cells.Any(x=>x.LocalName!="tc"))throw new InvalidOperationException("fragment 표가 직사각형이 아닙니다.");foreach(XmlElement cell in cells){XmlElement addr=RichXml.One(cell,"cellAddr"),span=RichXml.One(cell,"cellSpan");int r=Attr(addr,"rowAddr",desired.Rows-1),c=Attr(addr,"colAddr",desired.Columns-1);if(Attr(span,"rowSpan",1)!=1||Attr(span,"colSpan",1)!=1||!seen.Add(r+":"+c))throw new InvalidOperationException("fragment에 병합·중복 셀이 있습니다.");var ps=RichXml.Elements(RichXml.One(cell,"subList"));if(ps.Length!=1||ps[0].LocalName!="p"||Paragraph(ps[0],false).Text!=desired.Cells[r][c])throw new InvalidOperationException("fragment 셀 텍스트가 다릅니다.");}}
    }
    var items=manifest.GetElementsByTagName("*").Cast<XmlElement>().Where(x=>x.LocalName=="item").ToArray();
    if(items.Select(x=>x.GetAttribute("id")).Distinct().Count()!=items.Length||items.Any(x=>!entries.ContainsKey(x.GetAttribute("href"))))throw new InvalidOperationException("fragment manifest ID·내부 경로가 올바르지 않습니다.");
    var used=new HashSet<string>(StringComparer.Ordinal);
    for(int i=0;i<images.Count;i++){
     RichImage desired=expected.Images[i];XmlElement picture=images[i];Inline(picture);XmlElement size=RichXml.One(picture,"sz");if(Attr(size,"width",100000)!=desired.Width||Attr(size,"height",100000)!=desired.Height)throw new InvalidOperationException("fragment 그림 표시 크기가 다릅니다.");
     var refs=Desc(picture,"img");if(refs.Length!=1)throw new InvalidOperationException("fragment 그림 참조가 명확하지 않습니다.");string id=refs[0].GetAttribute("binaryItemIDRef");var mapped=items.Where(x=>x.GetAttribute("id")==id).ToArray();
     if(id.Length==0||mapped.Length!=1||mapped[0].GetAttribute("isEmbeded")!="1")throw new InvalidOperationException("fragment 그림이 포함된 자원이 아닙니다.");string href=mapped[0].GetAttribute("href");
     if(!href.StartsWith("BinData/",StringComparison.Ordinal)||!entries.ContainsKey(href))throw new InvalidOperationException("fragment 그림 경로가 패키지 내부에 없습니다.");byte[] bytes=Read(entries[href],16*1024*1024);
     if(!Png(bytes)||RichFragmentFile.Digest(bytes)!=desired.Sha256)throw new InvalidOperationException("fragment PNG 바이트가 예상과 다릅니다.");used.Add(href);
    }
    if(entries.Keys.Any(n=>n.StartsWith("BinData/",StringComparison.Ordinal)&&!n.EndsWith("/",StringComparison.Ordinal)&&!used.Contains(n)))throw new InvalidOperationException("fragment에 확인하지 않은 바이너리 자원이 있습니다.");
   }file.Position=0;
  }
 }
}






