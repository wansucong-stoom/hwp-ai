using System;
using System.Linq;
using HAddon;
class OnDemandTargetLocatorTests {
 const string C1="BODY/SECTION[0]/P[0]/TEXT[0]/TABLE[0]/ROW[0]/CELL[0]/PARALIST[0]";
 const string C2="BODY/SECTION[0]/P[0]/TEXT[0]/TABLE[0]/ROW[0]/CELL[1]/PARALIST[0]";
 sealed class Probe:IOnDemandNativeProbe {
  internal bool Complete=true,Stale,Text=true,Cell=true,Style=true,Structure=true,Page=true,Split,WrongText,WrongCell,WrongStyle,WrongStructure,DuplicateAnchors,Collide;
  internal int Checks,Searches,Inspections;internal Action DuringSearch;
  public void CheckSnapshot(){Checks++;if(Stale)throw new InvalidOperationException("stale");}
  public OnDemandAnchorSearch FindAnchor(string text){
   Searches++;if(DuringSearch!=null)DuringSearch();int list=Collide?7:text=="anchor one"?7:8;
   var hit=new OnDemandObservedParagraph{List=list,Paragraph=0,Text=text,TextVerified=true,WholeParagraph=true};
   return new OnDemandAnchorSearch{Complete=Complete,Matches=DuplicateAnchors?new[]{hit,hit}:new[]{hit}};
  }
  public OnDemandObservedParagraph InspectParagraph(int list,int paragraph){
   Inspections++;return new OnDemandObservedParagraph{List=list,Paragraph=paragraph,Start=0,End=4,Text=WrongText?"bad!":"same",FirstPhysicalPage=1,LastPhysicalPage=Split?2:1,ContainerToken=WrongCell?"wrong":"container",StyleToken=WrongStyle?"wrong":"style",StructureToken=WrongStructure?"wrong":"structure",TextVerified=Text,ContainerVerified=Cell,StyleVerified=Style,StructureVerified=Structure,PhysicalPageVerified=Page,WholeParagraph=true};
  }
 }
 static DocumentParagraph[] Document(){return new[]{new DocumentParagraph{Path=C1+"/P[0]",Text="anchor one",CellPath=C1},new DocumentParagraph{Path=C1+"/P[1]",Text="same",CellPath=C1},new DocumentParagraph{Path=C2+"/P[0]",Text="anchor two",CellPath=C2},new DocumentParagraph{Path=C2+"/P[1]",Text="same",CellPath=C2}};}
 static OnDemandExpectedTarget Target(string path){return new OnDemandExpectedTarget{XmlPath=path,Text="same",ContainerToken="container",StyleToken="style",StructureToken="structure"};}
 static void Assert(bool value,string message){if(!value)throw new Exception(message);}
 static void Reject(Action action){try{action();}catch(InvalidOperationException){return;}throw new Exception("expected rejection");}
 static OnDemandLocationResult Resolve(Probe p){return OnDemandTargetLocator.Resolve(p,Document(),new[]{Target(C1+"/P[1]")},new[]{1},()=>false);}
 static void Main(){
  var p=new Probe();var result=Resolve(p);Assert(result.AllResolved&&result.Targets.Length==1&&result.Targets[0].List==7&&result.Targets[0].Paragraph==1&&result.Targets[0].Text=="same","nearby anchor resolution");
  foreach(var bad in new[]{new Probe{Complete=false},new Probe{DuplicateAnchors=true},new Probe{Text=false},new Probe{Cell=false},new Probe{Style=false},new Probe{Structure=false},new Probe{Page=false},new Probe{Split=true},new Probe{WrongText=true},new Probe{WrongCell=true},new Probe{WrongStyle=true},new Probe{WrongStructure=true}}){var x=Resolve(bad);Assert(!x.AllResolved&&x.Targets.Length==0&&x.Unresolved.Length==1,"unproven result accepted");}
  var conflict=new Probe{Collide=true};result=OnDemandTargetLocator.Resolve(conflict,Document(),new[]{Target(C1+"/P[1]"),Target(C2+"/P[1]")},new[]{1},()=>false);Assert(!result.AllResolved&&result.Targets.Length==0&&result.Unresolved.Length==2,"all collision claimants rejected");
  p=new Probe();p.DuringSearch=()=>p.Stale=true;Reject(()=>Resolve(p));Assert(p.Inspections==0,"stale snapshot used");
  p=new Probe();bool stop=false;p.DuringSearch=()=>stop=true;try{OnDemandTargetLocator.Resolve(p,Document(),new[]{Target(C1+"/P[1]")},new[]{1},()=>stop);throw new Exception("expected cancellation");}catch(OperationCanceledException){}Assert(p.Inspections==0,"cancelled operation continued");
  var expected=Target(C1+"/P[1]");var doc=Document();p=new Probe();p.DuringSearch=()=>{expected.Text="mutated";doc[1].Text="mutated";};result=OnDemandTargetLocator.Resolve(p,doc,new[]{expected},new[]{1},()=>false);Assert(result.Targets.Length==1&&result.Targets[0].Text=="same","caller mutation changed frozen target");
  expected=Target(C1+"/P[1]");expected.StyleToken=null;p=new Probe();result=OnDemandTargetLocator.Resolve(p,Document(),new[]{expected},new[]{1},()=>false);Assert(result.Targets.Length==0&&p.Searches==0,"missing expected style proof");
  var objectDoc=Document();objectDoc[1].HasObjects=true;p=new Probe();result=OnDemandTargetLocator.Resolve(p,objectDoc,new[]{Target(C1+"/P[1]")},new[]{1},()=>false);Assert(!result.AllResolved&&result.Targets.Length==0&&p.Searches==0,"objects not overwritten");
  p=new Probe();result=OnDemandTargetLocator.Resolve(p,Document(),new[]{Target(C1+"/P[1]")},new[]{2},()=>false);Assert(!result.AllResolved&&result.Targets.Length==0,"out of scope page");
  p=new Probe();Reject(()=>OnDemandTargetLocator.Resolve(p,Document(),new[]{Target(C1+"/P[1]")},new[]{257},()=>false));Assert(p.Searches==0,"invalid page must not trigger a search");
  var duplicates=new[]{new DocumentParagraph{Path=C1+"/P[0]",Text="same",CellPath=C1},new DocumentParagraph{Path=C1+"/P[1]",Text="same",CellPath=C1}};
  p=new Probe();result=OnDemandTargetLocator.Resolve(p,duplicates,new[]{Target(C1+"/P[1]")},new[]{1},()=>false);Assert(!result.AllResolved&&result.Targets.Length==0&&p.Searches==0,"no unique anchor reported rather than guessed");
  p=new Probe();var second=Target(C2+"/P[1]");second.StyleToken=null;result=OnDemandTargetLocator.Resolve(p,Document(),new[]{Target(C1+"/P[1]"),second},new[]{1},()=>false);Assert(!result.AllResolved&&result.Targets.Length==1&&result.Unresolved.Length==1,"partial location explicitly reported");
  var repeated=new[]{new DocumentParagraph{Path=C1+"/P[0]",Text="anchor one",CellPath=C1},new DocumentParagraph{Path=C1+"/P[1]",Text="same",CellPath=C1},new DocumentParagraph{Path=C1+"/P[2]",Text="same",CellPath=C1}};
  p=new Probe();result=OnDemandTargetLocator.Resolve(p,repeated,new[]{Target(C1+"/P[1]"),Target(C1+"/P[2]")},new[]{1},()=>false);Assert(result.AllResolved&&result.Targets.Length==2&&p.Searches==1&&p.Inspections==2,"reuse single anchor search");
  Console.WriteLine("PASS on-demand anchor, duplicate text, physical page/cell/style/structure proofs, conflict and stale snapshot; synthetic only");
 }
}
