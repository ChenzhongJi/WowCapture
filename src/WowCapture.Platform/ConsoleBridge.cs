using System.IO;
using WowCapture.Platform.Interop;

namespace WowCapture.Platform;

/// <summary>
/// WinExe 没有自带控制台。命令行模式（<c>--bench-capture</c> 等）需要输出时，
/// 先尝试附着到父进程的控制台；失败就安静地放弃（调用方应同时落盘一份报告）。
/// </summary>
public static class ConsoleBridge
{
    public static bool TryAttachToParentConsole()
    {
        try
        {
            if (!NativeMethods.AttachConsole(NativeMethods.AttachParentProcess))
            {
                return false;
            }

            var writer = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            Console.SetOut(writer);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
