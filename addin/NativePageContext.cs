using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Hwp.Addon;

namespace HAddon {
 // Read-only snapshot for a future page-aware workflow. A separate HWP 2024
 // COM test found that option 7 includes simple table text, while option 0 did
 // not. Add-on IHwpObject behavior, page numbering and complex object
 // completeness have not been verified; text is not an editable address.
 internal sealed class NativePageSnapshot {
  internal int DocumentId;internal string Path,Digest;internal string[] Pages,TablePages;internal DocumentParagraph[] Paragraphs;
 }
 internal static class NativePageContext {
  const int MaxPages=256,MaxChars=400000,MaxPageChars=30000;
  static string Digest(string value){if(String.IsNullOrEmpty(value))throw new InvalidOperationException("문서 구조를 읽지 못했습니다.");return TableStableSnapshot.Digest(value);}
  internal static NativePageSnapshot Read(Func<IHwpObject> getDocument){
   if(getDocument==null)throw new ArgumentNullException("getDocument");var doc=getDocument();if(doc==null)throw new InvalidOperationException("열린 한글 문서를 확인하지 못했습니다.");
   int id=doc.GetID();string path=doc.GetPath()??"",raw=doc.GetTextFile("HWPML2X",""),before=Digest(raw);long count=doc.GetPageCount();
   // Capture the structured text from exactly the same serialized revision as
   // the before digest. Structural paths are not native editable addresses.
   DocumentParagraph[] paragraphs=DocumentStructure.Read(raw);
   if(count<1||count>MaxPages)throw new InvalidOperationException("문서 쪽 수가 분석 한도를 벗어납니다.");
   var pages=new List<string>();var tablePages=new List<string>();int total=0,tableTotal=0;
   for(int i=0;i<count;i++){
    var current=getDocument();if(current==null||current.GetID()!=id||(current.GetPath()??"")!=path)throw new InvalidOperationException("쪽 내용을 읽는 동안 문서가 바뀌었습니다.");
    string value=doc.GetPageText(i,7),withTables=doc.GetPageText(i,1);
    if(value==null||withTables==null||value.Length>MaxPageChars||withTables.Length>MaxPageChars||(total+=value.Length)>MaxChars||(tableTotal+=withTables.Length)>MaxChars)throw new InvalidOperationException("쪽 내용을 빠짐없이 읽을 수 없습니다.");
    pages.Add(value);tablePages.Add(withTables);
   }
   // A nonempty HWP document can yield empty page reads when the native API
   // is unsupported or the wrong mode is used; never treat that as content.
   if(total==0&&tableTotal==0)throw new InvalidOperationException("한글에서 쪽별 텍스트를 반환하지 않았습니다.");
   var final=getDocument();if(final==null||final.GetID()!=id||(final.GetPath()??"")!=path||final.GetPageCount()!=count||Digest(final.GetTextFile("HWPML2X",""))!=before)throw new InvalidOperationException("쪽 내용을 읽는 동안 문서가 바뀌었습니다.");
   return new NativePageSnapshot{DocumentId=id,Path=path,Digest=before,Pages=pages.ToArray(),TablePages=tablePages.ToArray(),Paragraphs=paragraphs};
  }
 }
}
