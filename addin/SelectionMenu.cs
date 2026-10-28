using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Threading;
using Hnc.Foundation.Interface;

namespace HAddon {
 // The manifest adds our button through Hancom's normal add-on loader.
 // Its shipped managed implementation exposes these three public properties.
 // Do not mutate Items while ContextMenu.Update is enumerating it.
 public sealed class SelectionMenuUpdater : IHncAddonUpdater {
  static readonly Dictionary<string,string> menuIds=new Dictionary<string,string>(StringComparer.Ordinal) {
   {"IDC_STOOM_AI_EDIT_SELECTION","IDC_Body_Block"},{"IDC_STOOM_AI_EDIT_BODY","IDC_Edit_Body"},
   {"IDC_STOOM_AI_REQUEST_TABLE","IDC_Table"},{"IDC_STOOM_AI_EDIT_CELLS","IDC_Cell_Block"},{"IDC_STOOM_AI_REQUEST_TEXTBOX","IDC_Textbox"}
  };
  readonly HashSet<object> queued=new HashSet<object>();
  bool disposed;
  public void Dispose(){disposed=true;queued.Clear();}
  public void Update(IHncWidget sender,IHncFrameEngine frameEngine,HncAddonBase addonMain){
   if(disposed||sender==null)return;
   try{
    var type=sender.GetType();
    var idProperty=type.GetProperty("Id",BindingFlags.Public|BindingFlags.Instance);
    string id=idProperty==null?null:Convert.ToString(idProperty.GetValue(sender,null));
    string expectedMenu;if(id==null||!menuIds.TryGetValue(id,out expectedMenu))return;
    var parentProperty=type.GetProperty("Parent",BindingFlags.Public|BindingFlags.Instance);
    object parent=parentProperty==null?null:parentProperty.GetValue(sender,null);
    if(parent==null||parent.GetType().FullName!="Hnc.Controls.Views.ContextMenu")return;
    var menuIdProperty=parent.GetType().GetProperty("Id",BindingFlags.Public|BindingFlags.Instance);
    string menuId=menuIdProperty==null?null:Convert.ToString(menuIdProperty.GetValue(parent,null));
    if(menuId!=expectedMenu)return;
    var itemsProperty=parent.GetType().GetProperty("Items",BindingFlags.Public|BindingFlags.Instance);
    var bottomProperty=type.GetProperty("MoveToBottom",BindingFlags.Public|BindingFlags.Instance);
    var items=itemsProperty==null?null:itemsProperty.GetValue(parent,null) as IList;
    if(items==null||items.IsReadOnly||items.IsFixedSize||bottomProperty==null||!bottomProperty.CanWrite)return;
    bottomProperty.SetValue(sender,false,null);
    if(items.IndexOf(sender)<=0||!queued.Add(sender))return;
    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Send,new Action(delegate{
     try{
      if(disposed)return;
      // The original menu may have closed or been replaced before this callback.
      if(Object.ReferenceEquals(parentProperty.GetValue(sender,null),parent)&&Object.ReferenceEquals(itemsProperty.GetValue(parent,null),items))
       MoveOwnItemToTop(items,sender);
     }catch{ /* Preserve the normal appended menu entry if the host changed. */ }
     finally{queued.Remove(sender);}
    }));
   }catch{ /* A future host can retain the normal manifest menu placement. */ }
  }
  internal static void MoveOwnItemToTop(IList items,object item){
   int index=items.IndexOf(item);if(index<=0)return;
   // Hancom handles ObservableCollection Remove/Add notifications, not Move.
   // ContextMenu.RemoveAt disposes the item, so move through its Items collection.
   items.RemoveAt(index);
   try{items.Insert(0,item);}catch{items.Insert(Math.Min(index,items.Count),item);throw;}
  }
 }
}
