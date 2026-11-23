using WowCapture.Core.Imaging;

namespace WowCapture.Core.Abstractions;

/// <summary>
/// 抓屏抽象。实现按架构 D4 分级：GDI（M1）→ PrintWindow（V1.1）→ WGC（V1.2）。
/// 调用方只依赖本接口，回退链由组合根决定。
/// </summary>
public interface IScreenCapturer
{
    /// <summary>
    /// 抓取整个虚拟屏幕（所有显示器拼成的矩形）的当前画面。
    /// 这是一次全量快照，调用方负责在会话结束时释放。
    /// </summary>
    /// <remarks>耗时在双 4K 上约 50–150ms，禁止在 UI 线程调用。</remarks>
    CapturedFrame CaptureVirtualScreen();
}
