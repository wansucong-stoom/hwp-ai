using System;
using System.Globalization;
using Hwp.Addon;

namespace HAddon {
 // Adapter for ResponsivePageReader. It reads only and does not prove page
 // completeness or authorize a write. Call exclusively on the HWP UI thread.
 internal sealed class NativeResponsivePageSource:IResponsivePageSource {
  readonly Func<IHwpObject> getDocument;readonly int documentId;readonly string path,identity;
  internal NativeResponsivePageSource(Func<IHwpObject> getDocument){
   if(getDocument==null)throw new ArgumentNullException("getDocument");this.getDocument=getDocument;
   var doc=getDocument();if(doc==null)throw new InvalidOperationException("열린 한글 문서를 확인하지 못했습니다.");
   documentId=doc.GetID();path=doc.GetPath()??"";identity=documentId.ToString(CultureInfo.InvariantCulture)+"\0"+path;
  }
  IHwpObject Current(){var doc=getDocument();if(doc==null||doc.GetID()!=documentId||(doc.GetPath()??"")!=path)throw new InvalidOperationException("쪽 내용을 읽는 동안 문서가 바뀌었습니다.");return doc;}
  public string Identity(){Current();return identity;}
  public string ReadXml(){var doc=Current();string result=doc.GetTextFile("HWPML2X","");Current();return result;}
  public long PageCount(){var doc=Current();long result=doc.GetPageCount();Current();return result;}
  public string ReadPage(int page,int option){
   if(page<0||(option!=1&&option!=7))throw new InvalidOperationException("쪽 조회 인수가 올바르지 않습니다.");
   var doc=Current();string result=doc.GetPageText(page,option);Current();return result;
  }
 }
}
