using System;
using System.Collections.Generic;
using Hwp.Addon;

namespace HAddon {
 // Experimental native primitive: caller must navigate to the intended cell,
 // pin the document, and verify the entire transaction before/after any write.
 // It is not used by the installed add-in's edit path.
 internal sealed class NativeTableBorderValue { internal object Type,Width,Color; }
 internal static class NativeTableBorderParameters {
  static readonly string[] Sides={"Left","Right","Top","Bottom"};
  static string Name(IHwpParameterSet set,string property,string side){
   string correct="Border"+property+side,legacy="BorderCorlor"+side;
   if(set.ItemExist(correct))return correct;
   if(property=="Color"&&set.ItemExist(legacy))return legacy;
   throw new InvalidOperationException("한글 셀 테두리 "+side+" "+property+" 속성을 확인하지 못했습니다.");
  }
  static IHwpParameterSet Defaults(IHwpObject doc,out IHwpAction action){
   if(doc==null)throw new ArgumentNullException("doc");action=doc.CreateAction("CellBorderFill");var set=action==null?null:action.CreateSet();
   if(action==null||set==null||!action.GetDefault(set))throw new InvalidOperationException("한글 셀 테두리 명령을 준비하지 못했습니다.");return set;
  }
  static void Side(string side){if(Array.IndexOf(Sides,side)<0)throw new InvalidOperationException("테두리 방향이 올바르지 않습니다.");}
  internal static NativeTableBorderValue Read(IHwpObject doc,string side){
   Side(side);IHwpAction action;var set=Defaults(doc,out action);
   return new NativeTableBorderValue{Type=set.GetItem(Name(set,"Type",side)),Width=set.GetItem(Name(set,"Width",side)),Color=set.GetItem(Name(set,"Color",side))};
  }
  internal static void Apply(IHwpObject doc,Dictionary<string,NativeTableBorderValue> sides){
   if(sides==null||sides.Count==0||sides.Count>4)throw new InvalidOperationException("적용할 테두리를 확인하지 못했습니다.");
   IHwpAction action;var set=Defaults(doc,out action);var updates=new List<KeyValuePair<string,object>>();
   foreach(var pair in sides){Side(pair.Key);if(pair.Value==null||pair.Value.Type==null||pair.Value.Width==null||pair.Value.Color==null)throw new InvalidOperationException("테두리 서식 값이 없습니다.");
    updates.Add(new KeyValuePair<string,object>(Name(set,"Type",pair.Key),pair.Value.Type));
    updates.Add(new KeyValuePair<string,object>(Name(set,"Width",pair.Key),pair.Value.Width));
    updates.Add(new KeyValuePair<string,object>(Name(set,"Color",pair.Key),pair.Value.Color));
   }
   foreach(var update in updates)set.SetItem(update.Key,update.Value);
   if(action.Execute(set)==0)throw new InvalidOperationException("한글 셀 테두리 명령을 완료하지 못했습니다.");
  }
 }
}
