using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HAddon;
internal static class NativeMemoTests {
 static int n;static void Check(bool b,string message){n++;if(!b)throw new Exception(message);}
 static void Fails(Action f,string message){bool failed=false;try{f();}catch(InvalidOperationException){failed=true;}Check(failed,message);}
 static void Main(string[] args){AppDomain.CurrentDomain.AssemblyResolve+=delegate(object s,ResolveEventArgs e){string name=new AssemblyName(e.Name).Name;return new[]{"HwpAddonHelper","Hnc.Foundation.Interface","Hnc.Foundation.InternalInterface"}.Contains(name)?Assembly.LoadFrom(Path.Combine(@"C:\Program Files (x86)\Hnc\Office 2024\HOffice130\Bin",name+".dll")):null;};try{Run(args);}catch(Exception ex){Console.Error.WriteLine(ex.Message);Environment.ExitCode=1;}}
 static void Run(string[] args){
  if(args.Length!=3)throw new ArgumentException("Pass blank HML, generated HML and memo-bearing HWPX probe.");
  string beforeRaw=File.ReadAllText(args[0],Encoding.UTF8);var before=ComposeXml.Parse(beforeRaw);var memo=RichMemo.Read(before);
  Check((int)memo["id"]==1&&(int)memo["width"]==15591&&(string)memo["lineColor"]=="#B6D7AE","blank HML memo definition converted from native color order");
  var generated=RichMemo.Read(ComposeXml.Parse(File.ReadAllText(args[1],Encoding.UTF8)));Check(memo.All(pair=>Equals(pair.Value,generated[pair.Key])),"the user's manual insertion preserved the supported memo style");
  using(var file=File.OpenRead(args[2]))RichFragmentPackage.ValidateMemo(file,memo);Check(true,"candidate fragment matches original memo style and section reference");
  Fails(()=>{using(var file=File.OpenRead(args[2]))RichFragmentPackage.ValidateMemo(file,null);},"memo definition cannot appear without the original");
  var wrong=new System.Collections.Generic.Dictionary<string,object>(memo);wrong["fillColor"]="#000000";
  Fails(()=>{using(var file=File.OpenRead(args[2]))RichFragmentPackage.ValidateMemo(file,wrong);},"different memo style rejected before editing");
  Fails(()=>RichMemo.Read(ComposeXml.Parse(beforeRaw.Replace("MemoShapeId=\"1\"","MemoShapeId=\"2\""))),"unknown section memo reference rejected");
  Console.WriteLine("PASS "+n+" native memo preservation checks on saved HML and a synthetic HWPX probe; no live HWP document modified.");
 }
}
