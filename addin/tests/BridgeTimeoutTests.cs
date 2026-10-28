using System;
using HAddon;
class BridgeTimeoutTests {
 static void Main(){
  if(BridgeClient.TimeoutSeconds("page-scope")!=250||BridgeClient.TimeoutSeconds("page-edit-plan")!=250||BridgeClient.TimeoutSeconds("document-target-discovery")!=250||BridgeClient.TimeoutSeconds("rich-compose")!=800||BridgeClient.TimeoutSeconds("status")!=65)throw new Exception("Bridge timeout policy");
  Console.WriteLine("PASS page-analysis bridge timeout policy");
 }
}
