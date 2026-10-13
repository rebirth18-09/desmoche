using System;
using Godot;

namespace Desmoche.Presentation;

public partial class StartupMenu : Control
{
    [Signal] public delegate void MultiplayerRequestedEventHandler();
    [Signal] public delegate void SingleplayerRequestedEventHandler();
    [Signal] public delegate void QuitRequestedEventHandler();
    private const string ContentPath = "Scroll/Center/Content/";
    private Button _multiplayer = null!;
    private Button _singleplayer = null!;
    private Button _quit = null!;
    private Button _rules = null!, _settings = null!, _closeHelp = null!, _closeSettings = null!;
    private CheckButton _fullscreen = null!;
    private Control _help = null!, _settingsPanel = null!;
    private VBoxContainer _content = null!;
    private bool _ready;

    public override void _Ready()
    {
        _multiplayer = GetNode<Button>(ContentPath + "Buttons/MultiplayerButton");
        _singleplayer = GetNode<Button>(ContentPath + "Buttons/SingleplayerButton");
        _quit = GetNode<Button>(ContentPath + "Buttons/QuitButton");
        _rules = GetNode<Button>(ContentPath + "Buttons/RulesButton");
        _settings = GetNode<Button>("SettingsButton");
        _help = GetNode<Control>("HelpOverlay");
        _settingsPanel = GetNode<Control>("SettingsOverlay");
        _closeHelp = GetNode<Button>("HelpOverlay/Center/Panel/Padding/Content/CloseButton");
        _closeSettings = GetNode<Button>("SettingsOverlay/Center/Panel/Padding/Content/CloseButton");
        _fullscreen = GetNode<CheckButton>("SettingsOverlay/Center/Panel/Padding/Content/FullscreenButton");
        _content = GetNode<VBoxContainer>("Scroll/Center/Content");
        _multiplayer.Pressed += RequestMultiplayer;
        _singleplayer.Pressed += RequestSingleplayer;
        _quit.Pressed += RequestQuit;
        _rules.Pressed += OpenHelp;
        _settings.Pressed += OpenSettings;
        _closeHelp.Pressed += CloseHelp;
        _closeSettings.Pressed += CloseSettings;
        _fullscreen.Toggled += ChangeFullscreen;
        Resized += UpdateLayout;
        _ready = true;
        UpdateLayout();
    }

    public void FocusDefault()
    {
        if (_help.Visible) _closeHelp.GrabFocus();
        else if (_settingsPanel.Visible) _closeSettings.GrabFocus();
        else _multiplayer.GrabFocus();
    }
    public override void _ExitTree()
    {
        _multiplayer.Pressed -= RequestMultiplayer;
        _singleplayer.Pressed -= RequestSingleplayer;
        _quit.Pressed -= RequestQuit;
        _rules.Pressed -= OpenHelp;
        _settings.Pressed -= OpenSettings;
        _closeHelp.Pressed -= CloseHelp;
        _closeSettings.Pressed -= CloseSettings;
        _fullscreen.Toggled -= ChangeFullscreen;
        Resized -= UpdateLayout;
        _ready = false;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!IsVisibleInTree() || @event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        if (_help.Visible) CloseHelp();
        else if (_settingsPanel.Visible) CloseSettings();
        else return;
        GetViewport().SetInputAsHandled();
    }

    private void OpenHelp() { _help.Show(); _closeHelp.GrabFocus(); }
    private void CloseHelp() { _help.Hide(); _rules.GrabFocus(); }
    private void OpenSettings()
    {
        _fullscreen.SetPressedNoSignal(GetWindow().Mode is Window.ModeEnum.Fullscreen or Window.ModeEnum.ExclusiveFullscreen);
        _settingsPanel.Show(); _fullscreen.GrabFocus();
    }
    private void CloseSettings() { _settingsPanel.Hide(); _settings.GrabFocus(); }
    private void ChangeFullscreen(bool enabled) => GetWindow().Mode = enabled ? Window.ModeEnum.Fullscreen : Window.ModeEnum.Windowed;

    private void UpdateLayout()
    {
        if (!_ready) return;
        float ratio = Math.Clamp(Size.Y / 900f, 0.5f, 1f);
        _content.CustomMinimumSize = new Vector2(Math.Clamp(Size.X - 64, 260, 620), 0);
        GetNode<Label>(ContentPath + "TitleLabel").AddThemeFontSizeOverride("font_size", (int)Math.Min(88 * ratio, (Size.X - 78) / 7.7f));
        GetNode<Label>(ContentPath + "SuitsLabel").AddThemeFontSizeOverride("font_size", (int)(34 * ratio));
        _content.AddThemeConstantOverride("separation", (int)(14 * ratio));
        GetNode<VBoxContainer>(ContentPath + "Buttons").AddThemeConstantOverride("separation", (int)(12 * ratio));
        GetNode<Control>(ContentPath + "Buttons").CustomMinimumSize = new Vector2(Math.Clamp(Size.X - 64, 260, 500), 0);
        _multiplayer.CustomMinimumSize = new Vector2(0, Math.Max(44, 76 * ratio));
        _singleplayer.CustomMinimumSize = new Vector2(0, Math.Max(44, 70 * ratio));
        foreach (Button button in new[] { _multiplayer, _singleplayer }) button.AddThemeFontSizeOverride("font_size", Math.Max(18, (int)(28 * ratio)));
        GetNode<Control>(ContentPath + "HeaderSpace").CustomMinimumSize = new Vector2(0, 26 * ratio);
        GetNode<Control>(ContentPath + "Buttons/ButtonSpace").CustomMinimumSize = new Vector2(0, 14 * ratio);
        GetNode<Control>(ContentPath + "Buttons/FooterSpace").CustomMinimumSize = new Vector2(0, 30 * ratio);
        foreach (string overlay in new[] { "HelpOverlay", "SettingsOverlay" })
            GetNode<Control>(overlay + "/Center/Panel").CustomMinimumSize = new Vector2(Math.Clamp(Size.X - 64, 280, 540), 0);
    }

    private void RequestMultiplayer() => EmitSignal(SignalName.MultiplayerRequested);
    private void RequestSingleplayer() => EmitSignal(SignalName.SingleplayerRequested);
    private void RequestQuit() => EmitSignal(SignalName.QuitRequested);
}
