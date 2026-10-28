using System;
using HAddon;
class DocumentAddressMapTests {
 static void Check(bool ok,string text){if(!ok)throw new Exception(text);}
 static InspectedNativeParagraph N(string text,int p){return new InspectedNativeParagraph{List=1,Paragraph=p,Start=0,End=text.Length,Text=text};}
 static DocumentParagraph X(string text,string path){return new DocumentParagraph{Text=text,Path=path};}
 static void Main(){
  var x=new[]{X("unique content","a"),X("duplicate","b"),X("duplicate","c"),X("two occurrences","d"),X("split content","e")};
  var n=new[]{N("unique content",0),N("duplicate",1),N("two occurrences",2),N("split content",3)};
  var result=DocumentAddressMap.Find(x,n,new[]{"unique   content duplicate two occurrences two occurrences split","content"});
  Check(result.Length==1&&result[0].XmlPath=="a"&&result[0].Page==1,"unique match only");
  result=DocumentAddressMap.Find(new[]{X("text","a")},new[]{N("text",0),N("text",1)},new[]{"text"});Check(result.Length==0,"ambiguous native address");
  var structural=DocumentAddressMap.FindStructural(new[]{X("first","a"),X("repeat","b"),X("repeat","c")},new[]{N("first",0),N("repeat",1)});
  Check(structural.Length==1&&structural[0].Page==0&&structural[0].XmlPath=="a","structural matches do not infer page");
  var physical=new[]{new PagePositionEvidence{XmlPath="a",List=1,Paragraph=0,Start=0,End=5,RawPage=2}};
  result=DocumentAddressMap.BindPages(structural,physical,new[]{"other","first"});Check(result.Length==1&&result[0].Page==2,"page cross-check");
  result=DocumentAddressMap.BindPages(structural,physical,new[]{"first","other"});Check(result.Length==0,"page text mismatch");
  physical[0].List=2;result=DocumentAddressMap.BindPages(structural,physical,new[]{"other","first"});Check(result.Length==0,"native address mismatch");
  Console.WriteLine("PASS candidate matching rejects duplicate XML/native text, repeated and split page text");
 }
}
