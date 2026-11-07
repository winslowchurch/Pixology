using System;
using System.Runtime.InteropServices;

namespace Pixology;

internal sealed class MacTrackpadPinch : IDisposable
{
    private const string ObjectiveCRuntime = "/usr/lib/libobjc.A.dylib";
    private const int GestureBegan = 1;
    private const int GestureChanged = 2;
    private const int GestureEnded = 3;
    private const int GestureCancelled = 4;
    private const int GestureFailed = 5;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GestureAction(IntPtr target, IntPtr selector, IntPtr recognizer);

    [StructLayout(LayoutKind.Sequential)]
    private struct Utf8TypeEncoding
    {
        public byte V;
        public byte At;
        public byte Colon;
        public byte At2;
        public byte Null;

        public Utf8TypeEncoding()
        {
            V = (byte)'v';
            At = (byte)'@';
            Colon = (byte)':';
            At2 = (byte)'@';
            Null = 0;
        }
    }

    private static readonly GestureAction s_gestureAction = HandleGestureAction;
    private static readonly IntPtr s_actionImplementation = Marshal.GetFunctionPointerForDelegate(s_gestureAction);
    private static MacTrackpadPinch s_active = null!;

    private readonly Action<float> _onMagnification;
    private IntPtr _view;
    private IntPtr _recognizer;
    private IntPtr _target;
    private double _lastMagnification;
    private bool _gestureStarted;
    private bool _disposed;

    public MacTrackpadPinch(Action<float> onMagnification)
    {
        _onMagnification = onMagnification;
        s_active = this;
    }

    public void TryAttach()
    {
        if (_disposed)
            return;

        var applicationClass = objc_getClass("NSApplication");
        var application = SendIntPtr(applicationClass, Selector("sharedApplication"));
        var window = SendIntPtr(application, Selector("keyWindow"));
        var view = SendIntPtr(window, Selector("contentView"));
        if (view == IntPtr.Zero || view == _view)
            return;

        RemoveRecognizer();

        var targetClass = GetTargetClass();
        _target = SendIntPtr(SendIntPtr(targetClass, Selector("alloc")), Selector("init"));
        var recognizerClass = objc_getClass("NSMagnificationGestureRecognizer");
        _recognizer = SendIntPtrTwoArgs(
            SendIntPtr(recognizerClass, Selector("alloc")),
            Selector("initWithTarget:action:"),
            _target,
            Selector("handleMagnification:"));
        _view = view;

        if (_target == IntPtr.Zero || _recognizer == IntPtr.Zero)
            throw new InvalidOperationException("Could not create the macOS trackpad pinch recognizer.");

        SendVoidArg(_view, Selector("addGestureRecognizer:"), _recognizer);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        RemoveRecognizer();
        _disposed = true;
        if (ReferenceEquals(s_active, this))
            s_active = null;
    }

    private void RemoveRecognizer()
    {
        if (_view != IntPtr.Zero && _recognizer != IntPtr.Zero)
            SendVoidArg(_view, Selector("removeGestureRecognizer:"), _recognizer);
        if (_recognizer != IntPtr.Zero)
            SendVoid(_recognizer, Selector("release"));
        if (_target != IntPtr.Zero)
            SendVoid(_target, Selector("release"));

        _view = IntPtr.Zero;
        _recognizer = IntPtr.Zero;
        _target = IntPtr.Zero;
        _gestureStarted = false;
    }

    private static IntPtr GetTargetClass()
    {
        const string className = "PixologyTrackpadPinchTarget";
        var targetClass = objc_getClass(className);
        if (targetClass != IntPtr.Zero)
            return targetClass;

        targetClass = objc_allocateClassPair(objc_getClass("NSObject"), className, 0);
        if (targetClass == IntPtr.Zero)
            throw new InvalidOperationException("Could not register the macOS trackpad pinch target.");

        var encoding = new Utf8TypeEncoding();
        if (!class_addMethod(targetClass, Selector("handleMagnification:"), s_actionImplementation, ref encoding))
            throw new InvalidOperationException("Could not install the macOS trackpad pinch callback.");

        objc_registerClassPair(targetClass);
        return targetClass;
    }

    private static void HandleGestureAction(IntPtr target, IntPtr selector, IntPtr recognizer)
    {
        s_active?.HandleGesture(recognizer);
    }

    private void HandleGesture(IntPtr recognizer)
    {
        var state = SendNInt(recognizer, Selector("state"));
        var magnification = SendDouble(recognizer, Selector("magnification"));

        if (state == GestureBegan)
        {
            _lastMagnification = magnification;
            _gestureStarted = true;
        }
        else if (state == GestureChanged && _gestureStarted)
        {
            var delta = magnification - _lastMagnification;
            _lastMagnification = magnification;
            var factor = (float)Math.Max(0.01, 1.0 + delta);
            _onMagnification(factor);
        }
        else if (state == GestureEnded || state == GestureCancelled || state == GestureFailed)
        {
            _gestureStarted = false;
        }
    }

    private static IntPtr Selector(string name)
    {
        return sel_registerName(name);
    }

    [DllImport(ObjectiveCRuntime)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(ObjectiveCRuntime)]
    private static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, nuint extraBytes);

    [DllImport(ObjectiveCRuntime)]
    private static extern void objc_registerClassPair(IntPtr classToRegister);

    [DllImport(ObjectiveCRuntime)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool class_addMethod(
        IntPtr classToChange,
        IntPtr selector,
        IntPtr implementation,
        ref Utf8TypeEncoding types);

    [DllImport(ObjectiveCRuntime)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(ObjectiveCRuntime, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIntPtr(IntPtr receiver, IntPtr selector);

    [DllImport(ObjectiveCRuntime, EntryPoint = "objc_msgSend")]
    private static extern IntPtr SendIntPtrTwoArgs(
        IntPtr receiver,
        IntPtr selector,
        IntPtr firstArgument,
        IntPtr secondArgument);

    [DllImport(ObjectiveCRuntime, EntryPoint = "objc_msgSend")]
    private static extern void SendVoidArg(IntPtr receiver, IntPtr selector, IntPtr argument);

    [DllImport(ObjectiveCRuntime, EntryPoint = "objc_msgSend")]
    private static extern void SendVoid(IntPtr receiver, IntPtr selector);

    [DllImport(ObjectiveCRuntime, EntryPoint = "objc_msgSend")]
    private static extern double SendDouble(IntPtr receiver, IntPtr selector);

    [DllImport(ObjectiveCRuntime, EntryPoint = "objc_msgSend")]
    private static extern nint SendNInt(IntPtr receiver, IntPtr selector);
}
