using System;
using System.Linq;
using System.Xml;

namespace HAddon {
 // A conservative first concurrency capability: a user may change text in
 // existing CHAR nodes while the worker is verifying a frozen snapshot. No
 // node, control, style, table grid or binary changes are treated as text.
 internal static class TableConcurrentText {
  static XmlElement[] Chars(ComposeXml value){return value.Body.GetElementsByTagName("CHAR").Cast<XmlElement>().ToArray();}
  static void Plain(XmlElement[] nodes){
   foreach(var node in nodes)foreach(XmlNode child in node.ChildNodes)
    if(child.NodeType!=XmlNodeType.Text&&child.NodeType!=XmlNodeType.CDATA&&child.NodeType!=XmlNodeType.Whitespace&&child.NodeType!=XmlNodeType.SignificantWhitespace)
     throw new InvalidOperationException("동시 편집의 본문 구조를 확인하지 못했습니다.");
  }
  internal static bool TryRebase(ComposeXml original,string beforeRaw,string afterRaw,TableStylePlan plan,int appliedCount){
   try{
    var before=ComposeXml.Parse(beforeRaw);var after=ComposeXml.Parse(afterRaw);
    var beforeText=Chars(before);var afterText=Chars(after);var originalText=Chars(original);
    if(beforeText.Length!=afterText.Length||beforeText.Length!=originalText.Length)return false;
    Plain(beforeText);Plain(afterText);Plain(originalText);
    var texts=afterText.Select(x=>x.InnerText).ToArray();
    var maskedBefore=ComposeXml.Parse(beforeRaw);var maskedAfter=ComposeXml.Parse(afterRaw);
    foreach(var n in Chars(maskedBefore))n.InnerText="STOOM-CONCURRENT-TEXT";
    foreach(var n in Chars(maskedAfter))n.InnerText="STOOM-CONCURRENT-TEXT";
    if(TableStableSnapshot.Digest(maskedBefore.Document.OuterXml)!=TableStableSnapshot.Digest(maskedAfter.Document.OuterXml))return false;
    var proposed=ComposeXml.Parse(original.Document.OuterXml);var proposedText=Chars(proposed);
    for(int i=0;i<texts.Length;i++)if(beforeText[i].InnerText!=texts[i])proposedText[i].InnerText=texts[i];
    TableStylePlan.Verify(proposed,after,plan,appliedCount);
    for(int i=0;i<texts.Length;i++)if(beforeText[i].InnerText!=texts[i])originalText[i].InnerText=texts[i];
    return true;
   }catch{return false;}
  }
 }
}
