using System.Diagnostics;
using System.IO;
using System.Text;
using WowCapture.App.Imaging;
using WowCapture.App.Services;
using WowCapture.Capture;
using WowCapture.Core.Imaging;
using WowCapture.Platform;

namespace WowCapture.App.Benchmarks;

/// <summary>
/// S1/T1.3 的实测入口：<c>WowCapture.exe --bench-capture [次数]</c>。
/// 输出三样东西——控制台（能附着时）、<c>data/bench-capture.txt</c> 报告、<c>bench-sample.png</c> 截图。
/// 样例图不只是好看：它是"抓到的画面确实是对的"这个验收标准的物证。
/// </summary>
internal static class CaptureBenchmark
{
    private const int WarmupRuns = 3;

    public static int Run(int iterations, FileLog log)
    {
        iterations = Math.Clamp(iterations, 3, 500);
        var attached = ConsoleBridge.TryAttachToParentConsole();
        var report = new StringBuilder();

        void Emit(string line)
        {
            report.AppendLine(line);
            log.Info(line);
            if (attached)
            {
                Console.WriteLine(line);
            }
        }

        try
        {
            // 两种 BitBlt 模式各跑一轮：CAPTUREBLT 能抓分层窗口，但可能贵得多，必须用数据决定默认值。
            var withLayered = Measure(iterations, includeLayered: true);
            var plain = Measure(iterations, includeLayered: false);

            var sample = withLayered.Sample;
            var bytes = sample?.Pixels.LongLength ?? 0;
            var megapixels = (sample?.Width ?? 0) * (double)(sample?.Height ?? 0) / 1_000_000.0;
            var peakMb = Process.GetCurrentProcess().PeakWorkingSet64 / 1024.0 / 1024.0;

            Emit("== WowCapture 捕获基准（S1） ==");
            Emit($"时间        : {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
            Emit($"虚拟屏      : {sample?.Bounds}   {megapixels:F2} MP / {bytes / 1024.0 / 1024.0:F1} MB");
            Emit($"迭代        : 每种模式 {iterations} 次（各预热 {WarmupRuns} 次）");
            Emit(string.Empty);
            Emit("模式                    min     P50     P95     max   BitBlt P50  Copy P50");
            EmitLine(Emit, "SRCCOPY", plain);
            EmitLine(Emit, "SRCCOPY|CAPTUREBLT", withLayered);
            Emit(string.Empty);

            var ratio = Percentile(withLayered.Totals, 50) / Math.Max(0.001, Percentile(plain.Totals, 50));
            Emit($"CAPTUREBLT 开销倍数: {ratio:F2}x（相对纯 SRCCOPY）");

            if (megapixels > 0)
            {
                var perMp = Percentile(withLayered.Totals, 50) / megapixels;
                const double Dual4Kmp = 8192 * 2160 / 1_000_000.0;
                Emit($"单位开销          : {perMp:F2} ms/MP");
                Emit($"外推双 4K ({Dual4Kmp:F1} MP): P50 ≈ {perMp * Dual4Kmp:F0} ms（预算 200ms 以内）");
            }

            Emit(string.Empty);
            Emit($"峰值工作集  : {peakMb:F1} MB（含迭代产生的内存抖动；单次会话约 1 帧）");

            var scan = sample is null ? default : BlankFrameDetector.Scan(sample);
            Emit($"空白检测    : 采样 {scan.SampledCount} 点，非黑 {scan.NonBlackCount}，颜色一致={scan.IsUniform}");

            if (sample is not null)
            {
                var pngPath = Path.Combine(AppPaths.DataRoot, "bench-sample.png");
                FrameBitmap.SavePng(sample, pngPath);
                Emit($"样例图      : {pngPath}");
            }

            var reportPath = Path.Combine(AppPaths.DataRoot, "bench-capture.txt");
            File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);
            log.Info($"基准报告已写入：{reportPath}");
            return 0;
        }
        catch (Exception ex)
        {
            log.Error("捕获基准失败", ex);
            if (attached)
            {
                Console.WriteLine($"失败：{ex.GetType().Name}: {ex.Message}");
            }

            return 1;
        }
    }

    private static void EmitLine(Action<string> emit, string name, Series series) =>
        emit($"{name,-22} {series.Totals.Min(),6:F1} {Percentile(series.Totals, 50),7:F1} {Percentile(series.Totals, 95),7:F1} {series.Totals.Max(),7:F1}   {Percentile(series.Blits, 50),9:F1} {Percentile(series.Copies, 50),9:F1}");

    private static Series Measure(int iterations, bool includeLayered)
    {
        var capturer = new GdiScreenCapturer { IncludeLayeredWindows = includeLayered };
        var totals = new List<double>(iterations);
        var blits = new List<double>(iterations);
        var copies = new List<double>(iterations);
        CapturedFrame? sample = null;

        for (var i = 0; i < WarmupRuns + iterations; i++)
        {
            var run = Stopwatch.StartNew();
            var frame = capturer.CaptureVirtualScreen();
            run.Stop();

            var stats = capturer.LastStats ?? default;
            if (i == 0)
            {
                sample = frame;
            }

            if (i < WarmupRuns)
            {
                continue;
            }

            totals.Add(run.Elapsed.TotalMilliseconds);
            blits.Add(stats.BlitMs);
            copies.Add(stats.CopyMs);
        }

        return new Series(totals, blits, copies, sample);
    }

    private static double Percentile(List<double> values, double percentile)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        var index = (int)Math.Round(percentile / 100.0 * (sorted.Length - 1), MidpointRounding.AwayFromZero);
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private sealed record Series(List<double> Totals, List<double> Blits, List<double> Copies, CapturedFrame? Sample);
}
