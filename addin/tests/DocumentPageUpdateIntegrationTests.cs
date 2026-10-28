using System;
using System.Collections.Generic;
using HAddon;
class DocumentPageUpdateIntegrationTests {
 const string Original="<HWPML><HEAD/><BODY><SECTION><P><TEXT><TABLE><ROW><CELL><PARALIST><P><TEXT><CHAR>old summary 1</CHAR></TEXT></P></PARALIST></CELL><CELL><PARALIST><P><TEXT><CHAR>old summary 2</CHAR></TEXT></P></PARALIST></CELL></ROW></TABLE></TEXT></P><P><TEXT><CHAR>later detail</CHAR></TEXT></P></SECTION></BODY></HWPML>";
 const string Prefix="BODY/SECTION[0]/P[0]/TEXT[0]/TABLE[0]/ROW[0]/";
 sealed class Fake:IDocumentUpdateBackend {
  internal string Xml=Original;
  readonly DocumentUpdateVerification verification=new DocumentUpdateVerification(Original,new Dictionary<string,string>{{"one",Prefix+"CELL[0]/PARALIST[0]/P[0]"},{"two",Prefix+"CELL[1]/PARALIST[0]/P[0]"}});
  public void Prepare(string revision,DocumentUpdateTarget t){if(revision!="version")throw new Exception("version");verification.ExpectedDigest(new[]{t},1);}
  public void Verify(string revision,IList<DocumentUpdateTarget> plan,int applied){verification.Verify(Xml,plan,applied);}
  public void Replace(DocumentUpdateTarget t){if(!Xml.Contains(">"+t.Original+"<"))throw new Exception("missing text");Xml=Xml.Replace(">"+t.Original+"<",">"+t.Replacement+"<");}
 }
 static void Main(){
  var f=new Fake();var transaction=new DocumentUpdateTransaction();
  var plan=new[]{new DocumentUpdateTarget{Id="one",Original="old summary 1",Replacement="updated 1"},new DocumentUpdateTarget{Id="two",Original="old summary 2",Replacement="updated 2"}};
  transaction.ApplyAsync(f,"version",plan,()=>false,null).GetAwaiter().GetResult();
  if(transaction.ChangedCount!=2||!f.Xml.Contains(">later detail<")||!f.Xml.Contains(">updated 1<")||!f.Xml.Contains(">updated 2<"))throw new Exception("document update failed");
  Console.WriteLine("PASS synthetic table-summary update and full-document verification; no model or Hancom runtime");
 }
}
