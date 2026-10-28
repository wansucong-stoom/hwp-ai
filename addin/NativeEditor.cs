using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Hwp.Addon;

namespace HAddon {
 // All calls run on the host UI dispatcher. Full document serialization stays local.
 internal sealed class NativeEditor {
  const int MaxText = 12000, MaxBlockXml = 8 * 1024 * 1024, MaxDocumentXml = 32 * 1024 * 1024;
  readonly Func<IHwpObject> getDocument;
  bool active;
  public bool RequiresReview { get; private set; }
  Snapshot target;
  Dictionary<string, object> disabledContext;

  internal sealed class Snapshot {
   public string TargetId, Path, Text, Hash, Reason, ParagraphText;
   public int DocumentId, List, Paragraph, Start, End, ParagraphNativeStart, ParagraphNativeEnd, PlainStart, PlainEnd;
   public bool WholeParagraph;
   public IHwpParameterSet CharShape, ParaShape;
   public Dictionary<string, object> Diagnostics;
   public bool Supported { get { return String.IsNullOrEmpty(Reason); } }
  }
  sealed class BlockInfo {
   public string Root, Text, Reason, InputPrefix;
   public int Paragraphs, InputLength;
   public bool LeadingBomRemoved;
   public int LeadingMetadata;
   public bool HasObjects;
   public readonly HashSet<string> Shapes = new HashSet<string>(StringComparer.Ordinal);
  }
  internal sealed class ParagraphInfo {
   public string Text;
   public int NativeStart, NativeEnd, PlainStart, PlainEnd;
  }

  public NativeEditor(Func<IHwpObject> getDocument) {
   if (getDocument == null) throw new ArgumentNullException("getDocument");
   this.getDocument = getDocument;
  }
  // Internal aggregate editor adapter. A fresh native target is created only
  // after its pinned contents, boundaries, and complete formats still match.
  internal Snapshot CurrentSnapshot { get { return target; } }
  internal static bool SameContentSnapshot(Snapshot before, Snapshot after) {
   return before != null && after != null && before.Supported && after.Supported &&
    SameRange(before, after) && before.Text == after.Text && before.ParagraphText == after.ParagraphText &&
    before.ParagraphNativeStart == after.ParagraphNativeStart && before.ParagraphNativeEnd == after.ParagraphNativeEnd &&
    before.PlainStart == after.PlainStart && before.PlainEnd == after.PlainEnd &&
    before.CharShape != null && after.CharShape != null && before.CharShape.IsEquivalent(after.CharShape) &&
    before.ParaShape != null && after.ParaShape != null && before.ParaShape.IsEquivalent(after.ParaShape);
  }
  internal static Dictionary<string, object> DescribeSnapshot(Snapshot snapshot) { return Context(snapshot); }
  internal static string ReadDocumentHash(IHwpObject doc) {
   string full = doc.GetTextFile("HWPML2X", "");
   if (String.IsNullOrEmpty(full) || full.Length > MaxDocumentXml) throw new InvalidOperationException("문서의 변경 확인 정보를 준비할 수 없습니다.");
   using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(full)));
  }
  public object BeginRequest() {
   active = true; RequiresReview = false; target = null; disabledContext = null;
   try {
    Snapshot captured = Capture(getDocument());
    if (captured.Supported) { captured.TargetId = NewId(); target = captured; }
    else disabledContext = Context(captured);
   } catch (Exception ex) {
    disabledContext = new Dictionary<string, object> { {"reason", "선택 영역을 준비하지 못했습니다: " + ex.Message} };
   }
   return State();
  }
  public void EndRequest() { active = false; target = null; disabledContext = null; }
  public object Invoke(string method, Dictionary<string, object> args) {
   if (!active) throw new InvalidOperationException("편집 요청이 끝났습니다. 문장을 다시 선택하고 새 요청을 시작하세요.");
   args = args ?? new Dictionary<string, object>();
   if (method == "hwp_editor_state") {
    Keys(args, new string[0]);
    if (target != null) Verify();
    return State();
   }
   if (method == "hwp_editor_rewrite") return Rewrite(args);
   if (method == "hwp_editor_format") return Format(args);
   throw new InvalidOperationException("지원하지 않는 한글 편집 도구입니다.");
  }

  object Rewrite(Dictionary<string, object> args) {
   Keys(args, new string[] {"expectedTargetId", "replacement"});
   Expected(args);
   object supplied;
   if (!args.TryGetValue("replacement", out supplied) || !(supplied is string))
    throw new InvalidOperationException("replacement에는 바꿀 본문 문자열을 넣으세요.");
   string replacement = (string)supplied;
   if (!PlainText(replacement) || replacement.Length == 0 || replacement.Length > MaxText)
    throw new InvalidOperationException("이 버전은 줄바꿈·탭·특수 제어문자·보충 유니코드 문자가 없는 1~12,000자 본문만 바꿀 수 있습니다.");
   IHwpObject doc = Verify(); Snapshot before = target;
   if (replacement == before.Text) return Refresh(doc, false);
   // Preparing action defaults does not change the document.
   IHwpAction action = doc.CreateAction("InsertText");
   if (action == null) throw new InvalidOperationException("한글 InsertText 액션을 만들 수 없습니다.");
   IHwpParameterSet parameters = action.CreateSet();
   if (parameters == null || !action.GetDefault(parameters)) throw new InvalidOperationException("본문 입력 매개변수를 읽지 못했습니다.");
   parameters.SetItem("Text", replacement);
   Verify(); // Recheck immediately before the first native document mutation.
   bool started = false;
   try {
    if (!doc.SetPos(before.List, before.Paragraph, before.Start) ||
        !doc.SelectText(before.Paragraph, before.Start, before.Paragraph, before.End) ||
        ReadSelected(doc, replacement: null, expectedLength: before.End - before.Start) != before.Text)
     throw new InvalidOperationException("원래 선택 영역을 다시 확인하지 못했습니다.");
    started = true;
    action.Execute(parameters);
    if (!doc.SetPos(before.List, before.Paragraph, before.Start) ||
        !doc.SelectText(before.Paragraph, before.Start, before.Paragraph, before.Start + replacement.Length) ||
        ReadSelected(doc, replacement, replacement.Length) != replacement)
     throw new InvalidOperationException("한글 본문의 변경 결과가 요청한 문장과 일치하지 않습니다.");
    ParagraphInfo actualParagraph = ReadParagraph(doc, before.List, before.Paragraph, before.Start, before.Start + replacement.Length, replacement);
    if (actualParagraph.NativeStart != before.ParagraphNativeStart ||
        actualParagraph.NativeEnd != before.ParagraphNativeEnd + replacement.Length - (before.End - before.Start) ||
        !MatchesParagraphRewrite(before.ParagraphText, before.PlainStart, before.PlainEnd, replacement, actualParagraph.Text))
     throw new InvalidOperationException("본문 문단 전체를 대조한 결과, 수정 범위 밖의 내용이나 남은 원문이 예상과 다릅니다.");
    IHwpParameterSet afterChar = doc.GetCharShape();
    if (afterChar == null) throw new InvalidOperationException("바꾼 본문의 글자 모양을 확인하지 못했습니다.");
    if (!before.CharShape.IsEquivalent(afterChar)) doc.SetCharShape(before.CharShape.Clone());
    IHwpParameterSet afterPara = doc.GetParaShape();
    if (afterPara == null) throw new InvalidOperationException("바꾼 본문의 문단 모양을 확인하지 못했습니다.");
    if (!before.ParaShape.IsEquivalent(afterPara)) doc.SetParaShape(before.ParaShape.Clone());
    Snapshot result = Capture(doc);
    if (!result.Supported || result.DocumentId != before.DocumentId || result.Path != before.Path ||
        result.List != before.List || result.Paragraph != before.Paragraph || result.Start != before.Start ||
        result.End != before.Start + replacement.Length || result.Text != replacement ||
        !before.CharShape.IsEquivalent(result.CharShape) || !before.ParaShape.IsEquivalent(result.ParaShape))
     throw new InvalidOperationException("본문 또는 기존 서식의 읽기 검증을 통과하지 못했습니다.");
    return Applied(result, true);
   } catch (Exception ex) {
    if (started) { RequiresReview = true; Invalidate("한글 입력은 실행됐지만 결과 확인을 마치지 못했습니다. 문서를 확인하세요. 자동 실행 취소는 하지 않았습니다."); }
    throw new InvalidOperationException((started ? "한글 입력 실행 후 확인 실패: " : "본문 적용 중단: ") + ex.Message, ex);
   }
  }

  object Format(Dictionary<string, object> args) {
   Keys(args, new string[] {"expectedTargetId", "patch"});
   Expected(args);
   object rawPatch;
   if (!args.TryGetValue("patch", out rawPatch) || !(rawPatch is Dictionary<string, object>))
    throw new InvalidOperationException("patch에는 변경할 서식 항목을 넣으세요.");
   var patch = (Dictionary<string, object>)rawPatch;
   Keys(patch, new string[] {"bold", "fontSizePt", "alignment"});
   bool charChange = patch.ContainsKey("bold") || patch.ContainsKey("fontSizePt");
   bool paraChange = patch.ContainsKey("alignment");
   if (paraChange && !target.WholeParagraph)
    throw new InvalidOperationException("문단 정렬은 선택 밖의 글에도 적용되므로 문단의 본문 전체를 선택한 경우에만 변경할 수 있습니다.");
   if (patch.Count == 0 || (charChange && paraChange))
    throw new InvalidOperationException("한 번에는 글자 모양 또는 문단 정렬 중 하나만 변경할 수 있습니다.");
   var desired = new Dictionary<string, int>(); object value;
   if (patch.TryGetValue("bold", out value)) {
    if (!(value is bool)) throw new InvalidOperationException("bold 값은 true 또는 false여야 합니다.");
    desired["Bold"] = (bool)value ? 1 : 0;
   }
   if (patch.TryGetValue("fontSizePt", out value)) {
    if (!(value is byte || value is short || value is int || value is long || value is float || value is double || value is decimal))
     throw new InvalidOperationException("fontSizePt 값은 숫자여야 합니다.");
    double size = Convert.ToDouble(value, CultureInfo.InvariantCulture), units = size * 100;
    if (Double.IsNaN(size) || Double.IsInfinity(size) || size < 1 || size > 400 || Math.Abs(units - Math.Round(units)) > 0.00001)
     throw new InvalidOperationException("글자 크기는 1~400pt, 소수점 둘째 자리까지 지정하세요.");
    desired["Height"] = (int)Math.Round(units);
   }
   if (patch.TryGetValue("alignment", out value)) {
    string alignment = value as string;
    if (alignment == "justify") desired["AlignType"] = 0;
    else if (alignment == "left") desired["AlignType"] = 1;
    else if (alignment == "right") desired["AlignType"] = 2;
    else if (alignment == "center") desired["AlignType"] = 3;
    else throw new InvalidOperationException("alignment 값은 left, center, right, justify 중 하나여야 합니다.");
   }
   // Hancom's ParameterSetObject.pdf documents AlignType=0/1/2/3.
   // https://raw.githubusercontent.com/hancom-io/devcenter-archive/main/hwp-automation/ParameterSetObject.pdf
   // Official CharShape example: Height=2000 means 20pt.
   // https://developer.hancom.com/en-us/webhwp/devguide/hwpctrl/properties/charshape
   IHwpObject doc = Verify(); Snapshot before = target;
   IHwpParameterSet shape = (charChange ? before.CharShape : before.ParaShape).Clone();
   bool changed = false;
   foreach (var item in desired) {
    int old;
    if (!TryInt(shape, item.Key, out old)) throw new InvalidOperationException("현재 선택 영역의 " + item.Key + " 서식을 확인할 수 없습니다.");
    if (old != item.Value) changed = true;
    shape.SetItem(item.Key, item.Value);
   }
   if (!changed) return Refresh(doc, false);
   Verify(); bool started = false;
   try {
    started = true;
    // Exactly one document-shape mutation per format call.
    if (charChange) doc.SetCharShape(shape); else doc.SetParaShape(shape);
    Snapshot after = Capture(doc);
    if (!after.Supported || !SameRange(before, after) || before.Text != after.Text)
     throw new InvalidOperationException("서식 변경 후 원문과 선택 영역의 검증을 통과하지 못했습니다.");
    IHwpParameterSet actual = charChange ? after.CharShape : after.ParaShape;
    if (!shape.IsEquivalent(actual) ||
        (charChange && !before.ParaShape.IsEquivalent(after.ParaShape)) ||
        (paraChange && !before.CharShape.IsEquivalent(after.CharShape)))
     throw new InvalidOperationException("요청한 서식 이외의 속성이 유지됐는지 확인하지 못했습니다.");
    foreach (var item in desired) {
     int got;
     if (!TryInt(actual, item.Key, out got) || got != item.Value)
      throw new InvalidOperationException("한글이 요청한 " + item.Key + " 서식을 반영했는지 확인하지 못했습니다.");
    }
    return Applied(after, true);
   } catch (Exception ex) {
    if (started) { RequiresReview = true; Invalidate("서식 실행 후 확인을 마치지 못했습니다. 문서를 확인하세요. 자동 실행 취소는 하지 않았습니다."); }
    throw new InvalidOperationException("서식 실행 후 확인 실패: " + ex.Message, ex);
   }
  }

  IHwpObject Verify() {
   if (target == null) throw new InvalidOperationException("이 요청에 사용할 수 있는 고정 선택 영역이 없습니다. 한 문단 안의 단순한 본문을 선택하고 새 요청을 시작하세요.");
   IHwpObject doc = getDocument(); Snapshot current = Capture(doc);
   if (!current.Supported || !SameRange(target, current) || current.Text != target.Text || current.Hash != target.Hash) {
    Invalidate("문서나 선택 영역이 바뀌어 적용을 중단했습니다. 새 요청을 시작하세요.");
    throw new InvalidOperationException("원문 또는 선택 영역이 바뀌었습니다. 내용을 다시 선택하고 새 요청을 시작하세요.");
   }
   return doc;
  }
  void Expected(Dictionary<string, object> args) {
   object value;
   if (target == null || !args.TryGetValue("expectedTargetId", out value) || !(value is string) || (string)value != target.TargetId)
    throw new InvalidOperationException("이 요청의 현재 targetId와 일치하는 expectedTargetId가 필요합니다.");
  }
  object Refresh(IHwpObject doc, bool changed) { return Applied(Capture(doc), changed); }
  object Applied(Snapshot captured, bool changed) {
   if (!captured.Supported) { Invalidate("적용 후 새 편집 대상을 준비하지 못했습니다."); throw new InvalidOperationException("적용 후 선택 영역을 확인하지 못했습니다."); }
   captured.TargetId = NewId(); target = captured;
   return new Dictionary<string, object> { {"applied", true}, {"changed", changed}, {"targetId", captured.TargetId}, {"context", Context(captured)} };
  }
  object State() {
   var properties = new List<string>(); int value;
   if (target != null) {
    if (TryInt(target.CharShape, "Bold", out value)) properties.Add("bold");
    if (TryInt(target.CharShape, "Height", out value)) properties.Add("fontSizePt");
    if (target.WholeParagraph && TryInt(target.ParaShape, "AlignType", out value)) properties.Add("alignment");
   }
   return new Dictionary<string, object> { {"targetId", target == null ? "" : target.TargetId},
    {"capabilities", new Dictionary<string, object> { {"rewrite", target != null}, {"format", target != null && properties.Count != 0}, {"formatProperties", properties.ToArray()} }},
    {"text", target == null ? "" : target.Text},
    {"context", target == null ? disabledContext : Context(target)} };
  }
  void Invalidate(string reason) { target = null; disabledContext = new Dictionary<string, object> { {"reason", reason} }; }
  static bool SameRange(Snapshot a, Snapshot b) {
   return a.DocumentId == b.DocumentId && String.Equals(a.Path, b.Path, StringComparison.Ordinal) &&
    a.List == b.List && a.Paragraph == b.Paragraph && a.Start == b.Start && a.End == b.End;
  }
  static Dictionary<string, object> Context(Snapshot s) {
   return new Dictionary<string, object> {
    {"scope", "singleParagraphSelection"}, {"reason", s.Reason ?? ""},
    {"documentId", s.DocumentId}, {"path", s.Path ?? ""},
    {"selection", new Dictionary<string, object> { {"list", s.List}, {"paragraph", s.Paragraph}, {"start", s.Start}, {"end", s.End} }},
    {"paragraphText", s.ParagraphText ?? ""},
    {"alignmentScope", s.WholeParagraph ? "selectedWholeParagraph" : "unsupportedForPartialSelection"},
    {"plainSelection", new Dictionary<string, object> { {"start", s.PlainStart}, {"end", s.PlainEnd} }},
    {"charShape", ShapeInfo(s.CharShape, false)}, {"paraShape", ShapeInfo(s.ParaShape, true)},
    {"diagnostics", s.Diagnostics}
   };
  }
  static object ShapeInfo(IHwpParameterSet shape, bool para) {
   var result = new Dictionary<string, object>(); int value;
   if (para) {
    if (TryInt(shape, "AlignType", out value)) result["alignment"] = value == 0 ? "justify" : value == 1 ? "left" : value == 2 ? "right" : value == 3 ? "center" : "unsupported";
   } else {
    if (TryInt(shape, "Bold", out value)) result["bold"] = value != 0;
    if (TryInt(shape, "Height", out value)) result["fontSizePt"] = value / 100.0;
   }
   return result;
  }
  static bool TryInt(IHwpParameterSet set, string key, out int value) {
   value = 0;
   try { if (set == null || !set.ItemExist(key)) return false; value = Convert.ToInt32(set.GetItem(key), CultureInfo.InvariantCulture); return true; }
   catch { return false; }
  }
  internal static Snapshot Capture(IHwpObject doc) {
   if (doc == null) throw new InvalidOperationException("열린 한글 문서가 없습니다.");
   var s = new Snapshot { DocumentId = doc.GetID(), Path = doc.GetPath() ?? "", Text = "", Diagnostics = new Dictionary<string, object>() };
   int sl = 0, sp = 0, so = 0, el = 0, ep = 0, eo = 0;
   if (!doc.GetSelectedPos(ref sl, ref sp, ref so, ref el, ref ep, ref eo) || (sl == el && sp == ep && so == eo)) {
    s.Reason = "한글에서 바꿀 본문을 먼저 선택하세요."; return s;
   }
   int start, end;
   if (!TryNormalizeSelection(sl, sp, so, el, ep, eo, out start, out end) || end - start > MaxText) {
    s.Reason = "이 버전은 같은 문단 안에서 선택한 12,000자 이하 본문만 편집합니다."; return s;
   }
   so = start; eo = end;
   s.List = sl; s.Paragraph = sp; s.Start = so; s.End = eo;
   if (doc.IsReadOnlyMode()) { s.Reason = "읽기 전용 문서에는 적용할 수 없습니다."; return s; }
   string block = doc.GetTextFile("HWPML2X", "saveblock"); BlockInfo parsed = ParseBlock(block);
   s.Diagnostics = new Dictionary<string, object> { {"xmlRootName", parsed.Root ?? ""}, {"paragraphCount", parsed.Paragraphs},
    {"textShapeIds", parsed.Shapes.OrderBy(x => x).ToArray()}, {"hasObjects", parsed.HasObjects}, {"leadingMetadata", parsed.LeadingMetadata},
    {"textLength", parsed.Text == null ? 0 : parsed.Text.Length}, {"nativeSpanLength", eo - so},
    {"xmlInputLength", parsed.InputLength}, {"xmlFirst8CodeUnits", parsed.InputPrefix}, {"xmlLeadingBomRemoved", parsed.LeadingBomRemoved} };
   if (!String.IsNullOrEmpty(parsed.Reason)) { s.Reason = parsed.Reason; return s; }
   if (parsed.Paragraphs != 1 || parsed.HasObjects || parsed.Shapes.Count < 1 ||
       (parsed.Shapes.Count > 1 && !HwpParagraphProofs.HasUniformSelectedStyle(block))) {
    s.Reason = "표·그림·필드·혼합 글자 모양이 없는 한 문단의 본문만 편집할 수 있습니다."; return s;
   }
   string text = ReadSelected(doc, parsed.Text, eo - so);
   if (text == null || text != parsed.Text || text.Length != eo - so || !PlainText(text)) {
    s.Reason = "선택 본문과 한글 위치를 정확히 대응할 수 없습니다. 탭·줄바꿈·특수문자가 없는 문장을 선택하세요."; return s;
   }
   s.Text = text;
   ParagraphInfo paragraph = ReadParagraph(doc, sl, sp, so, eo, text);
   s.ParagraphText = paragraph.Text;
   s.ParagraphNativeStart = paragraph.NativeStart; s.ParagraphNativeEnd = paragraph.NativeEnd;
   s.PlainStart = paragraph.PlainStart; s.PlainEnd = paragraph.PlainEnd;
   s.WholeParagraph = IsWholeParagraphSelection(paragraph.NativeStart, paragraph.NativeEnd, so, eo);
   s.Diagnostics["paragraphNativeStart"] = paragraph.NativeStart;
   s.Diagnostics["paragraphNativeEnd"] = paragraph.NativeEnd;
   s.Diagnostics["paragraphTextLength"] = paragraph.Text.Length;
   s.Hash = ReadDocumentHash(doc);
   s.CharShape = doc.GetCharShape(); s.ParaShape = doc.GetParaShape();
   if (s.CharShape == null || s.ParaShape == null) { s.Reason = "현재 선택 영역의 서식을 읽지 못했습니다."; return s; }
   s.CharShape = s.CharShape.Clone(); s.ParaShape = s.ParaShape.Clone();
   if (s.CharShape == null || s.ParaShape == null) s.Reason = "원래 서식을 보존할 수 없습니다.";
   return s;
  }
  static string ReadSelected(IHwpObject doc, string replacement, int expectedLength) {
   string raw = doc.GetSelectText();
   string normalized = NormalizeSelectedText(raw, null, replacement, expectedLength);
   if (normalized != null) return normalized;
   return NormalizeSelectedText(raw, doc.GetTextFile("TEXT", "saveblock"), replacement, expectedLength);
  }
  // Native positions include leading section/column controls; plain strings do not.
  // Infer the content origin from this paragraph, then independently read its
  // native content/prefix/suffix ranges. Never assume a fixed control width.
  static ParagraphInfo ReadParagraph(IHwpObject doc, int list, int paragraph, int start, int end, string selectedText) {
   try {
    ParagraphInfo entire = InspectParagraph(doc, list, paragraph);
    string text = entire.Text; int paragraphEnd = entire.NativeEnd;
    int nativeStart, plainStart, plainEnd;
    if (text == null || !TryMapParagraphSelection(text, paragraphEnd, start, end, selectedText, out nativeStart, out plainStart, out plainEnd) || nativeStart != entire.NativeStart)
     throw new InvalidOperationException("본문 문단의 한글 좌표와 평문 범위를 대응할 수 없습니다.");
    if (!SelectRange(doc, list, paragraph, nativeStart, paragraphEnd) || ReadSelected(doc, text, text.Length) != text ||
        !RangeEquals(doc, list, paragraph, nativeStart, start, text.Substring(0, plainStart)) ||
        !RangeEquals(doc, list, paragraph, end, paragraphEnd, text.Substring(plainEnd)))
     throw new InvalidOperationException("선택 앞뒤의 문단 내용과 한글 좌표가 일치하지 않습니다.");
    return new ParagraphInfo { Text = text, NativeStart = nativeStart, NativeEnd = paragraphEnd, PlainStart = plainStart, PlainEnd = plainEnd };
   } finally {
    if (!SelectRange(doc, list, paragraph, start, end))
     throw new InvalidOperationException("원래 본문 선택을 복원하지 못했습니다.");
   }
  }
  // Read-only inspection used to split one same-list drag into paragraph spans.
  // Its caller restores the original aggregate selection in a finally block.
  internal static ParagraphInfo InspectParagraph(IHwpObject doc, int list, int paragraph) {
   int actualList = 0, actualParagraph = 0, paragraphBegin = 0, paragraphEnd = 0;
   if (!doc.SetPos(list, paragraph, 0) || !doc.GetPos(ref actualList, ref actualParagraph, ref paragraphBegin) ||
       actualList != list || actualParagraph != paragraph || paragraphBegin < 0 || !doc.Run("MoveParaEnd") ||
       !doc.GetPos(ref actualList, ref actualParagraph, ref paragraphEnd) || actualList != list || actualParagraph != paragraph ||
       paragraphEnd < paragraphBegin || paragraphEnd > 50000)
    throw new InvalidOperationException("문단 경계를 확인하지 못했습니다.");
   if (paragraphBegin == paragraphEnd) return new ParagraphInfo { Text = "", NativeStart = paragraphBegin, NativeEnd = paragraphEnd };
   if (!SelectRange(doc, list, paragraph, paragraphBegin, paragraphEnd)) throw new InvalidOperationException("문단 전체를 선택하지 못했습니다.");
   BlockInfo parsed = ParseBlock(doc.GetTextFile("HWPML2X", "saveblock"));
   if (!String.IsNullOrEmpty(parsed.Reason)) throw new InvalidOperationException(parsed.Reason);
   if (!String.IsNullOrEmpty(parsed.Reason) || parsed.Paragraphs != 1 || parsed.HasObjects || parsed.Text == null || !PlainText(parsed.Text))
    throw new InvalidOperationException("문단 전체의 내용과 구조를 안전하게 읽지 못했습니다.");
   string text = ReadSelected(doc, parsed.Text, parsed.Text.Length);
   int nativeStart = paragraphEnd - parsed.Text.Length;
   if (text == null || nativeStart < paragraphBegin || (text.Length > 0 &&
       (!SelectRange(doc, list, paragraph, nativeStart, paragraphEnd) || ReadSelected(doc, text, text.Length) != text)))
    throw new InvalidOperationException("문단의 한글 좌표와 평문을 대응할 수 없습니다.");
   return new ParagraphInfo { Text = text, NativeStart = nativeStart, NativeEnd = paragraphEnd };
  }
  static bool RangeEquals(IHwpObject doc, int list, int paragraph, int start, int end, string expected) {
   if (start == end) return expected.Length == 0;
   return end - start == expected.Length && SelectRange(doc, list, paragraph, start, end) && ReadSelected(doc, expected, expected.Length) == expected;
  }
  internal static bool SelectRange(IHwpObject doc, int list, int paragraph, int start, int end) {
   if (start < 0 || end <= start || !doc.SetPos(list, paragraph, start) || !doc.SelectText(paragraph, start, paragraph, end)) return false;
   int sl = 0, sp = 0, so = 0, el = 0, ep = 0, eo = 0, normalizedStart, normalizedEnd;
   return doc.GetSelectedPos(ref sl, ref sp, ref so, ref el, ref ep, ref eo) &&
    sl == list && sp == paragraph && TryNormalizeSelection(sl, sp, so, el, ep, eo, out normalizedStart, out normalizedEnd) &&
    normalizedStart == start && normalizedEnd == end;
  }
  internal static bool TryNormalizeSelection(int sl, int sp, int so, int el, int ep, int eo, out int start, out int end) {
   start = Math.Min(so, eo); end = Math.Max(so, eo);
   return sl >= 0 && sp >= 0 && sl == el && sp == ep && start >= 0 && end > start;
  }
  internal static bool TryMapParagraphSelection(string paragraphText, int nativeEnd, int start, int end, string selectedText, out int nativeStart, out int plainStart, out int plainEnd) {
   nativeStart = 0; plainStart = 0; plainEnd = 0;
   if (paragraphText == null || selectedText == null || !PlainText(paragraphText) || !PlainText(selectedText) || nativeEnd < paragraphText.Length) return false;
   nativeStart = nativeEnd - paragraphText.Length;
   if (start < nativeStart || end > nativeEnd || end <= start || end - start != selectedText.Length) return false;
   plainStart = start - nativeStart; plainEnd = end - nativeStart;
   return paragraphText.Substring(plainStart, plainEnd - plainStart) == selectedText;
  }
  internal static bool IsWholeParagraphSelection(int nativeStart, int nativeEnd, int start, int end) {
   return nativeStart >= 0 && nativeEnd > nativeStart && start == nativeStart && end == nativeEnd;
  }
  internal static bool MatchesParagraphRewrite(string before, int start, int end, string replacement, string actual) {
   if (before == null || replacement == null || actual == null || start < 0 || end < start || end > before.Length) return false;
   return actual == before.Substring(0, start) + replacement + before.Substring(end);
  }
  // Exact native span and expected XML text guard every normalization. A literal
  // entity string that already matches the native span is kept unchanged.
  internal static string NormalizeSelectedText(string raw, string fallback, string expected, int expectedLength) {
   foreach (string value in new string[] { raw, fallback }) {
    if (Candidate(value, expected, expectedLength)) return value;
    string decoded = WebUtility.HtmlDecode(value);
    if (Candidate(decoded, expected, expectedLength)) return decoded;
    string trimmed = TrimFinalNewline(value);
    if (Candidate(trimmed, expected, expectedLength)) return trimmed;
    if (Candidate(TrimFinalNewline(decoded), expected, expectedLength)) return TrimFinalNewline(decoded);
   }
   return null;
  }
  static bool Candidate(string text, string expected, int length) {
   return text != null && text.Length == length && PlainText(text) && (expected == null || text == expected);
  }
  static string TrimFinalNewline(string value) {
   if (value == null) return null;
   if (value.EndsWith("\r\n", StringComparison.Ordinal)) return value.Substring(0, value.Length - 2);
   if (value.EndsWith("\r", StringComparison.Ordinal) || value.EndsWith("\n", StringComparison.Ordinal)) return value.Substring(0, value.Length - 1);
   return value;
  }
  static bool PlainText(string text) {
   if (text == null) return false;
   foreach (char c in text) if (Char.IsControl(c) || Char.IsSurrogate(c) || c == '\u2028' || c == '\u2029' || c == '\uFFFC') return false;
   return true;
  }
  static BlockInfo ParseBlock(string xml) {
   var result = new BlockInfo { Text = "", InputLength = xml == null ? -1 : xml.Length,
    InputPrefix = xml == null ? "(null)" : String.Join(" ", xml.Take(8).Select(c => ((int)c).ToString("X4", CultureInfo.InvariantCulture))) };
   string inputDiagnostics = " [type=" + (xml == null ? "null" : "System.String") + "; length=" + result.InputLength + "; first8UTF16=" + result.InputPrefix + "]";
   if (String.IsNullOrEmpty(xml) || xml.Length > MaxBlockXml) { result.Reason = "선택 영역의 구조를 읽을 수 없습니다." + inputDiagnostics; return result; }
   // A TextReader already contains decoded characters: unlike a byte stream,
   // XmlReader does not consume a U+FEFF signature. Remove exactly one at the
   // envelope start. Never trim/decode the XML or modify any body character.
   if (xml[0] == '\uFEFF') { xml = xml.Substring(1); result.LeadingBomRemoved = true; }
   try {
    var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxBlockXml };
    var document = new XmlDocument { XmlResolver = null };
    using (var input = new StringReader(xml)) using (var reader = XmlReader.Create(input, settings)) document.Load(reader);
    if (document.DocumentElement == null) throw new XmlException("문서 루트가 없습니다.");
    result.Root = document.DocumentElement.LocalName;
    XmlElement body = null;
    foreach (XmlNode node in document.DocumentElement.ChildNodes) if (node.NodeType == XmlNodeType.Element && node.LocalName == "BODY") { if (body != null) throw new XmlException("본문이 둘 이상입니다."); body = (XmlElement)node; }
    if (result.Root != "HWPML" || body == null) { result.Reason = "지원하지 않는 한글 선택 영역 XML 구조입니다." + inputDiagnostics; return result; }
    var text = new StringBuilder();
    // Walk only real body paragraphs. SECDEF/COLDEF may include page settings,
    // master-page text, or pictures, none of which belong to the selected body.
    // They are metadata only while preceding all visible text in the paragraph.
    foreach (XmlNode section in ContentElements(body, result)) {
     if (section.LocalName != "SECTION") { result.HasObjects = true; continue; }
     foreach (XmlNode paragraph in ContentElements(section, result)) {
      if (paragraph.LocalName != "P") { result.HasObjects = true; continue; }
      result.Paragraphs++; bool seenText = false;
      foreach (XmlNode run in ContentElements(paragraph, result)) {
       if (run.LocalName != "TEXT") { result.HasObjects = true; continue; }
       foreach (XmlNode node in ContentElements(run, result)) {
        string name = node.LocalName;
        if (name == "SECDEF" || name == "COLDEF") {
         if (seenText) result.HasObjects = true; else result.LeadingMetadata++;
         continue;
        }
        if (name != "CHAR") { result.HasObjects = true; continue; }
        if (node.ChildNodes.Cast<XmlNode>().Any(n => n.NodeType == XmlNodeType.Element)) result.HasObjects = true;
        text.Append(node.InnerText);
        if (node.InnerText.Length > 0) {
         seenText = true;
         XmlAttribute shape = run.Attributes == null ? null : run.Attributes["CharShape"];
         if (shape == null || String.IsNullOrEmpty(shape.Value)) result.HasObjects = true;
         else result.Shapes.Add(shape.Value);
        }
       }
      }
     }
    }
    result.Text = text.ToString();
   } catch (Exception ex) { result.Reason = "선택 영역의 한글 XML을 안전하게 확인하지 못했습니다: " + ex.Message + inputDiagnostics; }
   return result;
  }
  static IEnumerable<XmlNode> ContentElements(XmlNode parent, BlockInfo result) {
   foreach (XmlNode child in parent.ChildNodes) {
    if (child.NodeType == XmlNodeType.Element) yield return child;
    else if ((child.NodeType == XmlNodeType.Text || child.NodeType == XmlNodeType.CDATA) && !String.IsNullOrWhiteSpace(child.Value)) result.HasObjects = true;
   }
  }
  static void Keys(Dictionary<string, object> args, string[] allowed) {
   foreach (string key in args.Keys) if (!allowed.Contains(key, StringComparer.Ordinal)) throw new InvalidOperationException("지원하지 않는 편집 인자: " + key);
  }
  static string NewId() { return Guid.NewGuid().ToString("N"); }
 }
}
