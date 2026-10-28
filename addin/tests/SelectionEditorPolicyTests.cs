using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using HAddon;

internal static class SelectionEditorPolicyTests {
 static int scenarios;
 static void Main() {
  AppDomain.CurrentDomain.AssemblyResolve += Resolve;
  try { Run(); Console.WriteLine("PASS: " + scenarios + " selection editor scenarios; fake backend only, no HWP UI or document used."); Environment.ExitCode = 0; }
  catch (Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
 }
 static Assembly Resolve(object sender, ResolveEventArgs args) {
  string name = new AssemblyName(args.Name).Name;
  if (name != "HwpAddonHelper" && name != "Hnc.Foundation.Interface" && name != "Hnc.Foundation.InternalInterface") return null;
  string path = Path.Combine(@"C:\Program Files (x86)\Hnc\Office 2024\HOffice130\Bin", name + ".dll");
  return File.Exists(path) ? Assembly.LoadFrom(path) : null;
 }
 static void Run() {
  Scenario("no selection does not read document text", delegate {
   var backend = new FakeBackend(new string[] {"private unselected document"}, null) { Present = false, Mode = 0 };
   var state = Begin(new SelectionEditor(backend));
   Assert(!(bool)Context(state)["selectionPresent"] && !(bool)Caps(state)["rewrite"], "no-selection context and capability");
   Assert(backend.TextReads == 0 && backend.HashReads == 0, "no-selection must not inspect, capture, or hash body");
  });
  Scenario("multi paragraph rewrite preserves outside text and individual styles", delegate {
   var backend = new FakeBackend(new string[] {"PRE alpha", "beta SUF"}, Range(0, 4, 1, 4));
   var editor = new SelectionEditor(backend); var state = Begin(editor); string id = (string)state["targetId"];
   Assert((string)state["text"] == "alpha\nbeta", "fixed selection text is joined with LF");
   var result = Rewrite(editor, id, "newalpha\nnewbeta");
   Assert(backend.Paragraphs[0].Text == "PRE newalpha" && backend.Paragraphs[1].Text == "newbeta SUF", "unselected prefix and suffix are retained");
   Assert(backend.Paragraphs[0].Style == "style-0" && backend.Paragraphs[1].Style == "style-1", "different paragraph style tokens remain intact");
   Assert(backend.AppliedParagraphs.SequenceEqual(new int[] {1, 0}), "later paragraphs apply first");
   Assert((bool)result["applied"] && (int)result["changedParagraphs"] == 2 && (string)result["targetId"] != id, "verified result refreshes target identity");
  });
  Scenario("changed LF count is rejected before any mutation", delegate {
   var backend = new FakeBackend(new string[] {"first", "second"}, Range(0, 0, 1, 6));
   var editor = new SelectionEditor(backend); var state = Begin(editor);
   Fails(delegate { Rewrite(editor, (string)state["targetId"], "single paragraph"); });
   Assert(backend.ApplyCalls == 0 && backend.Paragraphs[0].Text == "first", "LF validation must run before native apply");
  });
  Scenario("control characters are rejected before any mutation", delegate {
   var backend = new FakeBackend(new string[] {"first", "second"}, Range(0, 0, 1, 6));
   var editor = new SelectionEditor(backend); var state = Begin(editor);
   Fails(delegate { Rewrite(editor, (string)state["targetId"], "first\nsec\tond"); });
   Assert(backend.ApplyCalls == 0, "tab must be rejected across the entire proposed replacement");
   Fails(delegate { Rewrite(editor, (string)state["targetId"], "first\nsec\u0001ond"); });
   Assert(backend.ApplyCalls == 0, "C0 control must be rejected before first paragraph edit");
  });
  Scenario("empty middle segment is preserved and never applied", delegate {
   var backend = new FakeBackend(new string[] {"left", "", "right"}, Range(0, 0, 2, 5));
   var editor = new SelectionEditor(backend); var state = Begin(editor);
   Assert((string)state["text"] == "left\n\nright", "empty paragraph occupies its original LF segment");
   Rewrite(editor, (string)state["targetId"], "LEFT\n\nRIGHT");
   Assert(backend.Paragraphs[1].Text == "" && backend.Paragraphs[1].Style == "style-1", "empty paragraph text and style unchanged");
   Assert(backend.ApplyCalls == 2 && !backend.AppliedParagraphs.Contains(1), "empty range never receives mutation");
  });
  Scenario("empty segment cannot be filled by model replacement", delegate {
   var backend = new FakeBackend(new string[] {"left", "", "right"}, Range(0, 0, 2, 5));
   var editor = new SelectionEditor(backend); var state = Begin(editor);
   Fails(delegate { Rewrite(editor, (string)state["targetId"], "LEFT\ninserted\nRIGHT"); });
   Assert(backend.ApplyCalls == 0 && backend.Paragraphs[1].Text == "", "all segments validate before any mutation");
  });
  Scenario("stale full document hash prevents every edit", delegate {
   var backend = new FakeBackend(new string[] {"chosen"}, Range(0, 0, 0, 6));
   var editor = new SelectionEditor(backend); var state = Begin(editor);
   backend.HashSalt++;
   Fails(delegate { Rewrite(editor, (string)state["targetId"], "changed"); });
   Assert(backend.ApplyCalls == 0 && backend.Paragraphs[0].Text == "chosen", "external document revision invalidates snapshot before apply");
  });
  Scenario("invalid format values are rejected before native mutation", delegate {
   var backend = new FakeBackend(new string[] {"chosen"}, Range(0, 0, 0, 6));
   var editor = new SelectionEditor(backend); var state = Begin(editor); string id = (string)state["targetId"];
   Fails(delegate { Format(editor, id, new Dictionary<string, object> {{"fontSizePt", Double.NaN}}); });
   Fails(delegate { Format(editor, id, new Dictionary<string, object> {{"bold", "true"}}); });
   Fails(delegate { Format(editor, id, new Dictionary<string, object> {{"alignment", "center"}, {"bold", true}}); });
   Assert(backend.ApplyCalls == 0 && backend.Paragraphs[0].Style == "style-0", "invalid patch must not mutate a style");
  });
  Scenario("failure after one committed paragraph requires review", delegate {
   var backend = new FakeBackend(new string[] {"first", "second"}, Range(0, 0, 1, 6)) { FailOnApply = 2 };
   var editor = new SelectionEditor(backend); var state = Begin(editor);
   Fails(delegate { Rewrite(editor, (string)state["targetId"], "FIRST\nSECOND"); });
   Assert(editor.RequiresReview && backend.Paragraphs[1].Text == "SECOND" && backend.Paragraphs[0].Text == "first", "partly changed document is explicitly retained for review");
   Assert(backend.ApplyCalls == 2, "no retry or automatic undo after partial failure");
  });
  Scenario("first native call can mutate then throw without returning a result", delegate {
   var backend = new FakeBackend(new string[] {"chosen"}, Range(0, 0, 0, 6)) { FailOnApply = 1, FailAfterMutation = true };
   var editor = new SelectionEditor(backend); var state = Begin(editor);
   Fails(delegate { Rewrite(editor, (string)state["targetId"], "changed"); });
   Assert(editor.RequiresReview && backend.Paragraphs[0].Text == "changed", "backend uncertain-mutation flag propagates despite no returned changed counter");
  });
  Scenario("reverse drag normalization preserves coordinates and list boundary", delegate {
   SelectionRange normalized = SelectionRange.Normalize(2, 3, 7, 2, 1, 4);
   Assert(normalized != null && normalized.List == 2 && normalized.StartParagraph == 1 && normalized.Start == 4 && normalized.EndParagraph == 3 && normalized.End == 7, "reverse selection is ordered by paragraph then offset");
   Assert(SelectionRange.Normalize(2, 3, 7, 3, 1, 4) == null, "cross-list selection is never normalized into one list");
  });
  Scenario("cell block selection remains present but unsupported", delegate {
   var backend = new FakeBackend(new string[] {"cell secret"}, Range(0, 0, 0, 11)) { Mode = 3, Present = true };
   var state = Begin(new SelectionEditor(backend));
   Assert((bool)Context(state)["selectionPresent"] && !(bool)Caps(state)["rewrite"] && !(bool)Caps(state)["format"], "cell-block selection is not confused with no selection or enabled editing");
   Assert(backend.TextReads == 0 && backend.HashReads == 0, "cell-block contents are not read through the plain-text path");
  });
 }
 static void Scenario(string name, Action test) { test(); scenarios++; Console.WriteLine("ok " + scenarios + " - " + name); }
 static void Assert(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); }
 static void Fails(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("FAIL: expected InvalidOperationException"); }
 static Dictionary<string, object> Begin(SelectionEditor editor) { var state = editor.BeginRequest() as Dictionary<string, object>; Assert(state != null, "state must be structured"); return state; }
 static Dictionary<string, object> Caps(Dictionary<string, object> state) { return (Dictionary<string, object>)state["capabilities"]; }
 static Dictionary<string, object> Context(Dictionary<string, object> state) { return (Dictionary<string, object>)state["context"]; }
 static Dictionary<string, object> Rewrite(SelectionEditor editor, string id, string replacement) { return (Dictionary<string, object>)editor.Invoke("hwp_editor_rewrite", new Dictionary<string, object> {{"expectedTargetId", id}, {"replacement", replacement}}); }
 static object Format(SelectionEditor editor, string id, Dictionary<string, object> patch) { return editor.Invoke("hwp_editor_format", new Dictionary<string, object> {{"expectedTargetId", id}, {"patch", patch}}); }
 static SelectionRange Range(int startParagraph, int start, int endParagraph, int end) { return new SelectionRange { List = 0, StartParagraph = startParagraph, Start = start, EndParagraph = endParagraph, End = end }; }

 sealed class Paragraph {
  public string Text, Style;
 }
 // Simulates paragraph-local native operations, not IHwpObject/COM.
 sealed class FakeBackend : ISelectionBackend {
  public readonly List<Paragraph> Paragraphs = new List<Paragraph>();
  public readonly List<int> AppliedParagraphs = new List<int>();
  public bool Present = true, HasObject = false, FailAfterMutation;
  public int Mode = 1, TextReads, HashReads, ApplyCalls, FailOnApply, HashSalt;
  public bool RequiresReview { get; private set; }
  SelectionRange current;
  public FakeBackend(string[] text, SelectionRange selected) {
   current = selected == null ? null : selected.Copy();
   for (int i = 0; i < text.Length; i++) Paragraphs.Add(new Paragraph { Text = text[i], Style = "style-" + i });
  }
  public void Reset() { RequiresReview = false; }
  public SelectionInfo ReadSelection() { return new SelectionInfo { Present = Present, Mode = Mode, HasObject = HasObject, Range = current == null ? null : current.Copy() }; }
  public ParagraphBounds Inspect(int list, int paragraph) {
   TextReads++; Paragraph p = Paragraphs[paragraph]; return new ParagraphBounds { Start = 0, End = p.Text.Length, Text = p.Text };
  }
  public SelectionSpan Capture(SelectionRange range) {
   TextReads++;
   Assert(range.List == 0 && range.StartParagraph == range.EndParagraph, "fake capture remains within one list and paragraph");
   Paragraph p = Paragraphs[range.StartParagraph];
   Assert(range.Start >= 0 && range.End >= range.Start && range.End <= p.Text.Length, "captured range is valid");
   current = range.Copy();
   var properties = new List<string> {"bold", "fontSizePt"};
   if (range.Start == 0 && range.End == p.Text.Length) properties.Add("alignment");
   return new SelectionSpan { Range = range.Copy(), Text = p.Text.Substring(range.Start, range.End - range.Start), ParagraphText = p.Text,
    ContentStart = 0, ContentEnd = p.Text.Length, Empty = range.Start == range.End, FormatProperties = properties.ToArray(),
    Context = new Dictionary<string, object> {{"paragraph", range.StartParagraph}, {"style", p.Style}}, Token = p.Style };
  }
  public bool Equivalent(SelectionSpan expected, SelectionSpan observed) {
   return expected != null && observed != null && SelectionRange.Equal(expected.Range, observed.Range) && expected.Text == observed.Text &&
    expected.ParagraphText == observed.ParagraphText && expected.Empty == observed.Empty && Object.Equals(expected.Token, observed.Token);
  }
  public SpanApplyResult Apply(SelectionSpan expected, string method, Dictionary<string, object> args) {
   ApplyCalls++;
   bool fail = FailOnApply > 0 && ApplyCalls == FailOnApply;
   if (fail && !FailAfterMutation) throw new InvalidOperationException("synthetic native failure before mutation");
   Paragraph p = Paragraphs[expected.Range.StartParagraph];
   string oldText = p.Text, oldStyle = p.Style;
   SelectionRange updated = expected.Range.Copy();
   if (method == "hwp_editor_rewrite") {
    string replacement = (string)args["replacement"];
    p.Text = p.Text.Substring(0, expected.Range.Start) + replacement + p.Text.Substring(expected.Range.End);
    updated.End = updated.Start + replacement.Length;
   } else if (method == "hwp_editor_format") {
    var patch = (Dictionary<string, object>)args["patch"];
    p.Style += ";" + String.Join(",", patch.OrderBy(k => k.Key).Select(k => k.Key + "=" + Convert.ToString(k.Value, CultureInfo.InvariantCulture)));
   } else throw new Exception("Unexpected fake operation: " + method);
   AppliedParagraphs.Add(expected.Range.StartParagraph);
   if (fail) { RequiresReview = true; throw new InvalidOperationException("synthetic native mutation then failure"); }
   return new SpanApplyResult { Span = Capture(updated), Changed = oldText != p.Text || oldStyle != p.Style };
  }
  public bool Restore(SelectionRange range) { current = range.Copy(); return true; }
  public string DocumentHash() {
   HashReads++;
   return HashSalt + "#" + String.Join("|", Paragraphs.Select(p => p.Text.Length + ":" + p.Text + ":" + p.Style));
  }
 }
}
