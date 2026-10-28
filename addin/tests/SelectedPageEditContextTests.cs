using System;
using HAddon;
namespace HAddon { internal sealed class PageScope {internal int[] Targets,Sources;} }
class SelectedPageEditContextTests {
 static void Reject(Action action){try{action();}catch(InvalidOperationException){return;}throw new Exception("expected rejection");}
 static void Main(){
  var pages=new[]{"summary target", "first detail", ""};var scope=new PageScope{Targets=new[]{1},Sources=new[]{2,3}};
  if(SelectedPageEditContext.Build(pages,scope,1,"target").Length!=2)throw new Exception("valid scope");
  Reject(()=>SelectedPageEditContext.Build(pages,scope,2,"detail"));
  Reject(()=>SelectedPageEditContext.Build(pages,scope,1,"unmatched"));
  Reject(()=>SelectedPageEditContext.Build(pages,new PageScope{Targets=new[]{1},Sources=new[]{2,2}},1,"target"));
  Reject(()=>SelectedPageEditContext.Build(pages,new PageScope{Targets=new[]{1},Sources=new[]{3}},1,"target"));
  Console.WriteLine("PASS selected page, source scope, no silent truncation and empty reference checks");
 }
}
