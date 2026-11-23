using WowCapture.Core.Output;
using Xunit;

namespace WowCapture.Core.Tests.Output;

public class FileNameTemplateTests
{
    private static readonly DateTimeOffset SampleTime =
        new(2025, 6, 1, 13, 4, 5, 123, TimeSpan.FromHours(8));

    [Fact]
    public void Format_默认模板含日期时间()
    {
        var name = FileNameTemplate.Format(FileNameTemplate.Default, SampleTime, 800, 600);

        Assert.Equal("2025-06-01_130405", name);
    }

    [Fact]
    public void Format_空模板回退默认()
    {
        Assert.Equal(
            FileNameTemplate.Format(null, SampleTime, 800, 600),
            FileNameTemplate.Format("   ", SampleTime, 800, 600));
    }

    [Fact]
    public void Format_未知占位符原样保留()
    {
        var name = FileNameTemplate.Format("截图 {date} {width}x{height}", SampleTime, 800, 600);

        Assert.Equal("截图 {date} 800x600", name);
    }

    [Fact]
    public void Format_非法字符换成下划线()
    {
        var name = FileNameTemplate.Format("a<b>c:d\"e|f?g*h", SampleTime, 800, 600);

        Assert.Equal("a_b_c_d_e_f_g_h", name);
    }

    [Fact]
    public void Format_结尾空格与点被裁掉()
    {
        Assert.Equal("shot", FileNameTemplate.Format("shot .", SampleTime, 800, 600));
        Assert.Equal("shot", FileNameTemplate.Format("shot.. ", SampleTime, 800, 600));
    }

    [Fact]
    public void Format_保留设备名加后缀()
    {
        Assert.Equal("CON_", FileNameTemplate.Format("CON", SampleTime, 800, 600));
        Assert.Equal("NUL_", FileNameTemplate.Format("NUL", SampleTime, 800, 600));
    }

    [Fact]
    public void Format_超长截断且不残留结尾分隔符()
    {
        var name = FileNameTemplate.Format(new string('a', 120) + "..", SampleTime, 800, 600);

        Assert.Equal(100, name.Length);
        Assert.Equal('a', name[^1]);
    }

    [Fact]
    public void Format_清洗到空时兜底()
    {
        // "..." 的点被 TrimEnd 裁空，触发兜底名。
        Assert.Equal("screenshot", FileNameTemplate.Format("...", SampleTime, 800, 600));
    }

    [Fact]
    public void Format_非法字符按下划线替换而非丢弃()
    {
        // "/" 等非法字符换 "_"，长度保留——用户能看出"我的模板被处理过"。
        Assert.Equal("___", FileNameTemplate.Format("///", SampleTime, 800, 600));
    }
}
