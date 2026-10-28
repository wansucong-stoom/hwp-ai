using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HAddon;
namespace HAddon {
 internal sealed class BridgeClient{public Task<Dictionary<string,object>> Request(Dictionary<string,object> x,string id){throw new NotImplementedException();}public static Dictionary<string,object> Data(Dictionary<string,object> x){return x;}}
 internal sealed class NativePageSnapshot{internal int DocumentId;internal string Path,Digest;internal string[] Pages;}
 internal static class TableStableSnapshot{internal static string Digest(string x){return x;}}
}
class PageScopeServiceTests {
 static Dictionary<string,object> Input(object[] t,object[] s,object[] u){return new Dictionary<string,object>{{"targetPages",t},{"sourcePages",s},{"uncertainties",u}};}
 static void Reject(Action action){try{action();}catch(InvalidOperationException){return;}throw new Exception("expected rejection");}
 static void Main(){
  var scope=PageScopeService.Compile(Input(new object[]{1,2,3,4,5},new object[]{6,7,8},new object[0]),8);
  if(scope.Targets.Length!=5||scope.Sources[0]!=6||scope.Sources[2]!=8)throw new Exception("page scope");
  Reject(()=>PageScopeService.Compile(Input(new object[]{1,1},new object[]{6},new object[0]),8));
  Reject(()=>PageScopeService.Compile(Input(new object[]{2,1},new object[]{6},new object[0]),8));
  Reject(()=>PageScopeService.Compile(Input(new object[]{1},new object[]{9},new object[0]),8));
  Reject(()=>PageScopeService.Compile(Input(new object[]{1},new object[]{6},new object[]{"ambiguous"}),8));
  Reject(()=>PageScopeService.Compile(Input(new object[]{1},new object[0],new object[0]),8));
  Console.WriteLine("PASS page scope proposal validation and ambiguity rejection");
 }
}
