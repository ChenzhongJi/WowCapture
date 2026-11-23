using System.Windows.Media.Imaging;
using WowCapture.App.Services;
using WowCapture.Core.Geometry;

namespace WowCapture.App.Pin;

/// <summary>管理所有贴图窗口：创建、关闭全部、统计数量。</summary>
internal sealed class PinManager : IDisposable
{
    private readonly List<PinWindow> _pins = [];
    private readonly FileLog _log;

    public PinManager(FileLog log) => _log = log;

    /// <summary>贴图数量变化（用于托盘菜单状态）。</summary>
    public event EventHandler? Changed;

    /// <summary>任一贴图的点击穿透状态变化。托盘需要据此启用/禁用"取消贴图穿透"。</summary>
    public event EventHandler? ClickThroughChanged;

    public int Count => _pins.Count;

    /// <summary>是否有任何贴图处于点击穿透状态（托盘菜单据此启用"取消穿透"）。</summary>
    public bool AnyClickThrough => _pins.Exists(p => p.IsClickThrough);

    /// <summary>把所有贴图从点击穿透状态恢复成可点击。</summary>
    public void DisableClickThroughAll()
    {
        foreach (var pin in _pins.ToArray())
        {
            pin.SetClickThrough(false);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 在指定物理位置贴一张图。
    /// </summary>
    /// <param name="image">要贴的图（物理像素）。</param>
    /// <param name="physicalPosition">窗口左上角的虚拟屏物理坐标。</param>
    /// <param name="dpiScale">该位置的显示缩放（物理像素 → DIP）。</param>
    public PinWindow Pin(BitmapSource image, ScreenRect physicalPosition, double dpiScale)
    {
        ArgumentNullException.ThrowIfNull(image);

        var scale = dpiScale <= 0 ? 1.0 : dpiScale;
        var window = new PinWindow(
            image,
            scale,
            physicalPosition.X / scale,
            physicalPosition.Y / scale,
            _log);

        window.Closed += (_, _) =>
        {
            _pins.Remove(window);
            Changed?.Invoke(this, EventArgs.Empty);
        };

        // 穿透状态变化必须转发出去，否则托盘里的"取消贴图穿透"不会跟着变成可用。
        window.ClickThroughChanged += (_, _) =>
        {
            ClickThroughChanged?.Invoke(this, EventArgs.Empty);
            Changed?.Invoke(this, EventArgs.Empty);
        };

        _pins.Add(window);
        window.Show();

        _log.Info($"贴图已创建 {image.PixelWidth}×{image.PixelHeight} @ {physicalPosition}，当前共 {_pins.Count} 张");
        Changed?.Invoke(this, EventArgs.Empty);
        return window;
    }

    public void CloseAll()
    {
        foreach (var pin in _pins.ToArray())
        {
            pin.Close();
        }
    }

    public void Dispose() => CloseAll();
}
