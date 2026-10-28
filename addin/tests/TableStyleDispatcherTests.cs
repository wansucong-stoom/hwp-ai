using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using HAddon;

// A WPF dispatcher model verifies that native-facing calls resume on the
// originating thread after the CPU-only background document verification.
internal static class TableStyleDispatcherTests {
 sealed class Fake:ITableStyleActions {
  readonly int thread=Thread.CurrentThread.ManagedThreadId;readonly ComposeXml xml;
  internal int checks;
  internal Fake(ComposeXml xml){this.xml=xml;}
  void Check(){if(Thread.CurrentThread.ManagedThreadId!=thread)throw new Exception("Native call escaped UI thread");checks++;}
  public ComposeXml ReadDocument(){Check();return xml;}
  public string ReadRawDocument(){Check();return xml.Document.OuterXml;}
  public string ReadCaret(){Check();return "unchanged";}
  public void CheckIdentity(){Check();}
  public void CopyCellStyle(int table,int row,int column){Check();throw new Exception("Unexpected write");}
  public void ApplyCellStyle(int table,int row,int column){Check();throw new Exception("Unexpected write");}
  public void RestoreCursor(){Check();throw new Exception("Unexpected navigation");}
 }
 [STAThread] static void Main(string[] args){
  try{
   var xml=ComposeXml.Parse(File.ReadAllText(args[0],Encoding.UTF8));var plan=TableStylePlan.Build(xml,0);plan.Changes=plan.Changes.Take(0).ToArray();
   var dispatcher=Dispatcher.CurrentDispatcher;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));var fake=new Fake(xml);var tx=new TableStyleTransaction();
   Task task=tx.ApplyAsync(fake,xml,plan,()=>false,null);var frame=new DispatcherFrame();
   task.ContinueWith(t=>dispatcher.BeginInvoke(new Action(()=>frame.Continue=false)));Dispatcher.PushFrame(frame);task.GetAwaiter().GetResult();
   if(fake.checks<5||!tx.CursorRestored)throw new Exception("Native readback did not complete on the originating dispatcher");
   Console.WriteLine("PASS WPF dispatcher continuation with fake native calls; no live HWP document modified.");
  }catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}
 }
}
