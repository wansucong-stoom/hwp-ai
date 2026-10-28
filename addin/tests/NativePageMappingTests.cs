using System;
using System.Collections.Generic;
using System.Linq;
using HAddon;

class NativePageMappingTests {
 sealed class FakePositions:IPagePositionBackend {
  int list=8,para=0,pos=0;bool moved;
  public string CursorState(){return list+":"+para+":"+pos;}
  public void CheckDocument(){}
  public bool Move(int l,int p,int x){list=l;para=p;pos=x;moved=true;return true;}
  public bool IsAt(int l,int p,int x){return list==l&&para==p&&pos==x;}
  public bool TryPage(out int p){p=list==3?(pos==0?1:2):1;return true;}
  public bool Restore(){list=8;para=0;pos=0;return true;}
 }
 sealed class Fake:IPageMappingSource {
  internal int Verifications,Scans,Inspections,Locations;internal int StaleAt=-1;internal NativePageSnapshot Caller;
  public void Verify(NativePageSnapshot s){Verifications++;if(Verifications==StaleAt)throw new InvalidOperationException("changed");if(s.Pages[0]!="unique repeat")throw new Exception("snapshot was mutated");}
  public NativeScanItem[] Scan(){Scans++;if(Caller!=null)Caller.Pages[0]="caller changed";return new[]{new NativeScanItem{List=1,Paragraph=0,Text="unique"}};}
  public InspectedNativeParagraph[] Inspect(NativePageSnapshot s,NativeScanItem[] a){Inspections++;return new[]{new InspectedNativeParagraph{List=1,Paragraph=0,Start=0,End=6,Text="unique"},new InspectedNativeParagraph{List=2,Paragraph=0,Start=0,End=6,Text="repeat"},new InspectedNativeParagraph{List=3,Paragraph=0,Start=0,End=5,Text="split"}};}
  public PagePositionEvidence[] PagePositions(NativePageSnapshot s,DocumentAddressCandidate[] a){Locations++;return NativePagePositionEvidence.Collect(new FakePositions(),a);}
 }
 static NativePageSnapshot Snapshot(){return new NativePageSnapshot{DocumentId=3,Path="example",Digest="fixed",Pages=new[]{"unique repeat","split"},Paragraphs=new[]{new DocumentParagraph{Path="a",Text="unique"},new DocumentParagraph{Path="b",Text="repeat"},new DocumentParagraph{Path="c",Text="repeat"},new DocumentParagraph{Path="d",Text="split"}}};}
 static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
 static void Main(){
  var snapshot=Snapshot();var fake=new Fake{Caller=snapshot};var result=NativePageMapping.Collect(fake,snapshot,()=>false);
  Check(result.Candidates.Length==1&&result.Candidates[0].XmlPath=="a"&&result.Candidates[0].Page==1,"duplicate and split omitted");
  Check(result.StructuralMatches==2&&result.PositionMatches==1&&result.TextParagraphs==4&&result.UnmappedTextParagraphs==3&&fake.Verifications>=4,"independent verification and counters");
  fake=new Fake{StaleAt=3};bool rejected=false;try{NativePageMapping.Collect(fake,Snapshot(),()=>false);}catch(InvalidOperationException e){rejected=e.Message=="changed";}Check(rejected&&fake.Locations==0,"stale revision does not reach page evidence");
  fake=new Fake();rejected=false;try{NativePageMapping.Collect(fake,Snapshot(),()=>true);}catch(OperationCanceledException){rejected=true;}Check(rejected&&fake.Scans==0,"cancel before scan");
  Console.WriteLine("PASS provisional mapping pipeline: duplicate text, split pages, frozen data, stale revision, cancellation; synthetic only");
 }
}
