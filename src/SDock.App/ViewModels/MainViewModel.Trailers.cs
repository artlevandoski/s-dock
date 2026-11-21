using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SDock.App.Input;

namespace SDock.App.ViewModels;

// Trailers: a game's YouTube trailer (from IGDB), played inside S-Dock. A pauses, Left/Right skip, Up/Down change the
// volume, B closes.
public sealed partial class MainViewModel
{
    /// <summary>Whether the trailer player can run here (WebView2 installed); tests replace it.</summary>
    public static Func<bool> TrailerPlayerAvailable { get; set; } = () => Controls.TrailerView.RuntimeAvailable;

    /// <summary>A "none found" answer is trusted this long before IGDB is asked again.</summary>
    public static readonly TimeSpan TrailerRetryAfter = TimeSpan.FromDays(30);

    [ObservableProperty] public partial bool IsTrailerOpen { get; set; }
    [ObservableProperty] public partial string TrailerTitle { get; set; } = "";
    [ObservableProperty] public partial string? TrailerVideoId { get; set; }

    /// <summary>A command for the player page (toggle(), seek(10), volume(-10)); the view runs it.</summary>
    public event Action<string>? TrailerScript;

    [RelayCommand]
    private async Task WatchTrailerAsync()
    {
        if (SelectedItem is not { } item || IsTrailerOpen) return;
        if (!TrailerPlayerAvailable())
        {
            await _ui.ShowMessageAsync("Watch Trailer",
                "Trailers play in Microsoft Edge WebView2, which isn't installed on this PC. Install the \"Microsoft Edge WebView2 Runtime\" from Microsoft and try again.");
            return;
        }
        var known = _library.GetTrailer(item.Id);
        var video = known?.VideoId;
        if (video is null && (known is null || DateTime.UtcNow - known.FetchedUtc > TrailerRetryAfter))
        {
            // Not looked up yet: ask IGDB now (without changing the game's match).
            if (Metadata is not { } service)
            {
                if (await _ui.ConfirmAsync("Watch Trailer", "Trailers come from IGDB, which needs your own free IGDB (Twitch) keys. Add them now?"))
                    await IgdbSettingsAsync();
                if (Metadata is null) return;
                service = Metadata;
            }
            StatusText = $"Looking for a trailer for {item.Title}…";
            try
            {
                video = await service.FindTrailerAsync(item.Edition);
            }
            catch (Exception ex) when (ex is SDock.Core.Metadata.IgdbException or HttpRequestException or TaskCanceledException)
            {
                await _ui.ShowMessageAsync("Watch Trailer", $"S-Dock couldn't reach IGDB to look for a trailer.\n\n{ex.Message}");
                return;
            }
        }
        if (video is null)
        {
            await _ui.ShowMessageAsync("Watch Trailer",
                $"IGDB has no trailer for “{item.Title}”. If IGDB knows the game by another name, Rename… it (or Match on IGDB…) and try again.");
            return;
        }
        OpenTrailer(item.Title, video);
    }

    public void OpenTrailer(string title, string videoId)
    {
        CloseMenu();
        TrailerTitle = title;
        TrailerVideoId = videoId;
        IsTrailerOpen = true;
        EventLog.Write($"Trailer opened: {title} ({videoId})");
    }

    [RelayCommand]
    public void CloseTrailer()
    {
        if (!IsTrailerOpen) return;
        IsTrailerOpen = false;
        TrailerVideoId = null;
    }

    /// <summary>A controller (or keyboard) press while a trailer plays.</summary>
    public void TrailerInput(PadAction action)
    {
        switch (action)
        {
            case PadAction.Accept or PadAction.Play: TrailerScript?.Invoke("toggle()"); break;
            case PadAction.Left: TrailerScript?.Invoke("seek(-10)"); break;
            case PadAction.Right: TrailerScript?.Invoke("seek(10)"); break;
            case PadAction.Up: TrailerScript?.Invoke("volume(10)"); break;
            case PadAction.Down: TrailerScript?.Invoke("volume(-10)"); break;
            case PadAction.Cancel or PadAction.Menu or PadAction.GameActions: CloseTrailer(); break;
        }
    }
}
