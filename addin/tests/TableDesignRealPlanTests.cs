using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;
using HAddon;
class TableDesignRealPlanTests {
 static void Main(string[] args){
  if(args.Length!=2)throw new ArgumentException("Saved HML and temporary AI plan required");
  var xml=ComposeXml.Parse(File.ReadAllText(args[0]));var snapshot=TableDesignSnapshot.Create(xml,0);
  var serializer=new JavaScriptSerializer{MaxJsonLength=4*1024*1024};var plan=serializer.DeserializeObject(File.ReadAllText(args[1])) as Dictionary<string,object>;
  var changes=TableDesignCompiler.Compile(snapshot,plan);var native=TableStylePlan.BuildFromDesign(xml,0,changes);
  TableStylePlan.VerifyFinalPlan(xml,native);TableStylePlan.Verify(xml,ComposeXml.Parse(xml.Document.OuterXml),native,0);
  Console.WriteLine("PASS AI plan compiles and has compatible shared edges for "+changes.Length+" cells; no HWP write");
 }
}
