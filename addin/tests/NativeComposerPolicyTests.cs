using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HAddon;

internal static class NativeComposerPolicyTests {
 static int checks;
 static void Main(){AppDomain.CurrentDomain.AssemblyResolve+=Resolve;try{Run();Console.WriteLine("PASS: "+checks+" composer assertions; fake backend and XML fixtures only, no HWP process or document used.");}catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}}
 static Assembly Resolve(object sender,ResolveEventArgs args){string name=new AssemblyName(args.Name).Name;if(!new string[]{"HwpAddonHelper","Hnc.Foundation.Interface","Hnc.Foundation.InternalInterface"}.Contains(name))return null;return Assembly.LoadFrom(Path.Combine(@"C:\Program Files (x86)\Hnc\Office 2024\HOffice130\Bin",name+".dll"));}
 static void Assert(bool value,string message){checks++;if(!value)throw new Exception(message);}
 static void Fails(Action action,string message){bool failed=false;try{action();}catch(InvalidOperationException){failed=true;}Assert(failed,message);}
 static Dictionary<string,object> Row(string role,string text){return new Dictionary<string,object>{{"role",role},{"text",text}};}
 static object[] Plan(params string[] texts){return texts.Select(t=>(object)Row("body",t)).ToArray();}
 static Dictionary<string,object> Begin(NativeComposer editor){return (Dictionary<string,object>)editor.BeginRequest();}
 static Dictionary<string,object> Apply(NativeComposer editor,string id,object plan){return (Dictionary<string,object>)editor.Invoke("hwp_editor_compose_apply",new Dictionary<string,object>{{"expectedTargetId",id},{"paragraphs",plan}});}
 static string Id(Dictionary<string,object> state){return (string)state["targetId"];}
 static Dictionary<string,object> Caps(Dictionary<string,object> state){return (Dictionary<string,object>)state["capabilities"];}
 static void Run(){
  var backend=new FakeBackend();var editor=new NativeComposer(backend);var state=Begin(editor);
  Assert((bool)Caps(state)["compose"]&&Id(state).Length>0,"empty target enables compose");
  Assert((string)((Dictionary<string,object>)state["context"])["scope"]=="emptyBodyParagraph","bridge scope exact");
  Assert(!(bool)Caps(state)["tables"]&&!(bool)Caps(state)["images"],"unsupported objects explicitly disabled");
  var receipt=Apply(editor,Id(state),new object[]{Row("title","한글 제목"),Row("body","문서 본문"),Row("bullet","항목")});
  Assert((bool)receipt["applied"]&&(bool)receipt["changed"]&&(int)receipt["paragraphCount"]==3,"successful receipt is verified plan count");
  Assert(backend.ApplyCalls==1&&backend.PrepareCalls==1&&backend.Reads>=3,"one native apply after repeated pin verification");
  Assert(backend.Rendered.SequenceEqual(new string[]{"한글 제목","문서 본문","• 항목"}),"bullet glyph is native text, body excludes Markdown markers");
  Assert(backend.Outside=="original outside"&&!editor.RequiresReview,"verified native result retains fake outside state");
  Fails(delegate{Apply(editor,Id(state),Plan("again"));},"applied target cannot be reused");

  foreach(Action<FakeBackend> stale in new Action<FakeBackend>[] {b=>b.Revision++,b=>b.DocumentId++,b=>b.Path="other",b=>b.Position++,b=>b.Paragraph++,b=>b.Font="other"}){
   backend=new FakeBackend();editor=new NativeComposer(backend);state=Begin(editor);stale(backend);
   Fails(delegate{Apply(editor,Id(state),Plan("text"));},"stale document/caret/style rejected");Assert(backend.ApplyCalls==0,"stale state causes no native apply");
  }
  backend=new FakeBackend{ChangeDuringPrepare=true};editor=new NativeComposer(backend);state=Begin(editor);
  Fails(delegate{Apply(editor,Id(state),Plan("text"));},"change during style preparation rejected");Assert(backend.ApplyCalls==0,"style preparation race causes no write");
  backend=new FakeBackend{CaptureError="not empty"};editor=new NativeComposer(backend);state=Begin(editor);
  Assert(!(bool)Caps(state)["compose"]&&Id(state)=="","nonempty and unsupported targets disable compose");
  Fails(delegate{Apply(editor,"",Plan("text"));},"unsupported target cannot apply");

  var invalid=new List<object>{null,"string",new object[0],new object[]{Row("body","")},new object[]{Row("unknown","text")},
   new object[]{new Dictionary<string,object>{{"role","body"},{"text","text"},{"theme","arbitrary"}}},
   new object[]{Row("body",new string('a',12001))},Plan(new string('a',12000),new string('b',12000),"c"),
   Enumerable.Range(0,129).Select(x=>(object)Row("body","a")).ToArray()};
  foreach(char forbidden in new char[]{'\0','\t','\r','\n','\u001F','\u007F','\u0085','\u009F','\uD800','\uDC00','\u2028','\u2029','\uFFFC','\uFFFE','\uFFFF'})invalid.Add(Plan("valid","late"+forbidden+"bad"));
  foreach(object plan in invalid){backend=new FakeBackend();editor=new NativeComposer(backend);state=Begin(editor);Fails(delegate{Apply(editor,Id(state),plan);},"invalid whole plan rejected");Assert(backend.PrepareCalls==0&&backend.ApplyCalls==0,"all paragraphs validate before native preparation/write");}
  Assert(NativeComposer.ValidateParagraphs(Enumerable.Range(0,128).Select(i=>(object)Row("body","a")).ToArray()).Length==128,"128 paragraphs accepted");
  Assert(NativeComposer.ValidateParagraphs(Plan(new string('a',12000),new string('b',12000))).Sum(x=>x.Text.Length)==24000,"exact total limit accepted");
  backend=new FakeBackend();editor=new NativeComposer(backend);state=Begin(editor);
  Fails(delegate{Apply(editor,"wrong",Plan("text"));},"wrong expected target rejected");Assert(backend.ApplyCalls==0,"target mismatch causes no mutation");
  editor.EndRequest();Fails(delegate{editor.Invoke("hwp_editor_compose_state",new Dictionary<string,object>());},"request lifecycle enforced");

  backend=new FakeBackend{FailBefore=true};editor=new NativeComposer(backend);state=Begin(editor);Fails(delegate{Apply(editor,Id(state),Plan("text"));},"pre-mutation native failure surfaced");Assert(!editor.RequiresReview,"known zero-mutation failure does not report partial apply");
  backend=new FakeBackend{FailDuring=true};editor=new NativeComposer(backend);state=Begin(editor);Fails(delegate{Apply(editor,Id(state),Plan("first","second"));},"partial native failure surfaced");Assert(editor.RequiresReview&&backend.Rendered.Length==1,"partial apply demands review and leaves evidence, no fake undo");
  backend=new FakeBackend{WrongCount=true};editor=new NativeComposer(backend);state=Begin(editor);Fails(delegate{Apply(editor,Id(state),Plan("first","second"));},"unverified receipt count rejected");Assert(editor.RequiresReview,"returned but wrong count requires document review");
  var title=NativeComposer.StyleFor("title");var heading=NativeComposer.StyleFor("heading");var body=NativeComposer.StyleFor("body");var bullet=NativeComposer.StyleFor("bullet");
  Assert(title.Char["Height"]==1800&&title.Char["Bold"]==1&&title.Char["TextColor"]==0x00A65824&&title.Para["AlignType"]==3,"native title typography exact");
  Assert(heading.Char["Height"]==1200&&heading.Char["Bold"]==1&&heading.Para["AlignType"]==1,"native heading typography exact");
  Assert(body.Char["Height"]==1000&&body.Para["LineSpacingType"]==0&&body.Para["LineSpacing"]==160,"body 10pt and 160 percent native spacing");
  Assert(bullet.Para["LeftMargin"]==2400&&bullet.Para["Indentation"]==-1200&&bullet.Para["HeadingType"]==0,"bullet hanging indent does not invoke automatic list engine");
  Assert(title.Char.Keys.OrderBy(x=>x).SequenceEqual(new string[]{"Bold","Height","TextColor"}),"font family and other baseline properties are preserved by clone rather than replaced by model");

  string old=Xml(P("outside before"),Empty(),P("outside after"));
  string next=Xml(P("outside before"),P("title","1","1"),P("body","2","2"),P("outside after"));
  Verify(old,next.Replace("<CHARSHAPELIST Count='3'>","<CHARSHAPELIST Count='4'><CHARSHAPE Id='3' Height='1200'/>"),1,new string[]{"title","body"});Assert(true,"append definitions and replace only target accepted");
  Verify("\uFEFF"+old,next,1,new string[]{"title","body"});Assert(true,"one leading XML BOM accepted");
  Fails(delegate{ComposeXml.Parse("\uFEFF\uFEFF"+old);},"two leading BOMs rejected");
  var embedded=ComposeXml.ReadSimpleParagraph(ComposeXml.Parse(Xml(P("inside\uFEFFvalue"))).Paragraphs[0]);Assert(embedded.Text=="inside\uFEFFvalue","body BOM not stripped");
  Fails(delegate{ComposeXml.Parse("&lt;HWPML&gt;");},"escaped XML wrappers not decoded");
  Fails(delegate{ComposeXml.Parse("<!DOCTYPE HWPML [<!ENTITY x 'bad'>]>"+old);},"DTD rejected");
  Fails(delegate{ComposeXml.Parse(old.Replace("</BODY>","<SECTION><P/></SECTION></BODY>"));},"multiple sections unsupported");
  Fails(delegate{Verify(old.Replace("</HWPML>","<TAIL><BINDATA Id='1'>AAAA</BINDATA></TAIL></HWPML>"),next.Replace("</HWPML>","<TAIL><BINDATA Id='1'>BBBB</BINDATA></TAIL></HWPML>"),1,new string[]{"title","body"});},"outside binary payload mutation caught");
  Fails(delegate{Verify(old,next.Replace("CHARSHAPELIST Count='3'","CHARSHAPELIST Count='2'"),1,new string[]{"title","body"});},"original definition count cannot shrink");
  Assert(title.Para["NextSpacing"]==2000&&heading.Para["PrevSpacing"]==1800,"paragraph point spacing uses URC twice HWPUNIT");
  Fails(delegate{Verify(old,next.Replace("outside before","changed outside"),1,new string[]{"title","body"});},"outside prefix text mutation caught");
  Fails(delegate{Verify(old,next.Replace("outside after","changed outside"),1,new string[]{"title","body"});},"outside suffix text mutation caught");
  Fails(delegate{Verify(old,next.Replace("<CHAR>outside after</CHAR>","<CHAR>outside after </CHAR>"),1,new string[]{"title","body"});},"outside significant whitespace preserved");
  Fails(delegate{Verify(old,next.Replace("<CHARSHAPE Id='0' Height='1000'/>","<CHARSHAPE Id='0' Height='9900'/>"),1,new string[]{"title","body"});},"existing style record changes caught");
  Fails(delegate{Verify(old,next.Replace("<FONT Id='0' Name='Original'/>","<FONT Id='0' Name='Changed'/>"),1,new string[]{"title","body"});},"existing font record changes caught");
  Fails(delegate{Verify(old,next.Replace("<CHARSHAPE Id='0' Height='1000'/>",""),1,new string[]{"title","body"});},"existing style record disappearance caught");
  Fails(delegate{Verify(old,next.Replace("<CHARSHAPE Id='0' Height='1000'/>","<CHARSHAPE Id='0' Height='1000'/><CHARSHAPE Id='0' Height='1000'/>"),1,new string[]{"title","body"});},"duplicate original style IDs rejected");
  Fails(delegate{Verify(old,Xml(P("outside before"),P("title"),P("outside after")),1,new string[]{"title","body"});},"generated paragraph count mismatch caught");
  Fails(delegate{Verify(old,next,1,new string[]{"wrong title","body"});},"native text mismatch caught");
  Fails(delegate{Verify(Xml(P("outside before"),P("existing"),P("outside after")),next,1,new string[]{"title","body"});},"nonempty original never replaced");
  string meta="<SECDEF><MASTERPAGE><P><TEXT><CHAR>header text</CHAR><PICTURE/></TEXT></P></MASTERPAGE></SECDEF><COLDEF Count='1'/>";
  string beforeMeta=Xml("<P><TEXT CharShape='0'>"+meta+"<CHAR/></TEXT></P>");
  string afterMeta=Xml("<P ParaShape='1'><TEXT CharShape='1'>"+meta+"<CHAR>new body</CHAR></TEXT></P>",P("second"));
  Verify(beforeMeta,afterMeta,0,new string[]{"new body","second"});Assert(true,"first paragraph metadata including masterpage subtree preserved");
  Fails(delegate{Verify(beforeMeta,afterMeta.Replace("header text","changed header"),0,new string[]{"new body","second"});},"metadata subtree changes caught");
  Fails(delegate{Verify(beforeMeta,afterMeta.Replace("<COLDEF Count='1'/>",""),0,new string[]{"new body","second"});},"metadata disappearance caught");
  Fails(delegate{Verify(beforeMeta,afterMeta.Replace(P("second"),"<P><TEXT>"+meta+"<CHAR>second</CHAR></TEXT></P>"),0,new string[]{"new body","second"});},"metadata must not duplicate into added paragraphs");
  Fails(delegate{ComposeXml.ReadSimpleParagraph(ComposeXml.Parse(Xml("<P><TEXT><CTRL/><CHAR/></TEXT></P>")).Paragraphs[0]);},"hidden native control in target refused");
  Fails(delegate{ComposeXml.ReadSimpleParagraph(ComposeXml.Parse(Xml("<P><TEXT><CHAR>text</CHAR><COLDEF/></TEXT></P>")).Paragraphs[0]);},"metadata after body text refused");
  Fails(delegate{ComposeXml.ReadSimpleParagraph(ComposeXml.Parse(Xml("<P><TEXT><CHAR><FIELD/></CHAR></TEXT></P>")).Paragraphs[0]);},"inline object subtree refused");
  Verify(old,next.Replace("ParaShape='1' Style='0'","Style='0' ParaShape='1'"),1,new string[]{"title","body"});Assert(true,"attribute order does not create false mutation");
  try{ComposeXml.Parse("not XML secret-content");throw new Exception("Expected XML failure");}catch(InvalidOperationException ex){Assert(ex.Message.Contains("first8=006E 006F 0074 0020")&&ex.Message.Contains("length=22"),"parser failure exposes code units and size, not raw document prefix");}
 }
 static string P(string text,string para="0",string character="0"){return "<P ParaShape='"+para+"' Style='0'><TEXT CharShape='"+character+"'><CHAR>"+System.Security.SecurityElement.Escape(text)+"</CHAR></TEXT></P>";}
 static string Empty(){return P("");}
 static string Xml(params string[] paragraphs){return "<HWPML><HEAD><MAPPINGTABLE><FACENAMELIST><FONTFACE Lang='Hangul' Count='1'><FONT Id='0' Name='Original'/></FONTFACE></FACENAMELIST><CHARSHAPELIST Count='3'><CHARSHAPE Id='0' Height='1000'/><CHARSHAPE Id='1' Height='1800'/><CHARSHAPE Id='2' Height='1000'/></CHARSHAPELIST><PARASHAPELIST Count='3'><PARASHAPE Id='0'/><PARASHAPE Id='1'/><PARASHAPE Id='2'/></PARASHAPELIST></MAPPINGTABLE></HEAD><BODY><SECTION Id='0'>"+String.Join("",paragraphs)+"</SECTION></BODY></HWPML>";}
 static void Verify(string before,string after,int target,string[] texts){ComposeXml.VerifyInsertion(ComposeXml.Parse(before),ComposeXml.Parse(after),target,texts);}
 sealed class FakeBackend:IComposeBackend{
  public int Reads,ApplyCalls,PrepareCalls,Revision,DocumentId=7,Paragraph=1,Position=16;
  public string Path="document.hwp",Font="original-font",Outside="original outside",CaptureError;
  public bool ChangeDuringPrepare,FailBefore,FailDuring,WrongCount;
  public string[] Rendered=new string[0];
  public bool RequiresReview{get;private set;}
  public void Reset(){RequiresReview=false;}
  public ComposeTarget Capture(){Reads++;if(CaptureError!=null)throw new InvalidOperationException(CaptureError);return new ComposeTarget{DocumentId=DocumentId,Path=Path,List=0,Paragraph=Paragraph,Position=Position,Hash=Revision.ToString(),Native=Font};}
  public bool Same(ComposeTarget a,ComposeTarget b){return a.DocumentId==b.DocumentId&&a.Path==b.Path&&a.List==b.List&&a.Paragraph==b.Paragraph&&a.Position==b.Position&&a.Hash==b.Hash&&(string)a.Native==(string)b.Native;}
  public void Prepare(ComposeTarget target,ComposeParagraph[] plan){PrepareCalls++;if(ChangeDuringPrepare)Revision++;}
  public int Apply(ComposeTarget target,ComposeParagraph[] plan){ApplyCalls++;if(FailBefore)throw new InvalidOperationException("before first native call");RequiresReview=true;if(FailDuring){Rendered=new string[]{plan[0].Rendered};throw new InvalidOperationException("after first native call");}Rendered=plan.Select(p=>p.Rendered).ToArray();RequiresReview=false;return plan.Length-(WrongCount?1:0);}
 }
}


