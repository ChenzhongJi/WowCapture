using System.Runtime.CompilerServices;

// Win32 声明保持 internal：只暴露给真正需要调用的程序集，避免把 P/Invoke 细节变成公开 API。
[assembly: InternalsVisibleTo("WowCapture.Capture")]
// 注意：宿主项目的 AssemblyName 是 "WowCapture"（只有 exe 想叫这个名字），不是 "WowCapture.App"。
[assembly: InternalsVisibleTo("WowCapture")]
