using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows.Threading;

namespace WowCapture.App.Services;

/// <summary>
/// 单实例唤起通道：第二次启动 exe 时，通过命名管道通知已在运行的实例，
/// 让它自己亮出来（托盘气泡 / 激活设置窗口），而不是弹一个"已在运行"的对话框。
/// </summary>
/// <remarks>
/// 刻意选命名管道而不是广播窗口消息：热键窗口是 message-only（HWND_MESSAGE），
/// 根本收不到 <c>HWND_BROADCAST</c>；管道不依赖窗口存在，也天然只在本机。
/// </remarks>
internal static class SingleInstanceSignaler
{
    public const string PipeName = "WowCapture.Activate";

    /// <summary>第二实例最长愿意等首实例响应的时间。</summary>
    private const int ConnectTimeoutMs = 400;

    /// <summary>
    /// 首实例调用：后台线程循环监听管道，收到"activate"后把回调调度回 UI 线程。
    /// 线程是 IsBackground 的，进程退出时随之结束，不需要显式关闭。
    /// </summary>
    public static void StartServer(Action onActivated, Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(onActivated);
        ArgumentNullException.ThrowIfNull(dispatcher);

        var thread = new Thread(() => ListenLoop(onActivated, dispatcher))
        {
            IsBackground = true,
            Name = "WowCapture.SingleInstanceListener",
        };
        thread.Start();
    }

    private static void ListenLoop(Action onActivated, Dispatcher dispatcher)
    {
        while (true)
        {
            NamedPipeServerStream? server = null;

            try
            {
                server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1);
                server.WaitForConnection();

                using (server)
                using (var reader = new StreamReader(server, Encoding.UTF8))
                {
                    if (reader.ReadLine() == "activate")
                    {
                        dispatcher.BeginInvoke(onActivated);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
            {
                // 管道被占用或正在关闭：等一下再挂回去，绝不能让监听线程死掉。
                Thread.Sleep(500);
            }
        }
    }

    /// <summary>第二实例调用：尝试唤起首实例。返回是否送达；失败时调用方应回退到提示对话框。</summary>
    public static bool TrySignalFirstInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(ConnectTimeoutMs);

            using var writer = new StreamWriter(client, Encoding.UTF8);
            writer.WriteLine("activate");
            writer.Flush();
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
        {
            return false;
        }
    }
}
