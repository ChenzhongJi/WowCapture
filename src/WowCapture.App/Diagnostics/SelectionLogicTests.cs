using System.Windows.Media;
using System.Windows.Media.Imaging;
using WowCapture.App.Overlay;
using WowCapture.App.Services;
using WowCapture.Core.Geometry;

namespace WowCapture.App.Diagnostics;

/// <summary>
/// 选区状态机的断言。这些用例不需要窗口——<see cref="OverlaySession"/> 的坐标逻辑
/// 与渲染、窗口完全解耦，所以可以像普通单元测试一样跑，几毫秒完成。
/// </summary>
/// <remarks>
/// 覆盖的是自研截图工具最容易错的三类情况：反向拖动、贴边、以及"点击"被当成 1px 选区。
/// </remarks>
internal static class SelectionLogicTests
{
    public static bool Run(FileLog log)
    {
        // 用例不需要真实画面，给一张 2×2 的图即可满足类型约束。
        var bitmap = new WriteableBitmap(2, 2, 96, 96, PixelFormats.Bgra32, null);
        var bounds = new ScreenRect(0, 0, 2880, 1800);
        var failures = 0;

        log.Info("选区状态机用例：");

        void Check(string name, ScreenRect? actual, ScreenRect? expected)
        {
            var ok = actual?.Equals(expected) ?? expected is null;
            if (!ok)
            {
                failures++;
            }

            log.Info($"  {name}: {(ok ? "通过" : $"失败（实际 {actual?.ToString() ?? "null"}，期望 {expected?.ToString() ?? "null"}）")}");
        }

        using (var session = OverlaySession.Create(bitmap, bounds, [], 0.45))
        {
            session.BeginSelection(new ScreenPoint(100, 100));
            session.UpdateSelection(new ScreenPoint(300, 250));
            session.EndSelection(new ScreenPoint(300, 250));
            Check("正向拖动 100,100→300,250", session.Selection, new ScreenRect(100, 100, 200, 150));
        }

        using (var session = OverlaySession.Create(bitmap, bounds, [], 0.45))
        {
            session.BeginSelection(new ScreenPoint(300, 250));
            session.UpdateSelection(new ScreenPoint(100, 100));
            session.EndSelection(new ScreenPoint(100, 100));
            Check("反向拖动 300,250→100,100", session.Selection, new ScreenRect(100, 100, 200, 150));
        }

        using (var session = OverlaySession.Create(bitmap, bounds, [], 0.45))
        {
            session.BeginSelection(new ScreenPoint(100, 100));
            session.EndSelection(new ScreenPoint(101, 100));
            Check("单击（1×0 px）", session.Selection, null);
        }

        using (var session = OverlaySession.Create(bitmap, bounds, [], 0.45))
        {
            session.BeginSelection(new ScreenPoint(100, 100));
            session.EndSelection(new ScreenPoint(102, 100));
            Check("极小但有面积的 2×0 px", session.Selection, null);
        }

        using (var session = OverlaySession.Create(bitmap, bounds, [], 0.45))
        {
            session.BeginSelection(new ScreenPoint(100, 100));
            session.EndSelection(new ScreenPoint(102, 102));
            Check("2×2 px 最小有效选区", session.Selection, new ScreenRect(100, 100, 2, 2));
        }

        using (var session = OverlaySession.Create(bitmap, bounds, [], 0.45))
        {
            session.BeginSelection(new ScreenPoint(500, 500));
            session.EndSelection(new ScreenPoint(700, 650));
            session.NudgeSelection(10, -10);
            Check("方向键平移 +10,-10", session.Selection, new ScreenRect(510, 490, 200, 150));

            session.NudgeSelection(-1000, -1000);
            Check("平移到左上角越界被夹住", session.Selection, new ScreenRect(0, 0, 200, 150));

            session.NudgeSelection(5000, 5000);
            Check("平移到右下角越界被夹住", session.Selection, new ScreenRect(2680, 1650, 200, 150));
        }

        using (var session = OverlaySession.Create(bitmap, bounds, [], 0.45))
        {
            session.BeginSelection(new ScreenPoint(2700, 1700));
            session.UpdateSelection(new ScreenPoint(3000, 2000));
            session.EndSelection(new ScreenPoint(3000, 2000));
            Check("拖出屏幕右下被夹到虚拟屏内", session.Selection, new ScreenRect(2700, 1700, 180, 100));
        }

        log.Info(failures == 0 ? "选区状态机用例：全部通过" : $"选区状态机用例：{failures} 项失败");
        return failures == 0;
    }
}
