using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;

namespace CodeDock.App.Controls;

public sealed class ExternalWindowHost : HwndHost
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsChild = 0x40000000L;
    private const long WsVisible = 0x10000000L;
    private const long WsPopup = 0x80000000L;
    private const long WsCaption = 0x00C00000L;
    private const long WsThickFrame = 0x00040000L;
    private const long WsTabStop = 0x00010000L;
    private const int WmParentNotify = 0x0210;
    private const int WmLButtonDown = 0x0201;
    private const int WmRButtonDown = 0x0204;
    private const int WmMButtonDown = 0x0207;
    private const int WmXButtonDown = 0x020B;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const uint WsClipChildren = 0x02000000;
    private const uint WsClipSiblings = 0x04000000;

    private IntPtr _hostHandle;
    private IntPtr _externalHandle;
    private IntPtr _originalParent;
    private IntPtr _originalStyle;
    private IntPtr _originalExtendedStyle;
    private NativeMethods.WindowPlacement _originalPlacement;

    public static readonly DependencyProperty WindowHandleProperty = DependencyProperty.Register(
        nameof(WindowHandle),
        typeof(long),
        typeof(ExternalWindowHost),
        new FrameworkPropertyMetadata(0L, OnWindowHandleChanged));

    public ExternalWindowHost()
    {
        Focusable = true;
        SizeChanged += OnSizeChanged;
        GotKeyboardFocus += OnGotKeyboardFocus;
    }

    public long WindowHandle
    {
        get => (long)GetValue(WindowHandleProperty);
        set => SetValue(WindowHandleProperty, value);
    }

    public bool IsAttached => _externalHandle != IntPtr.Zero;

    public string? LastError { get; private set; }

    public bool TryDetach(out string? error)
    {
        var success = DetachExternalWindow();
        error = LastError;
        return success;
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _hostHandle = NativeMethods.CreateWindowEx(
            0,
            "STATIC",
            string.Empty,
            unchecked((uint)(WsChild | WsVisible)) | WsClipChildren | WsClipSiblings,
            0,
            0,
            Math.Max(1, (int)ActualWidth),
            Math.Max(1, (int)ActualHeight),
            hwndParent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (_hostHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"The native embedded-window host could not be created. Error {Marshal.GetLastWin32Error()}.");
        }

        AttachExternalWindow(new IntPtr(WindowHandle));
        return new HandleRef(this, _hostHandle);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        if (!DetachExternalWindow())
        {
            return;
        }

        if (_hostHandle != IntPtr.Zero)
        {
            NativeMethods.DestroyWindow(_hostHandle);
            _hostHandle = IntPtr.Zero;
        }
    }

    protected override bool TabIntoCore(TraversalRequest request) => FocusExternalWindow();

    protected override IntPtr WndProc(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        // Mouse input sent to a hosted HWND does not travel through WPF's routed
        // input events. WM_PARENTNOTIFY is the reliable opportunity to transfer
        // keyboard focus to the IDE when one of its native children is clicked.
        if (message == WmParentNotify && IsMouseButtonDown(unchecked((int)wParam.ToInt64()) & 0xFFFF))
        {
            FocusExternalWindow();
        }

        return base.WndProc(hwnd, message, wParam, lParam, ref handled);
    }

    private static void OnWindowHandleChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        var host = (ExternalWindowHost)dependencyObject;
        if (host._hostHandle != IntPtr.Zero)
        {
            host.AttachExternalWindow(new IntPtr((long)args.NewValue));
        }
    }

    private bool AttachExternalWindow(IntPtr externalHandle)
    {
        LastError = null;
        if (_externalHandle == externalHandle)
        {
            ResizeExternalWindow();
            return true;
        }

        if (!DetachExternalWindow())
        {
            return false;
        }

        if (externalHandle == IntPtr.Zero)
        {
            return true;
        }

        if (_hostHandle == IntPtr.Zero || !NativeMethods.IsWindow(externalHandle))
        {
            LastError = "The selected IDE window is no longer valid.";
            return false;
        }

        _originalParent = NativeMethods.GetParent(externalHandle);
        _originalStyle = NativeMethods.GetWindowLongPtr(externalHandle, GwlStyle);
        _originalExtendedStyle = NativeMethods.GetWindowLongPtr(externalHandle, GwlExStyle);
        _originalPlacement = NativeMethods.WindowPlacement.Create();
        if (!NativeMethods.GetWindowPlacement(externalHandle, ref _originalPlacement))
        {
            LastError = $"The original IDE window placement could not be read. Error {Marshal.GetLastWin32Error()}.";
            ResetOriginalState();
            return false;
        }

        var hostedStyle = new IntPtr(
            (_originalStyle.ToInt64() & ~WsPopup & ~WsCaption & ~WsThickFrame) | WsChild | WsVisible | WsTabStop);
        if (!TrySetWindowLong(externalHandle, GwlStyle, hostedStyle, out var styleError))
        {
            LastError = styleError;
            ResetOriginalState();
            return false;
        }

        NativeMethods.SetParent(externalHandle, _hostHandle);
        if (NativeMethods.GetParent(externalHandle) != _hostHandle)
        {
            TrySetWindowLong(externalHandle, GwlStyle, _originalStyle, out _);
            LastError = $"The IDE window could not be attached to CodeDock. Error {Marshal.GetLastWin32Error()}.";
            ResetOriginalState();
            return false;
        }

        _externalHandle = externalHandle;
        if (!ResizeExternalWindow())
        {
            DetachExternalWindow();
            return false;
        }

        return true;
    }

    private bool DetachExternalWindow()
    {
        LastError = null;
        if (_externalHandle == IntPtr.Zero)
        {
            return true;
        }

        if (!NativeMethods.IsWindow(_externalHandle))
        {
            ResetAttachmentState();
            return true;
        }

        // SetParent does not change WS_CHILD/WS_POPUP.  In particular, when a
        // top-level window is restored to the desktop, GetParent can still
        // report the desktop HWND while WS_CHILD is present.  Restore the
        // styles before validating the parent or every normal top-level IDE
        // window can be reported as a failed detach with Win32 error 0.
        Marshal.SetLastPInvokeError(0);
        var previousParent = NativeMethods.SetParent(_externalHandle, _originalParent);
        var parentError = Marshal.GetLastWin32Error();
        if (previousParent == IntPtr.Zero && parentError != 0)
        {
            LastError = $"The IDE window could not be detached from CodeDock. Error {parentError}.";
            return false;
        }

        var styleRestored = TrySetWindowLong(_externalHandle, GwlStyle, _originalStyle, out var styleError);
        var extendedStyleRestored = TrySetWindowLong(
            _externalHandle,
            GwlExStyle,
            _originalExtendedStyle,
            out var extendedStyleError);
        if (!styleRestored || !extendedStyleRestored)
        {
            LastError = styleError ?? extendedStyleError;
            return false;
        }

        if (NativeMethods.GetParent(_externalHandle) != _originalParent)
        {
            LastError = "The IDE window could not be restored to its original parent.";
            return false;
        }

        var placementRestored = NativeMethods.SetWindowPlacement(_externalHandle, ref _originalPlacement);
        var frameRestored = NativeMethods.SetWindowPos(
            _externalHandle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoZOrder | SwpFrameChanged | SwpNoMove | SwpNoSize | SwpShowWindow);
        if (!placementRestored || !frameRestored)
        {
            LastError = $"The IDE window detached, but its original placement could not be fully restored. Error {Marshal.GetLastWin32Error()}.";
            return false;
        }

        ResetAttachmentState();
        return true;
    }

    private bool ResizeExternalWindow()
    {
        if (_externalHandle == IntPtr.Zero || !NativeMethods.IsWindow(_externalHandle))
        {
            return true;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var width = Math.Max(1, (int)Math.Round(ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(1, (int)Math.Round(ActualHeight * dpi.DpiScaleY));
        if (NativeMethods.SetWindowPos(
                _externalHandle,
                IntPtr.Zero,
                0,
                0,
                width,
                height,
                SwpNoZOrder | SwpFrameChanged | SwpShowWindow))
        {
            return true;
        }

        LastError = $"The embedded IDE window could not be resized. Error {Marshal.GetLastWin32Error()}.";
        return false;
    }

    private bool FocusExternalWindow()
    {
        if (_externalHandle == IntPtr.Zero || !NativeMethods.IsWindow(_externalHandle))
        {
            return false;
        }

        var currentThread = NativeMethods.GetCurrentThreadId();
        var ideThread = NativeMethods.GetWindowThreadProcessId(_externalHandle, out _);
        var inputAttached = ideThread != 0
            && ideThread != currentThread
            && NativeMethods.AttachThreadInput(currentThread, ideThread, true);

        try
        {
            // Keep WPF's logical focus on the HwndHost while Win32 focus moves to
            // the external child. This prevents WPF from immediately reclaiming it.
            if (!IsKeyboardFocusWithin)
            {
                Focus();
            }

            NativeMethods.SetFocus(_externalHandle);
            return NativeMethods.GetFocus() == _externalHandle;
        }
        finally
        {
            if (inputAttached)
            {
                NativeMethods.AttachThreadInput(currentThread, ideThread, false);
            }
        }
    }

    private static bool IsMouseButtonDown(int message) => message is
        WmLButtonDown or WmRButtonDown or WmMButtonDown or WmXButtonDown;

    private static bool TrySetWindowLong(IntPtr handle, int index, IntPtr value, out string? error)
    {
        Marshal.SetLastPInvokeError(0);
        var previous = NativeMethods.SetWindowLongPtr(handle, index, value);
        var lastError = Marshal.GetLastWin32Error();
        if (previous == IntPtr.Zero && lastError != 0)
        {
            error = $"A window style could not be updated. Error {lastError}.";
            return false;
        }

        error = null;
        return true;
    }

    private void ResetAttachmentState()
    {
        _externalHandle = IntPtr.Zero;
        ResetOriginalState();
    }

    private void ResetOriginalState()
    {
        _originalParent = IntPtr.Zero;
        _originalStyle = IntPtr.Zero;
        _originalExtendedStyle = IntPtr.Zero;
        _originalPlacement = default;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => ResizeExternalWindow();

    private void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => FocusExternalWindow();

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Point
        {
            internal int X;
            internal int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WindowPlacement
        {
            internal uint Length;
            internal uint Flags;
            internal uint ShowCommand;
            internal Point MinimumPosition;
            internal Point MaximumPosition;
            internal Rect NormalPosition;

            internal static WindowPlacement Create() => new() { Length = (uint)Marshal.SizeOf<WindowPlacement>() };
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr CreateWindowEx(
            uint extendedStyle,
            string className,
            string windowName,
            uint style,
            int x,
            int y,
            int width,
            int height,
            IntPtr parent,
            IntPtr menu,
            IntPtr instance,
            IntPtr parameter);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetParent(IntPtr windowHandle);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetParent(IntPtr childWindow, IntPtr newParent);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        internal static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        internal static extern IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr newValue);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPos(
            IntPtr windowHandle,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowPlacement(IntPtr windowHandle, ref WindowPlacement placement);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetWindowPlacement(IntPtr windowHandle, ref WindowPlacement placement);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetFocus(IntPtr windowHandle);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

        [DllImport("kernel32.dll")]
        internal static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    }
}
