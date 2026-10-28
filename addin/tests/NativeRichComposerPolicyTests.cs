using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using Hwp.Addon;
using System.Web.Script.Serialization;
using HAddon;

internal static class NativeRichComposerPolicyTests {
 static int checks;static string fixture;static Dictionary<string,object> supplied;static RichExpected expected;static byte[] png;
 static void Main(){AppDomain.CurrentDomain.AssemblyResolve+=Resolve;try{Run();Console.WriteLine("PASS: "+checks+" rich composer assertions; fake backend, owned temporary package copies, and synthetic HWPML only. No HWP process/document used.");}catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}}
 static Assembly Resolve(object sender,ResolveEventArgs args){string name=new AssemblyName(args.Name).Name;if(!new string[]{"HwpAddonHelper","Hnc.Foundation.Interface","Hnc.Foundation.InternalInterface"}.Contains(name))return null;return Assembly.LoadFrom(Path.Combine(@"C:\Program Files (x86)\Hnc\Office 2024\HOffice130\Bin",name+".dll"));}
 static void Assert(bool value,string message){checks++;if(!value)throw new Exception(message);}
 static void Fails(Action action,string message){bool failed=false;try{action();}catch(InvalidOperationException){failed=true;}Assert(failed,message);}
 static void FailsWith(Action action,string expectedMessage,string message){string actual="";try{action();}catch(InvalidOperationException ex){actual=ex.Message;}Assert(actual.Contains(expectedMessage),message+": "+actual);}
 static Dictionary<string,object> Begin(NativeRichComposer editor){return(Dictionary<string,object>)editor.BeginRequest();}
 static Dictionary<string,object> Context(Dictionary<string,object> state){return(Dictionary<string,object>)state["context"];}
 static string DirectoryFor(Dictionary<string,object> state){return(string)Context(state)["fragmentDirectory"];}
 static string Id(Dictionary<string,object> state){return(string)state["targetId"];}
 static Dictionary<string,object> Arguments(Dictionary<string,object> state,string path){return new Dictionary<string,object>{{"expectedTargetId",Id(state)},{"fragmentPath",path},{"fragmentSha256",RichFragmentFile.Digest(File.ReadAllBytes(path))},{"expected",supplied}};}
 static string Copy(Dictionary<string,object> state){string dir=DirectoryFor(state);if(String.IsNullOrEmpty(dir))throw new Exception("fake native capture disabled: "+Context(state)["reason"]);Directory.CreateDirectory(dir);string path=Path.Combine(dir,"fragment.hwpx");File.Copy(fixture,path,true);return path;}
 static void Run(){
  fixture=Path.GetFullPath(@"investigation\rich-fragment-fixture\fragment.hwpx");var receipt=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(@"investigation\rich-fragment-fixture\receipt.json",Encoding.UTF8));supplied=(Dictionary<string,object>)receipt["expected"];expected=RichExpected.Parse(supplied,42520);
  using(var zip=ZipFile.OpenRead(fixture))using(var input=zip.GetEntry("BinData/image1.png").Open())using(var output=new MemoryStream()){input.CopyTo(output);png=output.ToArray();}
  using(var file=File.OpenRead(fixture))RichFragmentPackage.Validate(file,expected);Assert(true,"real builder fixture passes exact native preflight");
  var backend=new FakeBackend();var editor=new NativeRichComposer(backend);var state=Begin(editor);string path=Copy(state),dir=DirectoryFor(state);
  Assert((bool)((Dictionary<string,object>)state["capabilities"])["richCompose"]&&(bool)((Dictionary<string,object>)state["capabilities"])["compose"]&&Id(state).Length==32,"empty target exposes rich/compose capabilities");
  Assert(Directory.Exists(DirectoryFor(state)),"successful native state creates an existing request GUID directory before builder realpath");
  Assert((string)Context(state)["scope"]=="emptyBodyParagraph"&&(int)Context(state)["contentWidthHwpUnit"]==42520,"bridge scope and finite host width exact");
  string stagedPath=null,originalPath=path;
  backend.OnApply=delegate(string filePath){
   stagedPath=filePath;Assert(filePath!=originalPath&&Path.GetDirectoryName(filePath)==dir&&Path.GetFileName(filePath).StartsWith("native-import-"),"native importer receives a distinct request-owned copy");
   using(var exclusive=new FileStream(filePath,FileMode.Open,FileAccess.Read,FileShare.None)){Assert(exclusive.Length>0,"closed import copy permits exclusive read by native importer");}
   bool locked=false;try{using(var write=new FileStream(originalPath,FileMode.Open,FileAccess.Write,FileShare.ReadWrite)){}}catch(IOException){locked=true;}Assert(locked,"verified original remains read-locked throughout native callback");
  };
  var applied=(Dictionary<string,object>)editor.Invoke("hwp_editor_rich_apply",Arguments(state,path));Assert((bool)applied["applied"]&&(bool)applied["changed"]&&(bool)applied["imageBytesVerified"]&&(int)applied["tableCount"]==1&&(int)applied["imageCount"]==1&&(int)applied["paragraphCount"]==5,"verified table/image receipt exact");
  Assert(backend.ApplyCalls==1&&!editor.RequiresReview,"one native import and verified success");Assert(stagedPath!=null&&!File.Exists(stagedPath)&&File.Exists(originalPath),"only the native copy is cleaned after callback; original remains until request end");editor.EndRequest();Assert(!Directory.Exists(dir),"owned temporary request directory cleaned after lock released");
  Fails(delegate{editor.Invoke("hwp_editor_rich_state",new Dictionary<string,object>());},"ended request cannot invoke");

  backend=new FakeBackend();editor=new NativeRichComposer(backend);state=Begin(editor);path=Copy(state);backend.Revision++;
  Fails(delegate{editor.Invoke("hwp_editor_rich_apply",Arguments(state,path));},"stale native document hash rejected");Assert(backend.ApplyCalls==0,"stale target causes zero import calls");editor.EndRequest();
  backend=new FakeBackend();editor=new NativeRichComposer(backend);state=Begin(editor);path=Copy(state);var wrong=Arguments(state,path);wrong["expectedTargetId"]="wrong";
  Fails(delegate{editor.Invoke("hwp_editor_rich_apply",wrong);},"wrong target ID rejected");Assert(backend.ApplyCalls==0,"wrong identity causes zero native writes");editor.EndRequest();
  backend=new FakeBackend();editor=new NativeRichComposer(backend);state=Begin(editor);path=Copy(state);wrong=Arguments(state,path);wrong["fragmentSha256"]=new string('0',64);
  Fails(delegate{editor.Invoke("hwp_editor_rich_apply",wrong);},"fragment content hash mismatch rejected");Assert(backend.ApplyCalls==0,"corrupt file causes zero import calls");editor.EndRequest();
  backend=new FakeBackend();editor=new NativeRichComposer(backend);state=Begin(editor);path=Copy(state);wrong=Arguments(state,path);wrong["fragmentPath"]=fixture;
  Fails(delegate{editor.Invoke("hwp_editor_rich_apply",wrong);},"external fixture path rejected despite valid digest");Assert(backend.ApplyCalls==0,"external path causes zero native writes");editor.EndRequest();
  foreach(string invalid in new string[]{Path.Combine(RichFragmentFile.OwnedRoot,Guid.NewGuid().ToString("N"),"x.hwpx"),Path.Combine(RichFragmentFile.OwnedRoot,"..","x.hwpx"),Path.Combine(RichFragmentFile.OwnedRoot,"x.txt")}){
   Fails(delegate{RichFragmentFile.Resolve(Path.Combine(RichFragmentFile.OwnedRoot,Guid.NewGuid().ToString("N")),invalid);},"other request/escape/non-HWPX path rejected");
  }
  foreach(bool during in new bool[]{false,true}){backend=new FakeBackend{FailBefore=!during,FailDuring=during};editor=new NativeRichComposer(backend);state=Begin(editor);path=Copy(state);Fails(delegate{editor.Invoke("hwp_editor_rich_apply",Arguments(state,path));},"backend failure surfaced");Assert(editor.RequiresReview==during,"post-mutation uncertainty requires review; known preflight failure does not");editor.EndRequest();}
  backend=new FakeBackend{WrongCount=true};editor=new NativeRichComposer(backend);state=Begin(editor);path=Copy(state);Fails(delegate{editor.Invoke("hwp_editor_rich_apply",Arguments(state,path));},"wrong native receipt count rejected");Assert(editor.RequiresReview,"wrong receipt after native return requires review");editor.EndRequest();
  backend=new FakeBackend();editor=new NativeRichComposer(backend);state=Begin(editor);path=Copy(state);dir=DirectoryFor(state);backend.OnApply=delegate(string unused){editor.EndRequest();};editor.Invoke("hwp_editor_rich_apply",Arguments(state,path));Assert(!Directory.Exists(dir),"reentrant closure defers owned cleanup until native readback/lock completion");

  var badExpected=new Dictionary<string,object>(supplied);badExpected["extra"]="unsafe";Fails(delegate{RichExpected.Parse(badExpected,42520);},"unknown expected fields rejected");
  badExpected=new Dictionary<string,object>(supplied);badExpected["paragraphTexts"]=new object[]{"line\nbreak"};Fails(delegate{RichExpected.Parse(badExpected,42520);},"inline controls rejected before reading source");
  Fails(delegate{RichExpected.Parse(supplied,1000);},"image wider than actual host width rejected");

  PackageFails("Contents/section0.xml",delegate(byte[] bytes){return Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("표와 그림 구조 검증","changed"));},"source paragraph mismatch rejected before native call");
  PackageFails("Contents/section0.xml",delegate(byte[] bytes){return Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("rowSpan=\"1\"","rowSpan=\"2\""));},"merged source cells rejected before native call");
  PackageFails("BinData/image1.png",delegate(byte[] bytes){bytes[bytes.Length-1]^=1;return bytes;},"source PNG bytes mismatch rejected");
  PackageFails("Contents/content.hpf",delegate(byte[] bytes){return Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("BinData/image1.png","https://example.invalid/image.png"));},"external resource manifest rejected");
  PackageFails("Scripts/sourceScripts.js",delegate(byte[] unused){return Encoding.UTF8.GetBytes("function OnDocument_Open(){danger();}");},"source executable script package rejected");

  string before=Native(false),after=Native(true);Verify(before,after);Assert(true,"native synthetic table/image import and outside preservation verified");
  FailsWith(delegate{Verify(before,after.Replace(P(expected.ParagraphTexts[0]),P("changed text")));},"문단 텍스트가 계획과 다릅니다","body text mismatch reports its category and paragraph");
  string unexpectedMeta="<P><TEXT><COLDEF Count='1'/><CHAR>"+System.Security.SecurityElement.Escape(expected.ParagraphTexts[0])+"</CHAR></TEXT></P>";
  FailsWith(delegate{Verify(before,after.Replace(P(expected.ParagraphTexts[0]),unexpectedMeta));},"문단 구역 정보가 원래 문서와 다릅니다","metadata mismatch reports its category and paragraph");
  Fails(delegate{Verify(before,after.Replace("outside prefix","changed outside"));},"native unselected prefix preserved");
  Fails(delegate{Verify(before,after.Replace("outside suffix","changed outside"));},"native unselected suffix preserved");
  Fails(delegate{Verify(before,after.Replace("ColCount='2'","ColCount='3'"));},"native table column count mismatch caught");
  Fails(delegate{Verify(before,after.Replace("<CHAR>편집 가능</CHAR>","<CHAR>wrong cell</CHAR>"));},"every native cell text checked");
  Fails(delegate{Verify(before,after.Replace("RowSpan='1'","RowSpan='2'"));},"native merged cells rejected");
  Fails(delegate{Verify(before,after.Replace("<CHAR>빈 셀</CHAR>","<CTRL/><CHAR>빈 셀</CHAR>"));},"native hidden cell control rejected");
  Fails(delegate{Verify(before,after.Replace("Width='24000'","Width='23999'"));},"native image display size checked");
  Fails(delegate{Verify(before,after.Replace("BinItem='2'","BinItem='0'"));},"unknown/zero native binary ordinal fails closed");
  Fails(delegate{Verify(before,after.Replace("BinItem='2'","BinItem='1'"));},"native image mapping must reach exact expected bytes");
  Fails(delegate{Verify(before,after.Replace(Convert.ToBase64String(png),Convert.ToBase64String(new byte[]{1,2,3})));},"native PNG raw bytes checked");
  Fails(delegate{Verify(before,after.Replace("Type='Embedding' Format='png' BinData='2'","Type='Link' Format='png' BinData='2'"));},"linked native image rejected");
  Fails(delegate{Verify(before,after.Replace("Height='1000'","Height='9000'"));},"existing style definitions cannot change");
  Fails(delegate{Verify(before,after.Replace("b2xkLWJ5dGVz","bmV3LWJ5dGVz"));},"existing binary payload preserved");
  Fails(delegate{Verify(before,after.Replace("</TAIL>","<SCRIPTCODE><SCRIPTSOURCE>danger</SCRIPTSOURCE></SCRIPTCODE></TAIL>"));},"new native tail script refused");
  Fails(delegate{Verify(before,after.Replace("<TEXT><CHAR>합성 파란 그림</CHAR>","<TEXT>hidden literal<CHAR>합성 파란 그림</CHAR>"));},"unparsed native text cannot hide between tags");
  string selectedBefore=Native(false).Replace(P(""),P("first selected")+P("second selected"));
  RichXml.VerifyDeletion(ComposeXml.Parse(selectedBefore),ComposeXml.Parse(before),1,2);Assert(true,"whole-paragraph deletion preserves outside original body/definitions/binaries");
  RichXml.VerifyReplacement(ComposeXml.Parse(selectedBefore),ComposeXml.Parse(after),1,2,expected);Assert(true,"whole multi paragraph native replacement matches expected inserted content");
  Fails(delegate{RichXml.VerifyReplacement(ComposeXml.Parse(selectedBefore),ComposeXml.Parse(after.Replace("outside suffix","changed")),1,2,expected);},"selection replacement cannot alter text after selected paragraphs");
  foreach(bool reverse in new bool[]{false,true}){
   var native=new FakeNativeDocument(selectedBefore,Native(true));native.SetSelection(reverse?2:1,reverse?15:0,reverse?1:2,reverse?0:15);
   editor=new NativeRichComposer(delegate{return native.Proxy;});state=Begin(editor);path=Copy(state);
   Assert((bool)((Dictionary<string,object>)state["capabilities"])["compose"]&&(string)state["source_scope"]=="wholeBodyParagraphSelection"&&(string)state["text"]=="first selected\nsecond selected"&&(bool)Context(state)["selectionPresent"],"native whole selection pin and exact source text, including reverse drag");
   Assert(native.DeleteCalls==0&&native.InsertCalls==0,"capture and source generation never delete selected text");
   applied=(Dictionary<string,object>)editor.Invoke("hwp_editor_rich_apply",Arguments(state,path));
   Assert(native.DeleteCalls==1&&native.InsertCalls==1&&native.Trace.SequenceEqual(new string[]{"Delete","InsertFile"}),"native selected replacement deletes once and inserts once in order");
   Assert((int)native.InsertParameters["KeepSection"]==0&&(int)native.InsertParameters["KeepStyle"]==1&&(string)native.InsertParameters["FileFormat"]=="HWPX","actual native action parameter keys and host section policy");
   Assert((bool)applied["applied"]&&!editor.RequiresReview,"verified native fake selection pipeline returns success");editor.EndRequest();
  }
  {
   var native=new FakeNativeDocument(selectedBefore,Native(true));native.SetSelection(1,1,2,15);editor=new NativeRichComposer(delegate{return native.Proxy;});state=Begin(editor);
   Assert(!(bool)((Dictionary<string,object>)state["capabilities"])["compose"]&&(bool)Context(state)["selectionPresent"]&&(string)state["text"]=="","partial paragraph selection is unsupported before inference, preserves selection-present state");Assert(native.DeleteCalls==0&&native.InsertCalls==0,"unsupported partial range never mutates native document");editor.EndRequest();
  }
  {
   var native=new FakeNativeDocument(selectedBefore,Native(true)){ForceMode=3};native.SetSelection(1,0,2,15);editor=new NativeRichComposer(delegate{return native.Proxy;});state=Begin(editor);
   Assert(!(bool)((Dictionary<string,object>)state["capabilities"])["compose"]&&(bool)Context(state)["selectionPresent"],"cell block/object mode is refused before inference");editor.EndRequest();
  }
  foreach(int failure in new int[]{1,2,3}){
   var native=new FakeNativeDocument(selectedBefore,Native(true)){ThrowInsert=failure==1,NoOpDelete=failure==2,ChangeOutsideAfterDelete=failure==3};native.SetSelection(1,0,2,15);editor=new NativeRichComposer(delegate{return native.Proxy;});state=Begin(editor);path=Copy(state);
   Fails(delegate{editor.Invoke("hwp_editor_rich_apply",Arguments(state,path));},"native post-delete/import failure surfaced");
   Assert(editor.RequiresReview==(failure!=2)&&native.DeleteCalls==1,"changed/deleted original requires review; a proven no-op deletion does not");
   Assert(native.InsertCalls==(failure==1?1:0),"failed deletion readback prevents later file insertion");editor.EndRequest();
  }
  {
   var native=new FakeNativeDocument(selectedBefore,Native(true));native.SetSelection(1,0,2,15);editor=new NativeRichComposer(delegate{return native.Proxy;});state=Begin(editor);path=Copy(state);wrong=Arguments(state,path);wrong["fragmentSha256"]=new string('0',64);
   Fails(delegate{editor.Invoke("hwp_editor_rich_apply",wrong);},"fragment validation happens before selection deletion");Assert(native.DeleteCalls==0&&native.InsertCalls==0,"bad fragment cannot remove original selected text");editor.EndRequest();
  }
  foreach(int count in new int[]{24,25}){
   string selected=String.Join("",Enumerable.Range(0,count).Select(i=>P("selected "+i)));string source=Native(false).Replace(P(""),selected);var native=new FakeNativeDocument(source,Native(true));native.SetSelection(1,0,count,("selected "+(count-1)).Length);
   editor=new NativeRichComposer(delegate{return native.Proxy;});state=Begin(editor);Assert((bool)((Dictionary<string,object>)state["capabilities"])["compose"]==(count==24),"whole-selection paragraph upper bound exact");if(count==24)Assert(((string)state["text"]).Split('\n').Length==24,"24 paragraph source text remains pinned in order");Assert(native.DeleteCalls==0&&native.InsertCalls==0,"paragraph count validation has zero native mutations");editor.EndRequest();
  }
  {
   var native=new FakeNativeDocument(selectedBefore,Native(true)){SelectionList=1};native.SetSelection(1,0,2,15);editor=new NativeRichComposer(delegate{return native.Proxy;});state=Begin(editor);Assert(!(bool)((Dictionary<string,object>)state["capabilities"])["compose"]&&(bool)Context(state)["selectionPresent"],"selected cell text list is refused before source draft inference");editor.EndRequest();
  }
  string terraDirectory=@"investigation\rich-terra-fixture-dfcb7093-76ec-47c8-bdef-bb192dffd5ff";
  var terra=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(terraDirectory,"receipt.json"),Encoding.UTF8));
  RichExpected terraExpected=RichExpected.Parse(terra["expected"],42520);
  using(var input=File.OpenRead(Path.Combine(terraDirectory,"fragment.hwpx")))RichFragmentPackage.Validate(input,terraExpected);
  Assert(terraExpected.Tables.Length==1&&terraExpected.Images.Length==1,"actual Terra PNG plus editable table fragment passes native offline ZIP preflight");
  foreach(int code in new int[]{0,1,2,-7}){
   var native=new FakeNativeDocument(Native(false),Native(true)){NoOpInsert=true,InsertReturn=code};native.SetCaret(1,0);editor=new NativeRichComposer(delegate{return native.Proxy;});state=Begin(editor);path=Copy(state);string message="";
   try{editor.Invoke("hwp_editor_rich_apply",Arguments(state,path));}catch(InvalidOperationException ex){message=ex.Message;}
   Assert(message.Contains("InsertFile="+code)&&message.Contains("changed=0")&&message.Contains("expectedP=7")&&message.Contains("actualP=3")&&message.Contains("actualS=1"),"no-op insertion reports native code and sanitized actual/expected counts");
   Assert(!editor.RequiresReview&&native.DeleteCalls==0&&native.InsertCalls==1,"unchanged empty-target import failure has no applied content and needs no false partial-edit warning");
   Assert(message.Length<=512&&!message.Contains("outside prefix")&&!message.Contains("fake-document.hwp"),"visible native failure stays within512 and logs no document content or path");editor.EndRequest();
  }
  {
   var native=new FakeNativeDocument(selectedBefore,Native(true)){NoOpInsert=true,InsertReturn=0};native.SetSelection(1,0,2,15);editor=new NativeRichComposer(delegate{return native.Proxy;});state=Begin(editor);path=Copy(state);string message="";
   try{editor.Invoke("hwp_editor_rich_apply",Arguments(state,path));}catch(InvalidOperationException ex){message=ex.Message;}
   Assert(editor.RequiresReview&&native.DeleteCalls==1&&native.InsertCalls==1,"failed import after a verified real deletion requires review");
   Assert(message.Contains("InsertFile=0")&&message.Contains("delete=1")&&message.Contains("changed=1")&&message.Contains("actualP=3")&&message.Length<=512,"selected deletion then no-op import is distinguished from unmodified command failure");editor.EndRequest();
  }
  foreach(int code in new int[]{0,1,2}){
   var native=new FakeNativeDocument(Native(false),Native(true)){InsertReturn=code};native.SetCaret(1,0);editor=new NativeRichComposer(delegate{return native.Proxy;});state=Begin(editor);path=Copy(state);applied=(Dictionary<string,object>)editor.Invoke("hwp_editor_rich_apply",Arguments(state,path));
   Assert((bool)applied["applied"]&&(int)applied["nativeExecuteResult"]==code&&!editor.RequiresReview,"only complete readback establishes application, with exact numeric code retained");editor.EndRequest();
  }
  {
   string wrongAfter=Native(true).Replace("</SECTION>",P("unexpected")+"</SECTION>");var native=new FakeNativeDocument(Native(false),wrongAfter);native.SetCaret(1,0);editor=new NativeRichComposer(delegate{return native.Proxy;});state=Begin(editor);path=Copy(state);string message="";
   try{editor.Invoke("hwp_editor_rich_apply",Arguments(state,path));}catch(InvalidOperationException ex){message=ex.Message;}
   Assert(editor.RequiresReview&&message.Contains("InsertFile=1")&&message.Contains("expectedP=7")&&message.Contains("actualP=8")&&message.Contains("stage=result-verify"),"success-like return cannot hide changed but invalid native structure");editor.EndRequest();
  }
  {
   var native=new FakeNativeDocument(selectedBefore,Native(true));native.SetSelection(1,0,2,15);editor=new NativeRichComposer(delegate{return native.Proxy;});state=Begin(editor);path=Copy(state);string observedCopy=null;
   native.OnPreparedPath=delegate(string copy){observedCopy=copy;File.WriteAllBytes(copy,new byte[]{1,2,3});};
   Fails(delegate{editor.Invoke("hwp_editor_rich_apply",Arguments(state,path));},"corrupt importer copy rejected before native deletion");
   Assert(native.DeleteCalls==0&&native.InsertCalls==0&&!editor.RequiresReview,"pre-delete copy corruption cannot erase selected original");
   Assert(observedCopy!=null&&!File.Exists(observedCopy)&&File.Exists(path),"failed preflight removes only importer copy");editor.EndRequest();
  }
  {
   var native=new FakeNativeDocument(selectedBefore,Native(true));native.SetSelection(1,0,2,15);editor=new NativeRichComposer(delegate{return native.Proxy;});state=Begin(editor);path=Copy(state);string observedCopy=null;
   native.OnDelete=delegate(string copy){observedCopy=copy;File.WriteAllBytes(copy,new byte[]{1,2,3});};
   Fails(delegate{editor.Invoke("hwp_editor_rich_apply",Arguments(state,path));},"copy is rechecked between verified deletion and native insertion");
   Assert(native.DeleteCalls==1&&native.InsertCalls==0&&editor.RequiresReview,"post-delete corruption stops later insertion and demands review");Assert(!File.Exists(observedCopy),"post-delete failure cleans importer copy");editor.EndRequest();
  }
  foreach(string mutation in new string[]{"corrupt","delete"}){
   var native=new FakeNativeDocument(Native(false),Native(true));native.SetCaret(1,0);editor=new NativeRichComposer(delegate{return native.Proxy;});state=Begin(editor);path=Copy(state);string source=path,observedCopy=null;string initialHash=RichFragmentFile.Digest(File.ReadAllBytes(path));
   native.OnInsert=delegate(string copy){observedCopy=copy;if(mutation=="corrupt")File.WriteAllBytes(copy,new byte[]{1,2,3});else if(mutation=="delete")File.Delete(copy);else{string replacement=copy+".replacement";File.WriteAllBytes(replacement,File.ReadAllBytes(source));File.Delete(copy);File.Move(replacement,copy);}};
   string mutationFailure="";try{editor.Invoke("hwp_editor_rich_apply",Arguments(state,path));}catch(InvalidOperationException ex){mutationFailure=ex.Message;}Assert(mutationFailure.Length>0,"after native callback the copy hash and file identity are rechecked: "+mutation);
   Assert(editor.RequiresReview&&native.InsertCalls==1,"copy change after document mutation cannot return successful receipt: "+mutation+" review="+editor.RequiresReview+" calls="+native.InsertCalls+" failure="+mutationFailure);
   Assert(observedCopy!=null&&!File.Exists(observedCopy)&&RichFragmentFile.Digest(File.ReadAllBytes(source))==initialHash,"failed native callback cleans importer copy and preserves verified original");editor.EndRequest();
  }
  {
   var native=new FakeNativeDocument(Native(false),Native(true)){ThrowInsert=true};native.SetCaret(1,0);editor=new NativeRichComposer(delegate{return native.Proxy;});state=Begin(editor);path=Copy(state);string observedCopy=null;native.OnInsert=delegate(string copy){observedCopy=copy;};
   Fails(delegate{editor.Invoke("hwp_editor_rich_apply",Arguments(state,path));},"throwing native import still runs copy verification and cleanup");Assert(!editor.RequiresReview&&native.DeleteCalls==0&&native.InsertCalls==1&&!File.Exists(observedCopy),"unchanged document after native exception retains no false partial-edit warning or copy");editor.EndRequest();
  }
  {
   backend=new FakeBackend();editor=new NativeRichComposer(backend);state=Begin(editor);path=Copy(state);string initialHash=RichFragmentFile.Digest(File.ReadAllBytes(path));
   using(var original=RichFragmentFile.Open(DirectoryFor(state),path,initialHash)){
    RichImportFile first=RichFragmentFile.CreateImportCopy(original,DirectoryFor(state),initialHash),replacement=RichFragmentFile.CreateImportCopy(original,DirectoryFor(state),initialHash);string firstPath=first.Path;
    try{
     Assert(first.Volume!=replacement.Volume||first.IndexHigh!=replacement.IndexHigh||first.IndexLow!=replacement.IndexLow,"separate identical copies have distinct real Windows file identities");
     RichFragmentFile.VerifyImportCopy(first);RichFragmentFile.VerifyImportCopy(replacement);
     first.Path=replacement.Path;
     Fails(delegate{RichFragmentFile.VerifyImportCopy(first);},"a different file with identical trusted bytes is rejected by volume/file ID");
    }finally{first.Path=firstPath;RichFragmentFile.RemoveImportCopy(first);RichFragmentFile.RemoveImportCopy(replacement);}
    Assert(!File.Exists(firstPath)&&!File.Exists(replacement.Path)&&File.Exists(path),"identity-check copies are cleaned without deleting verified original");
   }editor.EndRequest();
  }
  Assert(RichXml.ContentWidth(ComposeXml.Parse(before))==42520,"host content width derives from page minus margins");
  Fails(delegate{RichXml.ContentWidth(ComposeXml.Parse(before.Replace("<COLDEF Count='1'/>","<COLDEF Count='2'/>")));},"multiple body columns rejected before source generation");
 }
 static void PackageFails(string entry,Func<byte[],byte[]> mutate,string message){
  string dir=Path.Combine(RichFragmentFile.OwnedRoot,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);string path=Path.Combine(dir,"fragment.hwpx");File.Copy(fixture,path);
  try{using(var zip=ZipFile.Open(path,ZipArchiveMode.Update)){var old=zip.GetEntry(entry);byte[] bytes=new byte[0];if(old!=null){using(var input=old.Open())using(var output=new MemoryStream()){input.CopyTo(output);bytes=output.ToArray();}old.Delete();}var fresh=zip.CreateEntry(entry);using(var output=fresh.Open()){bytes=mutate(bytes);output.Write(bytes,0,bytes.Length);}}
   Fails(delegate{using(var input=File.OpenRead(path))RichFragmentPackage.Validate(input,expected);},message);
  }finally{RichFragmentFile.Cleanup(dir);}
 }
 static string P(string text){return "<P ParaShape='0' Style='0'><TEXT><CHAR>"+System.Security.SecurityElement.Escape(text)+"</CHAR></TEXT></P>";}
 static string Meta(){return "<SECDEF><PAGEDEF Width='59528' Height='84188'><PAGEMARGIN Left='8504' Right='8504' Gutter='0'/></PAGEDEF></SECDEF><COLDEF Count='1'/>";}
 static string Table(){var b=new StringBuilder("<TABLE RowCount='3' ColCount='2'><SHAPEOBJECT><POSITION TreatAsChar='true'/></SHAPEOBJECT>");for(int r=0;r<3;r++){b.Append("<ROW>");for(int c=0;c<2;c++)b.Append("<CELL RowAddr='").Append(r).Append("' ColAddr='").Append(c).Append("' RowSpan='1' ColSpan='1'><PARALIST>").Append(P(expected.Tables[0].Cells[r][c])).Append("</PARALIST></CELL>");b.Append("</ROW>");}return b.Append("</TABLE>").ToString();}
 static string Native(bool inserted){string map="<BINDATALIST Count='"+(inserted?2:1)+"'><BINITEM Type='Embedding' Format='png' BinData='1'/>"+(inserted?"<BINITEM Type='Embedding' Format='png' BinData='2'/>":"")+"</BINDATALIST><CHARSHAPELIST Count='1'><CHARSHAPE Id='0' Height='1000'/></CHARSHAPELIST><PARASHAPELIST Count='1'><PARASHAPE Id='0'/></PARASHAPELIST>";
  string prefix="<P ParaShape='0' Style='0'><TEXT>"+Meta()+"<CHAR>outside prefix</CHAR></TEXT></P>",body=P("");
  if(inserted)body=P(expected.ParagraphTexts[0])+"<P><TEXT>"+Table()+"<CHAR/></TEXT></P><P><TEXT><PICTURE><SHAPEOBJECT><SIZE Width='24000' Height='12000'/><POSITION TreatAsChar='true'/></SHAPEOBJECT><IMAGE BinItem='2'/></PICTURE><CHAR/></TEXT></P>"+P(expected.ParagraphTexts[3])+P("");
  string tail="<TAIL><BINDATASTORAGE><BINDATA Id='1' Encoding='Base64' Compress='false'>b2xkLWJ5dGVz</BINDATA>"+(inserted?"<BINDATA Id='2' Encoding='Base64' Compress='false'>"+Convert.ToBase64String(png)+"</BINDATA>":"")+"</BINDATASTORAGE></TAIL>";
  return "<HWPML><HEAD><MAPPINGTABLE>"+map+"</MAPPINGTABLE></HEAD><BODY><SECTION Id='0'>"+prefix+body+P("outside suffix")+"</SECTION></BODY>"+tail+"</HWPML>";
 }
 static void Verify(string before,string after){RichXml.Verify(ComposeXml.Parse(before),ComposeXml.Parse(after),1,expected);}
 sealed class FakeBackend:IRichComposeBackend{
  public int Revision,ApplyCalls;public bool FailBefore,FailDuring,WrongCount;public Action<string> OnApply;
  public bool RequiresReview{get;private set;}
  public bool SelectionPresent{get{return false;}}
  public void Reset(){RequiresReview=false;}
  public RichComposeTarget Capture(){return new RichComposeTarget{Pin=Revision.ToString(),Paragraph=1,EndParagraph=1,ContentWidth=42520};}
  public bool Same(RichComposeTarget a,RichComposeTarget b){return(string)a.Pin==(string)b.Pin&&a.Paragraph==b.Paragraph&&a.ContentWidth==b.ContentWidth;}
  public RichComposeReceipt Apply(RichComposeTarget target,string path,RichExpected plan){ApplyCalls++;if(FailBefore)throw new InvalidOperationException("before first native write");RequiresReview=true;if(OnApply!=null)OnApply(path);if(FailDuring)throw new InvalidOperationException("after native import");RequiresReview=false;return new RichComposeReceipt{ParagraphCount=plan.ParagraphTexts.Length,TableCount=plan.Tables.Length-(WrongCount?1:0),ImageCount=plan.Images.Length};}
 }
 sealed class CallbackProxy:RealProxy{
  readonly Func<IMethodCallMessage,object[],object> invoke;
  public CallbackProxy(Type type,Func<IMethodCallMessage,object[],object> invoke):base(type){this.invoke=invoke;}
  public override IMessage Invoke(IMessage message){var call=(IMethodCallMessage)message;try{object[] arguments=call.Args;object value=invoke(call,arguments);return new ReturnMessage(value,arguments,arguments.Length,call.LogicalCallContext,call);}catch(Exception ex){return new ReturnMessage(ex,call);}}
 }
 sealed class FakeNativeDocument{
  public readonly IHwpObject Proxy;public string FullXml,AfterXml;public int DeleteCalls,InsertCalls,ForceMode=-1,SelectionList,InsertReturn=1;public bool ThrowInsert,NoOpDelete,NoOpInsert,ChangeOutsideAfterDelete;
  public readonly List<string> Trace=new List<string>();public Dictionary<string,object> InsertParameters,PreparedInsertParameters;public Action<string> OnPreparedPath,OnDelete,OnInsert;
  int caretParagraph,caretPosition,startParagraph,start,endParagraph,end;bool selected;
  public FakeNativeDocument(string before,string after){FullXml=before;AfterXml=after;Proxy=(IHwpObject)new CallbackProxy(typeof(IHwpObject),Call).GetTransparentProxy();}
  public void SetCaret(int para,int pos){selected=false;caretParagraph=para;caretPosition=pos;}
  public void SetSelection(int sp,int so,int ep,int eo){selected=true;startParagraph=sp;start=so;endParagraph=ep;end=eo;caretParagraph=ep;caretPosition=eo;}
  void Normalize(out int sp,out int so,out int ep,out int eo){sp=startParagraph;so=start;ep=endParagraph;eo=end;if(sp>ep||(sp==ep&&so>eo)){int swap=sp;sp=ep;ep=swap;swap=so;so=eo;eo=swap;}}
  ComposeXml Xml(){return ComposeXml.Parse(FullXml);}
  string Text(int para){return ComposeXml.ReadSimpleParagraph(Xml().Paragraphs[para]).Text;}
  int Origin(int para){return ComposeXml.ReadSimpleParagraph(Xml().Paragraphs[para]).Metadata.Length>0?16:0;}
  string SelectedText(){int sp,so,ep,eo;Normalize(out sp,out so,out ep,out eo);if(sp!=ep)throw new Exception("fake block inspector expects single paragraph");string text=Text(sp);int offset=Math.Max(0,so-Origin(sp)),last=Math.Max(0,Math.Min(text.Length,eo-Origin(sp)));return text.Substring(offset,last-offset);}
  object Call(IMethodCallMessage call,object[] a){switch(call.MethodName){
   case "GetID":return 7;case "GetPath":return "fake-document.hwp";case "IsReadOnlyMode":return false;case "GetCurSelectedCtrl":return null;
   case "GetSelectionMode":return ForceMode>=0?ForceMode:selected?1:0;
   case "GetPos":a[0]=0;a[1]=caretParagraph;a[2]=caretPosition;return true;
   case "GetSelectedPos":a[0]=SelectionList;a[1]=selected?startParagraph:caretParagraph;a[2]=selected?start:caretPosition;a[3]=SelectionList;a[4]=selected?endParagraph:caretParagraph;a[5]=selected?end:caretPosition;return selected;
   case "SetPos":if((int)a[0]!=0)throw new Exception("fake only supports main body");caretParagraph=(int)a[1];caretPosition=(int)a[2];selected=false;return true;
   case "SelectText":SetSelection((int)a[0],(int)a[1],(int)a[2],(int)a[3]);return true;
   case "GetSelectText":return selected?SelectedText():"";
   case "GetTextFile":if((string)a[1]!="saveblock")return FullXml;if((string)a[0]=="TEXT")return SelectedText();int sp,so,ep,eo;Normalize(out sp,out so,out ep,out eo);string meta=so==0?String.Join("",ComposeXml.ReadSimpleParagraph(Xml().Paragraphs[sp]).Metadata):"";return "<HWPML><BODY><SECTION><P><TEXT CharShape='0'>"+meta+"<CHAR>"+System.Security.SecurityElement.Escape(SelectedText())+"</CHAR></TEXT></P></SECTION></BODY></HWPML>";
   case "Run":if((string)a[0]=="MoveParaEnd"){caretPosition=Origin(caretParagraph)+Text(caretParagraph).Length;selected=false;return true;}if((string)a[0]=="Delete"){DeleteCalls++;Trace.Add("Delete");if(NoOpDelete)return true;int first,begin,last,finish;Normalize(out first,out begin,out last,out finish);var xml=Xml();XmlNode anchor=xml.Paragraphs[first];XmlNode blank=xml.Document.ImportNode(ComposeXml.Parse("<HWPML><HEAD><MAPPINGTABLE/></HEAD><BODY><SECTION>"+P("")+"</SECTION></BODY></HWPML>").Paragraphs[0],true);anchor.ParentNode.InsertBefore(blank,anchor);for(int i=first;i<=last;i++)xml.Paragraphs[i].ParentNode.RemoveChild(xml.Paragraphs[i]);FullXml=xml.Document.OuterXml;if(ChangeOutsideAfterDelete)FullXml=FullXml.Replace("outside prefix","changed outside");selected=false;caretParagraph=first;caretPosition=begin;if(OnDelete!=null)OnDelete((string)PreparedInsertParameters["FileName"]);return true;}throw new Exception("unexpected fake run "+a[0]);
   case "CreateAction":return Action((string)a[0]);default:throw new Exception("unexpected fake native call "+call.MethodName);
  }}
  IHwpAction Action(string name){var parameters=new Dictionary<string,object>();if(name=="InsertFile")PreparedInsertParameters=parameters;IHwpParameterSet set=null;set=(IHwpParameterSet)new CallbackProxy(typeof(IHwpParameterSet),delegate(IMethodCallMessage call,object[] unusedArguments){if(call.MethodName=="SetItem"){parameters[(string)call.Args[0]]=call.Args[1];if((string)call.Args[0]=="FileName"&&OnPreparedPath!=null)OnPreparedPath((string)call.Args[1]);return null;}if(call.MethodName=="GetItem")return parameters[(string)call.Args[0]];if(call.MethodName=="ItemExist")return parameters.ContainsKey((string)call.Args[0]);throw new Exception("unexpected fake parameter call "+call.MethodName);}).GetTransparentProxy();
   return(IHwpAction)new CallbackProxy(typeof(IHwpAction),delegate(IMethodCallMessage call,object[] unusedArguments){if(call.MethodName=="CreateSet")return set;if(call.MethodName=="GetDefault")return true;if(call.MethodName=="Execute"){if(name!="InsertFile")throw new Exception("unexpected fake action "+name);InsertCalls++;Trace.Add("InsertFile");InsertParameters=parameters;if(OnInsert!=null)OnInsert((string)parameters["FileName"]);if(ThrowInsert)throw new InvalidOperationException("fake native import failure");if(NoOpInsert)return InsertReturn;FullXml=AfterXml;selected=false;return InsertReturn;}throw new Exception("unexpected fake action call "+call.MethodName);}).GetTransparentProxy();
  }
 }
}














