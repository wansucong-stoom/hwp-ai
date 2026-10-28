using System;
using HAddon;
namespace HAddon {internal sealed class NativeScanItem{internal long Kind;internal string Text;internal int List,Paragraph,Position;}}
class BoundedPageScanTests {
 sealed class Fake:IBoundedPageScanBackend {
  internal string Cursor="original";internal int ReadCount,Restores,Ends;internal bool Selection,FailInit,UserMoves,ThrowGoto,Incomplete,Changed;
  public void CheckDocument(){if(Changed)throw new InvalidOperationException("changed");}
  public string CursorState(){return Cursor;}
  public void GotoPage(int p){Cursor="page"+p;if(ThrowGoto)throw new InvalidOperationException("goto");}
  public void BeginSelection(){Cursor="select";Selection=true;}
  public bool HasSelection(){return Selection;}
  public bool InitSelectionScan(){return !FailInit;}
  public long ReadText(out string text){ReadCount++;if(UserMoves)Cursor="user";if(ReadCount==1){text="cell";return 4;}text="";return Incomplete?0:1;}
  public NativeScanItem CurrentScanPosition(long k,string text){Cursor="scanned";return new NativeScanItem{Kind=k,Text=text,List=3,Paragraph=0,Position=0};}
  public bool EndScan(){Ends++;return true;}
  public bool Restore(){Restores++;Cursor="original";return true;}
 }
 static void Assert(bool b,string m){if(!b)throw new Exception(m);}
 static void Expect(Action f){try{f();}catch(InvalidOperationException){return;}throw new Exception("expected failure");}
 static void Main(){
  var f=new Fake();var r=BoundedPageScan.Read(f,1,6,10);Assert(r.Items.Length==1&&r.Items[0].Kind==4&&!r.CoverageProven&&f.Restores==1&&f.Ends==1,"normal scan");
  f=new Fake{UserMoves=true};Expect(()=>BoundedPageScan.Read(f,1,6,10));Assert(f.Restores==0&&f.Ends==1&&f.Cursor=="user","do not overwrite user cursor");
  f=new Fake{FailInit=true};Expect(()=>BoundedPageScan.Read(f,1,6,10));Assert(f.Restores==1&&f.Ends==0,"initialization failure");
  f=new Fake{Incomplete=true};Expect(()=>BoundedPageScan.Read(f,1,6,10));Assert(f.Restores==1&&f.Ends==1,"incomplete scan is not success");
  f=new Fake{ThrowGoto=true};Expect(()=>BoundedPageScan.Read(f,1,6,10));Assert(f.Restores==0,"unknown caret after goto exception");
  Expect(()=>BoundedPageScan.Read(new Fake(),1,11,10));
  Console.WriteLine("PASS bounded selection scan, failure, user cursor and incomplete coverage; synthetic only");
 }
}
