using System;
using System.Collections;
using System.IO;
using System.Reflection;
using HAddon;
internal static class NativeEditorPolicyTests {
 static int checks;
 static void Main(){
  AppDomain.CurrentDomain.AssemblyResolve+=Resolve;
  try{Run();Console.WriteLine("PASS: "+checks+" native editor policy checks; no HWP document opened or modified.");Environment.ExitCode=0;}
  catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}
 }
 static Assembly Resolve(object sender,ResolveEventArgs args){
  string name=new AssemblyName(args.Name).Name;
  if(name!="HwpAddonHelper"&&name!="Hnc.Foundation.Interface"&&name!="Hnc.Foundation.InternalInterface")return null;
  string file=Path.Combine(@"C:\Program Files (x86)\Hnc\Office 2024\HOffice130\Bin",name+".dll");return File.Exists(file)?Assembly.LoadFrom(file):null;
 }
 static void Run(){
  string circle="\u25EF";
  Equal("가나다",Normalize("가나다",null,"가나다",3),"raw exact");
  Equal("&#9711;",Normalize("&#9711;",null,"&#9711;",7),"literal entity preserved");
  Equal(circle,Normalize("&#9711;",null,circle,1),"single decode with exact native span");
  Equal(circle,Normalize(null,"&#9711;\r\n",circle,1),"fallback decode and terminal newline");
  Equal("A",Normalize(null,"A\r\n","A",1),"terminal newline exact span");
  Equal(null,Normalize("ABC",null,"ABD",3),"wrong source rejected");
  Equal(null,Normalize("A\t",null,null,2),"tab rejected");
  Equal(null,Normalize("A\n",null,null,2),"newline rejected");
  Equal(null,Normalize("\u2028",null,null,1),"line separator rejected");
  Equal(null,Normalize("\uFFFC",null,null,1),"object character rejected");
  Equal(null,Normalize("\uD83D\uDE00",null,null,2),"unverified surrogate offsets rejected");
  object uniform=Block("<P><TEXT CharShape='2'><CHAR>가나다</CHAR></TEXT></P>");
  True(Eligible(uniform),"uniform paragraph accepted");Equal("가나다",Field(uniform,"Text"),"body text read");Equal(1,Field(uniform,"Paragraphs"),"paragraph counted");
  object mixed=Block("<P><TEXT CharShape='2'><CHAR>가</CHAR></TEXT><TEXT CharShape='3'><CHAR>나</CHAR></TEXT></P>");
  True(!Eligible(mixed),"mixed styles rejected");Equal(2,ShapeCount(mixed),"mixed shape diagnostic");
  True(Eligible(Block("<P><TEXT CharShape='2'><CHAR>가</CHAR></TEXT><TEXT CharShape='2'><CHAR>나</CHAR></TEXT></P>")),"uniform split runs accepted");
  object embedded=Block("<P><TEXT CharShape='2'><CHAR>가</CHAR><PICTURE><SHAPEOBJECT/></PICTURE></TEXT></P>");
  True((bool)Field(embedded,"HasObjects"),"object detected");True(!Eligible(embedded),"object rejected");
  object multiple=Block("<P><TEXT CharShape='2'><CHAR>가</CHAR></TEXT></P><P><TEXT CharShape='2'><CHAR>나</CHAR></TEXT></P>");
  Equal(2,Field(multiple,"Paragraphs"),"multiple paragraphs counted");True(!Eligible(multiple),"paragraph boundaries rejected");
  True(!Eligible(Block("<P><TEXT><CHAR>가</CHAR></TEXT></P>")),"missing shape rejected");
  True(!Eligible(Block("<P><TEXT CharShape='2'><CHAR>가<TAB/>나</CHAR></TEXT></P>")),"embedded text control rejected");
  True(Eligible(Parse("<h:HWPML xmlns:h='urn:test'><h:BODY><h:SECTION><h:P><h:TEXT CharShape='2'><h:CHAR>가</h:CHAR></h:TEXT></h:P></h:SECTION></h:BODY></h:HWPML>")),"namespace insensitive element names");
  True(Eligible(Parse("<HWPML><HEAD><CHARSHAPELIST><CHARSHAPE Id='2'/><CHARSHAPE Id='3'/></CHARSHAPELIST></HEAD><BODY><SECTION><P><TEXT CharShape='2'><CHAR>가</CHAR></TEXT></P></SECTION></BODY></HWPML>")),"header style definitions ignored");
  object dtd=Parse("<!DOCTYPE HWPML [<!ENTITY x '가'>]><HWPML><BODY><SECTION><P><TEXT CharShape='2'><CHAR>&x;</CHAR></TEXT></P></SECTION></BODY></HWPML>");
  True(!String.IsNullOrEmpty((string)Field(dtd,"Reason")),"DTD rejected without expansion");
  True(!Eligible(Parse("<document><BODY/></document>")),"unknown schema rejected");
  string bomFixture = "<HWPML><BODY><SECTION><P><TEXT CharShape='2'><CHAR>가나다</CHAR></TEXT></P></SECTION></BODY></HWPML>";
  object oneBom = Parse("\uFEFF" + bomFixture);
  True(Eligible(oneBom),"one leading decoded Unicode BOM accepted by Framework XML reader");
  True((bool)Field(oneBom,"LeadingBomRemoved"),"one leading BOM explicitly recorded");
  Equal(bomFixture.Length+1,Field(oneBom,"InputLength"),"XML diagnostics retain original envelope length");
  Equal("FEFF 003C 0048 0057 0050 004D 004C 003E",Field(oneBom,"InputPrefix"),"only first eight original UTF16 code units diagnosed");
  Equal("가나다",Field(oneBom,"Text"),"leading BOM removal leaves body unchanged");
  object declaredBom = Parse("\uFEFF<?xml version='1.0' encoding='UTF-16'?>" + bomFixture);
  True(Eligible(declaredBom),"single BOM with UTF16 XML declaration accepted");
  object bodyBom = Block("<P><TEXT CharShape='2'><CHAR>\uFEFF앞\uFEFF뒤</CHAR></TEXT></P>");
  True(Eligible(bodyBom),"BOM characters inside body remain valid text");
  Equal("\uFEFF앞\uFEFF뒤",Field(bodyBom,"Text"),"leading and internal body BOM preserved exactly");
  object duplicateBom = Parse("\uFEFF\uFEFF" + bomFixture);
  True(!Eligible(duplicateBom),"two leading BOMs are not silently normalized");
  True(((string)Field(duplicateBom,"Reason")).Contains("first8UTF16=FEFF FEFF"),"parse failure reports numeric envelope prefix");
  True(!Eligible(Parse(" \uFEFF" + bomFixture)),"BOM after whitespace not trimmed into validity");
  object htmlWrapped = Parse("&lt;HWPML/&gt;");
  True(!Eligible(htmlWrapped),"HTML wrapped XML is not implicitly decoded");
  True(((string)Field(htmlWrapped,"Reason")).Contains("first8UTF16=0026 006C 0074 003B"),"HTML wrapper distinguished by numeric signature");
  True(!Eligible(Parse("\"" + bomFixture + "\"")),"JSON quoted XML is not implicitly unwrapped");
  object nullXml = Parse(null);
  True(((string)Field(nullXml,"Reason")).Contains("type=null; length=-1"),"null XML has noncontent type and length diagnostics");
  True(!Eligible(Parse("\uFEFF<!DOCTYPE HWPML [<!ENTITY x '가'>]><HWPML><BODY><SECTION><P><TEXT CharShape='2'><CHAR>&x;</CHAR></TEXT></P></SECTION></BODY></HWPML>")),"single BOM does not weaken DTD prohibition");
  object metadata=Block("<P><TEXT CharShape='2'><SECDEF><PAGEDEF><PAGEMARGIN Left='100'/></PAGEDEF><MASTERPAGE><PARALIST><P><TEXT CharShape='3'><CHAR>바탕쪽</CHAR><PICTURE/></TEXT></P></PARALIST></MASTERPAGE></SECDEF><COLDEF/><CHAR>본문</CHAR></TEXT></P>");
  True(Eligible(metadata),"leading section and column metadata do not reject ordinary body");
  Equal(2,Field(metadata,"LeadingMetadata"),"metadata counted separately");
  Equal("본문",Field(metadata,"Text"),"master-page text is not selected body text");
  Equal(1,Field(metadata,"Paragraphs"),"master-page paragraph not counted as body paragraph");
  Equal(1,ShapeCount(metadata),"master-page shape not mixed with selected body shape");
  True(!Eligible(Block("<P><TEXT CharShape='2'><CHAR>가</CHAR><SECDEF/><CHAR>나</CHAR></TEXT></P>")),"section control inside text is rejected");
  True(!Eligible(Block("<P><TEXT CharShape='2'><CHAR>가</CHAR><COLDEF/><CHAR>나</CHAR></TEXT></P>")),"column control inside text is rejected");
  True(!Eligible(Block("<P><SECDEF/><TEXT CharShape='2'><CHAR>가</CHAR></TEXT></P>")),"metadata in unexpected body position is rejected");
  True(!Eligible(Block("<P><TEXT CharShape='2'><CHAR>가<SECDEF/></CHAR></TEXT></P>")),"metadata embedded in CHAR is rejected");
  True(!Eligible(Block("<P><TEXT CharShape='2'>unwrapped<CHAR>가</CHAR></TEXT></P>")),"unwrapped body text is rejected");
  True(!Eligible(Block("<P><TEXT CharShape='2'><SECDEF/><CHAR>가</CHAR><TABLE/></TEXT></P>")),"real selected body object is still rejected with metadata present");
  Equal("16:0:3",Map("가나다",19,16,19,"가나다"),"native first-paragraph origin separated from plain text");
  Equal("16:1:2",Map("가나다",19,17,18,"나"),"partial first-paragraph target maps exactly");
  Equal("7:2:4",Map("ABCDEF",13,9,11,"CD"),"origin inferred without a hardcoded sixteen");
  Equal("0:2:4",Map("ABCDEF",6,2,4,"CD"),"paragraph with no leading controls");
  Equal(null,Map("가나다",19,0,19,"가나다"),"selected hidden controls must not become rewrite input");
  Equal(null,Map("가나다",19,8,19,"가나다"),"partial hidden control cannot be silently removed");
  Equal(null,Map("가나다",19,17,19,"가나"),"native-to-plain mapping rejects wrong matching text");
  Equal(null,Map("가나다",2,0,2,"가나"),"native paragraph shorter than plain text rejected");
  Equal(null,Map("가\t나",3,0,1,"가"),"uncertain paragraph control offsets rejected");
  Equal("17:19",Range(0,0,19,0,0,17),"reverse drag normalized");
  Equal("17:19",Range(0,0,17,0,0,19),"forward drag unchanged");
  Equal(null,Range(0,0,17,0,1,19),"cross-paragraph reverse selection rejected");
  Equal(null,Range(0,0,17,1,0,19),"cross-list selection rejected");
  Equal(null,Range(0,0,-1,0,0,19),"negative native offsets rejected");
  Equal(null,Range(0,0,17,0,0,17),"empty native selection rejected");
  True(NativeEditor.IsWholeParagraphSelection(16,19,16,19),"alignment allowed only for all visible paragraph text");
  True(!NativeEditor.IsWholeParagraphSelection(16,19,17,19),"partial prefix excluded alignment rejected");
  True(!NativeEditor.IsWholeParagraphSelection(16,19,16,18),"partial suffix excluded alignment rejected");
  True(!NativeEditor.IsWholeParagraphSelection(16,19,0,19),"selection containing hidden metadata not treated as whole visible paragraph");
  True(!NativeEditor.MatchesParagraphRewrite("ABCD",0,4,"AB","ABCD"),"failed InsertText shortened-prefix must not pass");
  True(NativeEditor.MatchesParagraphRewrite("ABCD",0,4,"AB","AB"),"whole paragraph shortened rewrite");
  True(NativeEditor.MatchesParagraphRewrite("prefix ABCD suffix",7,11,"AB","prefix AB suffix"),"prefix and suffix preserved");
  True(!NativeEditor.MatchesParagraphRewrite("prefix ABCD suffix",7,11,"AB","prefix AB CD suffix"),"leftover source rejected");
  True(NativeEditor.MatchesParagraphRewrite("가나다",1,2,"새본문","가새본문다"),"mapped partial rewrite preserves unselected prefix and suffix");
  True(!NativeEditor.MatchesParagraphRewrite("가나다",1,2,"새본문","새본문"),"partial rewrite cannot drop unselected text");
 }
 static string Normalize(string raw,string fallback,string expected,int span){return NativeEditor.NormalizeSelectedText(raw,fallback,expected,span);}
 static string Map(string paragraph,int nativeEnd,int start,int end,string selected){int origin,plainStart,plainEnd;return NativeEditor.TryMapParagraphSelection(paragraph,nativeEnd,start,end,selected,out origin,out plainStart,out plainEnd)?origin+":"+plainStart+":"+plainEnd:null;}
 static string Range(int sl,int sp,int so,int el,int ep,int eo){int start,end;return NativeEditor.TryNormalizeSelection(sl,sp,so,el,ep,eo,out start,out end)?start+":"+end:null;}
 static object Block(string inner){return Parse("<HWPML><BODY><SECTION>"+inner+"</SECTION></BODY></HWPML>");}
 static object Parse(string xml){return typeof(NativeEditor).GetMethod("ParseBlock",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{xml});}
 static object Field(object value,string name){return value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).GetValue(value);}
 static int ShapeCount(object value){int count=0;foreach(object entry in (IEnumerable)Field(value,"Shapes"))count++;return count;}
 static bool Eligible(object value){return String.IsNullOrEmpty((string)Field(value,"Reason"))&&(int)Field(value,"Paragraphs")==1&&!(bool)Field(value,"HasObjects")&&ShapeCount(value)==1;}
 static void True(bool condition,string label){checks++;if(!condition)throw new Exception("FAIL: "+label);}
 static void Equal(object expected,object actual,string label){checks++;if(!Object.Equals(expected,actual))throw new Exception("FAIL: "+label+" expected="+(expected??"<null>")+" actual="+(actual??"<null>"));}
}
