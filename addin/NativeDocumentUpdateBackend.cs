using System;
using System.Collections.Generic;
using Hwp.Addon;

namespace HAddon {
 // Uses independently proven native paragraph addresses. Candidate text
 // matches alone cannot identify a target. Every planned prefix is checked
 // against the complete document, including untouched content and formats.
 internal sealed class NativeDocumentUpdateBackend:IDocumentUpdateBackend {
  readonly Func<IHwpObject> getDocument;readonly int documentId,list,paragraph,mode,sl,sp,el,ep;int position,so,eo;readonly string path,revision;
  readonly Dictionary<string,DocumentAddressCandidate> addresses;
  readonly Dictionary<string,NativeEditor.Snapshot> prepared=new Dictionary<string,NativeEditor.Snapshot>(StringComparer.Ordinal);
  readonly DocumentUpdateVerification verification;
  internal NativeDocumentUpdateBackend(Func<IHwpObject> getDocument,NativePageSnapshot snapshot,string originalXml,DocumentAddressCandidate[] targets){
   if(getDocument==null||snapshot==null||targets==null||targets.Length==0)throw new ArgumentNullException("snapshot");
   this.getDocument=getDocument;documentId=snapshot.DocumentId;path=snapshot.Path;revision=snapshot.Digest;
   if(TableStableSnapshot.Digest(originalXml)!=revision)throw new InvalidOperationException("원래 문서 상태가 일치하지 않습니다.");
   var doc=Current();if(!doc.GetPos(ref list,ref paragraph,ref position))throw new InvalidOperationException("원래 커서를 확인하지 못했습니다.");
   mode=doc.GetSelectionMode();if((mode&~31)!=0||((mode&15)!=0&&(mode&15)!=1))throw new InvalidOperationException("현재 선택 상태를 복원할 수 없습니다.");
   if((mode&15)==1&&(!doc.GetSelectedPos(ref sl,ref sp,ref so,ref el,ref ep,ref eo)||sl!=el))throw new InvalidOperationException("현재 선택을 복원할 수 없습니다.");
   addresses=new Dictionary<string,DocumentAddressCandidate>(StringComparer.Ordinal);var paths=new Dictionary<string,string>(StringComparer.Ordinal);
   var nativePositions=new HashSet<string>(StringComparer.Ordinal);
   foreach(var target in targets){
    if(target==null||String.IsNullOrEmpty(target.XmlPath)||addresses.ContainsKey(target.XmlPath)||target.List<0||target.Paragraph<0||target.Start<0||target.End<=target.Start||!nativePositions.Add(target.List+":"+target.Paragraph))throw new InvalidOperationException("중복되거나 잘못된 문단 주소입니다.");
    // The request or the mapper may retain the original object. Freeze every
    // field so asynchronous model work cannot change the target after capture.
    var copy=new DocumentAddressCandidate{XmlPath=target.XmlPath,Text=target.Text,Page=target.Page,List=target.List,Paragraph=target.Paragraph,Start=target.Start,End=target.End};
    addresses.Add(copy.XmlPath,copy);paths.Add(copy.XmlPath,copy.XmlPath);
   }
   verification=new DocumentUpdateVerification(originalXml,paths);
  }
  IHwpObject Current(){var doc=getDocument();if(doc==null||doc.IsReadOnlyMode()||doc.GetID()!=documentId||(doc.GetPath()??"")!=path)throw new InvalidOperationException("원래 문서가 변경되었습니다.");return doc;}
  void CheckCaret(IHwpObject doc){
   int l=0,p=0,x=0;if(!doc.GetPos(ref l,ref p,ref x)||l!=list||p!=paragraph||x!=position||doc.GetSelectionMode()!=mode)throw new InvalidOperationException("작업 중 커서나 선택이 바뀌었습니다.");
   if((mode&15)==1){int a=0,b=0,c=0,d=0,e=0,f=0;if(!doc.GetSelectedPos(ref a,ref b,ref c,ref d,ref e,ref f)||a!=sl||b!=sp||c!=so||d!=el||e!=ep||f!=eo)throw new InvalidOperationException("작업 중 선택이 바뀌었습니다.");}
  }
  internal static bool OwnsRange(DocumentAddressCandidate address,int end,int currentMode,int a,int b,int c,int d,int e,int f){
   if(address==null||(currentMode&15)!=1||a!=address.List||d!=address.List||b!=address.Paragraph||e!=address.Paragraph)return false;
   return Math.Min(c,f)==address.Start&&Math.Max(c,f)==end;
  }
  internal static int RemapPosition(int list,int paragraph,int offset,DocumentAddressCandidate target,int replacementLength){
   if(target==null||list!=target.List||paragraph!=target.Paragraph||offset<=target.Start)return offset;
   return offset>=target.End?offset+replacementLength-(target.End-target.Start):target.Start+Math.Min(offset-target.Start,replacementLength);
  }
  void RemapOriginalLocation(DocumentAddressCandidate target,int replacementLength){
   position=RemapPosition(list,paragraph,position,target,replacementLength);
   if((mode&15)==1){so=RemapPosition(sl,sp,so,target,replacementLength);eo=RemapPosition(el,ep,eo,target,replacementLength);}
  }
  // Native calls can re-enter the host message loop. If a user selected or
  // moved elsewhere, never overwrite their location while cleaning up.
  void RestoreOwned(DocumentAddressCandidate address,string replacement){
   var doc=Current();int a=0,b=0,c=0,d=0,e=0,f=0;
   if(!doc.GetSelectedPos(ref a,ref b,ref c,ref d,ref e,ref f)||
      (!OwnsRange(address,address.End,doc.GetSelectionMode(),a,b,c,d,e,f)&&
       (replacement==null||!OwnsRange(address,address.Start+replacement.Length,doc.GetSelectionMode(),a,b,c,d,e,f))))
    throw new InvalidOperationException("작업 중 선택 또는 커서가 달라져 사용자의 위치를 복원 명령으로 덮지 않았습니다.");
   if((mode&15)==1){if(!doc.SetPos(sl,sp,so)||!doc.SelectText(sp,so,ep,eo))throw new InvalidOperationException("원래 선택을 복원하지 못했습니다.");}else if(!doc.SetPos(list,paragraph,position))throw new InvalidOperationException("원래 커서를 복원하지 못했습니다.");CheckCaret(doc);
  }
  public void Prepare(string expected,DocumentUpdateTarget target){
   if(expected!=revision)throw new InvalidOperationException("문서 기준 상태가 다릅니다.");DocumentAddressCandidate address;
   if(!addresses.TryGetValue(target.Id,out address)||address.Text!=target.Original||prepared.ContainsKey(target.Id))throw new InvalidOperationException("편집할 원문과 문단 주소가 일치하지 않습니다.");
   // Verify that the original XML can express this replacement before the
   // first native mutation. This accepts equivalent text runs but deliberately
   // rejects mixed character shapes, embedded controls and objects.
   verification.ExpectedDigest(new[]{target},1);var doc=Current();CheckCaret(doc);
   try{
    if(!NativeEditor.SelectRange(doc,address.List,address.Paragraph,address.Start,address.End))throw new InvalidOperationException("편집할 한글 위치를 선택하지 못했습니다.");
    var captured=NativeEditor.Capture(doc);
    if(!captured.Supported||captured.Text!=target.Original||captured.ParagraphText!=target.Original||!captured.WholeParagraph)throw new InvalidOperationException("한글 문단 원문과 편집 위치가 일치하지 않습니다.");
    prepared.Add(target.Id,captured);
   }finally{RestoreOwned(address,null);}
  }
  public void Verify(string expected,IList<DocumentUpdateTarget> plan,int applied){
   if(expected!=revision)throw new InvalidOperationException("문서 기준 상태가 다릅니다.");var doc=Current();CheckCaret(doc);verification.Verify(doc.GetTextFile("HWPML2X",""),plan,applied);CheckCaret(doc);
  }
  public void Replace(DocumentUpdateTarget target){
   NativeEditor.Snapshot original;DocumentAddressCandidate address;if(!prepared.TryGetValue(target.Id,out original)||!addresses.TryGetValue(target.Id,out address))throw new InvalidOperationException("고정된 문단이 없습니다.");
   var doc=Current();CheckCaret(doc);bool rewritten=false;
   try{
    if(!NativeEditor.SelectRange(doc,address.List,address.Paragraph,address.Start,address.End))throw new InvalidOperationException("문단을 선택하지 못했습니다.");
    var editor=new NativeEditor(Current);try{
     editor.BeginRequest();var actual=editor.CurrentSnapshot;if(!NativeEditor.SameContentSnapshot(original,actual))throw new InvalidOperationException("적용할 원문이나 서식이 바뀌었습니다.");
     editor.Invoke("hwp_editor_rewrite",new Dictionary<string,object>{{"expectedTargetId",actual.TargetId},{"replacement",target.Replacement}});
     rewritten=true;
    }finally{editor.EndRequest();}
   }finally{if(rewritten)RemapOriginalLocation(address,target.Replacement.Length);RestoreOwned(address,target.Replacement);}
  }
 }
}
