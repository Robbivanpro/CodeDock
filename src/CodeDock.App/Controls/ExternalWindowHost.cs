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

    public static readonly DependencyProperty WindowHandleProperty = DependencyProperty.Register(
        nameof(WindowHandle),
        typeof(long),
        typeof(ExternalWindowHost),
        new FrameworkPropertyMetadata(0L, OnWindowHandleChanged));

    public ExternalWindowHost()
    {
        SizeChanged += (_, _) => ResizeExternalWindow();
        GotKeyboardFocus += (_, _) => FocusExternalWindow();
        MouseDown += (_, _) => FocusExternalWindow();
    }

    public long WindowHandle
    {
        get => (long)GetValue(WindowHandleProperty);
        set => SetValue(WindowHandleProperty, value);
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
            throw new InvalidOperationException("The native embedded-window host could not be created.");
        }

        AttachExternalWindow(new IntPtr(WindowHandle));
        return new HandleRef(this, _hostHandle);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        DetachExternalWindow();
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

    private void AttachExternalWindow(IntPtr externalHandle)
    {
        if (_externalHandle == externalHandle)
        {
            ResizeExternalWindow();
            return;
        }

        DetachExternalWindow();
        if (externalHandle == IntPtr.Zero || _hostHandle == IntPtr.Zero || !NativeMethods.IsWindow(externalHandle))
        {
            return;
        }

        _originalParent = NativeMethods.GetParent(externalHandle);
        _originalStyle = NativeMethods.GetWindowLongPtr(externalHandle, GwlStyle);
        _originalExtendedStyle = NativeMethods.GetWindowLongPtr(externalHandle, GwlExStyle);

        var hostedStyle = new IntPtr(
            (_originalStyle.ToInt64() & ~WsPopup & ~WsCaption & ~WsThickFrame) | WsChild | WsVisible);
        NativeMethods.SetWindowLongPtr(externalHandle, GwlStyle, hostedStyle);
        NativeMethods.SetParent(externalHandle, _hostHandle);

        if (NativeMethods.GetParent(externalHandle) != _hostHandle)
        {
            NativeMethods.SetWindowLongPtr(externalHandle, GwlStyle, _originalStyle);
            return;
        }

        _externalHandle = externalHandle;
        ResizeExternalWindow();
    }

    private void DetachExternalWindow()
    {
        if (_externalHandle == IntPtr.Zero)
        {
            return;
        }

        if (NativeMethods.IsWindow(_externalHandle))
        {
            NativeMethods.SetParent(_externalHandle, _originalParent);
            NativeMethods.SetWindowLongPtr(_externalHandle, GwlStyle, _originalStyle);
            NativeMethods.SetWindowLongPtr(_externalHandle, GwlExStyle, _originalExtendedStyle);
            NativeMethods.SetWindowPos(
                _externalHandle,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                SwpNoZOrder | SwpFrameChanged | 0x0001 | 0x0002 | SwpShowWindow);
        }

        _externalHandle = IntPtr.Zero;
        _originalParent = IntPtr.Zero;
        _originalStyle = IntPtr.Zero;
        _originalExtendedStyle = IntPtr.Zero;
    }

    private void ResizeExternalWindow()
    {
        if (_externalHandle == IntPtr.Zero || !NativeMethods.IsWindow(_externalHandle))
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var width = Math.Max(1, (int)Math.Round(ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(1, (int)Math.Round(ActualHeight * dpi.DpiScaleY));
        NativeMethods.SetWindowPos(
            _externalHandle,
            IntPtr.Zero,
            0,
            0,
            width,
            height,
            SwpNoZOrder | SwpFrameChanged | SwpShowWindow);
    }

    private void FocusExternalWindow()
    {
        if (_externalHandle != IntPtr.Zero && NativeMethods.IsWindow(_externalHandle))
        {
            NativeMethods.SetFocus(_externalHandle);
        }
    }

    private static class NativeMethods
    {
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

        [DllImport("user32.dll")]
        internal static extern IntPtr SetFocus(IntPtr windowHandle);
    }
}
