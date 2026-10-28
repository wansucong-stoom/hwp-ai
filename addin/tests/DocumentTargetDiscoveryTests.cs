using System;
using System.Collections.Generic;
using HAddon;
class DocumentTargetDiscoveryTests {
 static void Reject(Action a){try{a();}catch(InvalidOperationException){return;}throw new Exception("expected rejection");}
 static Dictionary<string,object> Plan(object[] targets,object[] uncertainty){return new Dictionary<string,object>{{"targets",targets},{"uncertainties",uncertainty}};}
 static void Main(){
  var pages=new[]{"same summary", "detail"};var p=new[]{new DocumentParagraph{Path="BODY/SECTION[0]/P[0]",Text="same",CellPath="cell1"},new DocumentParagraph{Path="BODY/SECTION[0]/P[1]",Text="same",CellPath="cell2",HasObjects=true}};
  var target=new[]{1};var source=new[]{2};var d=new DocumentTargetDiscovery(pages,p,target,source);
  pages[0]="changed";p[0].Text="changed";p[0].Path="changed";target[0]=2;
  var payload=d.Payload();var paragraphs=(object[])payload["paragraphs"];var first=(Dictionary<string,object>)paragraphs[0];
  if((string)first["id"]!="p0"||(string)first["text"]!="same"||first.ContainsKey("path")||((int[])payload["targetPages"])[0]!=1)throw new Exception("snapshot not frozen or path exposed");
  first["text"]="modified by caller";if((string)((Dictionary<string,object>)((object[])d.Payload()["paragraphs"])[0])["text"]!="same")throw new Exception("mutable payload");
  var r=d.Validate(Plan(new object[]{"p0","p1"},new object[]{"uncertain"}));
  if(r.Targets.Length!=2||r.Targets[0].XmlPath!="BODY/SECTION[0]/P[0]"||!r.Targets[1].HasObjects||r.Uncertainties.Length!=1)throw new Exception("discovery lost duplicates/objects/uncertainty");
  r.Targets[0].XmlPath="changed";if(d.Validate(Plan(new object[]{"p0"},new object[0])).Targets[0].XmlPath!="BODY/SECTION[0]/P[0]")throw new Exception("mutable result");
  Reject(()=>d.Validate(Plan(new object[]{"p0","p0"},new object[0])));Reject(()=>d.Validate(Plan(new object[]{"p9"},new object[0])));
  Reject(()=>new DocumentTargetDiscovery(new[]{"p"},new[]{new DocumentParagraph{Path="x",Text="a"}},new[]{2},new[]{1}));
  Console.WriteLine("PASS AI target discovery with immutable IDs, duplicate text and explicit uncertainty; no native writes");
 }
}
