using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
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
        SizeChanged += OnSizeChanged;
        GotKeyboardFocus += OnGotKeyboardFocus;
        MouseDown += OnMouseDown;
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
            (_originalStyle.ToInt64() & ~WsPopup & ~WsCaption & ~WsThickFrame) | WsChild | WsVisible);
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

        NativeMethods.SetParent(_externalHandle, _originalParent);
        if (NativeMethods.GetParent(_externalHandle) != _originalParent)
        {
            LastError = $"The IDE window could not be detached from CodeDock. Error {Marshal.GetLastWin32Error()}.";
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

    private void FocusExternalWindow()
    {
        if (_externalHandle != IntPtr.Zero && NativeMethods.IsWindow(_externalHandle))
        {
            NativeMethods.SetFocus(_externalHandle);
        }
    }

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

    private void OnGotKeyboardFocus(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e) => FocusExternalWindow();

    private void OnMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => FocusExternalWindow();

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

        [DllImport("user32.dll")]
        internal static extern IntPtr SetFocus(IntPtr windowHandle);
    }
}
