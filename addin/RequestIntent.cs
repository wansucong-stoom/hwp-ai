using System;
using System.Text.RegularExpressions;
namespace HAddon {
 // Retired keyword router retained for historical reference. Production uses
 // ConversationIntent and the bridge semantic router; this file is not built.
 internal static class RequestIntent {
  internal static string Choose(string prompt,bool hasSelection,int mode) {
   if(mode==1)return "generate";
   // Default to the editing surface: selected text stays the target, otherwise
   // compose into the fixed body position. Automatic classification is optional.
   if(mode==0)return hasSelection?"edit":"compose";
   if(hasSelection)return "edit";
   string text=(prompt??"").Trim();
   bool explanation=Regex.IsMatch(text,@"어떻게|무슨\s*(?:뜻|의미)|뭐야|무엇|가능\s*여부|(?:할|될|넣을|만들)\s*수\s*(?:있|없)|(?:설명|답변)\s*만|대화\s*로만|채팅\s*으로만|(?:넣|삽입|작성|조판|반영|수정|만들|추가)[\s\S]{0,15}(?:말고|말아|않고|하지\s*마|원하지\s*않|필요\s*(?:는|가)?\s*없)",RegexOptions.IgnoreCase);
   if(WantsRich(text,"")&&!explanation&&Regex.IsMatch(text,@"정리|작성|조판|생성|구성|삽입|배치|제작|만들|넣|추가|디자인|꾸미",RegexOptions.IgnoreCase))return "compose";
   if(WantsRich(text,"")&&!Regex.IsMatch(text,@"방법|원리|설명|알려|가능|필요\s?없|원하지|하지\s?마|말고|말아")&&Regex.IsMatch(text,@"(?:정리|작성|조판|생성|구성|삽입|배치|제작|만들어|넣어)[\s\S]{0,24}(?:해\s?줄래|해\s?줘|해\s?주세요|주\s?세요|줄래|줘)[\s?？.!~ㅋㅎ]*$",RegexOptions.IgnoreCase))return "compose";
   // Explanations and explicit chat-only requests must never insert a document.
   if(Regex.IsMatch(text,@"(방법|원리|가능\s?여부|무슨\s?뜻|뭐야|무엇인가|어떻게|단계|절차|설명|알려|답변\s?만|대화\s?로만|채팅\s?으로만)|[?？]|(?:넣|삽입|작성|조판|반영|수정)[\s\S]{0,10}(?:말고|말아|않고|하지\s?마)",RegexOptions.IgnoreCase))return "generate";
   // Require a direct imperative ending. Merely mentioning insertion, or quoting
   // a write command inside a question, must not change an empty document.
   bool explicitWrite=Regex.IsMatch(text,@"(?:(?:작성|조판|삽입|배치|반영|꾸미기|디자인|적용)(?:을|를)?\s*(?:해\s?줘(?:요)?|해\s?라|해\s?주세요|해\s?주십시오|부탁(?:해(?:요)?|드려(?:요)?|드립니다)?|해달(?:라)?|하자)|(?:채워|만들어|넣어|꾸며)\s*(?:줘(?:요)?|주\s?세요|주십시오|달라))[\s.!。~ㅋㅎ]*$|^\s*(?:create|compose|write|insert|typeset|layout)\b",RegexOptions.IgnoreCase);
   if(explicitWrite)return "compose";
   if(Regex.IsMatch(text,@"(방법|원리|무엇|뭐야|왜|어떻게|가능여부)|[?？]",RegexOptions.IgnoreCase))return "generate";
   if(Regex.IsMatch(text,@"^[^?？]{0,80}(작성|조판|작성요청|조판요청)\s*$"))return "compose";
   return "generate";
  }
  internal static bool WantsRich(string prompt,string priorDraft){
   return Regex.IsMatch(prompt??"",@"인포(?:그래픽)?|infographic|테이블|(?<!목)표\s*(?:로|와|과|도|를|형태|생성|작성|만들|정리)|다이어그램|이미지|그림\s*(?:으로|을|생성|만들)",RegexOptions.IgnoreCase)||Regex.IsMatch(priorDraft??"",@"^\s*\|[^\r\n]+\|\s*$",RegexOptions.Multiline);
  }
 }
}
