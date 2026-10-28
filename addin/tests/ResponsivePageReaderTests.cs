using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HAddon;
class ResponsivePageReaderTests {
 sealed class Context:SynchronizationContext {
  readonly Queue<KeyValuePair<SendOrPostCallback,object>> queue=new Queue<KeyValuePair<SendOrPostCallback,object>>();
  readonly AutoResetEvent signal=new AutoResetEvent(false);internal int Posts;
  public override void Post(SendOrPostCallback d,object state){lock(queue){queue.Enqueue(new KeyValuePair<SendOrPostCallback,object>(d,state));Posts++;}signal.Set();}
  internal void Complete(Task task){
   while(!task.IsCompleted){KeyValuePair<SendOrPostCallback,object> work=default(KeyValuePair<SendOrPostCallback,object>);bool found=false;lock(queue){if(queue.Count>0){work=queue.Dequeue();found=true;}}if(found)work.Key(work.Value);else signal.WaitOne(5000);}
   task.GetAwaiter().GetResult();
  }
 }
 sealed class Source:IResponsivePageSource {
  internal int Thread=System.Threading.Thread.CurrentThread.ManagedThreadId;internal bool Changed,RequireUserTurn,UserTurn;internal string Id="doc";internal int Calls;
  void Check(){if(System.Threading.Thread.CurrentThread.ManagedThreadId!=Thread)throw new Exception("native call crossed threads");}
  public string Identity(){Check();return Id;}
  public string ReadXml(){Check();return "<HWPML><HEAD/><BODY><SECTION><P><TEXT><CHAR>"+(Changed?"new":"old")+"</CHAR></TEXT></P></SECTION></BODY></HWPML>";}
  public long PageCount(){Check();return 3;}
  public string ReadPage(int p,int option){Check();Calls++;if(RequireUserTurn&&p==1&&!UserTurn)throw new Exception("UI input was not processed between pages");if(option!=1&&option!=7)throw new Exception("unexpected option");return "page "+p;}
 }
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 static void Main(){
  var context=new Context();SynchronizationContext.SetSynchronizationContext(context);
  var source=new Source{RequireUserTurn=true};int count=0;var task=ResponsivePageReader.ReadAsync(source,CancellationToken.None,(n,total)=>{count=n;if(n==1)context.Post(delegate{source.UserTurn=true;},null);});
  context.Complete(task);Check(task.Result.Pages.Length==3&&task.Result.Paragraphs[0].Text=="old"&&source.Calls==6&&count==3&&context.Posts>=3&&source.UserTurn,"normal/yield");Check(task.Result.RawXml==source.ReadXml()&&TableStableSnapshot.Digest(task.Result.RawXml)==task.Result.Digest,"returned raw XML must be exactly the pinned revision used by the parser");
  source=new Source();task=ResponsivePageReader.ReadAsync(source,CancellationToken.None,(n,total)=>{if(n==1)source.Changed=true;});bool rejected=false;
  try{context.Complete(task);}catch(InvalidOperationException){rejected=true;}Check(rejected,"document change must be detected");
  source=new Source();var cts=new CancellationTokenSource();task=ResponsivePageReader.ReadAsync(source,cts.Token,(n,total)=>{if(n==1)cts.Cancel();});rejected=false;
  try{context.Complete(task);}catch(OperationCanceledException){rejected=true;}Check(rejected&&source.Calls==2,"cancel between pages");
  source=new Source();task=ResponsivePageReader.ReadAsync(source,CancellationToken.None,(n,total)=>{if(n==1)source.Id="other";});rejected=false;
  try{context.Complete(task);}catch(InvalidOperationException){rejected=true;}Check(rejected&&source.Calls==2,"document identity change");
  SynchronizationContext.SetSynchronizationContext(null);source=new Source();task=ResponsivePageReader.ReadAsync(source,CancellationToken.None,null);rejected=false;
  try{task.GetAwaiter().GetResult();}catch(InvalidOperationException){rejected=true;}Check(rejected&&source.Calls==0,"must require UI context");
  Console.WriteLine("PASS responsive read: UI thread, page yields, cancellation, identity and document-change detection; no native HWP");
 }
}
