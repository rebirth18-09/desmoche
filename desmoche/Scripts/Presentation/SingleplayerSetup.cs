using System;
using Desmoche.Singleplayer.Core;
using Godot;

namespace Desmoche.Presentation;

/// <summary>Recoge opciones; Main y la sesión local crean y ejecutan la partida.</summary>
public partial class SingleplayerSetup : Control
{
    private const string ContentPath = "Scroll/Center/Panel/Padding/Content/";
    private Control _panel = null!;
    private LineEdit _name = null!;
    private MenuChoiceSelector _botCount = null!;
    private MenuChoiceSelector _difficulty = null!;
    private Label _difficultyHint = null!;
    private Label _message = null!;
    private Button _start = null!;
    private Button _back = null!;
    private bool _ready;

    public event Action<LocalMatchOptions>? StartRequested;
    public event Action? BackRequested;

    public override void _Ready()
    {
        _panel = GetNode<Control>("Scroll/Center/Panel");
        _name = GetNode<LineEdit>(ContentPath + "NameInput");
        _botCount = GetNode<MenuChoiceSelector>(ContentPath + "BotCountInput");
        _difficulty = GetNode<MenuChoiceSelector>(ContentPath + "DifficultyInput");
        _difficultyHint = GetNode<Label>(ContentPath + "DifficultyHint");
        _message = GetNode<Label>(ContentPath + "MessageLabel");
        _start = GetNode<Button>(ContentPath + "StartButton");
        _back = GetNode<Button>(ContentPath + "BackButton");

        // Los selectores visibles crean 1–3 bots y los cuatro niveles una sola vez.
        // Sus IDs conservan el contrato de LocalMatchOptions: bots 1–3, niveles 0–3.

        _start.Pressed += RequestStart;
        _back.Pressed += RequestBack;
        _name.TextChanged += OnNameChanged;
        _name.TextSubmitted += OnNameSubmitted;
        _difficulty.ItemSelected += OnDifficultySelected;
        Resized += UpdateWidth;
        _ready = true;
        UpdateWidth();
        UpdateDifficultyHint();
        OnNameChanged(_name.Text);
    }

    public void FocusDefault()
    {
        if (_ready) _name.GrabFocus();
    }

    public void ShowOptions()
    {
        if (!_ready) return;
        _message.Text = "";
        _message.Hide();
        OnNameChanged(_name.Text);
        FocusDefault();
    }

    public void ShowError(string message)
    {
        if (!_ready) return;
        _message.Text = message;
        _message.Visible = message.Length > 0;
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!IsVisibleInTree() || @event is not InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }) return;
        RequestBack();
        GetViewport().SetInputAsHandled();
    }

    public override void _ExitTree()
    {
        if (!_ready) return;
        _start.Pressed -= RequestStart;
        _back.Pressed -= RequestBack;
        _name.TextChanged -= OnNameChanged;
        _name.TextSubmitted -= OnNameSubmitted;
        _difficulty.ItemSelected -= OnDifficultySelected;
        Resized -= UpdateWidth;
        _ready = false;
    }

    private void RequestStart()
    {
        string name = _name.Text.Trim();
        if (name.Length == 0)
        {
            ShowError("Escribe tu nombre para empezar.");
            _name.GrabFocus();
            return;
        }

        _message.Hide();
        StartRequested?.Invoke(new LocalMatchOptions(name, _botCount.GetSelectedId(),
            (BotDifficulty)_difficulty.GetSelectedId()));
    }

    private void RequestBack() => BackRequested?.Invoke();

    private void OnNameChanged(string name) => _start.Disabled = string.IsNullOrWhiteSpace(name);

    private void OnNameSubmitted(string name) => _botCount.GetChoice(_botCount.Selected).GrabFocus();

    private void OnDifficultySelected(long index) => UpdateDifficultyHint();

    private void UpdateDifficultyHint()
    {
        _difficultyHint.Text = (BotDifficulty)_difficulty.GetSelectedId() switch
        {
            BotDifficulty.Easy => "Decisiones sencillas, con margen para practicar.",
            BotDifficulty.Medium => "Prioriza combinaciones y conserva cartas con posibilidades.",
            BotDifficulty.Hard => "Compara más combinaciones y cuida sus pagos.",
            BotDifficulty.Expert => "Busca jugadas completas y aprovecha lo que se ve en la mesa.",
            _ => ""
        };
    }

    private void UpdateWidth()
    {
        if (!_ready) return;
        _panel.CustomMinimumSize = new Vector2(Math.Clamp(Size.X - 48, 280, 760), 0);
        GetNode<Label>(ContentPath + "TitleLabel").AddThemeFontSizeOverride("font_size", Size.Y < 820 ? 40 : 48);
        GetNode<VBoxContainer>("Scroll/Center/Panel/Padding/Content").AddThemeConstantOverride("separation", Size.Y < 820 ? 8 : 12);
    }
}
