using System;
using System.Collections.Generic;
using System.Text;
using HAddon;

internal static class RequestIntentPolicyTests {
 sealed class Case {
  public string Name, Prompt, Expected; public bool Selection; public int Mode;
  public Case(string name,string prompt,string expected,bool selection=false,int mode=0) { Name=name;Prompt=prompt;Expected=expected;Selection=selection;Mode=mode; }
 }
 static int Main() {
  Console.OutputEncoding=new UTF8Encoding(false);
  var cases=new List<Case> {
   new Case("ordinary Korean question", "이 기능은 뭐야?", "generate"),
   new Case("typesetting method question", "한글에서 조판하는 방법 알려줘", "generate"),
   new Case("explicit chat-only instruction", "본문에 넣지 말고 설명만 해줘", "generate"),
   new Case("quoted command meaning question", "조판해줘 라고 하면 무슨 뜻이야?", "generate"),
   new Case("principle question", "보고서를 작성하는 원리가 궁금해", "generate"),
   new Case("answer only", "문서를 만들어줄 수 있는지 답변만 해줘", "generate"),
   new Case("chat only", "한글 본문에 넣어 달라는 게 아니라 채팅으로만 보여줘", "generate"),
   new Case("explicit do-not-insert", "문서에 삽입하지 말고 초안을 보여줘", "generate"),
   new Case("explicit do-not-reflect", "본문에 반영하지 마. 대화로만 보여줘", "generate"),
   new Case("how question before document insertion keyword", "문서에 어떻게 넣어?", "generate"),
   new Case("how to insert rather than insert now", "문서에 이미지를 넣으려면 어떻게 해야 해?", "generate"),
   new Case("quoted command translation question", "“조판해줘”라는 문장을 영어로 어떻게 번역해?", "generate"),
   new Case("explain typesetting", "조판에 대해 설명해줘", "generate"),
   new Case("write procedure question", "보고서 작성 절차를 정리해줘", "generate"),
   new Case("insertion steps question", "본문에 표를 삽입하는 단계를 알려줘", "generate"),
   new Case("ASCII question mark defaults to conversation", "보고서 작성해줘?", "generate"),
   new Case("fullwidth question mark defaults to conversation", "디자인을 넣어 조판해줘？", "generate"),
   new Case("direct report writing", "보고서 작성해줘", "compose"),
   new Case("direct spaced report writing", "보고서를 작성해 줘", "compose"),
   new Case("direct typesetting", "디자인 넣어서 조판해줘", "compose"),
   new Case("polite table and infographic request", "표와 인포그래픽으로도 정리해줄래?", "compose"),
   new Case("polite table request with question mark", "표로 정리해줘?", "compose"),
   new Case("table method remains question", "표를 생성하는 방법 알려줘", "generate"),
   new Case("table availability remains question", "표를 만들 수 있나요?", "generate"),
   new Case("table typesetting terse request", "표로 정리해서 다시 조판", "compose"),
   new Case("infographic insertion request", "인포 이미지를 본문에 넣어줘", "compose"),
   new Case("direct body insertion", "아래 내용을 본문에 넣어 줘", "compose"),
   new Case("direct document insertion", "초안을 문서에 삽입해 주세요", "compose"),
   new Case("short writing request", "회의 결과 보고서 작성", "compose"),
   new Case("short typesetting request", "안내문 조판", "compose"),
   new Case("English composition command", "Compose a project report", "compose"),
   new Case("question mode overrides direct write", "보고서 작성해줘", "generate",false,1),
   new Case("question mode overrides selected text", "선택한 문장을 수정해줘", "generate",true,1),
   new Case("body mode explicitly overrides question", "조판 방법을 설명만 해줘", "compose",false,2),
   new Case("body mode explicitly overrides selected text", "선택한 문장을 다듬어줘", "compose",true,2),
   new Case("automatic selected text editing", "선택한 문장을 간결하게 다듬어줘", "edit",true),
   new Case("selection precedes automatic write intent", "보고서 작성해줘", "edit",true),
   new Case("selection precedes automatic question intent", "이 문장은 무슨 뜻이야?", "edit",true),
   new Case("null input stays conversation", null, "generate"),
   new Case("whitespace input stays conversation", " \r\n ", "generate"),
   new Case("explanation content is conservatively chat", "AI 원리를 설명하는 보고서를 작성해줘", "generate"),
   new Case("method content is conservatively chat", "사용 방법 안내문을 작성해줘", "generate"),
   // Questions do not always contain punctuation or the literal word '방법'.
   new Case("capability question without question mark", "문서에 그림을 넣을 수 있나요", "generate"),
   new Case("formal capability question without question mark", "본문에 내용을 반영할 수 있습니까", "generate"),
   new Case("quoted command grammar question without question mark", "보고서 작성해줘라는 표현이 자연스러운가요", "generate"),
   new Case("quoted insertion meaning without known guard word", "본문에 넣는다는 게 무슨 의미인지 궁금해", "generate"),
   new Case("explicit insertion not needed", "본문에 삽입할 필요는 없어. 초안만 보여줘", "generate"),
   new Case("explicit refusal of reflection", "본문에 반영은 원하지 않아", "generate")
  };
  int passed=0,failed=0;
  foreach(var item in cases) {
   string actual=RequestIntent.Choose(item.Prompt,item.Selection,item.Mode);
   if(actual==item.Expected){passed++;Console.WriteLine("ok - "+item.Name);}
   else {failed++;Console.WriteLine("FAIL - "+item.Name+"; expected="+item.Expected+" actual="+actual+"; selected="+item.Selection+" mode="+item.Mode+"; prompt="+item.Prompt);}
  }
  Console.WriteLine("RequestIntent policy: "+passed+" passed, "+failed+" failed, "+cases.Count+" total. Pure C# only; no HWP UI or documents accessed.");
  return failed==0?0:1;
 }
}
