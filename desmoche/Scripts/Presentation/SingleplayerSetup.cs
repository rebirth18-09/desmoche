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
    private OptionButton _botCount = null!;
    private OptionButton _difficulty = null!;
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
        _botCount = GetNode<OptionButton>(ContentPath + "BotCountInput");
        _difficulty = GetNode<OptionButton>(ContentPath + "DifficultyInput");
        _difficultyHint = GetNode<Label>(ContentPath + "DifficultyHint");
        _message = GetNode<Label>(ContentPath + "MessageLabel");
        _start = GetNode<Button>(ContentPath + "StartButton");
        _back = GetNode<Button>(ContentPath + "BackButton");

        _botCount.AddItem("1 bot · 2 jugadores", 1);
        _botCount.AddItem("2 bots · 3 jugadores", 2);
        _botCount.AddItem("3 bots · 4 jugadores", 3);
        _botCount.Select(0);
        _difficulty.AddItem("Fácil", (int)BotDifficulty.Easy);
        _difficulty.AddItem("Medio", (int)BotDifficulty.Medium);
        _difficulty.AddItem("Difícil", (int)BotDifficulty.Hard);
        _difficulty.AddItem("Experto", (int)BotDifficulty.Expert);
        _difficulty.Select(_difficulty.GetItemIndex((int)BotDifficulty.Medium));

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

    private void OnNameSubmitted(string name) => _botCount.GrabFocus();

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
        if (_ready) _panel.CustomMinimumSize = new Vector2(Math.Clamp(Size.X - 24, 280, 520), 0);
    }
}
