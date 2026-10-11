using Godot;

namespace Desmoche.Presentation;

public partial class LoadingScreen : Control
{
    [Signal] public delegate void RetryRequestedEventHandler();
    [Signal] public delegate void QuitRequestedEventHandler();
    private const string ContentPath = "Center/Content/";
    private ProgressBar _progress = null!;
    private Label _status = null!;
    private HBoxContainer _errors = null!;

    public override void _Ready()
    {
        _progress = GetNode<ProgressBar>(ContentPath + "ProgressBar");
        _status = GetNode<Label>(ContentPath + "StatusLabel");
        _errors = GetNode<HBoxContainer>(ContentPath + "ErrorButtons");
        GetNode<Button>(ContentPath + "ErrorButtons/RetryButton").Pressed += Retry;
        GetNode<Button>(ContentPath + "ErrorButtons/QuitButton").Pressed += Quit;
        Reset();
    }

    public void Reset()
    {
        _errors.Visible = false;
        _status.Modulate = Colors.White;
        SetProgress(0, "Preparando el juego…");
    }

    public void SetProgress(double fraction, string message)
    {
        _progress.Value = System.Math.Clamp(fraction, 0, 1) * 100;
        _status.Text = message;
    }

    public void ShowError()
    {
        _status.Text = "No se pudo preparar el juego. Intenta de nuevo.";
        _status.Modulate = new Color(1, 0.7f, 0.65f);
        _errors.Visible = true;
        GetNode<Button>(ContentPath + "ErrorButtons/RetryButton").GrabFocus();
    }

    public override void _ExitTree()
    {
        GetNode<Button>(ContentPath + "ErrorButtons/RetryButton").Pressed -= Retry;
        GetNode<Button>(ContentPath + "ErrorButtons/QuitButton").Pressed -= Quit;
    }

    private void Retry() => EmitSignal(SignalName.RetryRequested);
    private void Quit() => EmitSignal(SignalName.QuitRequested);
}
