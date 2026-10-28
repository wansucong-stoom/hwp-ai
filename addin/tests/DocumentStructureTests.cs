using System;
using System.Linq;
using HAddon;
class DocumentStructureTests {
 static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
 static void Main(){
  const string xml="<HWPML><BODY><SECTION><P><TEXT><CHAR>앞</CHAR><TABLE><ROW><CELL RowAddr='0' ColAddr='0'><PARALIST><P><TEXT><CHAR>셀</CHAR></TEXT></P><P><TEXT><CHAR>셀</CHAR></TEXT></P></PARALIST></CELL></ROW></TABLE></TEXT></P><P><TEXT><CHAR>뒤</CHAR></TEXT></P></SECTION></BODY></HWPML>";
  var p=DocumentStructure.Read(xml);Assert(p.Length==4,"paragraph count");
  Assert(p[0].Text=="앞"&&p[0].HasObjects&&p[0].CellPath==null,"body object");
  Assert(p[1].Text=="셀"&&p[1].CellPath.Contains("/CELL[0]")&&p[1].Path!=p[2].Path,"duplicate text must retain distinct paths");
  Assert(!p[1].HasObjects&&p[3].Text=="뒤"&&p[3].CellPath==null,"paragraph content");
  Assert(DocumentStructure.Read("<HWPML><BODY><SECTION><P><TEXT><CHAR>before<CTRL/>after</CHAR></TEXT></P></SECTION></BODY></HWPML>")[0].HasObjects,"nested controls must be marked as objects");
  const string metadata="<SECDEF><MASTERPAGE><PARALIST><P><TEXT><CHAR>header</CHAR></TEXT></P></PARALIST></MASTERPAGE></SECDEF><COLDEF/>";
  var sectioned=DocumentStructure.Read("<HWPML><BODY><SECTION><P><TEXT>"+metadata+"<CHAR>body</CHAR></TEXT></P></SECTION></BODY></HWPML>");
  Assert(sectioned.Length==1&&sectioned[0].Text=="body"&&!sectioned[0].HasObjects,"leading layout paragraphs are not body objects or targets");
  Assert(DocumentStructure.Read("<HWPML><BODY><SECTION><P><TEXT><CHAR>body</CHAR>"+metadata+"</TEXT></P></SECTION></BODY></HWPML>")[0].HasObjects,"layout controls after visible text remain objects");
  bool rejected=false;try{DocumentStructure.Read("<!DOCTYPE HWPML [<!ENTITY x 'x'>]><HWPML><BODY>&x;</BODY></HWPML>");}catch{rejected=true;}
  Assert(rejected,"DTD must be prohibited");
  Console.WriteLine("PASS HWPML structural paths, nested cells, duplicates, objects and DTD rejection");
 }
}
