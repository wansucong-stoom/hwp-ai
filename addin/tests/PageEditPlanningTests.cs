using System;
using System.Collections.Generic;
using HAddon;
class PageEditPlanningTests {
 static Dictionary<string,object> Change(string id,string text,params object[] sources){return new Dictionary<string,object>{{"id",id},{"replacement",text},{"sourcePages",sources}};}
 static Dictionary<string,object> Plan(object[] changes,object[] uncertain){return new Dictionary<string,object>{{"changes",changes},{"uncertainties",uncertain}};}
 static void Reject(Action a){try{a();}catch(InvalidOperationException){return;}throw new Exception("expected rejection");}
 static void Main(){
  var pages=new[]{"summary","target","source","other"};var candidates=new[]{new DocumentAddressCandidate{XmlPath="a",Page=1,Text="old"},new DocumentAddressCandidate{XmlPath="b",Page=2,Text="old2"},new DocumentAddressCandidate{XmlPath="other",Page=4,Text="other"}};
  var planning=new PageEditPlanning(pages,candidates,new[]{1,2},new[]{3});
  var result=planning.Compile(Plan(new object[]{Change("a","new",3),Change("b","new2",3)},new object[0]));
  if(result.Length!=2||result[0].Original!="old"||result[1].Id!="b")throw new Exception("valid plan");
  Reject(()=>planning.Compile(Plan(new object[]{Change("other","bad",3)},new object[0])));
  Reject(()=>planning.Compile(Plan(new object[]{Change("a","bad",4)},new object[0])));
  Reject(()=>planning.Compile(Plan(new object[]{Change("a","bad",3,3)},new object[0])));
  Reject(()=>planning.Compile(Plan(new object[]{Change("a","bad",3),Change("a","again",3)},new object[0])));
  candidates[0].Text="mutated after capture";
  result=planning.Compile(Plan(new object[]{Change("a","new",3)},new object[0]));if(result[0].Original!="old")throw new Exception("candidate was not frozen");
  var payload=planning.Snapshot;((Dictionary<string,object>)((object[])payload["pages"])[0])["text"]="altered";((Dictionary<string,object>)((object[])payload["targets"])[0])["text"]="altered";pages[0]="altered";
  var fresh=planning.Snapshot;if((string)((Dictionary<string,object>)((object[])fresh["pages"])[0])["text"]!="summary"||(string)((Dictionary<string,object>)((object[])fresh["targets"])[0])["text"]!="old")throw new Exception("request payload was not isolated");
  var emptySource=new PageEditPlanning(new[]{"summary"," "},new[]{new DocumentAddressCandidate{XmlPath="a",Page=1,Text="old"}},new[]{1},new[]{2});
  Reject(()=>emptySource.Compile(Plan(new object[]{Change("a","bad",2)},new object[0])));
  Reject(()=>planning.Compile(Plan(new object[]{Change("a","bad\nother",3)},new object[0])));
  Reject(()=>planning.Compile(Plan(new object[]{Change("a","bad",3)},new object[]{"insufficient evidence"})));
  Reject(()=>new PageEditPlanning(pages,candidates,new[]{1,9},new[]{3}));
  if(planning.Snapshot.ContainsKey("structuredParagraphs"))throw new Exception("legacy payload unexpectedly includes document structure");
  var structure=new[]{new DocumentParagraph{Path="private-path",Text="table text",CellPath="private-cell",HasObjects=false},new DocumentParagraph{Text="",HasObjects=true}};
  var structured=new PageEditPlanning(new[]{"summary","source"},new[]{new DocumentAddressCandidate{XmlPath="a",Page=1,Text="old"}},new[]{1},new[]{2},structure);
  var paragraphs=(object[])structured.Snapshot["structuredParagraphs"];var first=(Dictionary<string,object>)paragraphs[0];var second=(Dictionary<string,object>)paragraphs[1];
  if(first.Count!=2||(string)first["text"]!="table text"||(bool)first["hasObjects"]||(string)second["text"]!=""||!(bool)second["hasObjects"])throw new Exception("ordered text-only structure contract");
  structure[0].Text="caller changed";structure[1].HasObjects=false;first["text"]="payload changed";
  paragraphs=(object[])structured.Snapshot["structuredParagraphs"];if((string)((Dictionary<string,object>)paragraphs[0])["text"]!="table text"||!(bool)((Dictionary<string,object>)paragraphs[1])["hasObjects"])throw new Exception("structure payload was not isolated");
  var scopedCandidate=new[]{new DocumentAddressCandidate{XmlPath="a",Page=1,Text="old"}};
  Reject(()=>new PageEditPlanning(new[]{"summary","source","outside"},scopedCandidate,new[]{1},new[]{2},structure));
  Reject(()=>new PageEditPlanning(new[]{"summary","source"},scopedCandidate,new[]{1},new[]{2},new DocumentParagraph[]{null}));
  Reject(()=>new PageEditPlanning(new[]{"summary","source"},scopedCandidate,new[]{1},new[]{2},new[]{new DocumentParagraph{Text=null}}));
  new PageEditPlanning(new[]{"summary","source"},scopedCandidate,new[]{1},new[]{2},new[]{new DocumentParagraph{Text=new string('x',400000)}});
  Reject(()=>new PageEditPlanning(new[]{"summary","source"},scopedCandidate,new[]{1},new[]{2},new[]{new DocumentParagraph{Text=new string('x',400001)}}));
  var tooMany=new DocumentParagraph[100001];for(int i=0;i<tooMany.Length;i++)tooMany[i]=new DocumentParagraph{Text=""};Reject(()=>new PageEditPlanning(new[]{"summary","source"},scopedCandidate,new[]{1},new[]{2},tooMany));
  Console.WriteLine("PASS scoped targets, source-page bounds, uncertainties, isolated optional structure and structure size limits");
 }
}
