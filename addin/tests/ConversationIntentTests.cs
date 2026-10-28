using System;
using System.Collections.Generic;
using HAddon;

internal static class ConversationIntentTests {
 static int checks;
 static void Assert(bool value,string label) { checks++;if(!value)throw new Exception(label); }
 static Dictionary<string,object> Result(string intent,bool table,bool image,bool previous,bool selected,string reference=null) {
  return new Dictionary<string,object>{
   {"intent",intent},{"needsTable",table},{"needsImage",image},
   {"usePreviousAnswer",previous},{"useSelectionInQuestion",selected},{"needsDocumentContext",false},{"tableReference",reference??(intent=="table_style"?"caret":"none")},{"model","gpt-6.1-terra"}
  };
 }
 static void Fails(Dictionary<string,object> raw,string label) {
  bool failed=false;
  try { ConversationIntent.Parse(raw,true,true); } catch(InvalidOperationException) { failed=true; }
  Assert(failed,label);
 }
 static void Main() {
  try {
   // The user's one-page infographic request must be a body composition.
   var plan=ConversationIntent.Parse(Result("compose",false,true,false,false),false,false);
   Assert(plan.Kind=="compose" && plan.NeedsImage && !plan.NeedsTable,"infographic composition");
   Assert(!plan.UsePreviousAnswer && !plan.UseSelectionInQuestion,"infographic has no prior source or selection");

   // Conversation can be followed by a document request without a mode switch.
   plan=ConversationIntent.Parse(Result("compose",true,false,true,false),false,true);
   Assert(plan.Kind=="compose" && plan.NeedsTable && plan.UsePreviousAnswer,"documentize prior answer with a real table");
   plan=ConversationIntent.Parse(Result("compose",true,false,true,false),false,false);
   Assert(!plan.UsePreviousAnswer,"nonexistent earlier answer cannot be requested");

   // A question cannot be transformed into a body operation by extraneous flags.
   plan=ConversationIntent.Parse(Result("question",true,true,true,true),true,true);
   Assert(plan.Kind=="question" && !plan.NeedsTable && !plan.NeedsImage,"question never creates objects");
   Assert(plan.UsePreviousAnswer && plan.UseSelectionInQuestion,"question can use real available context");
   plan=ConversationIntent.Parse(Result("question",false,false,false,true),false,true);
   Assert(!plan.UseSelectionInQuestion,"question cannot refer to a nonexistent selected excerpt");

   plan=ConversationIntent.Parse(Result("edit",true,false,false,true),true,false);
   Assert(plan.Kind=="edit" && plan.NeedsTable && !plan.UseSelectionInQuestion,"selected table edit stays pinned");
   plan=ConversationIntent.Parse(Result("edit",false,false,false,false),false,false);
   Assert(plan.Kind=="compose","no-selection edit becomes composition at captured caret");
   var crossPage=Result("compose",false,false,false,false);crossPage["needsDocumentContext"]=true;
   Assert(ConversationIntent.Parse(crossPage,false,false).NeedsDocumentContext,"cross-page source requirement remains explicit");
   var style=Result("table_style",false,false,false,false,"first");style["needsDocumentContext"]=true;
   plan=ConversationIntent.Parse(style,false,false,false,true);
   Assert(plan.Kind=="table_style"&&plan.TableReference=="first","first table reference does not require a caret in a table");
   plan=ConversationIntent.Parse(Result("table_style",false,false,false,false,"caret"),true,false,true,false);
   Assert(plan.Kind=="table_style"&&plan.TableReference=="caret","a pinned caret reference does not silently become the first table");
   bool unavailable=false;try{ConversationIntent.Parse(style,false,false,true,false);}catch(InvalidOperationException){unavailable=true;}Assert(unavailable,"first table reference needs an available native body table even with a table caret");

   Fails(null,"null response rejected");
   var bad=Result("delete",false,false,false,false);Fails(bad,"unknown intent cannot become an action");
   bad=Result("Question",false,false,false,false);Fails(bad,"intent enum is exact");
   bad=Result("question",false,false,false,false);bad["needsTable"]="false";Fails(bad,"string boolean rejected");
   bad=Result("question",false,false,false,false);bad["needsImage"]=0;Fails(bad,"numeric boolean rejected");
   bad=Result("question",false,false,false,false);bad["needsDocumentContext"]="true";Fails(bad,"document context flag must be boolean");
   bad=Result("question",false,false,false,false);bad["usePreviousAnswer"]=null;Fails(bad,"null boolean rejected");
   bad=Result("question",false,false,false,false);bad.Remove("useSelectionInQuestion");Fails(bad,"missing field rejected");
   bad=Result("question",false,false,false,false);bad["extra"]="unauthorized";Fails(bad,"unknown field rejected");
   bad=Result("question",false,false,false,false);bad.Remove("needsImage");bad["extra"]=false;Fails(bad,"unexpected replacement field rejected");
   bad=Result("question",false,false,false,false);bad["model"]=" ";Fails(bad,"blank model rejected");
   bad=Result("question",false,false,false,false);bad["model"]=new string('x',129);Fails(bad,"oversized model rejected");
   bad=Result("question",false,false,false,false);bad["model"]="model\nother";Fails(bad,"control characters in model rejected");
   bad=Result("question",false,false,false,false,"first");Fails(bad,"questions cannot smuggle a write reference");
   bad=Result("compose",false,false,false,false,"caret");Fails(bad,"composition cannot smuggle a table reference");
   bad=Result("table_style",false,false,false,false,"none");Fails(bad,"table styling needs an explicit semantic reference");
   bad=Result("question",false,false,false,false);bad.Remove("tableReference");Fails(bad,"missing semantic table reference rejected");
   bad=Result("question",false,false,false,false);bad["tableReference"]=1;Fails(bad,"numeric table reference rejected");
   Console.WriteLine("PASS: "+checks+" semantic intent policy checks; no document, HWP process, or inference used.");
  } catch(Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode=1; }
 }
}
