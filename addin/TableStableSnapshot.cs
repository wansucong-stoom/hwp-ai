using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace HAddon {
 // Readback comparison only, never document editing. Preserve the complete
 // XML infoset including image payload, but ignore HWP caret metadata,
 // Base64 wrapping and a valid BINDATA Size value. All other changes fail.
 internal static class TableStableSnapshot {
  static void Token(StringBuilder b,string s){s=s??"";b.Append(s.Length).Append(':').Append(s);}
  internal static string Digest(string raw){
   if(String.IsNullOrEmpty(raw)||raw.Length>32*1024*1024)throw new InvalidOperationException("문서 재확인 데이터가 올바르지 않습니다.");
   // Hancom may return a U+FEFF BOM in the .NET string, which StringReader
   // does not consume as a byte-order mark. Match ComposeXml.Parse exactly.
   string input=raw[0]=='\uFEFF'?raw.Substring(1):raw;
   var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=32*1024*1024,IgnoreWhitespace=false};
   var b=new StringBuilder(raw.Length);int skip=-1,charDepth=-1,binaryDepth=-1;var stack=new Stack<string>();
   using(var reader=XmlReader.Create(new StringReader(input),settings))while(reader.Read()){
    if(skip>=0){if(reader.NodeType==XmlNodeType.EndElement&&reader.Depth==skip)skip=-1;continue;}
    if(reader.NodeType==XmlNodeType.Element){
     if(reader.LocalName=="CARETPOS"&&stack.Count>0&&stack.Peek()=="DOCSETTING"&&stack.Contains("HEAD")){if(!reader.IsEmptyElement)skip=reader.Depth;continue;}
     string parent=stack.Count==0?"":stack.Peek();bool binary=reader.LocalName=="BINDATA"&&parent=="BINDATASTORAGE";
     b.Append('E');Token(b,reader.Name);var attrs=new SortedDictionary<string,string>(StringComparer.Ordinal);
     if(reader.HasAttributes)while(reader.MoveToNextAttribute()){
      string value=reader.Value;
      if(binary&&reader.Name=="Size"){long size;if(!Int64.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out size)||size<0)throw new InvalidOperationException("문서 이미지 크기 정보를 확인하지 못했습니다.");value="valid-size";}
      attrs.Add(reader.Name,value);
     }
     reader.MoveToElement();b.Append(attrs.Count).Append(';');foreach(var attr in attrs){Token(b,attr.Key);Token(b,attr.Value);}
     if(reader.LocalName=="CHAR"&&!reader.IsEmptyElement)charDepth=reader.Depth;
     if(binary&&!reader.IsEmptyElement)binaryDepth=reader.Depth;
     if(reader.IsEmptyElement){b.Append('X');Token(b,reader.Name);}else stack.Push(reader.LocalName);
    }else if(reader.NodeType==XmlNodeType.EndElement){
     b.Append('X');Token(b,reader.Name);if(reader.Depth==charDepth)charDepth=-1;if(reader.Depth==binaryDepth)binaryDepth=-1;
     if(stack.Count==0||stack.Pop()!=reader.LocalName)throw new InvalidOperationException("문서 재확인 구조가 올바르지 않습니다.");
    }else if(reader.NodeType==XmlNodeType.Text||reader.NodeType==XmlNodeType.CDATA||reader.NodeType==XmlNodeType.Whitespace||reader.NodeType==XmlNodeType.SignificantWhitespace){
     string value=reader.Value;if(binaryDepth>=0)value=String.Concat(value.Where(c=>c!=' '&&c!='\t'&&c!='\r'&&c!='\n'));
     if(value.Length==0||(binaryDepth<0&&charDepth<0&&String.IsNullOrWhiteSpace(value)))continue;
     b.Append('T');Token(b,value);
    }else if(reader.NodeType!=XmlNodeType.XmlDeclaration&&reader.NodeType!=XmlNodeType.Comment){b.Append('N');Token(b,reader.Name);Token(b,reader.Value);}
   }
   using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(b.ToString()))).Replace("-","");
  }
 }
}
