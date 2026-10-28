using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;

namespace HAddon {
 // Read-only semantic input for the table design model. Coordinates and IDs
 // refer to physical cells, so merged cells appear once, with their full span.
 internal static class TableDesignSnapshot {
  static XmlElement[] Children(XmlNode node){return node.ChildNodes.Cast<XmlNode>().OfType<XmlElement>().ToArray();}
  static int Number(XmlElement e,string key,int fallback){int n;if(!e.HasAttribute(key))return fallback;if(!Int32.TryParse(e.GetAttribute(key),NumberStyles.None,CultureInfo.InvariantCulture,out n)||n<0)throw new InvalidOperationException("표 셀 위치가 올바르지 않습니다.");return n;}
  static string Definition(ComposeXml xml,string list,string type,string id){
   var lists=Children(xml.Mapping).Where(e=>e.LocalName==list).ToArray();
   if(lists.Length!=1)throw new InvalidOperationException("표 서식 목록을 확인하지 못했습니다.");
   var matches=Children(lists[0]).Where(e=>e.LocalName==type&&e.GetAttribute("Id")==id).ToArray();
   if(matches.Length!=1)throw new InvalidOperationException("표 서식 정의를 확인하지 못했습니다.");
   // Preserve the actual element names and attributes in readable XML for the
   // model. The length-prefixed comparison canonical form is not an AI prompt.
   string value=matches[0].OuterXml;if(value.Length>12000)throw new InvalidOperationException("표 서식 정의가 너무 큽니다.");return value;
  }
  internal static Dictionary<string,object> Create(ComposeXml xml,int sourceIndex){
   if(xml==null)throw new ArgumentNullException("xml");
   var tables=xml.Paragraphs.SelectMany(p=>Children(p).Where(x=>x.LocalName=="TEXT").SelectMany(Children).Where(x=>x.LocalName=="TABLE")).ToArray();
   if(tables.Length<2||tables.Length>64||sourceIndex<0||sourceIndex>=tables.Length)throw new InvalidOperationException("기준 표와 대상 표를 확인하지 못했습니다.");
   var result=new List<object>();int total=0;
   for(int index=0;index<tables.Length;index++){
    var table=tables[index];int rows=Number(table,"RowCount",0),columns=Number(table,"ColCount",0);
    if(rows<1||columns<1||rows*columns>4096)throw new InvalidOperationException("표 크기를 확인하지 못했습니다.");
    var cells=new List<object>();var seen=new bool[rows,columns];
    foreach(var row in Children(table).Where(e=>e.LocalName=="ROW"))foreach(var c in Children(row).Where(e=>e.LocalName=="CELL")){
     int r=Number(c,"RowAddr",-1),col=Number(c,"ColAddr",-1),rs=Number(c,"RowSpan",1),cs=Number(c,"ColSpan",1);
     if(r<0||col<0||rs<1||cs<1||r+rs>rows||col+cs>columns)throw new InvalidOperationException("표 병합 범위를 확인하지 못했습니다.");
     for(int y=r;y<r+rs;y++)for(int x=col;x<col+cs;x++){if(seen[y,x])throw new InvalidOperationException("표 셀 범위가 겹칩니다.");seen[y,x]=true;}
     var paraLists=Children(c).Where(e=>e.LocalName=="PARALIST").ToArray();
     if(paraLists.Length!=1)throw new InvalidOperationException("표 셀 문단을 확인하지 못했습니다.");
     var paragraphs=Children(paraLists[0]).Where(e=>e.LocalName=="P").ToArray();
     var texts=paragraphs.SelectMany(p=>Children(p).Where(e=>e.LocalName=="TEXT")).ToArray();
     string text=String.Join("\n",paragraphs.Select(p=>String.Concat(Children(p).Where(e=>e.LocalName=="TEXT").SelectMany(Children).Where(e=>e.LocalName=="CHAR").Select(e=>e.InnerText))).ToArray());
     if(text.Length>12000)throw new InvalidOperationException("표 셀 텍스트가 너무 깁니다.");
     string border=c.GetAttribute("BorderFill");
     var style=new Dictionary<string,object>{{"borderFill",Definition(xml,"BORDERFILLLIST","BORDERFILL",border)},
      {"paragraphs",paragraphs.Select(p=>Definition(xml,"PARASHAPELIST","PARASHAPE",p.GetAttribute("ParaShape"))).Distinct().ToArray()},
      {"characters",texts.Select(t=>Definition(xml,"CHARSHAPELIST","CHARSHAPE",t.GetAttribute("CharShape"))).Distinct().ToArray()}};
     cells.Add(new Dictionary<string,object>{{"id","c"+r+"_"+col},{"row",r},{"column",col},{"rowSpan",rs},{"columnSpan",cs},{"text",text},{"style",style}});total++;
    }
    for(int y=0;y<rows;y++)for(int x=0;x<columns;x++)if(!seen[y,x])throw new InvalidOperationException("표의 빈 셀 위치를 확인하지 못했습니다.");
    result.Add(new Dictionary<string,object>{{"id","t"+index},{"rows",rows},{"columns",columns},{"cells",cells}});
   }
   if(total>2048)throw new InvalidOperationException("표 셀이 분석 가능 크기를 초과합니다.");
   return new Dictionary<string,object>{{"referenceTableId","t"+sourceIndex},{"tables",result}};
  }
 }
}
