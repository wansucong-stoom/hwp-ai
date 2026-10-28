using System;
using HAddon;
class DocumentTargetAnchorsTests {
 static DocumentParagraph P(string path,string text,bool obj=false){return new DocumentParagraph{Path=path,Text=text,HasObjects=obj};}
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Main(){
  var p=new[]{P("BODY/SECTION[0]/P[0]","unique anchor"),P("BODY/SECTION[0]/P[1]","repeat"),P("BODY/SECTION[0]/P[2]","other"),P("BODY/SECTION[0]/P[3]","repeat"),P("BODY/SECTION[1]/P[0]","other text")};
  var found=DocumentTargetAnchors.Find(p,new[]{"BODY/SECTION[0]/P[1]","BODY/SECTION[0]/P[3]"});
  Check(found.Length==2&&found[0].AnchorPath=="BODY/SECTION[0]/P[0]"&&found[0].ParagraphDelta==1&&found[1].AnchorPath=="BODY/SECTION[0]/P[0]"&&found[1].ParagraphDelta==3,"nearest truly unique anchor (substring collisions excluded)");
  var noAnchor=new[]{P("BODY/SECTION[0]/P[0]","repeat"),P("BODY/SECTION[0]/P[1]","repeat"),P("BODY/SECTION[1]/P[0]","elsewhere")};
  Check(DocumentTargetAnchors.Find(noAnchor,new[]{"BODY/SECTION[0]/P[1]"}).Length==0,"do not cross native paragraph-list boundary");
  noAnchor[0].HasObjects=true;Check(DocumentTargetAnchors.Find(noAnchor,new[]{"BODY/SECTION[0]/P[1]"}).Length==0,"object paragraph not an anchor");
  bool bad=false;try{DocumentTargetAnchors.Find(p,new[]{"missing"});}catch(InvalidOperationException){bad=true;}Check(bad,"unknown target rejected");
  Console.WriteLine("PASS duplicate target anchor suggestions, substring collisions and list boundaries; no native HWP");
 }
}
