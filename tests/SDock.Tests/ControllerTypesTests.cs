using Avalonia.Headless.XUnit;
using SDL;
using SDock.App.Input;
using SDock.App.ViewModels;
using SDock.Core;

namespace SDock.Tests;

/// <summary>Other controllers (through SDL): buttons by their label, sticks and triggers in S-Dock's terms.</summary>
public sealed class ControllerTypesTests : IDisposable
{
    private readonly TestEnv _env = new();
    public void Dispose() => _env.Dispose();

    private static readonly Dictionary<SDL_GamepadButton, SDL_GamepadButtonLabel> Xbox = new()
    {
        [SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH] = SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_A,
        [SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST] = SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_B,
        [SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST] = SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_X,
        [SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH] = SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_Y,
    };

    // A Switch Pro controller: A is on the right (east), B at the bottom (south).
    private static readonly Dictionary<SDL_GamepadButton, SDL_GamepadButtonLabel> SwitchPro = new()
    {
        [SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH] = SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_B,
        [SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST] = SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_A,
        [SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST] = SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_Y,
        [SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH] = SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_X,
    };

    private static readonly Dictionary<SDL_GamepadButton, SDL_GamepadButtonLabel> PlayStation = new()
    {
        [SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH] = SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_CROSS,
        [SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST] = SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_CIRCLE,
        [SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST] = SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_SQUARE,
        [SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH] = SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_TRIANGLE,
    };

    private static PadState Press(Dictionary<SDL_GamepadButton, SDL_GamepadButtonLabel> labels, params SDL_GamepadButton[] down) =>
        SdlPads.Translate(b => down.Contains(b),
            b => labels.GetValueOrDefault(b, SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_UNKNOWN), _ => 0);

    [Fact]
    public void The_button_marked_A_or_cross_always_confirms()
    {
        var south = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH;
        var east = SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST;
        Assert.Equal(PadButtons.A, Press(Xbox, south).Buttons);
        Assert.Equal(PadButtons.A, Press(SwitchPro, east).Buttons);    // A on the right on Nintendo controllers
        Assert.Equal(PadButtons.B, Press(SwitchPro, south).Buttons);
        Assert.Equal(PadButtons.A, Press(PlayStation, south).Buttons); // ✕
        Assert.Equal(PadButtons.B, Press(PlayStation, east).Buttons);  // ○
        Assert.Equal(PadButtons.X, Press(PlayStation, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST).Buttons);
        Assert.Equal(PadButtons.Y, Press(PlayStation, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH).Buttons);
        Assert.Equal(PadButtons.A, Press([], south).Buttons);           // no label known: by position
    }

    [Fact]
    public void Start_select_shoulders_dpad_sticks_and_triggers()
    {
        var s = SdlPads.Translate(
            b => b is SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START or SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK
                or SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER or SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT,
            _ => SDL_GamepadButtonLabel.SDL_GAMEPAD_BUTTON_LABEL_UNKNOWN,
            a => a switch
            {
                SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY => (short)-30000,      // SDL: pushed up is negative
                SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER => (short)32767,
                _ => (short)0,
            });
        Assert.Equal(PadButtons.Start | PadButtons.Back | PadButtons.LeftShoulder | PadButtons.DpadLeft, s.Buttons);
        Assert.Equal(30000, s.LeftY);                                      // S-Dock: up is positive
        Assert.Equal(255, s.RightTrigger);
        Assert.Equal(PadAction.Up, GamepadMapper.Direction(s with { Buttons = PadButtons.None }));
        Assert.True(s.AllButtons.HasFlag(PadButtons.RightTrigger));        // RT: next letter
    }

    [Fact]
    public void Two_controllers_count_as_one()
    {
        var a = new PadState(PadButtons.A, LeftX: 100, LeftY: 0, LeftTrigger: 10);
        var b = new PadState(PadButtons.Start, LeftX: -20000, LeftY: 5, RightTrigger: 200);
        var c = SdlPads.Combine(a, b);
        Assert.Equal(PadButtons.A | PadButtons.Start, c.Buttons);
        Assert.Equal(-20000, c.LeftX);
        Assert.Equal((10, 200), (c.LeftTrigger, c.RightTrigger));

        // A controller and its duplicate (e.g. Steam's virtual copy) pressing together: one press, not two.
        var mapper = new GamepadMapper();
        mapper.Reset(new PadState(PadButtons.None));
        var both = SdlPads.Combine(new PadState(PadButtons.A), new PadState(PadButtons.A));
        Assert.Equal([PadAction.Accept], mapper.Update(both, TimeSpan.FromSeconds(1)));
    }

    [AvaloniaFact]
    public void Settings_shows_the_connected_controllers()
    {
        using var lib = _env.OpenLibrary();
        var vm = new MainViewModel(lib, new EmulatorLauncher(), new FakeUi()) { ConnectedControllers = () => ["DualSense Wireless Controller"] };
        vm.OpenSettings();
        vm.SettingsSectionIndex = vm.SettingsSections.ToList().FindIndex(s => s.Title == "Playing");
        Assert.Equal("DualSense Wireless Controller", vm.SettingsRows.Single(r => r.Label == "Controllers").ValueText);
        vm.ConnectedControllers = () => [];
        vm.RefreshSettings();
        Assert.Equal("None connected", vm.SettingsRows.Single(r => r.Label == "Controllers").ValueText);
    }
}
