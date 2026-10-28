using System;
using HAddon;
class NativePagePositionEvidenceTests {
 sealed class Fake:IPagePositionBackend {
  internal string State="original";internal int Restores,Moves,Checks;internal bool DifferentLast,UserMoveOnRead,FailPage,ChangedDocument;
  public string CursorState(){return State;}
  public void CheckDocument(){Checks++;if(ChangedDocument)throw new InvalidOperationException("changed");}
  public bool Move(int l,int p,int x){Moves++;State=l+":"+p+":"+x;return true;}
  public bool IsAt(int l,int p,int x){return State==l+":"+p+":"+x;}
  public bool TryPage(out int raw){raw=DifferentLast&&Moves%2==0?6:5;if(UserMoveOnRead)State="user";return !FailPage;}
  public bool Restore(){Restores++;State="original";return true;}
 }
 static DocumentAddressCandidate C(string path,int p){return new DocumentAddressCandidate{XmlPath=path,List=2,Paragraph=p,Start=1,End=8,Text="content",Page=5};}
 static void Assert(bool b,string text){if(!b)throw new Exception(text);}
 static void Main(){
  var fake=new Fake();var found=NativePagePositionEvidence.Collect(fake,new[]{C("a",0)});
  Assert(found.Length==1&&found[0].RawPage==5&&fake.Moves==2&&fake.Restores==1&&fake.Checks==2,"same-page positions");
  fake=new Fake{DifferentLast=true};found=NativePagePositionEvidence.Collect(fake,new[]{C("a",0)});Assert(found.Length==0&&fake.Restores==1,"split paragraph omitted");
  fake=new Fake{UserMoveOnRead=true};bool rejected=false;try{NativePagePositionEvidence.Collect(fake,new[]{C("a",0)});}catch(InvalidOperationException){rejected=true;}Assert(rejected&&fake.State=="user"&&fake.Restores==0,"user caret not overwritten");
  fake=new Fake{FailPage=true};rejected=false;try{NativePagePositionEvidence.Collect(fake,new[]{C("a",0)});}catch(InvalidOperationException){rejected=true;}Assert(rejected&&fake.Restores==1,"restore after read failure");
  fake=new Fake();rejected=false;try{NativePagePositionEvidence.Collect(fake,new[]{C("a",0),C("b",0)});}catch(InvalidOperationException){rejected=true;}Assert(rejected&&fake.Moves==0,"duplicate native location rejected");
  fake=new Fake{ChangedDocument=true};rejected=false;try{NativePagePositionEvidence.Collect(fake,new[]{C("a",0)});}catch(InvalidOperationException){rejected=true;}Assert(rejected&&fake.Moves==0,"document change rejected");
  Console.WriteLine("PASS experimental page-position evidence, split paragraph, caret changes and stale document; no native HWP");
 }
}
