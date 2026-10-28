using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HAddon {
 // The caller must separately pin the selection and verify both endpoint
 // page numbers. The model receives this reference text as untrusted data;
 // only the pinned selection is ever eligible for native writes.
 internal static class SelectedPageEditContext {
  internal static object[] Build(string[] pages,PageScope scope,int selectedPage,string selectedText){
   if(pages==null||scope==null||scope.Targets==null||scope.Sources==null||selectedPage<1||selectedPage>pages.Length||!scope.Targets.Contains(selectedPage))throw new InvalidOperationException("선택 문단이 요청한 수정 쪽에 있는지 확인하지 못했습니다.");
   if(String.IsNullOrEmpty(selectedText)||pages[selectedPage-1]==null||pages[selectedPage-1].IndexOf(selectedText,StringComparison.Ordinal)<0)throw new InvalidOperationException("선택 문단과 해당 쪽 내용을 대조하지 못했습니다.");
   var output=new List<object>();var seen=new HashSet<int>();int bytes=0;bool anyText=false;
   foreach(int p in scope.Sources){
    if(p<1||p>pages.Length||!seen.Add(p)||pages[p-1]==null)throw new InvalidOperationException("참조할 쪽 내용을 확인하지 못했습니다.");
    string value=pages[p-1];bytes+=Encoding.UTF8.GetByteCount(value);if(bytes>250000)throw new InvalidOperationException("참조 쪽이 너무 커 한 번에 처리할 수 없습니다.");
    if(!String.IsNullOrWhiteSpace(value))anyText=true;
    output.Add(new Dictionary<string,object>{{"number",p},{"text",value}});
   }
   if(output.Count==0||!anyText)throw new InvalidOperationException("참조할 쪽의 텍스트를 읽지 못했습니다.");
   return output.ToArray();
  }
 }
}
