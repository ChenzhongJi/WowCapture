using WowCapture.Core.Geometry;

namespace WowCapture.Core.Abstractions;

/// <summary>显示器拓扑查询。截图开始前必须重新读取，用户随时可能插拔显示器或改缩放。</summary>
public interface IMonitorService
{
    /// <summary>当前所有显示器，按系统枚举顺序。</summary>
    IReadOnlyList<MonitorInfo> GetMonitors();

    /// <summary>所有显示器拼成的虚拟屏边界（左上角可能为负）。</summary>
    ScreenRect GetVirtualScreenBounds();
}
