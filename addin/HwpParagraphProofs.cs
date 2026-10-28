using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace HAddon {
 // Read-only XML evidence. Tokens describe a frozen HWPML paragraph, not a
 // native cursor or a rendered page. A selected saveblock has no cell parent;
 // matching it to one unique paragraph in the frozen full document is the
 // only way this component can return a container token. The caller must
 // independently verify same document/revision and a whole native selection.
 internal sealed class HwpSelectedParagraphProof {
  internal bool Unique;internal int MatchCount;
  internal string XmlPath,Text,ContainerToken,StyleToken,StructureToken;
 }
 internal sealed class HwpParagraphProofs {
  const int MaxXml=32*1024*1024,MaxParagraphs=100000;
  sealed class Plain {internal string Path,Text,Container,Style,Structure,Signature;}
  sealed class Model {
   internal XmlDocument Document;internal XmlElement Body;
   internal readonly Dictionary<string,XmlElement> Definitions=new Dictionary<string,XmlElement>(StringComparer.Ordinal);
   internal readonly Dictionary<string,XmlElement> Fonts=new Dictionary<string,XmlElement>(StringComparer.Ordinal);
   internal readonly Dictionary<string,XmlElement> Paragraphs=new Dictionary<string,XmlElement>(StringComparer.Ordinal);
   internal readonly Dictionary<string,string> Resolved=new Dictionary<string,string>(StringComparer.Ordinal);
  }
  readonly Model model;
  readonly Dictionary<string,Plain> eligible=new Dictionary<string,Plain>(StringComparer.Ordinal);
  readonly Dictionary<string,List<Plain>> signatures=new Dictionary<string,List<Plain>>(StringComparer.Ordinal);
  internal HwpParagraphProofs(string fullXml){
   model=Read(fullXml);
   foreach(var pair in model.Paragraphs){
    Plain p;try{p=Describe(model,pair.Key,pair.Value,true);}catch(InvalidOperationException){continue;}
    eligible.Add(pair.Key,p);List<Plain> group;if(!signatures.TryGetValue(p.Signature,out group)){group=new List<Plain>();signatures.Add(p.Signature,group);}group.Add(p);
   }
  }
  internal OnDemandExpectedTarget CreateExpected(string xmlPath){
   Plain p;if(xmlPath==null||!eligible.TryGetValue(xmlPath,out p))throw new InvalidOperationException("대상 문단의 원문·서식·구조를 증명하지 못했습니다.");
   return new OnDemandExpectedTarget{XmlPath=p.Path,Text=p.Text,ContainerToken=p.Container,StyleToken=p.Style,StructureToken=p.Structure};
  }
  internal HwpSelectedParagraphProof MatchSelected(string saveblockXml){
   var selected=Read(saveblockXml);
   // Section/column controls can contain master-page paragraphs. They are not
   // selected body paragraphs; Index deliberately does not traverse them.
   // Nested tables and other actual objects still fail Describe below.
   if(selected.Paragraphs.Count!=1)throw new InvalidOperationException("선택 영역이 한 문단이 아닙니다.");
   var pair=selected.Paragraphs.Single();var p=Describe(selected,pair.Key,pair.Value,false);List<Plain> matches;
   if(!signatures.TryGetValue(p.Signature,out matches))return new HwpSelectedParagraphProof{Unique=false,MatchCount=0,Text=p.Text};
   if(matches.Count!=1)return new HwpSelectedParagraphProof{Unique=false,MatchCount=matches.Count,Text=p.Text};
   var original=matches[0];return new HwpSelectedParagraphProof{Unique=true,MatchCount=1,XmlPath=original.Path,Text=original.Text,ContainerToken=original.Container,StyleToken=original.Style,StructureToken=original.Structure};
  }
  internal bool MatchesExpected(string xmlPath,string saveblockXml){
   Plain expected;if(xmlPath==null||!eligible.TryGetValue(xmlPath,out expected))return false;
   var selected=Read(saveblockXml);if(selected.Paragraphs.Count!=1)return false;
   var pair=selected.Paragraphs.Single();var observed=Describe(selected,pair.Key,pair.Value,false);
   // This checks text/style/structure only; it cannot prove which duplicate
   // paragraph or cell the native selection belongs to.
   return expected.Signature==observed.Signature;
  }
  // A native InsertText + restore-one-CharShape operation can preserve one
  // effective character style only. Numeric shape IDs are not that style:
  // HWP may keep several identical definitions. This permits those duplicate
  // IDs without permitting a bold/size/font/color difference to be flattened.
  internal static bool HasUniformSelectedStyle(string saveblockXml){
   try{var selected=Read(saveblockXml);if(selected.Paragraphs.Count!=1)return false;var p=selected.Paragraphs.Single();Describe(selected,p.Key,p.Value,false);return true;}
   catch(InvalidOperationException){return false;}catch(XmlException){return false;}
  }
  // Verification-only mutation of a private XML copy. Canonicalize duplicate
  // CharShape references to one existing semantic-equivalent ID; never change
  // definitions, live HWP, genuine mixed formatting, or other run attributes.
  // Whole-document digest comparison still protects every definition record.
  internal static void NormalizeEquivalentCharShapeIds(XmlElement paragraph){
   if(paragraph==null||paragraph.OwnerDocument==null)throw new ArgumentNullException("paragraph");
   CreateEquivalentCharShapeNormalizer(paragraph.OwnerDocument)(paragraph);
  }
  // A multi-target digest uses one mapping index/cache, rather than reparsing
  // the entire HWPML document once for every edited paragraph.
  internal static Action<XmlElement> CreateEquivalentCharShapeNormalizer(XmlDocument document){
   if(document==null)throw new ArgumentNullException("document");var m=Read(document.OuterXml);
   return paragraph=>{if(paragraph==null||paragraph.OwnerDocument!=document)throw new InvalidOperationException("정규화 문단의 문서가 다릅니다.");NormalizeEquivalentCharShapeIds(m,paragraph);};
  }
  static void NormalizeEquivalentCharShapeIds(Model m,XmlElement paragraph){
   var runs=Children(paragraph).Where(e=>e.LocalName=="TEXT"&&Children(e).Any(c=>c.LocalName=="CHAR")).ToArray();
   if(runs.Length==0)return;var attr=runs[0].GetAttributeNode("CharShape");
   // Existing uniform-ID verification remains available for minimal HWPML
   // fixtures without a mapping table. No ID equivalence is inferred there.
   if(attr==null||!m.Definitions.ContainsKey("CHARSHAPE:"+attr.Value))return;
   Describe(m,"verification-copy",paragraph,false);
   string semantic=Resolve(m,"CHARSHAPE",attr.Value,new List<string>()),canonical=attr.Value;
   foreach(var pair in m.Definitions){
    if(!pair.Key.StartsWith("CHARSHAPE:",StringComparison.Ordinal))continue;string id=pair.Key.Substring("CHARSHAPE:".Length);
    if(StringComparer.Ordinal.Compare(id,canonical)<0&&Resolve(m,"CHARSHAPE",id,new List<string>())==semantic)canonical=id;
   }
   foreach(var run in runs)run.SetAttribute("CharShape",canonical);
  }
  static IEnumerable<XmlElement> Children(XmlNode n){return n.ChildNodes.Cast<XmlNode>().OfType<XmlElement>();}
  static string Hash(string value){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","");}
  static void Put(StringBuilder b,string value){value=value??"";b.Append(value.Length).Append(':').Append(value);}
  static Model Read(string xml){
   if(String.IsNullOrWhiteSpace(xml)||xml.Length>MaxXml)throw new InvalidOperationException("문서 XML 크기가 올바르지 않습니다.");
   if(xml[0]=='\uFEFF')xml=xml.Substring(1);
   var doc=new XmlDocument{XmlResolver=null,PreserveWhitespace=true};
   using(var input=new StringReader(xml))using(var r=XmlReader.Create(input,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=MaxXml}))doc.Load(r);
   if(doc.DocumentElement==null||doc.DocumentElement.LocalName!="HWPML")throw new InvalidOperationException("HWPML 문서가 아닙니다.");
   var pending=new Stack<KeyValuePair<XmlElement,int>>();pending.Push(new KeyValuePair<XmlElement,int>(doc.DocumentElement,0));
   while(pending.Count>0){var next=pending.Pop();if(next.Value>128)throw new InvalidOperationException("XML 구조가 너무 깊습니다.");foreach(var child in Children(next.Key))pending.Push(new KeyValuePair<XmlElement,int>(child,next.Value+1));}
   var body=Children(doc.DocumentElement).Where(e=>e.LocalName=="BODY").ToArray();if(body.Length!=1)throw new InvalidOperationException("문서 본문을 확인하지 못했습니다.");
   var m=new Model{Document=doc,Body=body[0]};
   var heads=Children(doc.DocumentElement).Where(e=>e.LocalName=="HEAD").ToArray();
   if(heads.Length>1)throw new InvalidOperationException("문서 머리 정보가 중복됩니다.");
   if(heads.Length==1){var mappings=Children(heads[0]).Where(e=>e.LocalName=="MAPPINGTABLE").ToArray();if(mappings.Length>1)throw new InvalidOperationException("문서 서식 목록이 중복됩니다.");
    if(mappings.Length==1)foreach(var list in Children(mappings[0]))foreach(var def in Children(list)){
     if(def.LocalName=="FONTFACE")foreach(var font in Children(def).Where(e=>e.LocalName=="FONT"))Add(m.Fonts,def.GetAttribute("Lang")+":"+font.GetAttribute("Id"),font);
     else if(def.HasAttribute("Id"))Add(m.Definitions,def.LocalName+":"+def.GetAttribute("Id"),def);
    }
   }
   Index(m,m.Body,"BODY");return m;
  }
  static void Add(Dictionary<string,XmlElement> map,string key,XmlElement value){if(map.ContainsKey(key))throw new InvalidOperationException("서식 ID가 중복됩니다.");map.Add(key,value);}
  static void Index(Model m,XmlElement e,string path){
   if(e.LocalName=="SECDEF"||e.LocalName=="COLDEF")return;
   if(e.LocalName=="P"){if(m.Paragraphs.Count>=MaxParagraphs)throw new InvalidOperationException("문단 수가 너무 많습니다.");m.Paragraphs.Add(path,e);}
   var counts=new Dictionary<string,int>(StringComparer.Ordinal);foreach(var c in Children(e)){int n;counts.TryGetValue(c.LocalName,out n);counts[c.LocalName]=n+1;Index(m,c,path+"/"+c.LocalName+"["+n+"]");}
  }
  static string RefType(string name){switch(name){case "CharShape":return "CHARSHAPE";case "ParaShape":return "PARASHAPE";case "Style":case "NextStyle":return "STYLE";case "BorderFill":case "BorderFillId":case "BorderFillID":return "BORDERFILL";case "TabDef":return "TABDEF";default:return null;}}
  static string Resolve(Model m,string type,string id,List<string> stack){
   string key=type+":"+id;int at=stack.IndexOf(key);if(at>=0)return "@cycle:"+(stack.Count-at)+":"+type;
   bool root=stack.Count==0;string cached;if(root&&m.Resolved.TryGetValue(key,out cached))return cached;
   XmlElement def;if(!m.Definitions.TryGetValue(key,out def))throw new InvalidOperationException("참조한 문서 서식 정의를 찾지 못했습니다.");
   stack.Add(key);try{string result=Canonical(m,def,true,stack);if(root)m.Resolved.Add(key,result);return result;}finally{stack.RemoveAt(stack.Count-1);}
  }
  static string Attributes(Model m,XmlElement element,bool omitId,bool omitStyles,List<string> stack){
   var attrs=new SortedDictionary<string,string>(StringComparer.Ordinal);
   foreach(XmlAttribute a in element.Attributes){
    if(a.NamespaceURI=="http://www.w3.org/2000/xmlns/"||(omitId&&a.LocalName=="Id")||(element.LocalName=="P"&&a.LocalName=="InstId"))continue;
    // Native HWPML2X saveblock makes its first paragraph explicit about the
    // default no-break flags even when the full-document P omitted them.
    // Ignore only those exact false defaults; true breaks remain significant.
    if(element.LocalName=="P"&&a.NamespaceURI.Length==0&&(a.LocalName=="PageBreak"||a.LocalName=="ColumnBreak")&&a.Value=="false")continue;
    string type=RefType(a.LocalName);if(omitStyles&&type!=null)continue;string value=a.Value;
    if(element.LocalName=="FONTID"){
     XmlElement font;if(!m.Fonts.TryGetValue(a.LocalName+":"+a.Value,out font))throw new InvalidOperationException("글꼴 정의를 찾지 못했습니다.");value=Canonical(m,font,true,stack);
    }else if(type!=null)value=Resolve(m,type,a.Value,stack);
    attrs.Add(a.NamespaceURI+"|"+a.LocalName,value);
   }
   var b=new StringBuilder();foreach(var a in attrs){Put(b,a.Key);Put(b,a.Value);}return b.ToString();
  }
  static string Canonical(Model m,XmlElement e,bool omitId,List<string> stack){
   if(stack.Count>64)throw new InvalidOperationException("서식 참조가 너무 깊습니다.");var b=new StringBuilder();Put(b,e.NamespaceURI);Put(b,e.LocalName);Put(b,Attributes(m,e,omitId,false,stack));
   foreach(XmlNode child in e.ChildNodes){var c=child as XmlElement;if(c!=null){Put(b,Canonical(m,c,false,stack));continue;}
    if(child.NodeType==XmlNodeType.Comment)continue;if(child.NodeType==XmlNodeType.Text||child.NodeType==XmlNodeType.CDATA||child.NodeType==XmlNodeType.Whitespace||child.NodeType==XmlNodeType.SignificantWhitespace){if(!String.IsNullOrWhiteSpace(child.Value))Put(b,child.Value);}else throw new InvalidOperationException("서식의 추가 XML 데이터를 확인하지 못했습니다.");
   }return b.ToString();
  }
  static Plain Describe(Model m,string path,XmlElement paragraph,bool container){
   if(paragraph.ChildNodes.Cast<XmlNode>().Any(n=>!(n is XmlElement)&&!String.IsNullOrWhiteSpace(n.Value)))throw new InvalidOperationException("문단 실행 밖의 데이터입니다.");
   var runs=Children(paragraph).ToArray();if(runs.Length==0||runs.Any(e=>e.LocalName!="TEXT"))throw new InvalidOperationException("개체가 포함된 문단입니다.");
   string attrs=null,charAttrs=null;XmlElement firstContentRun=null;bool seenText=false;var text=new StringBuilder();var style=new StringBuilder();Put(style,Attributes(m,paragraph,false,false,new List<string>()));
   foreach(var run in runs){
    if(run.NamespaceURI!=paragraph.NamespaceURI)throw new InvalidOperationException("문단 요소의 XML 이름 공간이 일치하지 않습니다.");
    var children=Children(run).ToArray();var chars=new List<XmlElement>();
    foreach(var child in children){
     if(child.NamespaceURI!=paragraph.NamespaceURI)throw new InvalidOperationException("문단 요소의 XML 이름 공간이 일치하지 않습니다.");
     if(child.LocalName=="SECDEF"||child.LocalName=="COLDEF"){
      // HWP emits these before visible text, including inside a saveblock.
      // Their layout content is protected by the full-document revision, not
      // by this text-selection proof, whose native range excludes controls.
      if(seenText)throw new InvalidOperationException("본문 뒤의 구역·단 설정입니다.");continue;
     }
     if(child.LocalName!="CHAR"||Children(child).Any())throw new InvalidOperationException("개체가 포함된 문단입니다.");
     chars.Add(child);if(child.InnerText.Length>0)seenText=true;
    }
    if(run.ChildNodes.Cast<XmlNode>().Any(n=>!(n is XmlElement)&&!String.IsNullOrWhiteSpace(n.Value)))throw new InvalidOperationException("텍스트 실행 밖의 데이터입니다.");
    // Metadata-only runs do not contribute a visible character style.
    if(chars.Count==0){if(children.Length==0)throw new InvalidOperationException("빈 텍스트 실행입니다.");continue;}
    if(firstContentRun==null)firstContentRun=run;
    string current=Attributes(m,run,false,false,new List<string>());if(attrs==null)attrs=current;else if(attrs!=current)throw new InvalidOperationException("혼합 글자 서식 문단입니다.");
    foreach(var c in chars){string ca=Attributes(m,c,false,false,new List<string>());if(charAttrs==null)charAttrs=ca;else if(charAttrs!=ca)throw new InvalidOperationException("문자 속성이 혼합됐습니다.");
     if(c.ChildNodes.Cast<XmlNode>().Any(n=>n.NodeType!=XmlNodeType.Text&&n.NodeType!=XmlNodeType.CDATA&&n.NodeType!=XmlNodeType.Whitespace&&n.NodeType!=XmlNodeType.SignificantWhitespace))throw new InvalidOperationException("문자 내부 제어를 확인하지 못했습니다.");text.Append(c.InnerText);
    }
   }
   if(text.Length==0||text.Length>12000||text.ToString().Any(c=>Char.IsControl(c)||Char.IsSurrogate(c)||c=='\u2028'||c=='\u2029'||c=='\uFFFC'))throw new InvalidOperationException("지원되지 않는 문단 텍스트입니다.");
   Put(style,attrs);Put(style,charAttrs);string st=Hash(style.ToString());
   var structure=new StringBuilder();Put(structure,paragraph.NamespaceURI);Put(structure,paragraph.LocalName);Put(structure,Attributes(m,paragraph,false,true,new List<string>()));Put(structure,"TEXT+CHAR");Put(structure,Attributes(m,firstContentRun,false,true,new List<string>()));Put(structure,charAttrs);string shape=Hash(structure.ToString());
   var identity=new StringBuilder();Put(identity,text.ToString());Put(identity,st);Put(identity,shape);
   return new Plain{Path=path,Text=text.ToString(),Style=st,Structure=shape,Signature=Hash(identity.ToString()),Container=container?Container(m,path,paragraph):null};
  }
  static string Container(Model m,string path,XmlElement p){
   var b=new StringBuilder();Put(b,path);var ancestors=new List<XmlElement>();for(var n=p.ParentNode as XmlElement;n!=null&&n!=m.Body;n=n.ParentNode as XmlElement)ancestors.Add(n);ancestors.Reverse();
   foreach(var e in ancestors){Put(b,e.LocalName);Put(b,Attributes(m,e,false,false,new List<string>()));}
   return Hash(b.ToString());
  }
 }
}
