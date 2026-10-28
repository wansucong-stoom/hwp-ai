using System;
using System.Globalization;
using System.Linq;
using System.Xml;
using HAddon;

// Test-only emulation of Hancom's observed shared-edge update. This is separate
// from the production verifier: mutate an XML table just as native readback did.
internal static class TableBorderSimulation {
 static string Key(XmlElement value){var copy=(XmlElement)value.CloneNode(true);copy.RemoveAttribute("Id");return ComposeXml.Canonical(copy);}
 internal static void SwapTextStyles(XmlDocument document,string listName,string type,string reference,int a,int b){
  string sa=a.ToString(CultureInfo.InvariantCulture),sb=b.ToString(CultureInfo.InvariantCulture);var list=(XmlElement)document.GetElementsByTagName(listName)[0];
  foreach(XmlElement element in document.SelectNodes("//*"))foreach(XmlAttribute attr in element.Attributes)if(attr.LocalName==reference){if(attr.Value==sa)attr.Value=sb;else if(attr.Value==sb)attr.Value=sa;}
  foreach(XmlElement def in list.GetElementsByTagName(type)){string id=def.GetAttribute("Id");if(id==sa)def.SetAttribute("Id",sb);else if(id==sb)def.SetAttribute("Id",sa);}
 }
 internal static void CompactTextStyle(XmlDocument document,string listName,string type,string reference,int removed){
  var list=(XmlElement)document.GetElementsByTagName(listName)[0];var definitions=list.GetElementsByTagName(type).Cast<XmlElement>().ToArray();
  foreach(XmlElement element in document.SelectNodes("//*"))foreach(XmlAttribute attr in element.Attributes)if(attr.LocalName==reference){
   int id=Int32.Parse(attr.Value,CultureInfo.InvariantCulture);if(id==removed)throw new InvalidOperationException("Synthetic compact removed a referenced text style");if(id>removed)attr.Value=(id-1).ToString(CultureInfo.InvariantCulture);
  }
  list.RemoveChild(definitions.Single(x=>x.GetAttribute("Id")==removed.ToString(CultureInfo.InvariantCulture)));
  foreach(XmlElement def in list.GetElementsByTagName(type)){int id=Int32.Parse(def.GetAttribute("Id"),CultureInfo.InvariantCulture);if(id>removed)def.SetAttribute("Id",(id-1).ToString(CultureInfo.InvariantCulture));}
  list.SetAttribute("Count",list.GetElementsByTagName(type).Count.ToString(CultureInfo.InvariantCulture));
 }
 internal static void Compact(XmlDocument document,int removed){
  var list=(XmlElement)document.GetElementsByTagName("BORDERFILLLIST")[0];var definitions=list.GetElementsByTagName("BORDERFILL").Cast<XmlElement>().ToArray();
  foreach(XmlElement element in document.SelectNodes("//*"))foreach(XmlAttribute attr in element.Attributes)if(attr.LocalName=="BorderFill"||attr.LocalName=="BorderFillId"){
   int id=Int32.Parse(attr.Value,CultureInfo.InvariantCulture);if(id==removed)throw new InvalidOperationException("Synthetic compact removed a referenced style");if(id>removed)attr.Value=(id-1).ToString(CultureInfo.InvariantCulture);
  }
  list.RemoveChild(definitions.Single(x=>x.GetAttribute("Id")==removed.ToString(CultureInfo.InvariantCulture)));
  foreach(XmlElement def in list.GetElementsByTagName("BORDERFILL")){int id=Int32.Parse(def.GetAttribute("Id"),CultureInfo.InvariantCulture);if(id>removed)def.SetAttribute("Id",(id-1).ToString(CultureInfo.InvariantCulture));}
  list.SetAttribute("Count",list.GetElementsByTagName("BORDERFILL").Count.ToString(CultureInfo.InvariantCulture));
 }
 internal static void Propagate(XmlDocument document,int tableIndex,int row,int column){
  var table=document.GetElementsByTagName("TABLE").Cast<XmlElement>().ElementAt(tableIndex);
  var cells=table.GetElementsByTagName("CELL").Cast<XmlElement>().ToArray();
  var cell=cells.Single(c=>c.GetAttribute("RowAddr")==row.ToString(CultureInfo.InvariantCulture)&&c.GetAttribute("ColAddr")==column.ToString(CultureInfo.InvariantCulture));
  var list=(XmlElement)document.GetElementsByTagName("BORDERFILLLIST")[0];
  var source=list.ChildNodes.Cast<XmlNode>().OfType<XmlElement>().Single(b=>b.GetAttribute("Id")==cell.GetAttribute("BorderFill"));
  int rs=cell.HasAttribute("RowSpan")?Int32.Parse(cell.GetAttribute("RowSpan"),CultureInfo.InvariantCulture):1,cs=cell.HasAttribute("ColSpan")?Int32.Parse(cell.GetAttribute("ColSpan"),CultureInfo.InvariantCulture):1;
  string[] from={"LEFTBORDER","RIGHTBORDER","TOPBORDER","BOTTOMBORDER"},to={"RIGHTBORDER","LEFTBORDER","BOTTOMBORDER","TOPBORDER"};
  for(int i=0;i<4;i++){
   int length=i<2?rs:cs;for(int offset=0;offset<length;offset++){
   int rr=i<2?row+offset:i==2?row-1:row+rs,cc=i<2?(i==0?column-1:column+cs):column+offset;
   var neighbor=cells.SingleOrDefault(c=>{int r=Int32.Parse(c.GetAttribute("RowAddr")),col=Int32.Parse(c.GetAttribute("ColAddr")),sr=c.HasAttribute("RowSpan")?Int32.Parse(c.GetAttribute("RowSpan")):1,sc=c.HasAttribute("ColSpan")?Int32.Parse(c.GetAttribute("ColSpan")):1;return rr>=r&&rr<r+sr&&cc>=col&&cc<col+sc;});
   if(neighbor==null)continue;
   var current=list.ChildNodes.Cast<XmlNode>().OfType<XmlElement>().Single(b=>b.GetAttribute("Id")==neighbor.GetAttribute("BorderFill"));
   var replacement=(XmlElement)current.CloneNode(true);
   var edge=(XmlElement)source.SelectSingleNode(from[i]);
   if(edge==null)throw new InvalidOperationException("Synthetic source edge missing");
   var copied=document.CreateElement(to[i]);foreach(XmlAttribute a in edge.Attributes)copied.SetAttribute(a.Name,a.Value);copied.InnerXml=edge.InnerXml;
   var old=replacement.SelectSingleNode(to[i]);if(old==null)throw new InvalidOperationException("Synthetic destination edge missing");replacement.ReplaceChild(copied,old);
   string key=Key(replacement);var existing=list.ChildNodes.Cast<XmlNode>().OfType<XmlElement>().FirstOrDefault(b=>Key(b)==key);
   if(existing==null){int id=list.ChildNodes.Cast<XmlNode>().OfType<XmlElement>().Max(b=>Int32.Parse(b.GetAttribute("Id"),CultureInfo.InvariantCulture))+1;replacement.SetAttribute("Id",id.ToString(CultureInfo.InvariantCulture));list.AppendChild(replacement);list.SetAttribute("Count",list.ChildNodes.Cast<XmlNode>().OfType<XmlElement>().Count().ToString(CultureInfo.InvariantCulture));existing=replacement;}
   neighbor.SetAttribute("BorderFill",existing.GetAttribute("Id"));}
  }
 }
}
