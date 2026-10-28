using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using HAddon;
internal static class TableStableSnapshotTests {
 static int n;static void Check(bool ok,string message){n++;if(!ok)throw new Exception(message);}
 static void Main(string[] args){try{
  string raw=File.ReadAllText(args[0],Encoding.UTF8);var xml=ComposeXml.Parse(raw);string baseline=TableStableSnapshot.Digest(xml.Document.OuterXml);
  Check(TableStableSnapshot.Digest("\uFEFF"+xml.Document.OuterXml)==baseline,"UTF-8 BOM in HWPML string is handled like ComposeXml.Parse");
  var other=ComposeXml.Parse(xml.Document.OuterXml);var bin=(XmlElement)other.Document.GetElementsByTagName("BINDATA")[0];bin.SetAttribute("Size","999");bin.InnerText=Convert.ToBase64String(Convert.FromBase64String(bin.InnerText));
  Check(TableStableSnapshot.Digest(other.Document.OuterXml)==baseline,"base64 wrapping and numeric size are stable");
  var bytes=Convert.FromBase64String(bin.InnerText);bytes[0]^=1;bin.InnerText=Convert.ToBase64String(bytes);Check(TableStableSnapshot.Digest(other.Document.OuterXml)!=baseline,"image content remains protected");
  other=ComposeXml.Parse(xml.Document.OuterXml);other.Document.GetElementsByTagName("CHAR")[0].InnerText+="tampered";Check(TableStableSnapshot.Digest(other.Document.OuterXml)!=baseline,"cell text remains protected");
  other=ComposeXml.Parse(xml.Document.OuterXml);((XmlElement)other.Document.GetElementsByTagName("CELL")[0]).SetAttribute("BorderFill","1");Check(TableStableSnapshot.Digest(other.Document.OuterXml)!=baseline,"cell border remains protected");
  other=ComposeXml.Parse(xml.Document.OuterXml);((XmlElement)other.Document.GetElementsByTagName("CARETPOS")[0]).SetAttribute("List","888");Check(TableStableSnapshot.Digest(other.Document.OuterXml)==baseline,"volatile saved caret metadata is excluded");
  other=ComposeXml.Parse(xml.Document.OuterXml);((XmlElement)other.Document.GetElementsByTagName("BINDATA")[0]).SetAttribute("Compress","false");Check(TableStableSnapshot.Digest(other.Document.OuterXml)!=baseline,"image encoding metadata remains protected");
  other=ComposeXml.Parse(xml.Document.OuterXml);((XmlElement)other.Document.GetElementsByTagName("BINDATA")[0]).SetAttribute("Size","broken");bool bad=false;try{TableStableSnapshot.Digest(other.Document.OuterXml);}catch(InvalidOperationException){bad=true;}Check(bad,"invalid size is rejected");
  Console.WriteLine("PASS "+n+" stable snapshot comparisons on saved document; no live HWP document modified.");
 }catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}}
}
