using System;
using System.Collections.Generic;

namespace HAddon {
 // This class validates the model's intent metadata. It never reads or changes a document.
 internal sealed class ConversationIntent {
  internal readonly string Kind;
  internal readonly string TableReference;
  internal readonly bool NeedsTable, NeedsImage, UsePreviousAnswer, UseSelectionInQuestion, NeedsDocumentContext;

  ConversationIntent(string kind, bool table, bool image, bool previous, bool selected, bool context,string tableReference) {
   Kind=kind; NeedsTable=table; NeedsImage=image; UsePreviousAnswer=previous; UseSelectionInQuestion=selected;NeedsDocumentContext=context;TableReference=tableReference;
  }

  internal static ConversationIntent Parse(Dictionary<string,object> data,bool hasSelection,bool hasPreviousAnswer,bool hasTableCursor=false,bool hasTables=false) {
   if(data==null)throw new InvalidOperationException("요청 분류 응답을 확인하지 못했습니다.");
   string[] keys={"intent","needsTable","needsImage","usePreviousAnswer","useSelectionInQuestion","needsDocumentContext","tableReference","model"};
   if(data.Count!=keys.Length)throw new InvalidOperationException("요청 분류 응답의 항목 수가 올바르지 않습니다.");
   foreach(string key in keys)if(!data.ContainsKey(key))throw new InvalidOperationException("요청 분류 응답에 필요한 항목이 없습니다.");
   object rawIntent,rawModel,rawTable,rawImage,rawPrevious,rawSelected,rawContext;
   data.TryGetValue("intent",out rawIntent); data.TryGetValue("model",out rawModel);
   data.TryGetValue("needsTable",out rawTable); data.TryGetValue("needsImage",out rawImage);
   data.TryGetValue("usePreviousAnswer",out rawPrevious); data.TryGetValue("useSelectionInQuestion",out rawSelected);
   data.TryGetValue("needsDocumentContext",out rawContext);
   string kind=rawIntent as string,model=rawModel as string,reference=data["tableReference"] as string;
   if(kind!="question"&&kind!="edit"&&kind!="compose"&&kind!="table_style")throw new InvalidOperationException("요청 분류 결과가 올바르지 않습니다.");
   if(reference!="none"&&reference!="caret"&&reference!="first")throw new InvalidOperationException("기준 표의 지정 방식이 올바르지 않습니다.");
   if(kind=="table_style"){
    if(reference=="none")throw new InvalidOperationException("표 서식을 적용할 기준 표가 지정되지 않았습니다.");
    if(reference=="first"&&!hasTables)throw new InvalidOperationException("문서의 첫 번째 본문 표를 찾지 못했습니다.");
    if(reference=="caret"&&!hasTableCursor)throw new InvalidOperationException("기준 표 안에 커서를 두거나 셀을 선택해 다시 요청하세요.");
   }else if(reference!="none")throw new InvalidOperationException("표 서식 작업 이외의 요청에 기준 표가 지정되었습니다.");
   if(String.IsNullOrWhiteSpace(model)||model.Length>128)throw new InvalidOperationException("요청 분류에 사용한 모델을 확인하지 못했습니다.");
   foreach(char c in model)if(Char.IsControl(c))throw new InvalidOperationException("요청 분류에 사용한 모델 형식이 올바르지 않습니다.");
   if(!(rawTable is bool)||!(rawImage is bool)||!(rawPrevious is bool)||!(rawSelected is bool)||!(rawContext is bool))
    throw new InvalidOperationException("요청 분류의 작업 옵션을 확인하지 못했습니다.");

   if(kind=="edit"&&!hasSelection)kind="compose";
   bool document=kind!="question";
   return new ConversationIntent(
    kind, document&&(bool)rawTable, document&&(bool)rawImage,
    hasPreviousAnswer&&(bool)rawPrevious, kind=="question"&&hasSelection&&(bool)rawSelected,(bool)rawContext,reference);
  }
 }
}
