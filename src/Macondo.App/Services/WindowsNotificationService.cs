using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace Macondo.App.Services;

/// <summary>
/// Servizio nativo Windows per:
/// 1. Lampeggio dell'icona del programma nella barra delle applicazioni (Taskbar Flash)
/// 2. Visualizzazione di notifiche toast / balloon di sistema di Windows
/// </summary>
public static class WindowsNotificationService
{
    private const uint FLASHW_STOP = 0;
    private const uint FLASHW_CAPTION = 0x00000001;
    private const uint FLASHW_TRAY = 0x00000002;
    private const uint FLASHW_ALL = 0x00000003;
    private const uint FLASHW_TIMER = 0x00000004;
    private const uint FLASHW_TIMERNOFG = 0x0000000C;

    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;

    private const uint NIIF_NONE = 0x00000000;
    private const uint NIIF_INFO = 0x00000001;
    private const uint NIIF_WARNING = 0x00000002;
    private const uint NIIF_ERROR = 0x00000003;
    private const uint NIIF_LARGE_ICON = 0x00000020;

    private const uint WM_GETICON = 0x007F;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;

    private const uint NotificationIconId = 42101;

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam);

    /// <summary>
    /// Fa lampeggiare l'icona dell'applicazione sulla barra delle applicazioni di Windows
    /// finché l'utente non seleziona la finestra portandola in primo piano.
    /// </summary>
    public static void FlashTaskbar(Window? window)
    {
        if (!OperatingSystem.IsWindows() || window == null) return;

        try
        {
            var platformHandle = window.TryGetPlatformHandle();
            if (platformHandle == null || platformHandle.Handle == IntPtr.Zero) return;

            var fi = new FLASHWINFO
            {
                cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                hwnd = platformHandle.Handle,
                dwFlags = FLASHW_TRAY | FLASHW_TIMERNOFG,
                uCount = 0,
                dwTimeout = 0
            };

            FlashWindowEx(ref fi);
        }
        catch
        {
            // Fallback trasparente in caso di errore Win32
        }
    }

    /// <summary>
    /// Mostra una notifica popup nativa su Windows (Toast / Balloon) associata alla finestra dell'applicazione.
    /// </summary>
    public static void ShowWindowsNotification(Window? window, string title, string message)
    {
        if (!OperatingSystem.IsWindows() || window == null) return;

        try
        {
            var platformHandle = window.TryGetPlatformHandle();
            var hWnd = platformHandle?.Handle ?? IntPtr.Zero;
            if (hWnd == IntPtr.Zero) return;

            IntPtr hIcon = SendMessage(hWnd, WM_GETICON, (IntPtr)ICON_BIG, IntPtr.Zero);
            if (hIcon == IntPtr.Zero)

                hIcon = SendMessage(hWnd, WM_GETICON, (IntPtr)ICON_SMALL, IntPtr.Zero);

            var safeTitle = title.Length > 63 ? title[..63] : title;
            var safeMessage = message.Length > 255 ? message[..255] : message;

            var nid = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = hWnd,
                uID = NotificationIconId,
                uFlags = NIF_INFO | NIF_TIP | (hIcon != IntPtr.Zero ? NIF_ICON : 0),
                hIcon = hIcon,
                szTip = "Macondo Code Analyzer",
                szInfoTitle = safeTitle,
                szInfo = safeMessage,
                dwInfoFlags = NIIF_INFO | NIIF_LARGE_ICON
            };

            // Rimuove eventuale notifica precedente e registra la nuova
            Shell_NotifyIcon(NIM_DELETE, ref nid);
            Shell_NotifyIcon(NIM_ADD, ref nid);

            // Rimuove l'icona di notifica dopo qualche secondo per non sporcare la system tray
            _ = Task.Delay(10000).ContinueWith(_ =>
            {
                try
                {
                    var removeNid = new NOTIFYICONDATA
                    {
                        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                        hWnd = hWnd,
                        uID = NotificationIconId
                    };
                    Shell_NotifyIcon(NIM_DELETE, ref removeNid);
                }
                catch
                {
                    // Ignora
                }
            });
        }
        catch
        {
            // Fallback trasparente se non supportato
        }
    }

    /// <summary>
    /// Notifica il completamento dell'analisi: fa lampeggiare l'icona sulla barra delle applicazioni
    /// e mostra un popup di notifica nativo su Windows.
    /// </summary>
    public static void NotifyAnalysisCompleted(Window? window, string title, string message)
    {
        FlashTaskbar(window);
        ShowWindowsNotification(window, title, message);
    }
}
