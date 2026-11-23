using Avalonia.Input;
using Avalonia.Threading;
using SDock.App.Input;
using SDock.App.ViewModels;
using SDock.App.Views;

namespace SDock.App;

/// <summary>
/// --demo-tour: S-Dock shows itself off, driven through the same path as a controller, for recording a promo video.
/// Run it against a demo library (--data-dir). It notes when each part starts (for captions), then closes S-Dock.
/// </summary>
public static class DemoTour
{
    /// <param name="speed">1 = real time; 0 = no pauses (tests).</param>
    public static async Task RunAsync(MainWindow window, MainViewModel vm, string? marksFile, double speed = 1)
    {
        var marks = new List<string>();
        void Mark(string name) => marks.Add($"{DateTime.Now:O} {name}");
        async Task Wait(int ms) { if (speed > 0) await Task.Delay((int)(ms * speed)); Dispatcher.UIThread.RunJobs(); }
        async Task Press(PadAction action, int after = 650)
        {
            GamepadService.NoteController();
            GamepadService.Dispatch(window, action);
            await Wait(after);
        }
        async Task Repeat(PadAction action, int times, int each) { for (var i = 0; i < times; i++) await Press(action, each); }

        window.Cursor = new Cursor(StandardCursorType.None);
        await Wait(2500);                                         // the carousel settles; the first art loads

        Mark("browse");
        await Repeat(PadAction.Right, 6, 700);
        await Repeat(PadAction.Left, 2, 700);

        Mark("letters");
        await Press(PadAction.NextLetter, 1300);
        await Press(PadAction.NextLetter, 1300);
        await Press(PadAction.PreviousLetter, 1300);

        Mark("search");
        await Press(PadAction.Up, 900);                           // search, with the on-screen keyboard
        if (Controls.OnScreenKeyboard.In(window) is { } keyboard)
            foreach (var ch in "moon")
            {
                foreach (var move in keyboard.MovesTo(ch.ToString())) await Press(move, 170);
                await Press(PadAction.Accept, 420);
            }
        await Wait(1100);
        await Press(PadAction.Play, 1900);                        // Start: done, on the results
        await Press(PadAction.Up, 600);                           // clear it again
        await Repeat(PadAction.Cancel, 4, 160);
        await Press(PadAction.Cancel, 1200);

        Mark("trailer");
        await Press(PadAction.GameActions, 900);                  // X: Game Actions
        await Press(PadAction.Down, 2300);                        // resting on Watch Trailer (no video in the tour)

        Mark("actions");
        await Press(PadAction.Up, 500);
        await Press(PadAction.Accept, 1800);                      // the Emulator page
        await Press(PadAction.Cancel, 700);
        await Press(PadAction.Cancel, 900);

        Mark("grid");
        await Press(PadAction.ToggleView, 1800);                  // Y: the grid
        await Repeat(PadAction.Right, 3, 500);
        await Repeat(PadAction.Down, 2, 600);

        Mark("tabs");
        await Press(PadAction.NextLibrary, 700);                  // Arcade Archives
        await Press(PadAction.NextLibrary, 2000);                 // Favorites
        await Press(PadAction.NextLibrary, 2400);                 // Recently Played
        await Repeat(PadAction.PreviousLibrary, 3, 500);

        Mark("settings");
        await Press(PadAction.Menu, 1300);                        // Select: Settings
        await Repeat(PadAction.Down, 3, 900);                     // Emulators, Playing, Appearance
        await Press(PadAction.Accept, 800);
        await Press(PadAction.Right, 1500);                       // the background, changed
        await Press(PadAction.Left, 1000);
        await Press(PadAction.Cancel, 600);
        await Press(PadAction.Cancel, 1200);

        Mark("finale");
        await Press(PadAction.ToggleView, 1800);                  // back to the carousel
        await Repeat(PadAction.Right, 4, 950);
        await Wait(1500);
        Mark("end");
        if (marksFile is not null) await File.WriteAllLinesAsync(marksFile, marks);
    }
}
