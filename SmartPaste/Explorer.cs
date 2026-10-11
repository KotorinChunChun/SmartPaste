using System.Runtime.InteropServices;
using System.Text;

namespace SmartPaste;

internal static class Native
{
    internal delegate nint HookProc(int code, nint message, nint data);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint hwnd, StringBuilder text, int length);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern nint FindWindowEx(nint parent, nint after, string className, string? title);
    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool AddClipboardFormatListener(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool RemoveClipboardFormatListener(nint hwnd);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWindowsHookEx(int type, HookProc callback, nint module, uint threadId);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] internal static extern int GetMessage(out Message message, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] internal static extern bool PeekMessage(out Message message, nint hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] internal static extern bool PostThreadMessage(uint threadId, uint message, nint wparam, nint lparam);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint hwnd, int command);
    internal static string Class(nint hwnd) { var text = new StringBuilder(256); GetClassName(hwnd, text, text.Capacity); return text.ToString(); }
    internal static nint Focus(nint hwnd)
    {
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(GetWindowThreadProcessId(hwnd, out _), ref info) ? info.Focus : 0;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct GuiThreadInfo
    {
        internal int Size; internal uint Flags; internal nint Active, Focus, Capture, MenuOwner, MoveSize, Caret; internal Rect CaretRect;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct Message
    {
        internal nint Hwnd; internal uint Id; internal nuint WParam; internal nint LParam; internal uint Time; internal int X, Y; internal uint Private;
    }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardData
    { internal uint Key, ScanCode, Flags, Time; internal nuint Extra; }
}

[ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellServiceProvider
{
    [PreserveSig] int QueryService(ref Guid service, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object browser);
}

[ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellBrowserWindow
{
    [PreserveSig] int GetWindow(out nint hwnd);
    [PreserveSig] int ContextSensitiveHelp(bool enter);
}

internal static class Explorer
{
    // File-list focus only: address bars, rename fields, search boxes and menus are excluded.
    public static bool IsFileView(nint window, nint focus) => Native.Class(window) == "CabinetWClass"
        && Native.Class(focus) is "DirectUIHWND" or "SysListView32";

    public static string? DirectoryFor(nint window)
        => WithView<string?>(window, (_, path) => path, null);

    private static T WithView<T>(nint window, Func<object, string, T> action, T fallback)
    {
        if (Native.Class(window) != "CabinetWClass") return fallback;
        object? shell = null, windows = null;
        var matches = new List<(object View, string Path)>();
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            windows = ((dynamic)shell!).Windows();
            nint activeTab = 0, after = 0;
            while ((after = Native.FindWindowEx(window, after, "ShellTabWindowClass", null)) != 0)
            {
                if (!Native.IsWindowVisible(after)) continue;
                if (activeTab != 0) return fallback; // Do not guess when more than one tab is visible.
                activeTab = after;
            }
            int count = ((dynamic)windows).Count;
            for (int i = 0; i < count; i++)
            {
                object? item = null, browser = null, document = null, folder = null, self = null;
                try
                {
                    item = ((dynamic)windows).Item(i);
                    if (item == null || new nint((long)((dynamic)item).HWND) != window) continue;
                    if (activeTab != 0)
                    {
                        Guid service = new("4C96BE40-915C-11CF-99D3-00AA004AE837"), iid = typeof(IShellBrowserWindow).GUID;
                        if (((IShellServiceProvider)item).QueryService(ref service, ref iid, out browser) != 0) continue;
                        if (((IShellBrowserWindow)browser).GetWindow(out var tab) != 0 || tab != activeTab) continue;
                    }
                    document = ((dynamic)item).Document;
                    folder = ((dynamic)document).Folder;
                    self = ((dynamic)folder).Self;
                    string path = ((dynamic)self).Path;
                    if (Path.IsPathFullyQualified(path) && System.IO.Directory.Exists(path))
                    { matches.Add((document, path)); document = null; }
                }
                catch (Exception ex) when (ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
                finally { Release(self); Release(folder); Release(document); Release(browser); Release(item); }
            }
            return matches.Count == 1 ? action(matches[0].View, matches[0].Path) : fallback;
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { return fallback; }
        finally { foreach (var match in matches) Release(match.View); Release(windows); Release(shell); }
    }

    public static string? FocusedPath(nint window) => WithView<string?>(window, (view, _) =>
    {
        object? item = null;
        try { item = ((dynamic)view).FocusedItem; return item == null ? null : (string)((dynamic)item).Path; }
        finally { Release(item); }
    }, null);

    public static bool SelectFiles(nint window, string directory, string[] paths) => WithView(window, (view, path) =>
    {
        if (!ClipboardFiles.IsSameFolderCopy(paths, path) || !ClipboardFiles.SameDirectory(path, directory)) return false;
        object? folder = null, selected = null, focused = null;
        var items = new List<object>();
        try
        {
            folder = ((dynamic)view).Folder;
            foreach (var file in paths)
            {
                object? item = ((dynamic)folder).ParseName(Path.GetFileName(file));
                if (item == null) return false;
                items.Add(item);
            }
            // SELECT | DESELECTOTHERS | ENSUREVISIBLE | FOCUSED. Additional files stay selected.
            for (int i = 0; i < items.Count; i++) ((dynamic)view).SelectItem(items[i], i == 0 ? 29 : 1);
            focused = ((dynamic)view).FocusedItem;
            if (focused == null || !string.Equals((string)((dynamic)focused).Path, paths[0], StringComparison.OrdinalIgnoreCase)) return false;
            selected = ((dynamic)view).SelectedItems();
            var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < (int)((dynamic)selected).Count; i++)
            {
                object? item = null;
                try { item = ((dynamic)selected).Item(i); actual.Add((string)((dynamic)item).Path); }
                finally { Release(item); }
            }
            return actual.SetEquals(paths);
        }
        finally { Release(focused); Release(selected); foreach (var item in items) Release(item); Release(folder); }
    }, false);

    public static async Task<bool> SelectCreatedAsync(nint window, string directory, string[] paths)
    {
        if (paths.Length == 0) return true;
        foreach (int delay in new[] { 0, 100, 200, 400, 700, 1000 })
        {
            if (delay > 0) await Task.Delay(delay);
            // Do not change selection after the user switches to another app or navigates away.
            if (Native.GetForegroundWindow() != window) return false;
            if (SelectFiles(window, directory, paths)) return true;
        }
        return false;
    }
    private static void Release(object? obj) { if (obj != null && Marshal.IsComObject(obj)) Marshal.ReleaseComObject(obj); }
}

internal sealed record PasteSnapshot(nint Window, nint Focus, uint ClipboardSequence, string Directory, string[] Files, bool IsImage, long CapturedAt);

internal static class ClipboardFiles
{
    public static bool SameDirectory(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);

    public static bool IsSameFolderCopy(string[] files, string directory)
    {
        try { return files.Length > 0 && files.All(file => Path.GetDirectoryName(Path.GetFullPath(file)) is { } parent && SameDirectory(parent, directory)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException) { return false; }
    }
    public static string[]? ReadCopy()
    {
        return FromData(Clipboard.GetDataObject());
    }
    public static string[]? FromData(IDataObject? data)
    {
        if (data?.GetData(DataFormats.FileDrop) is not string[] files) return null;
        if (data.GetDataPresent("Preferred DropEffect"))
        {
            byte[] value;
            var effect = data.GetData("Preferred DropEffect");
            if (effect is Stream stream)
            {
                using var bytes = new MemoryStream(); if (stream.CanSeek) stream.Position = 0; stream.CopyTo(bytes); value = bytes.ToArray();
            }
            else if (effect is byte[] bytes) value = bytes;
            else return null;
            // Explorer uses COPY | LINK (5) for Ctrl+C. MOVE must still pass through.
            if (value.Length < 4 || (BitConverter.ToUInt32(value) & 3u) != 1u) return null;
        }
        var paths = files.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return paths.Length > 0 && paths.All(File.Exists) && paths.Any(p => Naming.Detect(Path.GetFileName(p)).Count > 0) ? paths : null;
    }
}

internal sealed class PasteHook : IDisposable
{
    private readonly Native.HookProc callback;
    private readonly Action<PasteSnapshot> received;
    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new();
    private nint hook;
    private uint threadId;
    private Exception? installError;
    private PasteSnapshot? snapshot;
    private int swallowedKey;
    private int pending;
    public PasteSnapshot? Snapshot { set => Volatile.Write(ref snapshot, value); }
    public void Complete() => Interlocked.Exchange(ref pending, 0);

    public PasteHook(Action<PasteSnapshot> received)
    {
        this.received = received;
        callback = OnKey;
        thread = new Thread(Run) { IsBackground = true, Name = "SmartPaste keyboard hook" };
        thread.Start();
        ready.Wait();
        if (installError != null) throw installError;
    }
    private void Run()
    {
        threadId = Native.GetCurrentThreadId();
        Native.PeekMessage(out _, 0, 0, 0, 0);
        hook = Native.SetWindowsHookEx(13, callback, Native.GetModuleHandle(null), 0);
        if (hook == 0) installError = new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        ready.Set();
        if (hook == 0) return;
        try { while (Native.GetMessage(out _, 0, 0, 0) > 0) { } }
        finally { Native.UnhookWindowsHookEx(hook); hook = 0; }
    }

    private nint OnKey(int code, nint message, nint data)
    {
        if (code < 0) return Native.CallNextHookEx(hook, code, message, data);
        var key = Marshal.PtrToStructure<Native.KeyboardData>(data);
        int id = (int)key.Key;
        bool down = message == 0x100 || message == 0x104;
        bool up = message == 0x101 || message == 0x105;
        if (swallowedKey == id)
        {
            if (up) swallowedKey = 0;
            return 1;
        }
        if (!down || (id != 0x56 && id != 0x2D)) return Native.CallNextHookEx(hook, code, message, data);
        bool ctrl = Held(0x11), shift = Held(0x10), alt = Held(0x12), win = Held(0x5B) || Held(0x5C);
        if (alt || win || !((id == 0x56 && ctrl && !shift) || (id == 0x2D && shift && !ctrl))) return Native.CallNextHookEx(hook, code, message, data);
        var context = Volatile.Read(ref snapshot);
        if (context == null || Environment.TickCount64 - context.CapturedAt > 650 || context.Window != Native.GetForegroundWindow()
            || context.Focus != Native.Focus(context.Window) || context.ClipboardSequence != Native.GetClipboardSequenceNumber())
            return Native.CallNextHookEx(hook, code, message, data);
        swallowedKey = id;
        if (Interlocked.CompareExchange(ref pending, 1, 0) == 0)
        {
            try { received(context); }
            catch { Complete(); }
        }
        return 1;
    }
    private static bool Held(int key) => (Native.GetAsyncKeyState(key) & 0x8000) != 0;
    public void Dispose()
    {
        Snapshot = null;
        if (thread.IsAlive) { Native.PostThreadMessage(threadId, 0x12, 0, 0); thread.Join(1500); }
        ready.Dispose();
    }
}
