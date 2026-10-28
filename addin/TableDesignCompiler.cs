using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace HAddon {
 // Untrusted model output is reduced to coordinates from a frozen snapshot.
 // This is a plan compiler only; it does not execute native actions.
 internal sealed class TableDesignCellRef {internal int Table,Row,Column;}
 internal sealed class TableDesignChange {
  internal TableDesignCellRef Target,Fill,Paragraph,Character,Left,Right,Top,Bottom;
  internal string Role;
 }
 internal static class TableDesignCompiler {
  static IDictionary<string,object> Dict(object x){var d=x as IDictionary<string,object>;if(d==null)throw new InvalidOperationException("표 디자인 계획 형식이 올바르지 않습니다.");return d;}
  static string Text(IDictionary<string,object> d,string key){object x;if(!d.TryGetValue(key,out x)||!(x is string)||String.IsNullOrWhiteSpace((string)x))throw new InvalidOperationException("표 디자인 계획의 셀 참조가 올바르지 않습니다.");return (string)x;}
  static int Integer(IDictionary<string,object> d,string key){object x;int n;if(!d.TryGetValue(key,out x)||!Int32.TryParse(Convert.ToString(x),out n)||n<0)throw new InvalidOperationException("표 셀 좌표가 올바르지 않습니다.");return n;}
  static IEnumerable<object> Items(object value){var x=value as IEnumerable;if(x==null||value is string||value is IDictionary)throw new InvalidOperationException("표 디자인 목록이 올바르지 않습니다.");foreach(object item in x)yield return item;}
  static object Field(IDictionary<string,object> d,string key){object x;if(!d.TryGetValue(key,out x))throw new InvalidOperationException("표 디자인 계획 항목이 없습니다.");return x;}
  static void Fields(IDictionary<string,object> d,params string[] names){if(d.Count!=names.Length||names.Any(n=>!d.ContainsKey(n)))throw new InvalidOperationException("표 디자인 계획에 알 수 없는 항목이 있습니다.");}
  internal static TableDesignChange[] Compile(Dictionary<string,object> snapshot,Dictionary<string,object> plan){
   Fields(plan,"assignments","uncertainties");string refId=Text(snapshot,"referenceTableId");var indexed=new Dictionary<string,Dictionary<string,TableDesignCellRef>>();int tableIndex=0;
   foreach(object rawTable in Items(Field(snapshot,"tables"))){var table=Dict(rawTable);string id=Text(table,"id");if(indexed.ContainsKey(id))throw new InvalidOperationException("표 번호가 중복됩니다.");var cells=new Dictionary<string,TableDesignCellRef>();
    foreach(object rawCell in Items(Field(table,"cells"))){var cell=Dict(rawCell);string cellId=Text(cell,"id");if(cells.ContainsKey(cellId))throw new InvalidOperationException("표 셀 번호가 중복됩니다.");cells.Add(cellId,new TableDesignCellRef{Table=tableIndex,Row=Integer(cell,"row"),Column=Integer(cell,"column")});}
    indexed.Add(id,cells);tableIndex++;
   }
   if(!indexed.ContainsKey(refId))throw new InvalidOperationException("기준 표를 찾지 못했습니다.");var source=indexed[refId];
   Func<string,TableDesignCellRef> reference=id=>{TableDesignCellRef item;if(!source.TryGetValue(id,out item))throw new InvalidOperationException("기준 표에 없는 셀 참조입니다.");return item;};
   int expected=indexed.Where(x=>x.Key!=refId).Sum(x=>x.Value.Count);var changes=new List<TableDesignChange>();var seen=new HashSet<string>();
   foreach(object raw in Items(Field(plan,"assignments"))){var a=Dict(raw);Fields(a,"tableId","cellId","role","fillSource","paragraphSource","characterSource","edges");string t=Text(a,"tableId"),c=Text(a,"cellId"),key=t+"/"+c;TableDesignCellRef target;
    if(t==refId||!indexed.ContainsKey(t)||!indexed[t].TryGetValue(c,out target)||!seen.Add(key))throw new InvalidOperationException("대상 셀을 확인하지 못했습니다.");
    var edges=Dict(Field(a,"edges"));Fields(edges,"left","right","top","bottom");string role=Text(a,"role");if(role.Length>80)throw new InvalidOperationException("셀 역할이 너무 깁니다.");
    changes.Add(new TableDesignChange{Target=target,Role=role,Fill=reference(Text(a,"fillSource")),Paragraph=reference(Text(a,"paragraphSource")),Character=reference(Text(a,"characterSource")),Left=reference(Text(edges,"left")),Right=reference(Text(edges,"right")),Top=reference(Text(edges,"top")),Bottom=reference(Text(edges,"bottom"))});
   }
   if(changes.Count!=expected)throw new InvalidOperationException("일부 대상 셀의 디자인 계획이 없습니다.");
   foreach(object u in Items(Field(plan,"uncertainties")))if(!(u is string)||((string)u).Length>500)throw new InvalidOperationException("표 분석 주의사항이 올바르지 않습니다.");
   return changes.ToArray();
  }
 }
}
