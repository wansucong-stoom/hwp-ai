using System;
using HAddon;
class PageTextCoverageTests {
 static DocumentParagraph P(string path,string text,bool objects=false){return new DocumentParagraph{Path=path,Text=text,HasObjects=objects};}
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Main(){
  var p=new[]{P("a","first paragraph"),P("b","next cell"),P("c","duplicate"),P("d","duplicate"),P("e","",true)};
  var result=PageTextCoverage.Assess(p,new[]{"first\n paragraph next", " cell duplicate"});
  Check(result.ParagraphsConsidered==4&&result.ParagraphsNotFound==1&&result.RepeatedTextsShortOfOccurrences==1,"duplicate accounting");
  Check(result.ObjectBearingParagraphs==1&&result.PotentiallyMissingPaths.Length==2,"object and ambiguous duplicate paths");
  result=PageTextCoverage.Assess(new[]{P("split","AB CD")},new[]{"AB", "CD"});Check(result.ParagraphsNotFound==0,"page split detection");
  result=PageTextCoverage.Assess(new[]{P("a","text")},new[]{"text",""});Check(result.EmptyPages==1,"empty page warning");
  result=PageTextCoverage.Assess(new[]{P("a","text")},new[]{"wrong"});Check(result.ParagraphsNotFound==1,"missing text warning");
  bool bad=false;try{PageTextCoverage.Assess(p,new string[]{null});}catch(ArgumentException){bad=true;}Check(bad,"null page cannot be evaluated");
  Console.WriteLine("PASS read-only omission diagnostics; no claim of complete text, page scope or edit safety");
 }
}
