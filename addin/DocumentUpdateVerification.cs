using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace HAddon {
 // Verifies each applied prefix. Adjacent runs with identical attributes may
 // be serialized separately or merged without changing their visible style.
 // This is a verifier, never a way to write a document into HWP.
 internal sealed class DocumentUpdateVerification {
  readonly XmlDocument original;
  readonly Dictionary<string,string> paths;
  internal DocumentUpdateVerification(string xml,Dictionary<string,string> targetPaths){
   if(String.IsNullOrWhiteSpace(xml)||xml.Length>32*1024*1024||targetPaths==null)throw new InvalidOperationException("문서 검증 정보를 확인하지 못했습니다.");
   original=new XmlDocument();original.XmlResolver=null;original.PreserveWhitespace=true;
   using(var input=new StringReader(xml.TrimStart('\uFEFF')))using(var reader=XmlReader.Create(input,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=32*1024*1024}))original.Load(reader);
   paths=new Dictionary<string,string>(targetPaths,StringComparer.Ordinal);
  }
  static IEnumerable<XmlElement> Children(XmlNode n){return n.ChildNodes.Cast<XmlNode>().OfType<XmlElement>();}
  static XmlElement Find(XmlDocument doc,string path){
   var parts=(path??"").Split('/');if(parts.Length==0||parts[0]!="BODY")throw new InvalidOperationException("본문 주소가 올바르지 않습니다.");
   var bodies=Children(doc.DocumentElement).Where(x=>x.LocalName=="BODY").ToArray();if(bodies.Length!=1)throw new InvalidOperationException("본문을 확인하지 못했습니다.");XmlElement cur=bodies[0];
   for(int i=1;i<parts.Length;i++){
    string part=parts[i];int open=part.LastIndexOf('['),index;
    if(open<=0||!part.EndsWith("]")||!Int32.TryParse(part.Substring(open+1,part.Length-open-2),out index)||index<0)throw new InvalidOperationException("문단 주소가 올바르지 않습니다.");
    cur=Children(cur).Where(x=>x.LocalName==part.Substring(0,open)).Skip(index).FirstOrDefault();if(cur==null)throw new InvalidOperationException("원래 문단을 찾지 못했습니다.");
   }
   if(cur.LocalName!="P")throw new InvalidOperationException("편집 대상이 문단이 아닙니다.");return cur;
  }
  static bool SameAttributes(XmlElement first,XmlElement second){
   if(first.Attributes.Count!=second.Attributes.Count)return false;
   foreach(XmlAttribute attr in first.Attributes){var other=second.GetAttributeNode(attr.LocalName,attr.NamespaceURI);if(other==null||other.Value!=attr.Value)return false;}
   return true;
  }
  // Collapse only adjacent plain text runs with the *same* complete attribute
  // set. We retain all other paragraph nodes and attributes in the snapshot.
  // Mixed character shapes, objects, hidden controls and mismatched text are
  // rejected rather than normalized away.
  static void MaskParagraph(XmlElement paragraph,string expected,Action<XmlElement> normalize){
   normalize(paragraph);
   var elements=Children(paragraph).ToArray();var runs=elements.Where(e=>e.LocalName=="TEXT").ToArray();
   if(runs.Length==0)throw new InvalidOperationException("문단의 글자 서식을 확인하지 못했습니다.");
   int start=Array.IndexOf(elements,runs[0]);
   if(runs.Where((r,i)=>start+i>=elements.Length||!Object.ReferenceEquals(elements[start+i],r)).Any())throw new InvalidOperationException("텍스트 사이에 문서 개체가 있어 검증할 수 없습니다.");
   var chars=new List<XmlElement>();var contentRuns=new List<XmlElement>();bool seenText=false;
   foreach(var run in runs){
    var children=Children(run).ToArray();if(children.Length==0)throw new InvalidOperationException("문단에 빈 텍스트 실행이 있습니다.");
    var currentChars=new List<XmlElement>();
    foreach(var child in children){
     if((child.LocalName=="SECDEF"||child.LocalName=="COLDEF")&&!seenText)continue;
     if(child.LocalName!="CHAR"||Children(child).Any())throw new InvalidOperationException("문단에 텍스트 이외의 개체가 있습니다.");
     currentChars.Add(child);if(child.InnerText.Length>0)seenText=true;
    }
    if(currentChars.Count==0)continue;
    if(contentRuns.Count>0&&(run.Name!=contentRuns[0].Name||run.NamespaceURI!=contentRuns[0].NamespaceURI||!SameAttributes(contentRuns[0],run)))throw new InvalidOperationException("혼합 글자 서식 문단은 아직 직접 검증할 수 없습니다.");
    contentRuns.Add(run);chars.AddRange(currentChars);
   }
   if(chars.Count==0)throw new InvalidOperationException("문단의 본문을 확인하지 못했습니다.");
   if(chars.Skip(1).Any(c=>c.Name!=chars[0].Name||c.NamespaceURI!=chars[0].NamespaceURI||!SameAttributes(chars[0],c))||chars.Any(c=>c.ChildNodes.Cast<XmlNode>().Any(n=>n.NodeType!=XmlNodeType.Text&&n.NodeType!=XmlNodeType.CDATA&&n.NodeType!=XmlNodeType.Whitespace&&n.NodeType!=XmlNodeType.SignificantWhitespace))||String.Concat(chars.Select(c=>c.InnerText).ToArray())!=expected)throw new InvalidOperationException("원래 문단의 내용 또는 구조가 다릅니다.");
   // Reject meaningful text or comments outside the CHAR elements instead of
   // erasing unexpected metadata while constructing a comparable snapshot.
   foreach(var run in runs)foreach(XmlNode node in run.ChildNodes)if(!(node is XmlElement)&&!(node is XmlWhitespace)&&!(node is XmlSignificantWhitespace))throw new InvalidOperationException("텍스트 실행의 추가 데이터를 확인하지 못했습니다.");
   // Keep leading section/column controls byte-for-byte in this private XML
   // copy. Only character content is masked; layout/master-page edits remain
   // visible to the complete snapshot comparison.
   var first=contentRuns[0];foreach(var child in Children(first).Where(e=>e.LocalName=="CHAR").ToArray())first.RemoveChild(child);
   var canonical=(XmlElement)chars[0].CloneNode(false);canonical.InnerText="STOOM-PINNED-PARAGRAPH";first.AppendChild(canonical);
   foreach(var run in contentRuns.Skip(1))paragraph.RemoveChild(run);
  }
  XmlDocument Parse(string xml){
   if(String.IsNullOrWhiteSpace(xml)||xml.Length>32*1024*1024)throw new InvalidOperationException("문서 검증 정보를 확인하지 못했습니다.");
   var copy=new XmlDocument();copy.XmlResolver=null;copy.PreserveWhitespace=true;
   using(var input=new StringReader(xml.TrimStart('\uFEFF')))using(var reader=XmlReader.Create(input,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=32*1024*1024}))copy.Load(reader);
   return copy;
  }
  string MaskedDigest(XmlDocument doc,IList<DocumentUpdateTarget> plan,int applied,bool originalText){
   var visited=new HashSet<string>(StringComparer.Ordinal);
   var normalize=applied==0?null:HwpParagraphProofs.CreateEquivalentCharShapeNormalizer(doc);
   for(int i=0;i<applied;i++){
    var t=plan[i];string path;if(t==null||!paths.TryGetValue(t.Id,out path)||!visited.Add(path))throw new InvalidOperationException("편집 문단 주소가 중복되거나 없습니다.");
    MaskParagraph(Find(doc,path),originalText?t.Original:t.Replacement,normalize);
   }
   return TableStableSnapshot.Digest(doc.OuterXml);
  }
  internal string ExpectedDigest(IList<DocumentUpdateTarget> plan,int applied){
   if(plan==null||applied<0||applied>plan.Count)throw new InvalidOperationException("편집 검증 범위가 올바르지 않습니다.");
   return MaskedDigest((XmlDocument)original.CloneNode(true),plan,applied,true);
  }
  internal void Verify(string current,IList<DocumentUpdateTarget> plan,int applied){
   var expected=ExpectedDigest(plan,applied);
   if(MaskedDigest(Parse(current),plan,applied,false)!=expected)throw new InvalidOperationException("편집 계획 밖의 문서 내용 또는 서식이 달라졌습니다.");
  }
 }
}
