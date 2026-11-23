using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WowCapture.App.Windows;

/// <summary>首次运行引导：一页纸说明热键、截图流程和贴图操作，看完即可关。</summary>
internal sealed class FirstRunWindow : Window
{
    public FirstRunWindow(string hotkey)
    {
        Title = "WowCapture 已就绪";
        Width = 480;
        Height = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Content = BuildContent(hotkey);
    }

    private UIElement BuildContent(string hotkey)
    {
        var panel = new StackPanel { Margin = new Thickness(26) };

        panel.Children.Add(new TextBlock
        {
            Text = "欢迎使用 WowCapture",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 14),
        });

        panel.Children.Add(Paragraph($"按 {hotkey} 开始截图。托盘常驻，右键有菜单；也可以点托盘菜单里的\"截图\"。"));
        panel.Children.Add(Paragraph("拖动框选，或把鼠标停在某个窗口上直接选中整窗；选区内可画箭头/矩形/文字/马赛克/序号。"));
        panel.Children.Add(Paragraph("Enter 复制到剪贴板，F3 贴到屏幕。贴图支持拖动、滚轮缩放、Alt+滚轮调透明度、双击复位。"));
        panel.Children.Add(Paragraph("Esc 取消截图；Ctrl+Z 撤销上一步标注。"));

        var ok = new Button
        {
            Content = "开始使用",
            Width = 120,
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };

        ok.Click += (_, _) => Close();
        panel.Children.Add(ok);

        return panel;
    }

    private static TextBlock Paragraph(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 10),
        LineHeight = 21,
        Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33)),
    };
}
