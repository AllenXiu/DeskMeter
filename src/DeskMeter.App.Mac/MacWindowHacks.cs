using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace DeskMeter.App.Mac;

/// <summary>
/// macOS 原生窗口行为：点击穿透 + 窗口层级（overlay 浮动可见 / desktop 钉在桌面壁纸之上图标之下）。
/// 通过 objc_msgSend 直接设置 NSWindow.ignoresMouseEvents / level / collectionBehavior。
/// </summary>
public static class MacWindowHacks
{
    private const long DesktopLevel = -2147483622; // kCGDesktopIconWindowLevelKey（壁纸之上、应用窗口之下；比纯桌面层高一级，避免被壁纸层盖住）
    private const long FloatingLevel = 3;          // NSFloatingWindowLevel：始终在普通窗口之上
    private const ulong BehaviorCanJoinAllSpaces = 1 << 0;
    private const ulong BehaviorIgnoresCycle = 1 << 3;
    private const ulong BehaviorStationary = 1 << 4;
    private const ulong BehaviorFullScreenAuxiliary = 1 << 8;

    public static void Apply(Window window, bool clickThrough, bool pinnedToDesktop)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var handle = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero) return;
        try
        {
            MsgSendBool(handle, Sel("setIgnoresMouseEvents:"), clickThrough);
            MsgSendBool(handle, Sel("setMovableByWindowBackground:"), false);
            if (pinnedToDesktop)
            {
                MsgSendLong(handle, Sel("setLevel:"), DesktopLevel);
                MsgSendULong(handle, Sel("setCollectionBehavior:"),
                    BehaviorCanJoinAllSpaces | BehaviorStationary | BehaviorIgnoresCycle);
            }
            else
            {
                // overlay：浮动层级 + 可加入全部 Space（含全屏 App 的 Space），保证可见
                MsgSendLong(handle, Sel("setLevel:"), FloatingLevel);
                MsgSendULong(handle, Sel("setCollectionBehavior:"),
                    BehaviorCanJoinAllSpaces | BehaviorFullScreenAuxiliary);
            }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("window hacks: " + ex.Message); }
    }

    /// <summary>强制无 Dock 图标（NSApplicationActivationPolicyAccessory=1）。
    /// 直接以可执行文件方式被 launchd 拉起时 LSUIElement 不总是生效，这里程序内再设一次。</summary>
    public static void HideFromDock()
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var app = MsgSendId(ObjCGetClass("NSApplication"), Sel("sharedApplication"));
            MsgSendLong(app, Sel("setActivationPolicy:"), 1); // NSApplicationActivationPolicyAccessory
        }
        catch { /* 非 GUI 会话等场景静默 */ }
    }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr Sel(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")]
    private static extern IntPtr ObjCGetClass(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgSendId(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void MsgSendBool(IntPtr receiver, IntPtr selector, bool value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void MsgSendLong(IntPtr receiver, IntPtr selector, long value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void MsgSendULong(IntPtr receiver, IntPtr selector, ulong value);
}
