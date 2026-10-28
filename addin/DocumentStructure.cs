using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace HAddon {
 // An HWPML structural path identifies an XML node in this snapshot only.
 // It is not a native HWP list/paragraph address or a page number.
 internal sealed class DocumentParagraph {
  public string Path, Text, CellPath;
  public bool HasObjects;
 }
 internal static class DocumentStructure {
  const int MaxXml=32*1024*1024, MaxParagraphs=100000;
  internal static DocumentParagraph[] Read(string xml) {
   if(String.IsNullOrWhiteSpace(xml)||xml.Length>MaxXml)throw new InvalidOperationException("문서 구조를 읽을 수 없습니다.");
   if(xml[0]=='\uFEFF')xml=xml.Substring(1);
   var doc=new XmlDocument();doc.XmlResolver=null;
   var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=MaxXml};
   using(var input=new StringReader(xml))using(var reader=XmlReader.Create(input,settings))doc.Load(reader);
   if(doc.DocumentElement==null||doc.DocumentElement.LocalName!="HWPML")throw new InvalidOperationException("한글 문서 구조가 아닙니다.");
   var bodies=Elements(doc.DocumentElement).Where(e=>e.LocalName=="BODY").ToArray();
   if(bodies.Length!=1)throw new InvalidOperationException("문서 본문을 확인할 수 없습니다.");
   var result=new List<DocumentParagraph>();Visit(bodies[0],"BODY",null,result);return result.ToArray();
  }
  static IEnumerable<XmlElement> Elements(XmlNode node){return node.ChildNodes.Cast<XmlNode>().OfType<XmlElement>();}
  static void Visit(XmlElement node,string path,string cell,List<DocumentParagraph> result){
   // Master-page paragraphs inside section/column controls are layout
   // metadata, not body paragraphs available for native text replacement.
   if(node.LocalName=="SECDEF"||node.LocalName=="COLDEF")return;
   if(node.LocalName=="CELL")cell=path;
   if(node.LocalName=="P"){
    var runs=Elements(node).Where(x=>x.LocalName=="TEXT").ToArray();
    var contents=runs.SelectMany(Elements).ToArray();
    bool seenText=false,objects=Elements(node).Any(x=>x.LocalName!="TEXT");
    foreach(var content in contents){
     if((content.LocalName=="SECDEF"||content.LocalName=="COLDEF")&&!seenText)continue;
     if(content.LocalName!="CHAR"||Elements(content).Any())objects=true;
     if(content.LocalName=="CHAR"&&content.InnerText.Length>0)seenText=true;
    }
    if(result.Count>=MaxParagraphs)throw new InvalidOperationException("문단 수가 분석 한도를 초과합니다.");
    result.Add(new DocumentParagraph{Path=path,CellPath=cell,Text=String.Concat(contents.Where(x=>x.LocalName=="CHAR").Select(x=>x.InnerText).ToArray()),
     HasObjects=objects});
   }
   var counts=new Dictionary<string,int>(StringComparer.Ordinal);
   foreach(var child in Elements(node)){
    int index;counts.TryGetValue(child.LocalName,out index);counts[child.LocalName]=index+1;
    Visit(child,path+"/"+child.LocalName+"["+index+"]",cell,result);
   }
  }
 }
}
