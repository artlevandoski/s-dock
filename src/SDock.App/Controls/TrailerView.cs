using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Microsoft.Web.WebView2.Core;
using SDock.Core;

namespace SDock.App.Controls;

/// <summary>
/// Plays a YouTube trailer inside S-Dock, in Microsoft Edge WebView2 (part of Windows 10 and 11). The player page is
/// served from https://sdock.player/ (in memory), which gives YouTube's embedded player the web origin it requires.
/// Its browsing data stays in the S-Dock folder (data\webview), never in AppData.
/// </summary>
public sealed class TrailerView : NativeControlHost
{
    public const string PlayerHost = "https://sdock.player/";

    private IntPtr _hwnd;
    private CoreWebView2Controller? _controller;
    private string? _pendingVideo;
    private static CoreWebView2Environment? _environment;

    /// <summary>Raised with a message when WebView2 can't start (e.g. its runtime is missing).</summary>
    public event Action<string>? Failed;

    /// <summary>Esc pressed while the player itself has the keyboard (after a click on it).</summary>
    public event Action? CloseRequested;

    /// <summary>True when the WebView2 runtime is installed (it is on current Windows 10 and 11).</summary>
    public static bool RuntimeAvailable
    {
        get
        {
            try { return !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString()); }
            catch (Exception) { return false; }
        }
    }

    public CoreWebView2? Web => _controller?.CoreWebView2;

    /// <summary>Starts playing a YouTube video (by its ID).</summary>
    public void Play(string videoId)
    {
        _pendingVideo = videoId;
        if (Web is { } web) web.Navigate(PlayerHost + "?v=" + Uri.EscapeDataString(videoId));
    }

    /// <summary>Stops the video and blanks the view.</summary>
    public void Stop()
    {
        _pendingVideo = null;
        Web?.Navigate("about:blank");
    }

    /// <summary>Runs a command in the player page: toggle(), seek(seconds), volume(delta).</summary>
    public Task<string> RunAsync(string script) => Web?.ExecuteScriptAsync(script) ?? Task.FromResult("null");

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows() || parent.HandleDescriptor != "HWND") return base.CreateNativeControlCore(parent);
        _hwnd = CreateWindowExW(0, "Static", "", WsChild | WsVisible | WsClipChildren, 0, 0, 1, 1, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        _ = StartAsync(_hwnd);
        return new PlatformHandle(_hwnd, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _controller?.Close();
        _controller = null;
        if (_hwnd != IntPtr.Zero) DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
    }

    private async Task StartAsync(IntPtr hwnd)
    {
        try
        {
            _environment ??= await CoreWebView2Environment.CreateAsync(null, Path.Combine(AppPaths.DataDir, "webview"),
                new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required"));
            var controller = await _environment.CreateCoreWebView2ControllerAsync(hwnd);
            if (_hwnd != hwnd) { controller.Close(); return; } // closed while starting
            _controller = controller;
            controller.DefaultBackgroundColor = System.Drawing.Color.Black;
            controller.AcceleratorKeyPressed += (_, e) =>
            {
                if (e.KeyEventKind == CoreWebView2KeyEventKind.KeyDown && e.VirtualKey == 0x1B) // Esc
                {
                    e.Handled = true;
                    CloseRequested?.Invoke();
                }
            };
            var web = controller.CoreWebView2;
            web.Settings.AreDevToolsEnabled = false;
            web.Settings.AreDefaultContextMenusEnabled = false;
            web.Settings.IsStatusBarEnabled = false;
            web.Settings.IsZoomControlEnabled = false;
            web.AddWebResourceRequestedFilter(PlayerHost + "*", CoreWebView2WebResourceContext.All);
            web.WebResourceRequested += (_, e) =>
            {
                var html = new MemoryStream(Encoding.UTF8.GetBytes(PlayerPage));
                e.Response = _environment.CreateWebResourceResponse(html, 200, "OK", "Content-Type: text/html; charset=utf-8");
            };
            // Links in the player (the YouTube logo, end screens) never open other pages in here.
            web.NewWindowRequested += (_, e) => e.Handled = true;
            web.NavigationStarting += (_, e) =>
            {
                if (!e.Uri.StartsWith(PlayerHost, StringComparison.Ordinal) && e.Uri != "about:blank") e.Cancel = true;
            };
            Resize();
            if (_pendingVideo is { } video) Play(video);
        }
        catch (Exception ex)
        {
            Failed?.Invoke(ex.Message);
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Resize();
    }

    private void Resize()
    {
        if (_controller is null || _hwnd == IntPtr.Zero) return;
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var w = (int)Math.Ceiling(Bounds.Width * scale);
        var h = (int)Math.Ceiling(Bounds.Height * scale);
        MoveWindow(_hwnd, 0, 0, w, h, true);
        _controller.Bounds = new System.Drawing.Rectangle(0, 0, w, h);
    }

    /// <summary>
    /// The player page: YouTube's IFrame player filling the view, with small functions S-Dock calls for the
    /// controller (play/pause, seek, volume) and a state S-Dock can read.
    /// </summary>
    private const string PlayerPage = """
        <!doctype html>
        <html><head><meta charset="utf-8"><meta name="referrer" content="strict-origin-when-cross-origin">
        <style>html,body{margin:0;height:100%;background:#000;overflow:hidden}#p{position:fixed;inset:0;width:100%;height:100%}</style>
        </head><body><div id="p"></div>
        <script>
          window.sdockState = 'loading';
          var id = new URLSearchParams(location.search).get('v');
          var player;
          var tag = document.createElement('script');
          tag.src = 'https://www.youtube.com/iframe_api';
          document.head.appendChild(tag);
          function onYouTubeIframeAPIReady() {
            player = new YT.Player('p', {
              videoId: id, host: 'https://www.youtube-nocookie.com',
              playerVars: { autoplay: 1, rel: 0, playsinline: 1, modestbranding: 1, iv_load_policy: 3, origin: location.origin },
              events: {
                onReady: function (e) { window.sdockState = 'ready'; e.target.playVideo(); },
                onStateChange: function (e) { window.sdockState = ({'-1':'unstarted','0':'ended','1':'playing','2':'paused','3':'buffering','5':'cued'})[e.data] || String(e.data); },
                onError: function (e) { window.sdockState = 'error ' + e.data; }
              }
            });
          }
          function toggle() { if (!player) return; player.getPlayerState() === 1 ? player.pauseVideo() : player.playVideo(); }
          function seek(s) { if (player) player.seekTo(Math.max(0, player.getCurrentTime() + s), true); }
          function volume(d) { if (player) { player.unMute(); player.setVolume(Math.max(0, Math.min(100, player.getVolume() + d))); } }
        </script></body></html>
        """;

    private const uint WsChild = 0x40000000, WsVisible = 0x10000000, WsClipChildren = 0x02000000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string windowName, uint style, int x, int y,
        int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr hwnd, int x, int y, int w, int h, bool repaint);
}
