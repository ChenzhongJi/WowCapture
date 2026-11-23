namespace WowCapture.Core.Input;

/// <summary>热键注册结果的中文文案，界面与诊断共用，避免两处措辞不一致。</summary>
public static class HotkeyRegisterResultText
{
    public static string Describe(this HotkeyRegisterResult result) => result switch
    {
        HotkeyRegisterResult.Success => "成功",
        HotkeyRegisterResult.AlreadyUsed => "被其它程序占用",
        HotkeyRegisterResult.Invalid => "组合非法",
        HotkeyRegisterResult.Failed => "失败",
        _ => result.ToString(),
    };
}
