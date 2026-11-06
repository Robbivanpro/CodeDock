using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace CodeDock.App.Controls;

public sealed class ExternalWindowHost : HwndHost
{
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const int SwRestore = 9;
    private const long WsChild = 0x40000000L;
    private const long WsVisible = 0x10000000L;
    private const long WsMinimize = 0x20000000L;
    private const long WsMaximize = 0x01000000L;
    private const long WsPopup = 0x80000000L;
    private const long WsCaption = 0x00C00000L;
    private const long WsThickFrame = 0x00040000L;
    private const long WsSystemMenu = 0x00080000L;
    private const long WsMinimizeBox = 0x00020000L;
    private const long WsMaximizeBox = 0x00010000L;
    private const long WsTabStop = 0x00010000L;
    private const int WmParentNotify = 0x0210;
    private const uint WmSize = 0x0005;
    private const int WmLButtonDown = 0x0201;
    private const int WmRButtonDown = 0x0204;
    private const int WmMButtonDown = 0x0207;
    private const int WmXButtonDown = 0x020B;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const uint WsClipChildren = 0x02000000;
    private const uint WsClipSiblings = 0x04000000;
    private const uint RdwInvalidate = 0x0001;
    private const uint RdwErase = 0x0004;
    private const uint RdwAllChildren = 0x0080;
    private const uint RdwUpdateNow = 0x0100;

    private IntPtr _hostHandle;
    private IntPtr _externalHandle;
    private IntPtr _pendingExternalHandle;
    private IntPtr _originalParent;
    private IntPtr _originalStyle;
    private IntPtr _originalExtendedStyle;
    private NativeMethods.WindowPlacement _originalPlacement;

    public static readonly DependencyProperty WindowHandleProperty = DependencyProperty.Register(
        nameof(WindowHandle),
        typeof(long),
        typeof(ExternalWindowHost),
        new FrameworkPropertyMetadata(0L, OnWindowHandleChanged));

    public static readonly DependencyProperty RequiresContentResizeKickProperty = DependencyProperty.Register(
        nameof(RequiresContentResizeKick),
        typeof(bool),
        typeof(ExternalWindowHost),
        new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty RequiresElectronRenderKickProperty = DependencyProperty.Register(
        nameof(RequiresElectronRenderKick),
        typeof(bool),
        typeof(ExternalWindowHost),
        new FrameworkPropertyMetadata(false));

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

    public bool RequiresContentResizeKick
    {
        get => (bool)GetValue(RequiresContentResizeKickProperty);
        set => SetValue(RequiresContentResizeKickProperty, value);
    }

    public bool RequiresElectronRenderKick
    {
        get => (bool)GetValue(RequiresElectronRenderKickProperty);
        set => SetValue(RequiresElectronRenderKickProperty, value);
    }

    public bool IsAttached => _externalHandle != IntPtr.Zero;

    public string? LastError { get; private set; }

    public bool TryAttach(long windowHandle)
    {
        if (windowHandle == 0 || _hostHandle == IntPtr.Zero)
        {
            return false;
        }

        return AttachExternalWindow(new IntPtr(windowHandle));
    }

    public bool TryDetach(out string? error)
    {
        _pendingExternalHandle = IntPtr.Zero;
        var success = DetachExternalWindow();
        error = LastError;
        return success;
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        var (initialWidth, initialHeight) = GetDpiScaledSize();
        _hostHandle = NativeMethods.CreateWindowEx(
            0,
            "STATIC",
            string.Empty,
            unchecked((uint)(WsChild | WsVisible)) | WsClipChildren | WsClipSiblings,
            0,
            0,
            initialWidth,
            initialHeight,
            hwndParent.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero);

        if (_hostHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"The native embedded-window host could not be created. Error {Marshal.GetLastWin32Error()}.");
        }

        if (WindowHandle != 0)
        {
            _pendingExternalHandle = new IntPtr(WindowHandle);
            if (IsHostLayoutReady())
            {
                AttachExternalWindow(_pendingExternalHandle);
            }
        }

        return new HandleRef(this, _hostHandle);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        _pendingExternalHandle = IntPtr.Zero;
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

    protected override void OnWindowPositionChanged(Rect rcBoundingBox)
    {
        base.OnWindowPositionChanged(rcBoundingBox);
        if (_pendingExternalHandle != IntPtr.Zero && _externalHandle == IntPtr.Zero && IsHostLayoutReady())
        {
            AttachExternalWindow(_pendingExternalHandle);
            return;
        }

        ResizeExternalWindow();
    }

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
        var nextHandle = new IntPtr((long)args.NewValue);
        if (nextHandle == IntPtr.Zero)
        {
            host._pendingExternalHandle = IntPtr.Zero;
            if (host._hostHandle != IntPtr.Zero && host._externalHandle != IntPtr.Zero)
            {
                host.DetachExternalWindow();
            }

            return;
        }

        host.LastError = null;
        host._pendingExternalHandle = nextHandle;
        if (host._hostHandle != IntPtr.Zero && host.IsHostLayoutReady())
        {
            host.AttachExternalWindow(nextHandle);
        }
    }

    private bool IsHostLayoutReady() =>
        _hostHandle != IntPtr.Zero
        && ActualWidth >= 10
        && ActualHeight >= 10;

    private bool AttachExternalWindow(IntPtr externalHandle)
    {
        if (externalHandle == IntPtr.Zero)
        {
            _pendingExternalHandle = IntPtr.Zero;
            return DetachExternalWindow();
        }

        LastError = null;
        if (_externalHandle == externalHandle)
        {
            _pendingExternalHandle = IntPtr.Zero;
            ResizeExternalWindow();
            return true;
        }

        if (!DetachExternalWindow())
        {
            return false;
        }

        if (_hostHandle == IntPtr.Zero || !NativeMethods.IsWindow(externalHandle))
        {
            _pendingExternalHandle = IntPtr.Zero;
            LastError = "The selected IDE window is no longer valid.";
            return false;
        }

        if (!IsHostLayoutReady())
        {
            _pendingExternalHandle = externalHandle;
            return true;
        }

        _pendingExternalHandle = IntPtr.Zero;
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

        // Restore maximized or minimized top-level windows and move/resize them onto
        // the host's screen bounds BEFORE converting to WS_CHILD. Moving the top-level
        // window onto CodeDock's monitor triggers WM_DPICHANGED when the app came from
        // another monitor with different DPI scaling, and resizing while top-level lets
        // WebView2 (Teams, Outlook) and WinUI 3 (WhatsApp) update their internal bounds.
        if (NativeMethods.IsIconic(externalHandle) || NativeMethods.IsZoomed(externalHandle))
        {
            NativeMethods.ShowWindow(externalHandle, SwRestore);
        }

        var (initialWidth, initialHeight) = GetTargetPhysicalSize();
        var targetX = 0;
        var targetY = 0;
        if (NativeMethods.GetWindowRect(_hostHandle, out var hostScreenRect))
        {
            targetX = hostScreenRect.Left;
            targetY = hostScreenRect.Top;
        }

        NativeMethods.SetWindowPos(
            externalHandle,
            IntPtr.Zero,
            targetX,
            targetY,
            initialWidth,
            initialHeight,
            SwpNoZOrder | SwpNoActivate | SwpFrameChanged);

        var currentStyle = NativeMethods.GetWindowLongPtr(externalHandle, GwlStyle).ToInt64();
        const long topLevelStyleMask = WsPopup
            | WsCaption
            | WsThickFrame
            | WsSystemMenu
            | WsMinimizeBox
            | WsMaximizeBox
            | WsMaximize
            | WsMinimize;
        var hostedStyle = new IntPtr(
            (currentStyle & ~topLevelStyleMask)
            | WsChild | WsVisible | WsTabStop);
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
            TrySetWindowLong(externalHandle, GwlExStyle, _originalExtendedStyle, out _);
            LastError = $"The IDE window could not be attached to CodeDock. Error {Marshal.GetLastWin32Error()}.";
            ResetOriginalState();
            return false;
        }

        _externalHandle = externalHandle;
        if (!ResizeExternalWindow(forceFrameChange: true, notifyContent: true))
        {
            DetachExternalWindow();
            return false;
        }

        if (RequiresElectronRenderKick)
        {
            PulseElectronRenderSurface();
        }

        ScheduleDeferredResizes();
        return true;
    }

    private bool DetachExternalWindow()
    {
        if (_externalHandle == IntPtr.Zero)
        {
            return true;
        }

        if (!NativeMethods.IsWindow(_externalHandle))
        {
            ResetAttachmentState();
            return true;
        }

        // SetParent does not change WS_CHILD/WS_POPUP. In particular, when a
        // top-level window is restored to the desktop, GetParent can still
        // report the desktop HWND while WS_CHILD is present. Restore the
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

        LastError = null;
        ResetAttachmentState();
        return true;
    }

    private bool ResizeExternalWindow(bool forceFrameChange = false, bool notifyContent = false)
    {
        if (_externalHandle == IntPtr.Zero || !NativeMethods.IsWindow(_externalHandle))
        {
            return true;
        }

        var (width, height) = GetTargetPhysicalSize();
        var targetX = 0;
        var targetY = 0;
        var targetWidth = width;
        var targetHeight = height;

        // Account for a retained non-client frame in the single resize operation.
        // Moving first to the unadjusted bounds and correcting it immediately after
        // makes Chromium/WebView2 windows visibly oscillate during WPF layout passes.
        if (NativeMethods.GetWindowRect(_externalHandle, out var extWinRect)
            && NativeMethods.GetClientRect(_externalHandle, out var extClientRect))
        {
            var clientOrigin = new NativeMethods.Point { X = 0, Y = 0 };
            if (NativeMethods.ClientToScreen(_externalHandle, ref clientOrigin))
            {
                var leftBorder = Math.Max(0, clientOrigin.X - extWinRect.Left);
                var topBorder = Math.Max(0, clientOrigin.Y - extWinRect.Top);
                var totalWidthBorder = Math.Max(0, (extWinRect.Right - extWinRect.Left) - (extClientRect.Right - extClientRect.Left));
                var totalHeightBorder = Math.Max(0, (extWinRect.Bottom - extWinRect.Top) - (extClientRect.Bottom - extClientRect.Top));

                if ((totalWidthBorder > 0 || totalHeightBorder > 0)
                    && totalWidthBorder < 64
                    && totalHeightBorder < 64)
                {
                    targetX = -leftBorder;
                    targetY = -topBorder;
                    targetWidth += totalWidthBorder;
                    targetHeight += totalHeightBorder;
                }
            }
        }

        if (!forceFrameChange && IsExternalWindowAtBounds(targetX, targetY, targetWidth, targetHeight))
        {
            if (notifyContent)
            {
                RefreshEmbeddedContent(width, height);
            }

            return true;
        }

        var flags = SwpNoZOrder | SwpShowWindow | SwpNoActivate;
        if (forceFrameChange)
        {
            flags |= SwpFrameChanged;
        }

        if (!NativeMethods.SetWindowPos(
                _externalHandle,
                IntPtr.Zero,
                targetX,
                targetY,
                targetWidth,
                targetHeight,
                flags))
        {
            LastError = $"The embedded IDE window could not be resized. Error {Marshal.GetLastWin32Error()}.";
            return false;
        }

        if (notifyContent)
        {
            RefreshEmbeddedContent(width, height);
        }

        return true;
    }

    private void RefreshEmbeddedContent(int width, int height)
    {
        if (!RequiresContentResizeKick)
        {
            return;
        }

        NativeMethods.EnumChildWindows(
            _externalHandle,
            (childHandle, _) =>
            {
                var classNameBuilder = new StringBuilder(128);
                NativeMethods.GetClassName(childHandle, classNameBuilder, classNameBuilder.Capacity);
                var className = classNameBuilder.ToString();
                if (!className.Contains("DesktopChildSiteBridge", StringComparison.Ordinal)
                    && !className.Contains("DesktopWindowContentBridge", StringComparison.Ordinal)
                    && !className.Contains("WinUIDesktopWin32WindowClass", StringComparison.Ordinal))
                {
                    return true;
                }

                if (!NativeMethods.GetWindowRect(childHandle, out var childRect)
                    || childRect.Right - childRect.Left != width
                    || childRect.Bottom - childRect.Top != height)
                {
                    NativeMethods.SetWindowPos(
                        childHandle,
                        IntPtr.Zero,
                        0,
                        0,
                        width,
                        height,
                        SwpNoZOrder | SwpNoActivate | SwpShowWindow);
                }

                return true;
            },
            IntPtr.Zero);

        var sizeParameter = new IntPtr((height << 16) | (width & 0xFFFF));
        NativeMethods.PostMessage(_externalHandle, WmSize, IntPtr.Zero, sizeParameter);
        NativeMethods.RedrawWindow(
            _externalHandle,
            IntPtr.Zero,
            IntPtr.Zero,
            RdwInvalidate | RdwErase | RdwAllChildren | RdwUpdateNow);
    }

    private void PulseElectronRenderSurface()
    {
        var (width, height) = GetTargetPhysicalSize();
        var pulseWidth = Math.Max(1, width - 1);
        var pulseHeight = Math.Max(1, height - 1);
        const uint flags = SwpNoZOrder | SwpNoActivate | SwpShowWindow;

        NativeMethods.SetWindowPos(
            _externalHandle,
            IntPtr.Zero,
            0,
            0,
            pulseWidth,
            pulseHeight,
            flags);
        NativeMethods.SetWindowPos(
            _externalHandle,
            IntPtr.Zero,
            0,
            0,
            width,
            height,
            flags);
        RefreshEmbeddedContent(width, height);
    }

    private bool IsExternalWindowAtBounds(int x, int y, int width, int height)
    {
        if (!NativeMethods.GetWindowRect(_externalHandle, out var windowRect))
        {
            return false;
        }

        var hostOrigin = new NativeMethods.Point { X = 0, Y = 0 };
        if (!NativeMethods.ClientToScreen(_hostHandle, ref hostOrigin))
        {
            return false;
        }

        return windowRect.Left - hostOrigin.X == x
            && windowRect.Top - hostOrigin.Y == y
            && windowRect.Right - windowRect.Left == width
            && windowRect.Bottom - windowRect.Top == height;
    }

    private void ScheduleDeferredResizes()
    {
        _ = Dispatcher.BeginInvoke(
            () => ResizeExternalWindow(notifyContent: true),
            DispatcherPriority.Loaded);
        foreach (var delayMs in new[] { 100, 300, 650, 1200, 2000 })
        {
            var timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(delayMs)
            };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                ResizeExternalWindow(notifyContent: true);
            };
            timer.Start();
        }
    }

    private (int Width, int Height) GetTargetPhysicalSize()
    {
        if (_hostHandle != IntPtr.Zero
            && NativeMethods.GetClientRect(_hostHandle, out var hostRect)
            && hostRect.Right - hostRect.Left > 10
            && hostRect.Bottom - hostRect.Top > 10)
        {
            return (hostRect.Right - hostRect.Left, hostRect.Bottom - hostRect.Top);
        }

        return GetDpiScaledSize();
    }

    private (int Width, int Height) GetDpiScaledSize()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = Math.Max(1, (int)Math.Round(ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(1, (int)Math.Round(ActualHeight * dpi.DpiScaleY));
        return (width, height);
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

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_pendingExternalHandle != IntPtr.Zero && _externalHandle == IntPtr.Zero && IsHostLayoutReady())
        {
            AttachExternalWindow(_pendingExternalHandle);
            return;
        }

        ResizeExternalWindow();
    }

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
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsZoomed(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShowWindow(IntPtr windowHandle, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetClientRect(IntPtr windowHandle, out Rect rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetWindowRect(IntPtr windowHandle, out Rect rect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ClientToScreen(IntPtr windowHandle, ref Point point);

        internal delegate bool EnumChildProc(IntPtr windowHandle, IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumChildWindows(IntPtr parentHandle, EnumChildProc callback, IntPtr parameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetClassName(IntPtr windowHandle, StringBuilder className, int maxCount);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostMessage(
            IntPtr windowHandle,
            uint message,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RedrawWindow(
            IntPtr windowHandle,
            IntPtr updateRect,
            IntPtr updateRegion,
            uint flags);

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
