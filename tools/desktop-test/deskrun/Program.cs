// deskrun <desktop> <waitSeconds> [--log <file>] -- <exe> [args...]
//
// Starts a process on a separate Win32 desktop in WinSta0 (created on first use). Windows on it
// never appear on the user's screen or take focus, but processes on the same desktop can still
// drive them with UI Automation. The desktop is destroyed when its last handle closes, so this
// process holds it open until the child exits or waitSeconds elapses (exit code 124).
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

int sep = Array.IndexOf(args, "--");
if (args.Length < 4 || sep < 2 || sep == args.Length - 1)
{
    Console.Error.WriteLine("usage: deskrun <desktop> <waitSeconds> [--log <file>] -- <exe> [args...]");
    return 2;
}
var desktop = args[0];
var wait = int.Parse(args[1]);
string? log = null;
for (int i = 2; i < sep; i++)
    if (args[i] == "--log" && i + 1 < sep) log = args[++i];
var commandLine = string.Join(' ', args[(sep + 1)..].Select(Quote));

var hDesk = Native.CreateDesktop(desktop, IntPtr.Zero, IntPtr.Zero, 0, Native.GENERIC_ALL, IntPtr.Zero);
if (hDesk == IntPtr.Zero) throw new Win32Exception();

var si = new Native.STARTUPINFO { cb = Marshal.SizeOf<Native.STARTUPINFO>(), lpDesktop = $@"WinSta0\{desktop}" };
FileStream? logStream = null;
if (log != null)
{
    logStream = new FileStream(log, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
    var h = logStream.SafeFileHandle.DangerousGetHandle();
    Native.SetHandleInformation(h, Native.HANDLE_FLAG_INHERIT, Native.HANDLE_FLAG_INHERIT);
    si.dwFlags = Native.STARTF_USESTDHANDLES;
    si.hStdOutput = h;
    si.hStdError = h;
}

if (!Native.CreateProcess(null, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, log != null,
        Native.CREATE_NO_WINDOW, IntPtr.Zero, null, ref si, out var pi))
    throw new Win32Exception();
Console.WriteLine($"pid={pi.dwProcessId}");

if (wait <= 0) return 0;
if (Native.WaitForSingleObject(pi.hProcess, (uint)wait * 1000) != 0) { Console.WriteLine("timeout"); return 124; }
Native.GetExitCodeProcess(pi.hProcess, out var code);
logStream?.Dispose();
return (int)code;

// Windows argv quoting (CommandLineToArgvW rules).
static string Quote(string a)
{
    if (a.Length > 0 && a.IndexOfAny([' ', '\t', '"']) < 0) return a;
    var sb = new StringBuilder("\"");
    int slashes = 0;
    foreach (var c in a)
    {
        if (c == '\\') { slashes++; continue; }
        sb.Append('\\', c == '"' ? slashes * 2 + 1 : slashes).Append(c);
        slashes = 0;
    }
    return sb.Append('\\', slashes * 2).Append('"').ToString();
}

static class Native
{
    public const uint GENERIC_ALL = 0x10000000;
    public const uint CREATE_NO_WINDOW = 0x08000000;
    public const int STARTF_USESTDHANDLES = 0x100;
    public const uint HANDLE_FLAG_INHERIT = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO
    {
        public int cb; public string? lpReserved; public string? lpDesktop; public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateDesktop(string lpszDesktop, IntPtr lpszDevice, IntPtr pDevmode, int dwFlags, uint dwDesiredAccess, IntPtr lpsa);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CreateProcess(string? app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string? cwd, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetHandleInformation(IntPtr h, uint mask, uint flags);
    [DllImport("kernel32.dll")] public static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll")] public static extern bool GetExitCodeProcess(IntPtr h, out uint code);
}
