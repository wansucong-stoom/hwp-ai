using System;
using System.Collections;
using System.IO;
using System.Reflection;
using HAddon;

internal static class SelectionMenuPolicyTests {
 static int checks;
 static void Main() {
  AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs args) {
   if (new AssemblyName(args.Name).Name != "Hnc.Foundation.Interface") return null;
   return Assembly.LoadFrom(@"C:\Program Files (x86)\Hnc\Office 2024\HOffice130\Bin\Hnc.Foundation.Interface.dll");
  };
  try { Run(); Console.WriteLine("PASS: " + checks + " selection menu policy checks"); Environment.ExitCode = 0; }
  catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
 }
 static void Run() {
  object cut = new object(), copy = new object(), own = new object(), paste = new object();
  var list = new ArrayList(new object[] {cut, copy, own, paste});
  SelectionMenuUpdater.MoveOwnItemToTop(list, own);
  Order(list, new object[] {own, cut, copy, paste}, "only own entry moves; native relative order preserved");
  SelectionMenuUpdater.MoveOwnItemToTop(list, own);
  Order(list, new object[] {own, cut, copy, paste}, "already first is idempotent with no duplicate");
  SelectionMenuUpdater.MoveOwnItemToTop(list, new object());
  Order(list, new object[] {own, cut, copy, paste}, "absent item leaves builtin menu unchanged");
  var empty = new ArrayList();
  SelectionMenuUpdater.MoveOwnItemToTop(empty, own);
  True(empty.Count == 0, "empty menu unchanged");
  var one = new ArrayList(new object[] {own});
  SelectionMenuUpdater.MoveOwnItemToTop(one, own);
  Order(one, new object[] {own}, "single item retained");
  var rollback = new FailFirstTopInsert(new object[] {cut, copy, own, paste});
  bool failed = false;
  try { SelectionMenuUpdater.MoveOwnItemToTop(rollback, own); } catch (InvalidOperationException) { failed = true; }
  True(failed, "insertion error reaches caller");
  Order(rollback, new object[] {cut, copy, own, paste}, "failed move restores original item and order");
  var observable = new System.Collections.ObjectModel.ObservableCollection<object>(new object[] {cut, own, copy});
  var changes = new ArrayList();
  observable.CollectionChanged += delegate(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) { changes.Add(e.Action); };
  SelectionMenuUpdater.MoveOwnItemToTop((IList)observable, own);
  True(changes.Count == 2 &&
       (System.Collections.Specialized.NotifyCollectionChangedAction)changes[0] == System.Collections.Specialized.NotifyCollectionChangedAction.Remove &&
       (System.Collections.Specialized.NotifyCollectionChangedAction)changes[1] == System.Collections.Specialized.NotifyCollectionChangedAction.Add,
       "move uses Remove then Add notifications supported by native sync handler");
 }
 sealed class FailFirstTopInsert : ArrayList {
  bool fail = true;
  public FailFirstTopInsert(object[] initial) : base(initial) { }
  public override void Insert(int index, object value) {
   if (index == 0 && fail) { fail = false; throw new InvalidOperationException("synthetic native insertion failure"); }
   base.Insert(index, value);
  }
 }
 static void Order(IList actual, object[] expected, string label) {
  bool same = actual.Count == expected.Length;
  if (same) for (int i = 0; i < expected.Length; i++) if (!Object.ReferenceEquals(actual[i], expected[i])) same = false;
  True(same, label);
 }
 static void True(bool condition, string label) {
  checks++;
  if (!condition) throw new Exception("FAIL: " + label);
 }
}
