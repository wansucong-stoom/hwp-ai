using System;
using System.Collections.Generic;
using System.Linq;
using HAddon;
class DocumentUpdateTransactionTests {
 sealed class Fake:IDocumentUpdateBackend{
  internal Dictionary<string,string> Values=new Dictionary<string,string>{{"a","old a"},{"b","old b"},{"c","keep"}};
  internal int Prepares,Replacements,VerifyCalls;internal bool FailOnPrepare,MutateThenThrow,TamperPlanDuringPrepare,TamperPlanDuringWrite;internal string Revision="v1";internal Action DuringVerify;
  public void Prepare(string revision,DocumentUpdateTarget t){Prepares++;if(FailOnPrepare&&t.Id=="b")throw new Exception("unresolvable");if(revision!=Revision||!Values.ContainsKey(t.Id)||Values[t.Id]!=t.Original)throw new Exception("stale");if(TamperPlanDuringPrepare)t.Replacement="altered";}
  public void Verify(string revision,IList<DocumentUpdateTarget> plan,int applied){
   VerifyCalls++;if(DuringVerify!=null)DuringVerify();
   if(revision!=Revision||Values["c"]!="keep")throw new Exception("external change");
   for(int i=0;i<plan.Count;i++)if(Values[plan[i].Id]!=(i<applied?plan[i].Replacement:plan[i].Original))throw new Exception("plan mismatch");
  }
  public void Replace(DocumentUpdateTarget t){Replacements++;Values[t.Id]=t.Replacement;if(TamperPlanDuringWrite)t.Replacement="altered";if(MutateThenThrow)throw new Exception("write uncertain");}
 }
 static DocumentUpdateTarget[] Plan(){return new[]{new DocumentUpdateTarget{Id="a",Original="old a",Replacement="new a"},new DocumentUpdateTarget{Id="b",Original="old b",Replacement="new b"}};}
 static void Assert(bool b,string message){if(!b)throw new Exception(message);}
 static void Main(){
  var ok=new Fake();var tx=new DocumentUpdateTransaction();tx.ApplyAsync(ok,"v1",Plan(),()=>false,null).GetAwaiter().GetResult();
  Assert(ok.Prepares==2&&ok.Values["a"]=="new a"&&ok.Values["b"]=="new b"&&tx.AppliedCount==2&&!tx.RequiresReview,"normal");
  var fail=new Fake{FailOnPrepare=true};tx=new DocumentUpdateTransaction();try{tx.ApplyAsync(fail,"v1",Plan(),()=>false,null).GetAwaiter().GetResult();throw new Exception("expected rejection");}catch(Exception e){Assert(e.Message=="unresolvable","prepare rejection");}Assert(fail.Replacements==0,"must prepare every target before writing");
  var uncertain=new Fake{MutateThenThrow=true};tx=new DocumentUpdateTransaction();try{tx.ApplyAsync(uncertain,"v1",Plan(),()=>false,null).GetAwaiter().GetResult();throw new Exception("expected failure");}catch(Exception e){Assert(e.Message=="write uncertain","write failure");}Assert(tx.RequiresReview&&tx.AttemptedId=="a"&&uncertain.Values["b"]=="old b","partial mutation warning");
  var changed=new Fake();tx=new DocumentUpdateTransaction();try{tx.ApplyAsync(changed,"v1",Plan(),()=>false,n=>changed.Values["c"]="user edit").GetAwaiter().GetResult();throw new Exception("expected conflict");}catch(Exception e){Assert(e.Message=="external change","conflict");}Assert(changed.Replacements==1&&tx.AppliedCount==1&&tx.RequiresReview,"stop after external change and report partial application");
  var cancelled=new Fake();tx=new DocumentUpdateTransaction();bool stop=false;try{tx.ApplyAsync(cancelled,"v1",Plan(),()=>stop,n=>stop=true).GetAwaiter().GetResult();throw new Exception("expected cancellation");}catch(OperationCanceledException){}Assert(tx.RequiresReview&&tx.ChangedCount==1&&cancelled.Values["b"]=="old b","partial cancellation");
  var stopDuringRead=new Fake();tx=new DocumentUpdateTransaction();bool stopNow=false;stopDuringRead.DuringVerify=()=>{if(stopDuringRead.VerifyCalls==2)stopNow=true;};try{tx.ApplyAsync(stopDuringRead,"v1",Plan(),()=>stopNow,null).GetAwaiter().GetResult();throw new Exception("expected cancellation");}catch(OperationCanceledException){}Assert(stopDuringRead.Replacements==0&&!tx.RequiresReview,"cancellation received during pre-write verification must prevent write");
  var dup=new Fake();tx=new DocumentUpdateTransaction();var bad=Plan();bad[1].Id="a";try{tx.ApplyAsync(dup,"v1",bad,()=>false,null).GetAwaiter().GetResult();throw new Exception("expected rejection");}catch(InvalidOperationException){}Assert(dup.Prepares==0,"reject duplicate targets");
  var tamper=new Fake{TamperPlanDuringPrepare=true};tx=new DocumentUpdateTransaction();try{tx.ApplyAsync(tamper,"v1",Plan(),()=>false,null).GetAwaiter().GetResult();throw new Exception("expected rejection");}catch(InvalidOperationException){}Assert(tamper.Replacements==0&&!tx.RequiresReview,"detect changed plan before first write");
  tamper=new Fake{TamperPlanDuringWrite=true};tx=new DocumentUpdateTransaction();try{tx.ApplyAsync(tamper,"v1",Plan(),()=>false,null).GetAwaiter().GetResult();throw new Exception("expected rejection");}catch(InvalidOperationException){}Assert(tamper.Replacements==1&&tx.RequiresReview&&tx.AttemptedId=="a"&&tamper.Values["b"]=="old b","detect changed plan after uncertain write");
  Console.WriteLine("PASS multi-target preparation, revision conflict, duplicate target and uncertain write tests; synthetic backend only");
 }
}
