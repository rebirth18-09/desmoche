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

    public override void _Ready()
    {
        _multiplayer = GetNode<Button>(ContentPath + "Buttons/MultiplayerButton");
        _singleplayer = GetNode<Button>(ContentPath + "Buttons/SingleplayerButton");
        _quit = GetNode<Button>(ContentPath + "Buttons/QuitButton");
        _multiplayer.Pressed += RequestMultiplayer;
        _singleplayer.Pressed += RequestSingleplayer;
        _quit.Pressed += RequestQuit;
    }

    public void FocusDefault() => _multiplayer.GrabFocus();
    public override void _ExitTree()
    {
        _multiplayer.Pressed -= RequestMultiplayer;
        _singleplayer.Pressed -= RequestSingleplayer;
        _quit.Pressed -= RequestQuit;
    }

    private void RequestMultiplayer() => EmitSignal(SignalName.MultiplayerRequested);
    private void RequestSingleplayer() => EmitSignal(SignalName.SingleplayerRequested);
    private void RequestQuit() => EmitSignal(SignalName.QuitRequested);
}
