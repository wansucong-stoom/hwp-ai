using System;
using System.Globalization;
using Hwp.Addon;

namespace HAddon {
 // PrintPageNo was verified as an integer/JValue in the installed HWP 2024
 // add-in at pages 1, 5 and 6, including a table caret. A number alone does
 // not establish behavior for page-number resets or spanning paragraphs;
 // native endpoints and the frozen page text must also be cross-checked.
 internal static class NativePageIndicator {
  // HwpAddonHelper.GetItem may return a Newtonsoft JValue rather than the
  // underlying CLR primitive. Do not coerce text, floating-point values or
  // arbitrary objects into page numbers.
  internal static object UnwrapNumber(object raw){
   if(raw!=null&&raw.GetType().FullName=="Newtonsoft.Json.Linq.JValue"){
    var value=raw.GetType().GetProperty("Value");if(value==null)return null;
    try{return value.GetValue(raw,null);}catch{return null;}
   }
   return raw;
  }
  static bool Integral(object value){return value is byte||value is sbyte||value is short||value is ushort||value is int||value is uint||value is long||value is ulong;}
  // Diagnostics contain API metadata and a numeric page value only; never
  // include the document's text or exception messages, which can contain paths.
  internal static string Diagnose(IHwpObject document){
   if(document==null)return "KeyIndicator: 문서 없음";
   IHwpParameterSet set;
   try{set=document.KeyIndicator();}catch(Exception ex){return "KeyIndicator 호출 예외: "+ex.GetType().Name;}
   if(set==null)return "KeyIndicator: 반환 없음";
   var details=new System.Text.StringBuilder("KeyIndicator: 반환됨");
   try{details.Append("\n세트 ID: ").Append(set.SetID??"(없음)");}catch(Exception ex){details.Append("\n세트 ID 읽기 예외: ").Append(ex.GetType().Name);}
   try{details.Append("\n항목 수: ").Append(set.Count);}catch(Exception ex){details.Append("\n항목 수 읽기 예외: ").Append(ex.GetType().Name);}
   bool exists=false;try{exists=set.ItemExist("PrintPageNo");details.Append("\nPrintPageNo 항목: ").Append(exists?"있음":"없음");}catch(Exception ex){details.Append("\n항목 확인 예외: ").Append(ex.GetType().Name);}
   try{
    var value=set.GetItem("PrintPageNo");details.Append("\n쪽 번호 자료형: ").Append(value==null?"null":value.GetType().FullName);
    var unwrapped=UnwrapNumber(value);if(!Object.ReferenceEquals(value,unwrapped))details.Append("\n내부 자료형: ").Append(unwrapped==null?"null":unwrapped.GetType().FullName);
    if(Integral(unwrapped))details.Append("\n쪽 번호 값: ").Append(Convert.ToString(unwrapped,CultureInfo.InvariantCulture));
   }catch(Exception ex){details.Append("\n쪽 번호 읽기 예외: ").Append(ex.GetType().Name);}
   return details.ToString();
  }
  internal static bool TryReadRaw(IHwpObject document,out int rawPage){
   rawPage=0;if(document==null)return false;
   try{
    var indicator=document.KeyIndicator();
    if(indicator==null||!indicator.ItemExist("PrintPageNo"))return false;
    object raw=UnwrapNumber(indicator.GetItem("PrintPageNo"));
    if(!Integral(raw))return false;
    long value=Convert.ToInt64(raw,CultureInfo.InvariantCulture);
    if(value<0||value>Int32.MaxValue)return false;
    rawPage=(int)value;return true;
   }catch{return false;}
  }
 }
}
