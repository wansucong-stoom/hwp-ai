using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;

namespace HAddon {
 internal static class ChatTheme {
  internal static SolidColorBrush Color(string hex) {
   var brush = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(hex));
   if (brush.CanFreeze) brush.Freeze();
   return brush;
  }

  internal static DrawingImage StoomMark() {
   var drawing = new DrawingGroup();
   // Exact canonical assets/stoom-mark.svg geometry, colors and viewBox.
   drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0,0,38,29))));
   var pen = new Pen(Color("#17394C"),2);
   drawing.Children.Add(new GeometryDrawing(null,pen,new EllipseGeometry(new Point(14.5,14.5),13.5,13.5)));
   var arm=Geometry.Parse("M23 6H37").Clone();arm.Transform=new RotateTransform(-35,30,6);
   drawing.Children.Add(new GeometryDrawing(null,pen,arm));
   drawing.Freeze();var image=new DrawingImage(drawing);image.Freeze();return image;
  }
  static Style Read(string target, string setters, string template) {
   string xaml = "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type " + target + "}'>" +
    "<Setter Property='FontFamily' Value='맑은 고딕, Segoe UI'/><Setter Property='FontSize' Value='13'/><Setter Property='Foreground' Value='#233247'/>" +
    "<Setter Property='SnapsToDevicePixels' Value='True'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>" + setters +
    "<Setter Property='Template'><Setter.Value>" + template + "</Setter.Value></Setter></Style>";
   return (Style)XamlReader.Parse(xaml);
  }

  internal static Style Button(bool primary) {
   string background = primary ? "#2563EB" : "#F0F4FA";
   string foreground = primary ? "#FFFFFF" : "#344256";
   string hover = primary ? "#1D4ED8" : "#E5ECF5";
   string pressed = primary ? "#1E40AF" : "#D8E3F0";
   return Read("Button", "<Setter Property='Background' Value='" + background + "'/><Setter Property='Foreground' Value='" + foreground + "'/><Setter Property='BorderBrush' Value='Transparent'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='14,9'/><Setter Property='MinHeight' Value='36'/><Setter Property='Cursor' Value='Hand'/><Setter Property='HorizontalContentAlignment' Value='Center'/><Setter Property='VerticalContentAlignment' Value='Center'/>",
    "<ControlTemplate TargetType='{x:Type Button}'><Border x:Name='Chrome' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='9' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}' RecognizesAccessKey='True'/></Border><ControlTemplate.Triggers>" +
    "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Chrome' Property='Background' Value='" + hover + "'/></Trigger>" +
    "<Trigger Property='IsPressed' Value='True'><Setter TargetName='Chrome' Property='Background' Value='" + pressed + "'/></Trigger>" +
    "<Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Chrome' Property='BorderBrush' Value='#60A5FA'/></Trigger>" +
    "<Trigger Property='IsEnabled' Value='False'><Setter TargetName='Chrome' Property='Opacity' Value='0.45'/><Setter Property='Cursor' Value='Arrow'/></Trigger>" +
    "</ControlTemplate.Triggers></ControlTemplate>");
  }

  internal static Style TextBox() {
   return Read("TextBox", "<Setter Property='Background' Value='#FFFFFF'/><Setter Property='BorderBrush' Value='#DCE4EF'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='11,10'/><Setter Property='CaretBrush' Value='#2563EB'/><Setter Property='SelectionBrush' Value='#BFDBFE'/><Setter Property='VerticalContentAlignment' Value='Center'/>",
    "<ControlTemplate TargetType='{x:Type TextBox}'><Border x:Name='Chrome' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='9' Padding='{TemplateBinding Padding}'><ScrollViewer x:Name='PART_ContentHost' Focusable='False'/></Border><ControlTemplate.Triggers>" +
    "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Chrome' Property='BorderBrush' Value='#A9BDD8'/></Trigger>" +
    "<Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='Chrome' Property='BorderBrush' Value='#2563EB'/></Trigger>" +
    "<Trigger Property='IsEnabled' Value='False'><Setter TargetName='Chrome' Property='Background' Value='#F1F5F9'/><Setter Property='Foreground' Value='#8A97A8'/></Trigger>" +
    "</ControlTemplate.Triggers></ControlTemplate>");
  }

  internal static Style ComboBox() {
   return Read("ComboBox", "<Setter Property='Background' Value='#FFFFFF'/><Setter Property='BorderBrush' Value='#DCE4EF'/><Setter Property='BorderThickness' Value='1'/><Setter Property='Padding' Value='11,8'/><Setter Property='MinHeight' Value='36'/><Setter Property='HorizontalContentAlignment' Value='Left'/><Setter Property='VerticalContentAlignment' Value='Center'/><Setter Property='ScrollViewer.CanContentScroll' Value='True'/>",
    "<ControlTemplate TargetType='{x:Type ComboBox}'><Grid><Border x:Name='Chrome' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='9'/>" +
    "<ToggleButton x:Name='Toggle' Focusable='False' ClickMode='Press' IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'><ToggleButton.Template><ControlTemplate TargetType='{x:Type ToggleButton}'><Border Background='Transparent' CornerRadius='9'><Path Data='M 0 0 L 4 4 L 8 0' Stroke='#64748B' StrokeThickness='1.5' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,12,0'/></Border></ControlTemplate></ToggleButton.Template></ToggleButton>" +
    "<TextBlock Margin='11,7,32,7' Text='{Binding SelectionBoxItem, RelativeSource={RelativeSource TemplatedParent}}' TextTrimming='CharacterEllipsis' VerticalAlignment='{TemplateBinding VerticalContentAlignment}' IsHitTestVisible='False'/>" +
    "<Popup x:Name='PART_Popup' Placement='Bottom' IsOpen='{TemplateBinding IsDropDownOpen}' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'><Border Background='#FFFFFF' BorderBrush='#DCE4EF' BorderThickness='1' CornerRadius='9' Margin='0,4,0,0' Padding='4' MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}' MaxHeight='300'><ScrollViewer CanContentScroll='True' VerticalScrollBarVisibility='Auto'><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/></ScrollViewer></Border></Popup>" +
    "</Grid><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Chrome' Property='BorderBrush' Value='#A9BDD8'/></Trigger><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='Chrome' Property='BorderBrush' Value='#2563EB'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='Chrome' Property='Opacity' Value='0.5'/><Setter Property='Foreground' Value='#8A97A8'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
  }
 }
}
