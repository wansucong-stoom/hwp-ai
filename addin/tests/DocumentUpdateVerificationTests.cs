using System;
using System.Collections.Generic;
using HAddon;
class DocumentUpdateVerificationTests {
 static void Check(bool b,string m){if(!b)throw new Exception(m);}
 static void Main(){
  const string xml="<HWPML><HEAD/><BODY><SECTION><P><TEXT CharShape='1'><CHAR>old</CHAR></TEXT></P><P><TEXT><CHAR>keep</CHAR></TEXT></P></SECTION></BODY></HWPML>";
  var plan=new[]{new DocumentUpdateTarget{Id="a",Original="old",Replacement="new"}};
  var verify=new DocumentUpdateVerification(xml,new Dictionary<string,string>{{"a","BODY/SECTION[0]/P[0]"}});
  verify.Verify(xml,plan,0);verify.Verify(xml.Replace(">old<",">new<"),plan,1);
  bool failed=false;try{verify.Verify(xml.Replace(">old<",">new<").Replace(">keep<",">other<"),plan,1);}catch(InvalidOperationException){failed=true;}Check(failed,"untouched text change");
  failed=false;try{verify.Verify(xml.Replace(">old<",">new<").Replace("CharShape='1'","CharShape='2'"),plan,1);}catch(InvalidOperationException){failed=true;}Check(failed,"style change");
  failed=false;try{new DocumentUpdateVerification(xml.Replace("<CHAR>old</CHAR>","<CHAR>old</CHAR><PICTURE/>"),new Dictionary<string,string>{{"a","BODY/SECTION[0]/P[0]"}}).ExpectedDigest(plan,1);}catch(InvalidOperationException){failed=true;}Check(failed,"object must not be overwritten");
  var split=xml.Replace("<TEXT CharShape='1'><CHAR>old</CHAR></TEXT>","<TEXT CharShape='1'><CHAR>o</CHAR></TEXT><TEXT CharShape='1'><CHAR>ld</CHAR></TEXT>");
  var splitVerify=new DocumentUpdateVerification(split,new Dictionary<string,string>{{"a","BODY/SECTION[0]/P[0]"}});
  splitVerify.Verify(xml.Replace(">old<",">new<"),plan,1);
  splitVerify.Verify(split.Replace("<CHAR>o</CHAR>","<CHAR>n</CHAR>").Replace("<CHAR>ld</CHAR>","<CHAR>ew</CHAR>"),plan,1);
  failed=false;try{splitVerify.Verify(xml.Replace(">old<",">new<").Replace("CharShape='1'","CharShape='2'"),plan,1);}catch(InvalidOperationException){failed=true;}Check(failed,"style must survive merged equivalent runs");
  failed=false;try{new DocumentUpdateVerification(split.Replace("<TEXT CharShape='1'><CHAR>ld", "<TEXT CharShape='2'><CHAR>ld"),new Dictionary<string,string>{{"a","BODY/SECTION[0]/P[0]"}}).ExpectedDigest(plan,1);}catch(InvalidOperationException){failed=true;}Check(failed,"mixed styles remain unsupported");
  failed=false;try{splitVerify.Verify(split.Replace("<CHAR>o</CHAR>","<CHAR>n<!--metadata--></CHAR>").Replace("<CHAR>ld</CHAR>","<CHAR>ew</CHAR>"),plan,1);}catch(InvalidOperationException){failed=true;}Check(failed,"hidden char metadata must not be discarded");
  string mappings="<HEAD><MAPPINGTABLE><CHARSHAPELIST><CHARSHAPE Id='1' Height='1000'/><CHARSHAPE Id='2' Height='1000'/><CHARSHAPE Id='3' Height='1100'/></CHARSHAPELIST></MAPPINGTABLE></HEAD>";
  string semantic=split.Replace("<HEAD/>",mappings).Replace("<TEXT CharShape='1'><CHAR>ld", "<TEXT CharShape='2'><CHAR>ld");
  var semanticVerify=new DocumentUpdateVerification(semantic,new Dictionary<string,string>{{"a","BODY/SECTION[0]/P[0]"}});
  string semanticAfter=xml.Replace("<HEAD/>",mappings).Replace(">old<",">new<").Replace("<TEXT CharShape='1'><CHAR>new", "<TEXT CharShape='2'><CHAR>new");
  semanticVerify.Verify(semanticAfter,plan,1);
  failed=false;try{semanticVerify.Verify(semanticAfter.Replace("<TEXT CharShape='2'>", "<TEXT CharShape='3'>"),plan,1);}catch(InvalidOperationException){failed=true;}Check(failed,"semantic style difference remains protected");
  failed=false;try{semanticVerify.Verify(semanticAfter.Replace("Id='2' Height='1000'", "Id='2' Height='900'"),plan,1);}catch(InvalidOperationException){failed=true;}Check(failed,"definition changes outside paragraph remain protected");
  failed=false;try{semanticVerify.Verify(semanticAfter.Replace(">keep<",">other<"),plan,1);}catch(InvalidOperationException){failed=true;}Check(failed,"semantic normalization only masks target text");
  failed=false;try{new DocumentUpdateVerification(semantic.Replace("<TEXT CharShape='2'>", "<TEXT CharShape='2' Lang='foreign'>"),new Dictionary<string,string>{{"a","BODY/SECTION[0]/P[0]"}}).ExpectedDigest(plan,1);}catch(InvalidOperationException){failed=true;}Check(failed,"other run attributes remain protected");
  const string metadata="<SECDEF><MASTERPAGE><PARALIST><P><TEXT><CHAR>header</CHAR></TEXT></P></PARALIST></MASTERPAGE></SECDEF><COLDEF Count='1'/>";
  string sectioned=xml.Replace("<CHAR>old</CHAR>",metadata+"<CHAR>old</CHAR>");
  var sectionedVerify=new DocumentUpdateVerification(sectioned,new Dictionary<string,string>{{"a","BODY/SECTION[0]/P[0]"}});
  sectionedVerify.Verify(sectioned.Replace(">old<",">new<"),plan,1);
  failed=false;try{sectionedVerify.Verify(sectioned.Replace(">old<",">new<").Replace(">header<",">changed<"),plan,1);}catch(InvalidOperationException){failed=true;}Check(failed,"master-page content is not masked");
  failed=false;try{sectionedVerify.Verify(sectioned.Replace(">old<",">new<").Replace("Count='1'", "Count='2'"),plan,1);}catch(InvalidOperationException){failed=true;}Check(failed,"column metadata is not masked");
  Console.WriteLine("PASS full-document expected-prefix verification with content, style and object safeguards");
 }
}
