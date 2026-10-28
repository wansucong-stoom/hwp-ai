using System;
using System.Collections.Generic;
using HAddon;
class ConcurrentEditEvidenceTests {
 static ConcurrentEditEvidence E(string revision="r1",string document="doc",string layout="layout",string mapping="mapping",string source="source",string target="target",bool trusted=true){return new ConcurrentEditEvidence(document,revision,layout,mapping,source,new Dictionary<string,string>{{"p1",target}},trusted);}
 static void Check(ConcurrentEditEvidence a,ConcurrentEditEvidence b,ConcurrentEditResult expected){var result=ConcurrentEditCheck.Compare(a,b);if(result!=expected)throw new Exception("expected "+expected+", got "+result);}
 static void Main(){
  var original=E();
  Check(original,E(),ConcurrentEditResult.Unchanged);
  Check(original,E(source:"contradiction"),ConcurrentEditResult.Unproven);
  Check(original,E(revision:"r2"),ConcurrentEditResult.SafeOutsideScope);
  Check(original,E(revision:"r2",trusted:false),ConcurrentEditResult.Unproven);
  Check(original,E(revision:"r2",layout:null),ConcurrentEditResult.Unproven);
  Check(original,E(revision:"r2",document:"other"),ConcurrentEditResult.DocumentChanged);
  Check(original,E(revision:"r2",layout:"changed"),ConcurrentEditResult.LayoutChanged);
  Check(original,E(revision:"r2",mapping:"changed"),ConcurrentEditResult.MappingChanged);
  Check(original,E(revision:"r2",source:"changed"),ConcurrentEditResult.SourceChanged);
  Check(original,E(revision:"r2",target:"changed"),ConcurrentEditResult.TargetsChanged);
  var source=new Dictionary<string,string>{{"p1","old"}};var frozen=new ConcurrentEditEvidence("doc","r1","l","m","s",source,true);source["p1"]="new";
  if(frozen.Target("p1")!="old")throw new Exception("evidence must be immutable");
  Console.WriteLine("PASS independent concurrency contract; native evidence provider not implemented");
 }
}
