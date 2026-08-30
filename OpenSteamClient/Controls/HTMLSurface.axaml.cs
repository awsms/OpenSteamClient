// HAS_RENDERIMMEDIATE (for custom avalonia builds (on by default))
//#define HAS_RENDERIMMEDIATE
//#define LOTS_OF_SPEW

using System;
using Avalonia.Reactive;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using OpenSteamworks;
using OpenSteamworks.Callbacks.Structs;
using OpenSteamworks.Generated;
using static OpenSteamworks.Callbacks.CallbackManager;
using Avalonia.Threading;
using Avalonia.Input;
using OpenSteamworks.Data.Enums;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
using Avalonia.Interactivity;
using OpenSteamClient.PlatformSpecific;
using System.Globalization;
using System.Text;
using System.Linq;
using System.Threading;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;
using OpenSteamworks.Client.Startup;
using Avalonia.Media.Imaging;
using System.IO;
using OpenSteamworks.Utils;
using Avalonia.Skia.Helpers;
using OpenSteamworks.Callbacks;
using OpenSteamworks.Data;
using OpenSteamClient.DI;


namespace OpenSteamClient.Controls;

public static class AvaloniaCursors
{
    public readonly static Cursor Default = Cursor.Default;
    public readonly static Cursor None = new(StandardCursorType.None);
    public readonly static Cursor Arrow = new(StandardCursorType.Arrow);
    public readonly static Cursor IBeam = new(StandardCursorType.Ibeam);
    public readonly static Cursor Wait = new(StandardCursorType.Wait);
    public readonly static Cursor Waitarrow = new(StandardCursorType.AppStarting);
    public readonly static Cursor Crosshair = new(StandardCursorType.Cross);
    public readonly static Cursor UpArrow = new(StandardCursorType.UpArrow);
    public readonly static Cursor SizeNW = new(StandardCursorType.TopLeftCorner);
    public readonly static Cursor SizeSE = new(StandardCursorType.BottomRightCorner);
    public readonly static Cursor SizeNE = new(StandardCursorType.TopRightCorner);
    public readonly static Cursor SizeSW = new(StandardCursorType.BottomLeftCorner);
    public readonly static Cursor SizeW = new(StandardCursorType.LeftSide);
    public readonly static Cursor SizeE = new(StandardCursorType.RightSide);
    public readonly static Cursor SizeN = new(StandardCursorType.TopSide);
    public readonly static Cursor SizeS = new(StandardCursorType.BottomSide);
    public readonly static Cursor SizeWE = new(StandardCursorType.SizeWestEast);
    public readonly static Cursor SizeNS = new(StandardCursorType.SizeNorthSouth);
    public readonly static Cursor SizeAll = new(StandardCursorType.SizeAll);
    public readonly static Cursor No = new(StandardCursorType.No);
    public readonly static Cursor Hand = new(StandardCursorType.Hand);
    // This is where we'd have a lot of "panning" cursors, but I have no idea what they are. Maybe the cursor you get when you middle click to navigate?
    public readonly static Cursor Help = new(StandardCursorType.Help);


    public static Cursor GetCursorForEMouseCursor(EMouseCursor steamCursor)
    {
        return steamCursor switch
        {
            EMouseCursor.dc_none => None,
            EMouseCursor.dc_arrow => Arrow,
            EMouseCursor.dc_ibeam => IBeam,
            EMouseCursor.dc_hourglass => Wait,
            EMouseCursor.dc_waitarrow => Waitarrow,
            EMouseCursor.dc_crosshair => Crosshair,
            EMouseCursor.dc_sizenw => SizeNW,
            EMouseCursor.dc_sizese => SizeSE,
            EMouseCursor.dc_sizene => SizeNE,
            EMouseCursor.dc_sizesw => SizeSW,
            EMouseCursor.dc_sizew => SizeW,
            EMouseCursor.dc_sizee => SizeE,
            EMouseCursor.dc_sizen => SizeN,
            EMouseCursor.dc_sizes => SizeS,
            EMouseCursor.dc_sizewe => SizeWE,
            EMouseCursor.dc_sizens => SizeNS,
            EMouseCursor.dc_sizeall => SizeAll,
            EMouseCursor.dc_no => No,
            EMouseCursor.dc_hand => Hand,
            EMouseCursor.dc_help => Help,
            _ => Default,
        };
    }
}

/// <summary>
/// An HTMLSurface for showing a webpage via IClientHTMLSurface.
/// Cannot be instantiated from axaml, must be created in codebehind.
/// Only handles paints, input events and cursor changes, register other handlers yourself
/// </summary>
public partial class HTMLSurface : UserControl
{
    private class HTMLBufferImg : ICustomDrawOperation
    {
        public const int BPP = 4;

        public SKBitmap targetBitmap;
        internal bool isCurrentlyRenderable = false;
        private static readonly SKPaint simplePaint;
        private SKImageInfo imageInfo;

        static HTMLBufferImg()
        {
            simplePaint = new SKPaint
            {
                IsAntialias = true,
                HintingLevel = SKPaintHinting.Full,
                IsAutohinted = true,
                IsDither = true,
                SubpixelText = true,
                BlendMode = SKBlendMode.Src,
            };
        }

        public HTMLBufferImg(SKColorType format, SKAlphaType alphaFormat, int width, int height)
        {
            SpewyLog("allocating initial bitmap of size " + width + "x" + height);
            imageInfo = new(width, height, format, alphaFormat);
            this.targetBitmap = AllocBitmap();
        }

        private SKBitmap AllocBitmap()
        {
            if (imageInfo.Width == 0 || imageInfo.Height == 0)
            {
                throw new InvalidOperationException("Cannot allocate a bitmap of size 0");
            }

            return new(imageInfo);
        }

        public Rect Bounds => new(0, 0, imageInfo.Width, imageInfo.Height);

        public bool Equals(ICustomDrawOperation? other)
        {
            return other != null && other.GetType() == typeof(HTMLBufferImg) && this.targetBitmap.Equals((other as HTMLBufferImg)!.targetBitmap);
        }

        public bool HitTest(Point p)
            => Bounds.Contains(p);

        public void Render(ImmediateDrawingContext context)
        {
            if (!isCurrentlyRenderable)
            {
                return;
            }

            var leasef = context.PlatformImpl.GetFeature<ISkiaSharpApiLeaseFeature>();
            if (leasef == null)
            {
                throw new PlatformNotSupportedException("Your platform does not support SkiaSharpApiLeaseFeature.");
            }

            using (var lease = leasef.Lease())
            {
                lease.SkCanvas.DrawBitmap(targetBitmap, 0f, 0f);
            }
        }
        

        private void ResizeInternal(int newWidth, int newHeight)
        {
            unsafe
            {
                checked
                {
                    // Disallow resizing to 0x0
                    if (newWidth == 0 || newHeight == 0)
                    {
                        SpewyLog("Trying to resize to 0! Resize ignored");
                        return;
                    }

                    this.imageInfo.Width = newWidth;
                    this.imageInfo.Height = newHeight;

                    var oldBitmap = targetBitmap;
                    targetBitmap = AllocBitmap();
                    oldBitmap.Dispose();

                    // if (currentPtr != 0) {
                    //     NativeMemory.Free((void*)currentPtr);
                    // }

                    //currentPtrSize = (nuint)(this.targetBitmapInfo.Width * this.targetBitmapInfo.Height * this.targetBitmapInfo.BytesPerPixel);
                    //currentPtr = (IntPtr)NativeMemory.AllocZeroed(currentPtrSize);
                }
            }
        }

        Stopwatch resizeTime = new();
        Stopwatch installPixelsTime = new();
        
        public void UpdateData(HTML_NeedsPaint_t updateEvent)
        {
            SpewyLog("UpdateData called " + updateEvent.unWide + "x" + updateEvent.unTall + " : " + updateEvent.pBGRA);
            resizeTime.Reset();
            installPixelsTime.Reset();

            if (updateEvent.unWide == 0 || updateEvent.unWide == 1 || updateEvent.unTall == 0 || updateEvent.unTall == 1)
            {
                SpewyLog("Ignoring UpdateData where new width or height was invalid");
                return;
            }

            if (updateEvent.pBGRA == 0)
            {
                SpewyLog("Ignoring UpdateData where dataPtr is null");
                return;
            }

            if (imageInfo.Width != updateEvent.unWide || imageInfo.Height != updateEvent.unTall)
            {
                resizeTime.Start();
                SpewyLog("need resize");
                ResizeInternal((int)updateEvent.unWide, (int)updateEvent.unTall);
                resizeTime.Stop();
            }

            SpewyLog("copy pixels");
            installPixelsTime.Start();
            CopyPixels(updateEvent);
            installPixelsTime.Stop();
            isCurrentlyRenderable = true;
            SpewyLog("UpdateData took " + resizeTime.Elapsed.TotalMilliseconds + "ms + " + installPixelsTime.Elapsed.TotalMilliseconds + "ms");
        }

        private unsafe void CopyPixels(HTML_NeedsPaint_t updateEvent) {
            //var updateRect = SKRect.Create(updateEvent.unUpdateX, updateEvent.unUpdateY, updateEvent.unWide, updateEvent.unTall);
            nint ptr = targetBitmap.GetAddress(0, 0);
            if (ptr == 0) {
                return;
            }

            var numBytesToCopy = updateEvent.unTall * updateEvent.unWide * BPP;
            NativeMemory.Copy((void*)updateEvent.pBGRA, (void*)ptr, numBytesToCopy);

            //TODO: This code bugs, why?
            // var numBytesToCopy = updateEvent.unUpdateTall * updateEvent.unUpdateWide * BPP;

            // // At what target pixel and ptr should we copy the source to
            // var pixelStartTarget = updateEvent.unUpdateX + (updateEvent.unUpdateY * imageInfo.Width);
            // nint startPtrTarget = (nint)(ptr + (pixelStartTarget * BPP));

            // // At what source pixel and ptr should we start copying this change
            // var pixelStartSource = updateEvent.unUpdateX + (updateEvent.unUpdateY * updateEvent.unWide);
            // nint startPtrSource = (nint)(updateEvent.pBGRA + (pixelStartSource * BPP));

            // NativeMemory.Copy((void*)startPtrSource, (void*)startPtrTarget, numBytesToCopy);
        }

        public void Dispose()
        {
            // No-op. This will leak memory, but too bad since Avalonia tries to dispose it AND reuse it at the same time. Don't really know why, but it's just how it's done.
        }

        public void ActuallyDispose() {
            targetBitmap.Dispose();
        }
    }

    private readonly HTMLBufferImg htmlImgBuffer;
    private readonly IClientHTMLSurface surface;
    private readonly ISteamClient client;
    private readonly SteamHTML htmlHost;
    private readonly IDisposable boundsSubscription;
    private readonly ICallbackHandler[] callbackHandlers;
    private bool htmlHostStarted;
    private bool isDisposed;
    private static readonly Encoding utfEncoder = new UTF32Encoding(false, true, false);
    public HHTMLBrowser BrowserHandle { get; private set; } = 0;
    // The scroll multiplier affects how fast scrolling works. Piping the scroll wheel directly into steam makes scrolling slow, so simply multiply it by this value.
    //TODO: this is terrible and doesn't allow for smooth scrolling. This seems to be the same underlying issue that ValveSteam had on Linux for a long time, until their recent switch to a full web browser window. 
    // However, on the release of the deck, if you tricked ValveSteam it was running on deck, it scrolled the library smoothly (albeit with only software rendering)
    // Maybe we can use something else to simulate smooth scrolling?
    private const int SCROLL_MULTIPLIER = 35;

    public HTMLSurface() : base()
    {
        this.client = AvaloniaApp.Container.Get<ISteamClient>();
        this.htmlHost = AvaloniaApp.Container.Get<SteamHTML>();

        InitializeComponent();

        // Allocate an initial buffer at 720p, since we don't know our size yet
        this.htmlImgBuffer = new HTMLBufferImg(SKColorType.Bgra8888, SKAlphaType.Unpremul, 720, 1080);
        this.Focusable = true;

        this.surface = client.IClientHTMLSurface;
        this.callbackHandlers =
        [
            client.CallbackManager.Register<HTML_NeedsPaint_t>(this.OnHTML_NeedsPaint),
            client.CallbackManager.Register<HTML_SetCursor_t>(this.OnHTML_SetCursor),
            client.CallbackManager.Register<HTML_ShowToolTip_t>(this.OnHTML_ShowToolTip_t),
            client.CallbackManager.Register<HTML_HideToolTip_t>(this.OnHTML_HideToolTip_t),
        ];
        // Reactive bloat. No events here, need to do this instead...
        this.boundsSubscription = this.GetObservable(BoundsProperty).Subscribe(new AnonymousObserver<Rect>(BoundsChange));
    }

    private void OnHTML_SetCursor(ICallbackHandler handler, in HTML_SetCursor_t setCursor)
    {
        if (setCursor.unBrowserHandle == BrowserHandle)
        {
            SetCursorNonUICode(AvaloniaCursors.GetCursorForEMouseCursor(setCursor.eMouseCursor));
        }
    }

    public void Refresh()
    {
        this.surface.Reload(this.BrowserHandle);
    }

    public void Previous()
    {
        this.surface.GoBack(this.BrowserHandle);
    }

    public void Next()
    {
        this.surface.GoForward(this.BrowserHandle);
    }

    public void OpenDevTools()
    {
        this.surface.OpenDeveloperTools(this.BrowserHandle);
    }

    private void SetCursorNonUICode(Cursor cursor)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            this.Cursor = cursor;
        }, DispatcherPriority.Input);
    }

    private void ForceRedrawNonUICode()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
#if HAS_RENDERIMMEDIATE
            VisualRoot?.Renderer.RenderImmediate(this);
#else
            VisualRoot?.Renderer.AddDirty(this);
            VisualRoot?.Renderer.Paint(this.Bounds);
#endif
        }, DispatcherPriority.MaxValue);
#if !HAS_RENDERIMMEDIATE
        Dispatcher.UIThread.Invoke(() => { }, DispatcherPriority.ContextIdle);
#endif
    }

    private void BoundsChange(Rect newBounds)
    {
        if (this.BrowserHandle != 0)
        {
            SpewyLog($"Bounds changed! ({(uint)newBounds.Width}x{(uint)newBounds.Height})");
            this.surface.SetSize(this.BrowserHandle, (uint)newBounds.Width, (uint)newBounds.Height);
        }
    }

    Stopwatch paintUpdateDataTime = new();
    Stopwatch forceRedrawTime = new();
    private void OnHTML_NeedsPaint(ICallbackHandler handler, in HTML_NeedsPaint_t paintEvent)
    {
        if (paintEvent.unBrowserHandle == this.BrowserHandle)
        {
            forceRedrawTime.Reset();
            paintUpdateDataTime.Reset();
            paintUpdateDataTime.Start();
            htmlImgBuffer.UpdateData(paintEvent);
            SpewyLog("Page scale: " + paintEvent.flPageScale);
            SpewyLog("Avalonia scale: " + TopLevel.GetTopLevel(this)?.RenderScaling);
            paintUpdateDataTime.Stop();
            forceRedrawTime.Start();
            this.ForceRedrawNonUICode();
            forceRedrawTime.Stop();
            SpewyLog("Paint took " + forceRedrawTime.Elapsed.TotalMilliseconds + "ms + " + paintUpdateDataTime.Elapsed.TotalMilliseconds + "ms ");
            htmlImgBuffer.isCurrentlyRenderable = false;
        }
    }

    #if LOTS_OF_SPEW
    private static void SpewyLog(object? str) {
        Console.WriteLine(str);
    }
    #else
    private static void SpewyLog(object? str) {
        
    }
    #endif

    private void OnHTML_ShowToolTip_t(ICallbackHandler handler, in HTML_ShowToolTip_t ev)
    {
        if (ev.unBrowserHandle == this.BrowserHandle)
        {
            var message = ev.pchMsg;
            Dispatcher.UIThread.Invoke(() =>
            {
                this[ToolTip.TipProperty] = message;
                this[ToolTip.IsOpenProperty] = true;
            });
        }
    }

    private void OnHTML_HideToolTip_t(ICallbackHandler handler, in HTML_HideToolTip_t ev)
    {
        if (ev.unBrowserHandle == this.BrowserHandle)
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                this[ToolTip.IsOpenProperty] = false;
            });
        }
    }

    public void SetBackgroundMode(bool background)
    {
        this.surface.SetBackgroundMode(this.BrowserHandle, background);
    }

    internal void RequestRepaint()
    {
        //TODO: this is a hack. Can we find a better way?
        this.surface.SetSize(this.BrowserHandle, (uint)this.Bounds.Width, (uint)this.Bounds.Height);
    }

    /// <summary>
    /// Sets this webview with the current user's web auth token for accessing steam websites.
    /// </summary>
    /// <returns></returns>
    public async Task SetSteamCookies()
    {
        string[] domains = ["https://store.steampowered.com", "https://help.steampowered.com", "https://steamcommunity.com"];
        StringBuilder language = new(128);
        this.client.IClientUser.GetLanguage(language, language.Capacity);

        string vractiveStr = "0";
        if (this.client.IClientUtils.IsSteamRunningInVR())
        {
            vractiveStr = "1";
        }

        foreach (var item in domains)
        {
            checked
            {
                this.surface.SetCookie(item, "steamLoginSecure", await GetWebToken(), "/", 0, true, true);
                this.surface.SetCookie(item, "Steam_Language", language.ToString(), "/", 0, false, false);
                this.surface.SetCookie(item, "vractive", vractiveStr, "/", 0, false, false);
            }
        }
    }

    public async Task<string> GetWebToken()
    {
        // No need to use incrementing stringbuilder here, since the webauth tokens are always this size
        StringBuilder sb = new(1024);
        string token;
        if (!this.client.IClientUser.GetCurrentWebAuthToken(sb, (uint)sb.Capacity))
        {
			var result = await this.client.CallbackManager.RunAsyncCall(() => this.client.IClientUser.RequestWebAuthToken());
            if (result.Failed)
            {
                throw new InvalidOperationException("SetWebAuthToken failed due to CallResult failure: " + result.FailureReason);
            }

            token = result.Data.m_rgchToken;
        }
        else
        {
            token = sb.ToString();
        }

        return token;
    }

    public async Task<HHTMLBrowser> CreateBrowserAsync(string userAgent, string? customCSS)
    {
        await GetWebToken();
        await this.htmlHost.Start();
        this.htmlHostStarted = true;

        if (this.isDisposed)
        {
            StopHTMLHost();
            throw new ObjectDisposedException(nameof(HTMLSurface));
        }

		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
		OpenSteamworks.Callbacks.CallResult<HTML_BrowserReady_t> result;
		SteamAPICall<HTML_BrowserReady_t> callHandle = default;
		try
		{
			result = await client.CallbackManager.RunAsyncCall(
				() => callHandle = this.surface.CreateBrowser(userAgent, customCSS), timeout.Token);
		}
		catch (Exception exception) when (exception.Message == "API call not completed")
		{
			result = await RetryGetBrowserResultAsync(callHandle, exception, timeout.Token);
		}
		catch
		{
			StopHTMLHostAfterFailure();
			throw;
		}

        if (result.Failed)
        {
            StopHTMLHostAfterFailure();
            throw new InvalidOperationException("CreateBrowser failed due to CallResult failure: " + result.FailureReason);
        }

        if (this.isDisposed)
        {
            this.surface.RemoveBrowser(result.Data.unBrowserHandle);
            StopHTMLHost();
            throw new ObjectDisposedException(nameof(HTMLSurface));
        }

        this.BrowserHandle = result.Data.unBrowserHandle;

        SpewyLog("Created new browser with handle " + this.BrowserHandle);
        this.surface.AllowStartRequest(this.BrowserHandle, true);
        this.surface.SetSize(this.BrowserHandle, (uint)this.Bounds.Width, (uint)this.Bounds.Height);

        return result.Data.unBrowserHandle;
    }

	private async Task<CallResult<HTML_BrowserReady_t>> RetryGetBrowserResultAsync(
		SteamAPICall<HTML_BrowserReady_t> callHandle,
		Exception originalException,
		CancellationToken cancellationToken)
	{
		const int callbackId = 4501;
		var callbackSize = Marshal.SizeOf<HTML_BrowserReady_t>();
		var callbackData = new byte[callbackSize];

		for (var attempt = 0; attempt < 20; attempt++)
		{
			if (this.client.IClientUtils.GetAPICallResult(
				callHandle, callbackData, callbackSize, callbackId, out var failed))
			{
				var data = MemoryMarshal.Read<HTML_BrowserReady_t>(callbackData);
				return new CallResult<HTML_BrowserReady_t>(
					failed, this.client.IClientUtils.GetAPICallFailureReason(callHandle), data);
			}

			await Task.Delay(25, cancellationToken);
		}

		var failureReason = this.client.IClientUtils.GetAPICallFailureReason(callHandle);
		StopHTMLHostAfterFailure();
		throw new InvalidOperationException(
			$"CreateBrowser result remained unavailable ({failureReason}).", originalException);
	}

	private void StopHTMLHostAfterFailure()
	{
		try
		{
			StopHTMLHost();
		}
		catch (InvalidOperationException)
		{
			// The steamwebhelper watcher may be replacing an exited process.
		}
	}

    private void StopHTMLHost()
    {
        if (!this.htmlHostStarted)
        {
            return;
        }

        this.htmlHostStarted = false;
        this.htmlHost.Stop();
    }

    public void RemoveBrowser()
    {
        if (this.isDisposed)
        {
            return;
        }

        this.isDisposed = true;
        this.boundsSubscription.Dispose();
        foreach (var callbackHandler in this.callbackHandlers)
        {
            callbackHandler.Dispose();
        }

        // Invalidate the handle before calling native code so queued callbacks and
        // input events cannot target a browser while it is being destroyed.
        var browserHandle = this.BrowserHandle;
        this.BrowserHandle = 0;
        try
        {
            if (browserHandle != 0)
            {
                this.surface.RemoveBrowser(browserHandle);
            }
        }
        finally
        {
            // Shutdown the interface if no other surfaces are left.
            StopHTMLHost();
        }
    }

    public void LoadURL(string url)
    {
        this.surface.LoadURL(this.BrowserHandle, url, null);
    }

    public override void Render(DrawingContext context)
    {
        context.DrawRectangle(Brushes.Black, null, this.Bounds, 0, 0, default);
        context.Custom(this.htmlImgBuffer);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (this.BrowserHandle != 0)
        {
            this.surface.MouseWheel(this.BrowserHandle, (int)e.Delta.Y * SCROLL_MULTIPLIER, (int)e.Delta.X * SCROLL_MULTIPLIER);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (this.BrowserHandle != 0)
        {
            var rect = e.GetCurrentPoint(this).Position;
            this.surface.MouseMove(this.BrowserHandle, (int)rect.X, (int)rect.Y);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerMoved(e);
        if (this.BrowserHandle != 0)
        {
            var prop = e.GetCurrentPoint(this).Properties;
            var btn = PointerPropertiesToHTMLMouseButton(prop);
            this.surface.MouseDown(this.BrowserHandle, btn);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerMoved(e);
        if (this.BrowserHandle != 0)
        {
            var prop = e.GetCurrentPoint(this).Properties;
            var btn = PointerPropertiesToHTMLMouseButton(prop);
            this.surface.MouseUp(this.BrowserHandle, btn);
        }
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        if (this.BrowserHandle != 0)
        {
            this.surface.SetKeyFocus(this.BrowserHandle, true);
        }
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        if (this.BrowserHandle != 0)
        {
            this.surface.SetKeyFocus(this.BrowserHandle, false);
        }
    }

    private static EHTMLMouseButton PointerPropertiesToHTMLMouseButton(PointerPointProperties prop)
    {
        switch (prop.PointerUpdateKind)
        {
            case PointerUpdateKind.LeftButtonPressed:
            case PointerUpdateKind.LeftButtonReleased:
                return EHTMLMouseButton.Left;

            case PointerUpdateKind.MiddleButtonPressed:
            case PointerUpdateKind.MiddleButtonReleased:
                return EHTMLMouseButton.Middle;

            case PointerUpdateKind.RightButtonPressed:
            case PointerUpdateKind.RightButtonReleased:
                return EHTMLMouseButton.Right;

            default:
                throw new Exception("PointerPressed received but no mouse buttons were down.");
        }
    }

    private static EHTMLKeyModifiers KeyModifiersToEHTMLKeyModifiers(KeyModifiers modifiers)
    {
        EHTMLKeyModifiers htmlKeyModifiers = EHTMLKeyModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            htmlKeyModifiers |= EHTMLKeyModifiers.CtrlDown;
        }

        if (modifiers.HasFlag(KeyModifiers.Alt))
        {
            htmlKeyModifiers |= EHTMLKeyModifiers.AltDown;
        }

        if (modifiers.HasFlag(KeyModifiers.Shift))
        {
            htmlKeyModifiers |= EHTMLKeyModifiers.ShiftDown;
        }

        return htmlKeyModifiers;
    }

    private static int GetNativeKeyCodeForKeyEvent(KeyEventArgs e)
    {
        if (OperatingSystem.IsLinux())
        {
            return (int)X11KeyTransform.X11KeyFromKey(e.Key);
        }
        else if (OperatingSystem.IsWindows())
        {
            return 0;
            // return (int)e.NativeKeyCode;
        }

        throw new PlatformNotSupportedException("This OS is not supported.");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Keys shouldn't do anything else as long as this control has focus
        e.Handled = true;

        base.OnKeyDown(e);
        SpewyLog("OnKeyDown a:'" + e.Key + "' s:'" + e.KeySymbol + "' n:'" + GetNativeKeyCodeForKeyEvent(e) + "'");
        if (this.BrowserHandle != 0)
        {
            // If the key has an avalonia-provided symbol AND the keypress doesn't have any modifiers it's eligible for being typed
            if (e.KeySymbol != null)
            {
                // strip out all control characters so we don't type them
                string controlCharsRemoved = new(e.KeySymbol.Where(c => !char.IsControl(c)).ToArray());

                // If the character didn't consist entirely of control characters, type it
                if (controlCharsRemoved != string.Empty)
                {
                    SpewyLog("is char");
                    this.surface.KeyChar(this.BrowserHandle, BitConverter.ToInt32(utfEncoder.GetBytes(e.KeySymbol)), KeyModifiersToEHTMLKeyModifiers(e.KeyModifiers));
                }
            }

            SpewyLog("is actual key");
            this.surface.KeyDown(this.BrowserHandle, GetNativeKeyCodeForKeyEvent(e), KeyModifiersToEHTMLKeyModifiers(e.KeyModifiers), false);
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        SpewyLog("OnKeyUp a:'" + e.Key + "' s:'" + e.KeySymbol + "' n:'" + GetNativeKeyCodeForKeyEvent(e) + "'");
        if (this.BrowserHandle != 0)
        {
            this.surface.KeyUp(this.BrowserHandle, GetNativeKeyCodeForKeyEvent(e), KeyModifiersToEHTMLKeyModifiers(e.KeyModifiers));
        }
    }
}
