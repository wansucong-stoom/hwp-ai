using System;
using HAddon;
class HwpParagraphProofsTests {
 const string Path="BODY/SECTION[0]/P[0]/TEXT[0]/TABLE[0]/ROW[0]/CELL[0]/PARALIST[0]/P[0]";
 static string Header(string ch,string para,string style,string border){return "<HEAD><MAPPINGTABLE><BORDERFILLLIST><BORDERFILL Id='"+border+"' FillColor='white'/></BORDERFILLLIST><CHARSHAPELIST><CHARSHAPE Id='"+ch+"' Height='1000' BorderFillId='"+border+"'/></CHARSHAPELIST><PARASHAPELIST><PARASHAPE Id='"+para+"' Align='Left'><PARABORDER BorderFill='"+border+"'/></PARASHAPE></PARASHAPELIST><STYLELIST><STYLE Id='"+style+"' Name='body' CharShape='"+ch+"' ParaShape='"+para+"' NextStyle='"+style+"'/></STYLELIST></MAPPINGTABLE></HEAD>";}
 static string P(string text,string ch,string para,string style){return "<P InstId='50' ParaShape='"+para+"' Style='"+style+"'><TEXT CharShape='"+ch+"'><CHAR>"+text+"</CHAR></TEXT></P>";}
 static string Full(string text){return "<HWPML>"+Header("0","0","0","1")+"<BODY><SECTION><P><TEXT><TABLE RowCount='1' ColCount='1'><ROW><CELL RowAddr='0' ColAddr='0' RowSpan='1' ColSpan='1' BorderFill='1'><PARALIST>"+P(text,"0","0","0")+"</PARALIST></CELL></ROW></TABLE></TEXT></P></SECTION></BODY></HWPML>";}
 static string Block(string text){return "<HWPML>"+Header("9","8","7","6")+"<BODY><SECTION>"+P(text,"9","8","7")+"</SECTION></BODY></HWPML>";}
 static void Check(bool b,string name){if(!b)throw new Exception(name);}
 static void Reject(Action action){try{action();}catch(InvalidOperationException){return;}catch(System.Xml.XmlException){return;}throw new Exception("expected rejection");}
 static void Main(){try{Run();}catch(Exception ex){Console.Error.WriteLine(ex.GetType().FullName+": "+ex.Message);Environment.ExitCode=1;}}
 static void Run(){
  var proofs=new HwpParagraphProofs(Full("summary"));var expected=proofs.CreateExpected(Path);var found=proofs.MatchSelected(Block("summary"));
  Check(found.Unique&&found.MatchCount==1&&found.XmlPath==Path&&found.Text=="summary","unique full-to-saveblock mapping");
  Check(found.StyleToken==expected.StyleToken&&found.StructureToken==expected.StructureToken&&found.ContainerToken==expected.ContainerToken,"proof tokens");
  Check(!proofs.MatchSelected(Block("other")).Unique,"text mismatch");
  Check(!proofs.MatchSelected(Block("summary").Replace("Height='1000'","Height='1200'")).Unique,"referenced style change");
  Check(!proofs.MatchSelected(Block("summary").Replace("FillColor='white'","FillColor='red'")).Unique,"transitive style change");
  Func<string,string> fonts=s=>s.Replace("<MAPPINGTABLE>","<MAPPINGTABLE><FACENAMELIST><FONTFACE Lang='Hangul'><FONT Id='0' Name='FontA'/></FONTFACE></FACENAMELIST>").Replace("Height='1000' BorderFillId='1'/>","Height='1000' BorderFillId='1'><FONTID Hangul='0'/></CHARSHAPE>").Replace("Height='1000' BorderFillId='6'/>","Height='1000' BorderFillId='6'><FONTID Hangul='0'/></CHARSHAPE>");
  var fontProofs=new HwpParagraphProofs(fonts(Full("summary")));string fontBlock=fonts(Block("summary")).Replace("FONT Id='0'","FONT Id='5'").Replace("FONTID Hangul='0'","FONTID Hangul='5'");
  Check(fontProofs.MatchSelected(fontBlock).Unique,"font index remap");Check(!fontProofs.MatchSelected(fontBlock.Replace("Name='FontA'","Name='FontB'")).Unique,"font definition changes style");
  Check(!proofs.MatchSelected(Block("summary").Replace("Align='Left'","Align='Right'")).Unique,"paragraph shape change");
  Check(proofs.MatchSelected(Block("summary").Replace("InstId='50'","InstId='1234'")).Unique,"serialization instance id excluded");
  Check(proofs.MatchSelected(Block("summary").Replace("<P InstId=", "<P PageBreak='false' ColumnBreak='false' InstId=")).Unique,"saveblock explicit no-break defaults");
  Check(!proofs.MatchSelected(Block("summary").Replace("<P InstId=", "<P PageBreak='true' InstId=")).Unique,"nondefault page break remains protected");
  Check(!proofs.MatchSelected(Block("summary").Replace("<P InstId=", "<P ColumnBreak='true' InstId=")).Unique,"nondefault column break remains protected");
  var split=Block("summary").Replace("<CHAR>summary</CHAR>","<CHAR>sum</CHAR><CHAR>mary</CHAR>");Check(proofs.MatchSelected(split).Unique,"adjacent char segmentation");
  split=Block("summary").Replace("<TEXT CharShape='9'><CHAR>summary</CHAR></TEXT>","<TEXT CharShape='9'><CHAR>sum</CHAR></TEXT><TEXT CharShape='9'><CHAR>mary</CHAR></TEXT>");Check(proofs.MatchSelected(split).Unique,"adjacent equivalent runs");
  string alias=split.Replace("</CHARSHAPELIST>","<CHARSHAPE Id='10' Height='1000' BorderFillId='6'/></CHARSHAPELIST>").Replace("<TEXT CharShape='9'><CHAR>mary", "<TEXT CharShape='10'><CHAR>mary");
  Check(proofs.MatchSelected(alias).Unique&&HwpParagraphProofs.HasUniformSelectedStyle(alias),"semantic equivalent mixed numeric shape IDs");
  Check(!HwpParagraphProofs.HasUniformSelectedStyle(alias.Replace("Id='10' Height='1000'","Id='10' Height='1100'")),"genuine mixed style still rejected");
  const string metadata="<SECDEF><MASTERPAGE><PARALIST><P><TEXT><CHAR>master page text</CHAR></TEXT></P></PARALIST></MASTERPAGE></SECDEF><COLDEF Count='1'/>";
  string withMetadata=Block("summary").Replace("<CHAR>summary</CHAR>",metadata+"<CHAR>summary</CHAR>");
  Check(proofs.MatchSelected(withMetadata).Unique,"saveblock leading section metadata is not selected body");
  string separateMetadata=Block("summary").Replace("<TEXT CharShape='9'>","<TEXT CharShape='999'>"+metadata+"</TEXT><TEXT CharShape='9'>");
  Check(proofs.MatchSelected(separateMetadata).Unique,"metadata-only run does not override content style");
  var sourceMetadata=new HwpParagraphProofs(Full("summary").Replace("<CHAR>summary</CHAR>",metadata+"<CHAR>summary</CHAR>"));
  Check(sourceMetadata.MatchSelected(Block("summary")).Unique,"native content range may exclude source controls");
  Reject(()=>proofs.MatchSelected(Block("summary").Replace("<CHAR>summary</CHAR>","<CHAR>summary</CHAR>"+metadata)));
  string declared=Block("summary").Replace("<TEXT CharShape='9'>","<TEXT xmlns:x='urn:unused' CharShape='9'>");Check(proofs.MatchSelected(declared).Unique,"namespace declaration is metadata");
  var namespaced=new HwpParagraphProofs(Full("summary").Replace("<HWPML>","<HWPML xmlns='urn:hwpml-test'>"));
  string namespaceBlock=Block("summary").Replace("<HWPML>","<HWPML xmlns='urn:hwpml-test' xmlns:x='urn:hwpml-test'>").Replace("<TEXT CharShape='9'>","<x:TEXT CharShape='9'>").Replace("</TEXT>","</x:TEXT>");
  Check(namespaced.MatchSelected(namespaceBlock).Unique,"namespace prefixes do not change identity");
  Check(!proofs.MatchSelected(namespaceBlock).Unique,"different namespace changes paragraph identity");
  string duplicate=Full("summary").Replace("</PARALIST>",P("summary","0","0","0")+"</PARALIST>");var dup=new HwpParagraphProofs(duplicate).MatchSelected(Block("summary"));Check(!dup.Unique&&dup.MatchCount==2,"duplicate does not prove container");
  Check(proofs.MatchesExpected(Path,Block("summary")),"explicit expected style/text match");
  Reject(()=>proofs.MatchSelected(Block("summary").Replace("<CHAR>summary</CHAR>","<CHAR>summary</CHAR><FIELD/>")));
  Reject(()=>proofs.MatchSelected(Block("summary").Replace("<TEXT CharShape='9'>","outside<TEXT CharShape='9'>")));
  Reject(()=>proofs.MatchSelected(Block("summary").Replace("<TEXT CharShape='9'>","<x:TEXT xmlns:x='urn:unexpected' CharShape='9'>").Replace("</TEXT>","</x:TEXT>")));
  Reject(()=>new HwpParagraphProofs("<!DOCTYPE HWPML [<!ENTITY x 'x'>]><HWPML><BODY>&x;</BODY></HWPML>"));
  string deep="";for(int i=0;i<140;i++)deep+="<X>";for(int i=0;i<140;i++)deep+="</X>";Reject(()=>new HwpParagraphProofs("<HWPML><BODY>"+deep+"</BODY></HWPML>"));
  var otherCell=new HwpParagraphProofs(Full("summary").Replace("RowAddr='0'","RowAddr='1'")).CreateExpected(Path);Check(otherCell.ContainerToken!=expected.ContainerToken,"cell geometry changes container");
  // Saved output from an earlier isolated synthetic HWP 2024 experiment.
  // Constructing a block tests the real mapping definitions, not live APIs.
  string fixture=@"investigation\probe-before.hml";if(System.IO.File.Exists(fixture)){
   string raw=System.IO.File.ReadAllText(fixture);var real=new HwpParagraphProofs(raw);
   const string realPath="BODY/SECTION[0]/P[2]/TEXT[0]/TABLE[0]/ROW[0]/CELL[0]/PARALIST[0]/P[0]";
   Check(real.CreateExpected(realPath).Text=="TABLEPROBE34","saved synthetic HWP fixture definitions");
   var d=new System.Xml.XmlDocument();d.LoadXml(raw);var target=d.SelectSingleNode("//CELL[1]/PARALIST/P");
   var block=d.CloneNode(true) as System.Xml.XmlDocument;var section=block.SelectSingleNode("/HWPML/BODY/SECTION");while(section.HasChildNodes)section.RemoveChild(section.FirstChild);section.AppendChild(block.ImportNode(target,true));
   Check(real.MatchSelected(block.OuterXml).Unique,"saved synthetic fixture unique selected paragraph");
  }
  // Exact full/saveblock XML emitted by isolated native HWP 2024 RepeatFind
  // inspection. Unlike the constructed block above, saveblock injects first-P
  // break defaults, section settings and renumbers fonts/character shapes.
  string nativeFull=@"addin\tests\fixtures\hwp2024-repeat-full.hml",nativeSelected=@"addin\tests\fixtures\hwp2024-repeat-saveblock.hml";
  if(System.IO.File.Exists(nativeFull)&&System.IO.File.Exists(nativeSelected)){
   var native=new HwpParagraphProofs(System.IO.File.ReadAllText(nativeFull));string selected=System.IO.File.ReadAllText(nativeSelected);var matched=native.MatchSelected(selected);
   Check(matched.Unique&&matched.XmlPath=="BODY/SECTION[0]/P[1]"&&matched.Text=="BODYANCHOR21","actual native full/saveblock paragraph proof");
   Check(!native.MatchSelected(selected.Replace("Height=\"1000\"", "Height=\"1100\"")).Unique,"actual native changed style remains rejected");
  }
  Console.WriteLine("PASS XML paragraph proofs: unique saveblock mapping, remapped definitions, transitive styles, duplicates and objects; no native HWP");
 }
}
