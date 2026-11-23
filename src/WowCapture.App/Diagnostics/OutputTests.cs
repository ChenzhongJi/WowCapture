using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WowCapture.App.Output;
using WowCapture.App.Services;
using WowCapture.Core.Output;
using WowCapture.Core.Settings;

namespace WowCapture.App.Diagnostics;

/// <summary>T1.8 的断言：命名模板求值 + 落盘命名/自增/格式。</summary>
internal static class OutputTests
{
    public static bool Run(FileLog log)
    {
        var failures = 0;
        var time = new DateTimeOffset(2026, 11, 20, 15, 4, 5, 123, TimeSpan.FromHours(8));

        void Check(string name, string actual, string expected)
        {
            var ok = actual == expected;
            if (!ok)
            {
                failures++;
            }

            log.Info($"  {name}: {(ok ? "通过" : $"失败（实际 \"{actual}\"，期望 \"{expected}\"）")}");
        }

        log.Info("命名模板用例：");
        Check("完整模板", FileNameTemplate.Format("{yyyy}-{MM}-{dd}_{HHmmss}_{width}x{height}", time, 1440, 600), "2026-11-20_150405_1440x600");
        Check("未知占位符原样保留", FileNameTemplate.Format("{date}_{yyyy}", time, 10, 10), "{date}_2026");
        Check("非法字符替换", FileNameTemplate.Format("a:b*c?d<e>f", time, 10, 10), "a_b_c_d_e_f");
        Check("结尾点被裁掉", FileNameTemplate.Format("shot...", time, 10, 10), "shot");
        Check("空白模板回退默认", FileNameTemplate.Format("   ", time, 10, 10), "2026-11-20_150405");
        Check("设备名加下划线", FileNameTemplate.Format("CON", time, 10, 10), "CON_");

        log.Info("保存用例：");

        // 用应用自己的数据目录而不是 %TEMP%：受控电脑上 %TEMP% 未必可写，
        // 而这个目录本来就是应用必须能写的地方，测试失败才有意义。
        var temp = Path.Combine(AppPaths.DataRoot, $"test-output-{Guid.NewGuid():N}");

        try
        {
            var bitmap = new WriteableBitmap(64, 48, 96, 96, PixelFormats.Bgra32, null);
            var settings = new AppSettings
            {
                Output = new AppSettings.OutputSettings
                {
                    Directory = temp,
                    FileNameTemplate = "shot_{width}x{height}",
                    Format = "png",
                },
            };

            var first = ScreenshotSaver.Save(bitmap, settings, log);
            var second = ScreenshotSaver.Save(bitmap, settings, log);

            Check("首次保存", Path.GetFileName(first ?? string.Empty), "shot_64x48.png");
            Check("同名自增", Path.GetFileName(second ?? string.Empty), "shot_64x48_1.png");

            var bothExist = first is not null && File.Exists(first) && second is not null && File.Exists(second);
            if (!bothExist)
            {
                failures++;
            }

            log.Info($"  文件确实落盘: {(bothExist ? "通过" : "失败")}");

            settings.Output.Format = "jpg";
            var jpg = ScreenshotSaver.Save(bitmap, settings, log);
            Check("jpg 扩展名", Path.GetFileName(jpg ?? string.Empty), "shot_64x48.jpg");
        }
        finally
        {
            try
            {
                if (Directory.Exists(temp))
                {
                    Directory.Delete(temp, recursive: true);
                }
            }
            catch (IOException)
            {
                // 临时目录删不掉不影响结论。
            }
        }

        log.Info(failures == 0 ? "输出用例：全部通过" : $"输出用例：{failures} 项失败");
        return failures == 0;
    }
}
