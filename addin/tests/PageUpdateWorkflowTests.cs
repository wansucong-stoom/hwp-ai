using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HAddon;
namespace HAddon { internal sealed class NativePageSnapshot { internal int DocumentId;internal string Path,Digest;internal string[] Pages,TablePages;internal DocumentParagraph[] Paragraphs; } }
class PageUpdateWorkflowTests {
 sealed class Fake:IPageUpdateEnvironment,IDocumentUpdateBackend {
  internal bool Evidence=true,Stale,CancelAfterPlan,MutateEvidenceInputs;internal int AnalyzerCalls,BackendCalls,Writes,Checks;internal string Text="old";internal string PlanId="a";
  internal NativePageSnapshot Current=new NativePageSnapshot{DocumentId=1,Path="x",Digest="v1",Pages=new[]{"old", "source"}};
  public NativePageSnapshot Capture(){return Current;}
  public DocumentAddressCandidate[] Collect(NativePageSnapshot s){return new[]{new DocumentAddressCandidate{XmlPath="a",Page=1,Text="old",List=3,Paragraph=0,Start=0,End=3},new DocumentAddressCandidate{XmlPath="b",Page=2,Text="source",List=4,Paragraph=0,Start=0,End=6}};}
  public void RequireEvidence(NativePageSnapshot s,DocumentAddressCandidate[] c,int[] target,int[] source){Checks++;if(!Evidence||s.Digest!="v1"||c.Length!=1||c[0].XmlPath!="a"||target[0]!=1||source[0]!=2)throw new InvalidOperationException("unproven");if(MutateEvidenceInputs){s.Pages[0]="mutated";c[0].List=999;target[0]=2;source[0]=1;}}
  public void VerifyRevision(NativePageSnapshot s){if(Stale)throw new InvalidOperationException("stale");}
  public Task<Dictionary<string,object>> Analyze(PageEditPlanning s,string prompt){AnalyzerCalls++;if(CancelAfterPlan)Stale=true;return Task.FromResult(new Dictionary<string,object>{{"changes",new object[]{new Dictionary<string,object>{{"id",PlanId},{"replacement","new"},{"sourcePages",new object[]{2}}}}},{"uncertainties",new object[0]}});}
  public IDocumentUpdateBackend CreateBackend(NativePageSnapshot s,DocumentAddressCandidate[] targets){BackendCalls++;if(targets.Length!=1||targets[0].XmlPath!="a"||targets[0].List!=3||s.Pages[0]!="old")throw new Exception("scope widened");return this;}
  public void Prepare(string revision,DocumentUpdateTarget target){if(revision!="v1"||target.Original!=Text)throw new Exception("prepare");}
  public void Verify(string revision,IList<DocumentUpdateTarget> plan,int applied){if(Text!=(applied==0?"old":"new"))throw new Exception("verification");}
  public void Replace(DocumentUpdateTarget target){Writes++;Text=target.Replacement;}
 }
 static void Reject(Action a,string expected){try{a();}catch(InvalidOperationException e){if(e.Message==expected)return;throw;}throw new Exception("expected rejection: "+expected);}
 static void Check(bool ok,string s){if(!ok)throw new Exception(s);}
 static void Main(){
  var f=new Fake();var workflow=new PageUpdateWorkflow();int changed=workflow.RunAsync(f,new[]{1},new[]{2},"update",()=>false,null).GetAwaiter().GetResult();Check(changed==1&&f.Text=="new"&&f.Checks==2&&f.Writes==1,"success");
  f=new Fake{Evidence=false};workflow=new PageUpdateWorkflow();var f1=f;var w1=workflow;Reject(()=>w1.RunAsync(f1,new[]{1},new[]{2},"update",()=>false,null).GetAwaiter().GetResult(),"unproven");Check(f.AnalyzerCalls==0&&f.BackendCalls==0&&f.Writes==0,"no unproven read/write");
  f=new Fake{CancelAfterPlan=true};workflow=new PageUpdateWorkflow();var f2=f;var w2=workflow;Reject(()=>w2.RunAsync(f2,new[]{1},new[]{2},"update",()=>false,null).GetAwaiter().GetResult(),"stale");Check(f.AnalyzerCalls==1&&f.BackendCalls==0&&f.Writes==0,"stale plan not applied");
  f=new Fake{PlanId="b"};workflow=new PageUpdateWorkflow();try{workflow.RunAsync(f,new[]{1},new[]{2},"update",()=>false,null).GetAwaiter().GetResult();throw new Exception("expected scope rejection");}catch(InvalidOperationException){}Check(f.BackendCalls==0&&f.Writes==0,"outside target rejected");
  f=new Fake{MutateEvidenceInputs=true};workflow=new PageUpdateWorkflow();changed=workflow.RunAsync(f,new[]{1},new[]{2},"update",()=>false,null).GetAwaiter().GetResult();Check(changed==1&&f.Writes==1,"evidence backend must not mutate authoritative scope or target");
  Console.WriteLine("PASS page update workflow evidence gates, stale plan, scope and synthetic apply");
 }
}
