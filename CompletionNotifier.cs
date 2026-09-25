using System.Runtime.InteropServices;
using System.Windows.Threading;
using TikTokScrapper.Core;

namespace TikTokScrapper;

// A temporary notification-area icon lets Windows display a native notification
// without opening a window, changing focus, or requiring an installed app identity.
internal sealed class CompletionNotifier : IDisposable
{
    private NotifyIconData data;
    private readonly DispatcherTimer cleanup = new() { Interval = TimeSpan.FromMinutes(2) };
    private bool added;

    public CompletionNotifier() => cleanup.Tick += (_, _) => Dispose();

    public void Show(nint window, CompletionNotice notice)
    {
        Dispose();
        data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = window, Id = 1,
            Flags = 0x2 | 0x4, Icon = LoadIcon(0, new nint(32516)), Tip = "TikTok Scrapper",
            Info = "", Title = "",
        };
        added = ShellNotifyIcon(0, ref data); // NIM_ADD
        if (added)
        {
            data.TimeoutOrVersion = 4;
            ShellNotifyIcon(4, ref data); // NIM_SETVERSION, NOTIFYICON_VERSION_4
            data.Flags = 0x10; // NIF_INFO
            data.Info = notice.Message;
            data.Title = notice.Title;
            data.InfoFlags = 0x1 | 0x10; // NIIF_INFO | NIIF_NOSOUND (one explicit ding below)
            ShellNotifyIcon(1, ref data); // NIM_MODIFY
            cleanup.Start();
        }
        System.Media.SystemSounds.Asterisk.Play();
    }

    public void Dispose()
    {
        cleanup.Stop();
        if (added) { ShellNotifyIcon(2, ref data); added = false; } // NIM_DELETE
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id, Flags, CallbackMessage;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
        public uint InfoFlags;
        public Guid Guid;
        public nint BalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll", EntryPoint = "LoadIconW")]
    private static extern nint LoadIcon(nint instance, nint name);
}
