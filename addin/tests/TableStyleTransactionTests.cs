using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using HAddon;
internal static class TableStyleTransactionTests {
 static int checks;
 static void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
 sealed class Fake:ITableStyleActions,ITableStyleCursorHandoff,ITableStyleTextRebase {
  readonly TableStylePlan plan;XmlDocument current;public int Copies,Applies,Restores,CaretReads,MoveAtCaretRead,RawReads,ChangeAtRawRead,Adoptions,TextAtRead,Rebases;public bool IncludeBom,RawNoise,Corrupt,FailBefore,WrongCopy,FailCopy,MutateOnCopy,EnableHandoff;public string AdoptedCaret,RestoredCaret,InitialCaret="stable",MovedCaret="user-moved";
  public Fake(ComposeXml document,TableStylePlan plan){this.plan=plan;current=(XmlDocument)document.Document.CloneNode(true);}
  public ComposeXml ReadDocument(){return ComposeXml.Parse(current.OuterXml);}
  public string ReadRawDocument(){RawReads++;if(TextAtRead==RawReads)((XmlElement)((XmlElement)current.GetElementsByTagName("TABLE")[1]).GetElementsByTagName("CHAR")[0]).InnerText="USER EDIT";var raw=current.OuterXml;if(ChangeAtRawRead>0&&RawReads>=ChangeAtRawRead)raw=RawNoise?raw+" ":raw.Replace("RowCount=\"5\"","RowCount=\"4\"");return IncludeBom?"\uFEFF"+raw:raw;}
  public void RefreshAnchorTexts(ComposeXml updated){Rebases++;}
  public string ReadCaret(){CaretReads++;return MoveAtCaretRead>0&&CaretReads>=MoveAtCaretRead?MovedCaret:InitialCaret;}
  public void CheckIdentity(){if(FailBefore)throw new InvalidOperationException("document switched");}
  public void CopyCellStyle(int table,int row,int column){Copies++;if(table!=plan.SourceIndex)throw new Exception("bad source");if(MutateOnCopy)((XmlElement)current.GetElementsByTagName("CHAR")[0]).InnerText="tampered";if(FailCopy)throw new InvalidOperationException("copy failed");}
  public void ApplyCellStyle(int table,int row,int column){
   var op=plan.Changes.Single(x=>x.Table==table&&x.Row==row&&x.Column==column);
   var tbl=current.GetElementsByTagName("TABLE").Cast<XmlElement>().ToArray()[table];
   var cell=tbl.GetElementsByTagName("CELL").Cast<XmlElement>().Single(x=>x.GetAttribute("RowAddr")==row.ToString()&&x.GetAttribute("ColAddr")==column.ToString());
   cell.SetAttribute("BorderFill",WrongCopy?"2":op.Border);var p=(XmlElement)cell.GetElementsByTagName("P")[0];p.SetAttribute("ParaShape",op.Paragraph);var text=(XmlElement)p.GetElementsByTagName("TEXT")[0];text.SetAttribute("CharShape",op.Character);
   TableBorderSimulation.Propagate(current,table,row,column);
   if(Corrupt)((XmlElement)text.GetElementsByTagName("CHAR")[0]).InnerText="tampered";Applies++;
  }
  public bool TryAdoptCursor(string expected){if(!EnableHandoff||ReadCaret()!=expected)return false;Adoptions++;AdoptedCaret=expected;return true;}
  public void RestoreCursor(){Restores++;RestoredCaret=AdoptedCaret??"stable";}
 }
 static void Main(string[] args){
  AppDomain.CurrentDomain.AssemblyResolve+=delegate(object sender,ResolveEventArgs e){string name=new System.Reflection.AssemblyName(e.Name).Name;return new[]{"HwpAddonHelper","Hnc.Foundation.Interface","Hnc.Foundation.InternalInterface"}.Contains(name)?System.Reflection.Assembly.LoadFrom(Path.Combine(@"C:\Program Files (x86)\Hnc\Office 2024\HOffice130\Bin",name+".dll")):null;};
  try{Run(args);}catch(Exception ex){Console.Error.WriteLine(ex.Message);Environment.ExitCode=1;}
 }
 static void Run(string[] args){
  if(args.Length!=1)throw new ArgumentException("Pass the explicitly authorized saved HML fixture.");
  var before=ComposeXml.Parse(File.ReadAllText(args[0],Encoding.UTF8));var plan=TableStylePlan.Build(before,0);
  var moving=new Fake(before,plan){MoveAtCaretRead=2};var movingTx=new TableStyleTransaction();bool moved=false;try{movingTx.ApplyAsync(moving,before,plan,()=>false,null).GetAwaiter().GetResult();}catch(InvalidOperationException){moved=true;}
  Check(moved&&moving.Applies==0&&moving.Copies==0&&moving.Restores==0,"user cursor movement during background verification stops before writes and is not overwritten");
  foreach(var test in new[]{new {At=2,Stage="문서 읽기 중"},new {At=3,Stage="검증을 기다리는 동안"},new {At=4,Stage="문서 재확인 중"}}){var f=new Fake(before,plan){MoveAtCaretRead=test.At};var t=new TableStyleTransaction();bool specific=false;try{t.ApplyAsync(f,before,plan,()=>false,null).GetAwaiter().GetResult();}catch(InvalidOperationException e){specific=e.Message.Contains(test.Stage);}Check(specific&&f.Applies==0&&f.Restores==0,"cursor-drift stage is diagnosed without overwriting user's state");}
  var editing=new Fake(before,plan){ChangeAtRawRead=2};var editingTx=new TableStyleTransaction();bool edited=false;try{editingTx.ApplyAsync(editing,before,plan,()=>false,null).GetAwaiter().GetResult();}catch(InvalidOperationException){edited=true;}
  Check(edited&&editing.Applies==0&&editing.Copies==0&&editing.Restores==0,"changed raw document during background verification stops before writes");
  {var baseCopy=ComposeXml.Parse(before.Document.OuterXml);var p=TableStylePlan.Build(baseCopy,0);var f=new Fake(baseCopy,p){TextAtRead=6};var t=new TableStyleTransaction();t.ApplyAsync(f,baseCopy,p,()=>false,null).GetAwaiter().GetResult();Check(f.Rebases==1&&f.Applies==p.Changes.Length&&f.ReadDocument().Document.OuterXml.Contains("USER EDIT"),"user text edit during background verification survives remaining table changes");}
  var zeroPlan=TableStylePlan.Build(before,0);zeroPlan.Changes=new TableStyleCell[0];var noise=new Fake(before,zeroPlan){ChangeAtRawRead=2,RawNoise=true};var noiseTx=new TableStyleTransaction();noiseTx.ApplyAsync(noise,before,zeroPlan,()=>false,null).GetAwaiter().GetResult();
  Check(noiseTx.CursorRestored&&noise.Applies==0,"equivalent XML whitespace does not falsely report a document change");
  moving=new Fake(before,plan){MoveAtCaretRead=10};movingTx=new TableStyleTransaction();moved=false;try{movingTx.ApplyAsync(moving,before,plan,()=>false,null).GetAwaiter().GetResult();}catch(InvalidOperationException){moved=true;}
  Check(moved&&moving.Applies==1&&moving.Restores==0&&movingTx.RequiresReview,"user cursor movement after a write stops and leaves user's cursor untouched");
  foreach(int at in new[]{2,3,4,10}){var handoff=new Fake(before,plan){MoveAtCaretRead=at,EnableHandoff=true};var handoffTx=new TableStyleTransaction();handoffTx.ApplyAsync(handoff,before,plan,()=>false,null).GetAwaiter().GetResult();Check(handoff.Applies==plan.Changes.Length&&handoff.Adoptions==1&&handoff.RestoredCaret=="user-moved"&&handoffTx.CursorRestored&&!handoffTx.RequiresReview,"verified cursor-only handoff resumes all pinned cells and restores the latest user position");}
  {var handoff=new Fake(before,plan){MoveAtCaretRead=11,EnableHandoff=true,InitialCaret="18:0:0:0",MovedCaret="0:14:8:0"};var handoffTx=new TableStyleTransaction();handoffTx.ApplyAsync(handoff,before,plan,()=>false,null).GetAwaiter().GetResult();Check(handoff.Applies==plan.Changes.Length&&handoff.Adoptions==1&&handoff.RestoredCaret=="0:14:8:0"&&!handoffTx.RequiresReview,"reported cell-to-body cursor move after apply during worker verification resumes the pinned plan");}
  {var handoff=new Fake(before,plan){MoveAtCaretRead=3,EnableHandoff=true,ChangeAtRawRead=2};var handoffTx=new TableStyleTransaction();bool blocked=false;try{handoffTx.ApplyAsync(handoff,before,plan,()=>false,null).GetAwaiter().GetResult();}catch(InvalidOperationException){blocked=true;}Check(blocked&&handoff.Adoptions==0&&handoff.Copies==0&&handoff.Applies==0&&handoff.Restores==0,"cursor handoff never masks a concurrent document edit");}
  {var handoff=new Fake(before,plan){MoveAtCaretRead=10,EnableHandoff=true,Corrupt=true};var handoffTx=new TableStyleTransaction();bool blocked=false;try{handoffTx.ApplyAsync(handoff,before,plan,()=>false,null).GetAwaiter().GetResult();}catch(InvalidOperationException){blocked=true;}Check(blocked&&handoff.Applies==1&&handoff.Adoptions==0&&handoff.Restores==0&&handoffTx.RequiresReview,"corrupt apply plus cursor drift does not adopt or overwrite user position");}
  var conflict=ComposeXml.Parse(before.Document.OuterXml);var sourceCell=conflict.Document.GetElementsByTagName("TABLE").Cast<XmlElement>().First().GetElementsByTagName("CELL").Cast<XmlElement>().Single(c=>c.GetAttribute("RowAddr")=="0"&&c.GetAttribute("ColAddr")=="1");
  var borderList=(XmlElement)conflict.Document.GetElementsByTagName("BORDERFILLLIST")[0];var definitions=borderList.GetElementsByTagName("BORDERFILL").Cast<XmlElement>().ToArray();var fork=(XmlElement)definitions.Single(x=>x.GetAttribute("Id")==sourceCell.GetAttribute("BorderFill")).CloneNode(true);
  fork.SetAttribute("Id",(definitions.Max(x=>Int32.Parse(x.GetAttribute("Id")))+1).ToString());fork.SelectSingleNode("LEFTBORDER").Attributes["Width"].Value="0.5mm";borderList.AppendChild(fork);borderList.SetAttribute("Count",(definitions.Length+1).ToString());sourceCell.SetAttribute("BorderFill",fork.GetAttribute("Id"));
  var conflictPlan=TableStylePlan.Build(conflict,0);var conflictFake=new Fake(conflict,conflictPlan);var conflictTx=new TableStyleTransaction();bool conflictFailed=false;try{conflictTx.Apply(conflictFake,conflict,conflictPlan);}catch(InvalidOperationException){conflictFailed=true;}
  Check(conflictFailed&&conflictFake.Copies==0&&conflictFake.Applies==0&&!conflictTx.RequiresReview,"conflicting source edges stop before any native command");
  var fake=new Fake(before,plan);var tx=new TableStyleTransaction();tx.Apply(fake,before,plan);
  Check(fake.Applies==plan.Changes.Length&&fake.Copies==fake.Applies&&!tx.RequiresReview&&fake.Restores==1,"all planned targets applied and verified");
  var already=fake.ReadDocument();var donePlan=TableStylePlan.Build(already,0);var doneFake=new Fake(already,donePlan);var doneTx=new TableStyleTransaction();doneTx.Apply(doneFake,already,donePlan);
  Check(donePlan.Changes.Length==0&&doneFake.Copies==0&&doneFake.Applies==0&&doneFake.Restores==0&&doneTx.CursorRestored,"matching tables require no native actions or cursor movement");
  var one=ComposeXml.Parse(already.Document.OuterXml);var oneTable=one.Document.GetElementsByTagName("TABLE").Cast<XmlElement>().ElementAt(1);var oneCell=oneTable.GetElementsByTagName("CELL").Cast<XmlElement>().First();((XmlElement)oneCell.GetElementsByTagName("TEXT")[0]).SetAttribute("CharShape","9");
  var onePlan=TableStylePlan.Build(one,0);var oneFake=new Fake(one,onePlan){IncludeBom=true};var oneTx=new TableStyleTransaction();int notified=0;oneTx.ApplyAsync(oneFake,one,onePlan,()=>false,n=>notified=n).GetAwaiter().GetResult();
  Check(onePlan.Changes.Length==1&&oneFake.Applies==1&&notified==1&&oneTx.CursorRestored&&!oneTx.RequiresReview,"BOM-prefixed background readback completes and reports a verified native apply");
  Check(oneTx.FullVerifications==2&&oneTx.ReusedVerifications==1,"unchanged post-copy snapshot reuses the previous full verification");
  var two=ComposeXml.Parse(one.Document.OuterXml);var twoCells=two.Document.GetElementsByTagName("TABLE").Cast<XmlElement>().ElementAt(1).GetElementsByTagName("CELL").Cast<XmlElement>().ToArray();((XmlElement)twoCells[1].GetElementsByTagName("TEXT")[0]).SetAttribute("CharShape","9");
  var twoPlan=TableStylePlan.Build(two,0);var twoFake=new Fake(two,twoPlan);var twoTx=new TableStyleTransaction();bool cancel=false,stopped=false;try{twoTx.ApplyAsync(twoFake,two,twoPlan,()=>cancel,n=>cancel=true).GetAwaiter().GetResult();}catch(OperationCanceledException){stopped=true;}
  Check(twoPlan.Changes.Length==2&&stopped&&twoFake.Applies==1&&twoTx.AppliedCount==1,"cancel prevents additional writes after a verified cell");
  var changedCopy=new Fake(before,plan){MutateOnCopy=true};var changedCopyTx=new TableStyleTransaction();bool changedCopyStopped=false;try{changedCopyTx.ApplyAsync(changedCopy,before,plan,()=>false,null).GetAwaiter().GetResult();}catch(InvalidOperationException){changedCopyStopped=true;}
  Check(changedCopyStopped&&changedCopy.Copies==1&&changedCopy.Applies==0&&changedCopyTx.RequiresReview,"a mutation during copy is verified and stopped before applying a target cell");
  fake=new Fake(before,plan){Corrupt=true};tx=new TableStyleTransaction();bool failed=false;try{tx.Apply(fake,before,plan);}catch(InvalidOperationException){failed=true;}
  Check(failed&&fake.Applies==1&&tx.RequiresReview&&fake.Restores==1,"unexpected cell text change stops further writes and requires review");
  Check(tx.Applying&&tx.AttemptedTable==1&&tx.AttemptedRow==0&&tx.AttemptedColumn==0,"partial apply reports the unverified target cell");
  fake=new Fake(before,plan){WrongCopy=true};tx=new TableStyleTransaction();failed=false;try{tx.Apply(fake,before,plan);}catch(InvalidOperationException){failed=true;}
  Check(failed&&fake.Applies==1&&tx.RequiresReview,"incorrect copied style stops further writes");
  fake=new Fake(before,plan){FailBefore=true};tx=new TableStyleTransaction();failed=false;try{tx.Apply(fake,before,plan);}catch(InvalidOperationException){failed=true;}
  Check(failed&&fake.Applies==0&&!tx.RequiresReview,"changed document blocks every write");
  fake=new Fake(before,plan){FailCopy=true};tx=new TableStyleTransaction();failed=false;try{tx.Apply(fake,before,plan);}catch(InvalidOperationException){failed=true;}
  Check(failed&&fake.Applies==0&&!tx.RequiresReview,"failed copy without document mutation is verified unchanged");
  Check(!tx.Applying&&tx.AttemptedTable==plan.SourceIndex&&tx.AttemptedRow==0&&tx.AttemptedColumn==0,"copy failure reports the source cell");
  fake=new Fake(before,plan){FailCopy=true,MutateOnCopy=true};tx=new TableStyleTransaction();failed=false;try{tx.Apply(fake,before,plan);}catch(InvalidOperationException){failed=true;}
  Check(failed&&fake.Applies==0&&tx.RequiresReview,"failed copy after mutation still requires review");
  Console.WriteLine("PASS "+checks+" table-style transaction scenarios on saved HML with a synthetic adapter; no live HWP document modified.");
 }
}
