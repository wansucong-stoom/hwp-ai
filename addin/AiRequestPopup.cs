using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace HAddon {
 internal sealed class AiRequestPopup : Window {
  readonly SelectionEditor ownedEditor;
  readonly Func<string, bool> send;
  readonly bool canSend;
  readonly TextBox instruction;
  readonly TextBlock status;
  readonly Button sendButton;
  bool sending, transferred, closed, released;
  bool positionNearCursor;
  POINT cursor;
  IntPtr hostRoot;

  internal AiRequestPopup(SelectionRequest request, string scope, string preview,
   bool canSend, Func<string, bool> send) {
   if (send == null) throw new ArgumentNullException("send");
   ownedEditor = request == null ? null : request.Editor;
   string originalScope = String.IsNullOrWhiteSpace(scope) ? "문서에 원하는 작업을 요청하세요" : scope;
   this.send = send;
   this.canSend = canSend || ownedEditor != null;
   Title = "AI 편집";
   Width = 480; Height = 410; MinWidth = 360; MinHeight = 330;
   WindowStyle = WindowStyle.None;
   AllowsTransparency = true;
   ResizeMode = ResizeMode.CanResize;
   ShowInTaskbar = false;
   WindowStartupLocation = WindowStartupLocation.CenterScreen;
   Background = Brushes.Transparent;
   FontFamily = new FontFamily("맑은 고딕, Segoe UI");
   FontSize = 13;
   Foreground = ChatTheme.Color("#233247");

   var frame = new Border {
    Background = ChatTheme.Color("#FFFFFF"), CornerRadius = new CornerRadius(14),
    BorderBrush = ChatTheme.Color("#DCE4EF"), BorderThickness = new Thickness(1),
    Margin = new Thickness(5),
    Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = System.Windows.Media.Color.FromRgb(31, 49, 78), BlurRadius = 12, ShadowDepth = 2, Opacity = 0.12 }
   };
   var grid = new Grid { Margin = new Thickness(21, 17, 21, 16) };
   for (int i = 0; i < 6; i++) grid.RowDefinitions.Add(new RowDefinition { Height = i == 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
   var header = new Grid { Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 13) };
   header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
   header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
   header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
   var badge = new Border { Background = ChatTheme.Color("#EFF4F8"), CornerRadius = new CornerRadius(8), Width = 36, Height = 36, Margin = new Thickness(0, 0, 11, 0) };
   badge.Child = new Image { Source = ChatTheme.StoomMark(), Width = 29, Height = 29, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
   header.Children.Add(badge);
   var title = new TextBlock { Text = "AI 편집", FontSize = 17, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
   Grid.SetColumn(title, 1); header.Children.Add(title);
   var closeButton = new Button { Content = "×", Style = ChatTheme.Button(false), Width = 29, Height = 29, MinHeight = 0, Padding = new Thickness(0), FontSize = 20, Foreground = ChatTheme.Color("#64748B"), ToolTip = "닫기 · Esc" };
   closeButton.Click += delegate { Close(); };
   Grid.SetColumn(closeButton, 2); header.Children.Add(closeButton);
   header.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch (InvalidOperationException) { } } };
   grid.Children.Add(header);

   var scopeLabel = new TextBlock {
    Text = originalScope,
    FontSize = 12, Foreground = ChatTheme.Color("#64748B"), TextWrapping = TextWrapping.Wrap,
    Focusable = false, IsHitTestVisible = false, Margin = new Thickness(0, 0, 0, 10)
   };
   Grid.SetRow(scopeLabel, 1); grid.Children.Add(scopeLabel);
   string selectedPreview = preview ?? "";
   var previewFrame = new Border { Background = ChatTheme.Color("#F6F8FC"), CornerRadius = new CornerRadius(8), Padding = new Thickness(11, 8, 11, 8), Margin = new Thickness(0, 0, 0, 10), Visibility = selectedPreview.Length == 0 ? Visibility.Collapsed : Visibility.Visible };
   var previewContent = new StackPanel();
   previewContent.Children.Add(new TextBlock { Text = "선택한 글 · " + selectedPreview.Length + "자", FontSize = 10.5, Foreground = ChatTheme.Color("#718197"), Margin = new Thickness(0,0,0,4) });
   previewContent.Children.Add(new TextBox { Text = selectedPreview, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = ChatTheme.Color("#526278"), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), MaxHeight = 95, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, ToolTip = "선택한 전체 문장을 스크롤하여 확인하세요." });
   previewFrame.Child = previewContent;
   Grid.SetRow(previewFrame, 2); grid.Children.Add(previewFrame);
   instruction = new TextBox {
    Text = "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    Style = ChatTheme.TextBox(), FontSize = 13, MaxLength = 16000,
    MinHeight = 70, VerticalContentAlignment = VerticalAlignment.Top,
    ToolTip = "원하는 변경을 입력하세요. Ctrl+Enter로 보냅니다."
   };
   Grid.SetRow(instruction, 3); grid.Children.Add(instruction);
   status = new TextBlock {
    Text = ownedEditor != null ? "\uC9C8\uBB38\uC740 \uB300\uD654\uB85C, \uC791\uC131\u00B7\uD3B8\uC9D1 \uC694\uCCAD\uC740 \uBCF8\uBB38\uC5D0 \uBC18\uC601\uD569\uB2C8\uB2E4." : canSend ? "원하는 변경을 자유롭게 적어 주세요." : "현재 이 요청을 보낼 수 없습니다. 범위나 연결 상태를 확인하고 다시 요청하세요.",
    FontSize = 11, Foreground = ChatTheme.Color("#77869A"), TextWrapping = TextWrapping.Wrap,
    Margin = new Thickness(1, 8, 0, 12), Focusable = false
   };
   Grid.SetRow(status, 4); grid.Children.Add(status);
   var footer = new Grid();
   footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
   footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
   var hint = new TextBlock { Text = "Ctrl + Enter  보내기", FontSize = 11, Foreground = ChatTheme.Color("#8A97A8"), VerticalAlignment = VerticalAlignment.Center };
   footer.Children.Add(hint);
   sendButton = new Button { Content = "보내기  ↗", Style = ChatTheme.Button(true), IsEnabled = this.canSend, MinWidth = 105, FontWeight = FontWeights.SemiBold, Padding = new Thickness(17, 9, 17, 9) };
   sendButton.Click += delegate { SendRequest(); };
   Grid.SetColumn(sendButton, 1); footer.Children.Add(sendButton);
   Grid.SetRow(footer, 5); grid.Children.Add(footer);
   frame.Child = grid;
   var outer = new Grid(); outer.Children.Add(frame);
   var grip = new System.Windows.Controls.Primitives.Thumb { Width = 13, Height = 13, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 9, 9), Cursor = Cursors.SizeNWSE, Background = Brushes.Transparent };
   var gripTemplate = new ControlTemplate(typeof(System.Windows.Controls.Primitives.Thumb));
   var gripPath = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
   gripPath.SetValue(System.Windows.Shapes.Path.DataProperty, Geometry.Parse("M 1 10 L 10 1 M 6 10 L 10 6"));
   gripPath.SetValue(System.Windows.Shapes.Path.StrokeProperty, ChatTheme.Color("#BCC8D8"));
   gripPath.SetValue(System.Windows.Shapes.Path.StrokeThicknessProperty, 1.2);
   gripTemplate.VisualTree = gripPath; grip.Template = gripTemplate;
   grip.DragDelta += delegate(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e) { Width = Math.Max(MinWidth, Width + e.HorizontalChange); Height = Math.Max(MinHeight, Height + e.VerticalChange); };
   outer.Children.Add(grip); Content = outer;

   PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
    if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) != 0) { e.Handled = true; SendRequest(); }
    else if (e.Key == Key.Escape) { e.Handled = true; Close(); }
   };
   Closed += delegate { closed = true; if (!sending) ReleaseIfOwned(); };
   SourceInitialized += delegate { if (positionNearCursor) PlaceNearCursor(); };
   Loaded += delegate {
    if (positionNearCursor) PlaceNearCursor();
    Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate { if (!closed) { instruction.Focus(); Keyboard.Focus(instruction); } }));
   };
   TrySetHostOwner();
  }
  internal static string Preview(string value) {
   if (String.IsNullOrEmpty(value)) return "";
   string text = value.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
   if (text.Length <= 60) return text;
   int take = 59;
   if (Char.IsHighSurrogate(text[take - 1]) && Char.IsLowSurrogate(text[take])) take--;
   return text.Substring(0, take) + "…";
  }

  void SendRequest() {
   if (closed || sending || !canSend) return;
   string prompt = instruction.Text;
   if (String.IsNullOrWhiteSpace(prompt)) {
    status.Text = "요청할 내용을 입력하세요."; instruction.Focus(); return;
   }
   sending = true; sendButton.IsEnabled = false;
   try {
    if (send(prompt)) {
     transferred = true;
     if (!closed) Close();
    } else if (!closed) {
     status.Text = "요청을 보내지 못했습니다. 작업이 끝났거나 중단되었다면 닫고 다시 요청하세요.";
    }
   } catch (Exception ex) {
    if (!closed) status.Text = "요청을 전달하지 못했습니다: " +
     (ex.Message.Length <= 180 ? ex.Message : "창을 닫고 다시 요청하세요.");
   } finally {
    sending = false;
    if (closed) ReleaseIfOwned(); else sendButton.IsEnabled = canSend;
   }
  }

  void ReleaseIfOwned() {
   if (released || transferred) return;
   released = true;
   if (ownedEditor != null) { try { ownedEditor.EndRequest(); } catch { } }
  }

  void TrySetHostOwner() {
   try {
    IntPtr foreground = GetForegroundWindow();
    uint foregroundProcess;
    if (foreground == IntPtr.Zero || GetWindowThreadProcessId(foreground, out foregroundProcess) == 0) return;
    uint ownProcess;
    using (var process = Process.GetCurrentProcess()) ownProcess = (uint)process.Id;
    if (foregroundProcess != ownProcess) return;
    // Follow the ownership chain so another owned tool window is not our host.
    IntPtr root = GetAncestor(foreground, 3); // GA_ROOTOWNER
    uint rootProcess;
    if (root == IntPtr.Zero || GetWindowThreadProcessId(root, out rootProcess) == 0 || rootProcess != ownProcess) return;
    new WindowInteropHelper(this).Owner = root;
    hostRoot = root;
    if (GetCursorPos(out cursor)) {
     positionNearCursor = true;
     WindowStartupLocation = WindowStartupLocation.Manual;
    }
   } catch {
    positionNearCursor = false;
    WindowStartupLocation = WindowStartupLocation.CenterScreen;
   }
  }

  void PlaceNearCursor() {
   try {
    var helper = new WindowInteropHelper(this);
    HwndSource source = HwndSource.FromHwnd(helper.Handle);
    if (source == null || source.CompositionTarget == null) source = HwndSource.FromHwnd(hostRoot);
    if (source == null || source.CompositionTarget == null) return;
    Matrix fromDevice = source.CompositionTarget.TransformFromDevice;
    Point at = fromDevice.Transform(new Point(cursor.X, cursor.Y));
    Rect work = SystemParameters.WorkArea;
    IntPtr monitor = MonitorFromPoint(cursor, 2); // MONITOR_DEFAULTTONEAREST
    var info = new MONITORINFO { Size = Marshal.SizeOf(typeof(MONITORINFO)) };
    if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info)) {
     Point a = fromDevice.Transform(new Point(info.Work.Left, info.Work.Top));
     Point b = fromDevice.Transform(new Point(info.Work.Right, info.Work.Bottom));
     work = new Rect(a, b);
    }
    if (work.Width <= 0 || work.Height <= 0) return;
    double width = Math.Min(Width, work.Width), height = Math.Min(Height, work.Height);
    MinWidth = Math.Min(MinWidth, width); MinHeight = Math.Min(MinHeight, height);
    Width = width; Height = height;
    Left = Math.Max(work.Left, Math.Min(at.X + 14, work.Right - width));
    Top = Math.Max(work.Top, Math.Min(at.Y + 18, work.Bottom - height));
   } catch { /* Keep the operating system's usable initial window position. */ }
  }

  [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
  [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
  [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }
  [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window, uint flags);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
  [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool GetCursorPos(out POINT point);
  [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT point, uint flags);
  [DllImport("user32.dll", CharSet = CharSet.Auto)] [return: MarshalAs(UnmanagedType.Bool)] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
 }
}

