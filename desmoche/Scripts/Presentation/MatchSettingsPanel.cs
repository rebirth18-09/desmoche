using System;
using System.Collections.Generic;
using System.Linq;
using Desmoche.Domain;
using Godot;

namespace Desmoche.Presentation;

/// <summary>Edita una copia. Solo Aplicar entrega las opciones a la pantalla creadora.</summary>
public partial class MatchSettingsPanel : ColorRect
{
    private PanelContainer _panel = null!;
    private VBoxContainer _fields = null!;
    private ScrollContainer _scroll = null!;
    private Label _hint = null!;
    private Button _apply = null!, _cancel = null!, _reset = null!;
    private readonly List<SpinBox> _times = new();
    private readonly List<CheckButton> _wins = new();
    private Action<MatchSettings>? _save;
    private Control? _returnFocus;
    private bool _editable;
    private bool _arrangeQueued;
    public bool IsEditable => _editable;

    public override void _Ready()
    {
        Name = "MatchSettingsOverlay";
        Color = new Color(0, 0, 0, .72f);
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _panel = new PanelContainer { Name = "Panel" };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(.025f, .065f, .057f, .99f),
            BorderColor = new Color(.67f, .51f, .24f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 16, CornerRadiusTopRight = 16,
            CornerRadiusBottomLeft = 16, CornerRadiusBottomRight = 16
        };
        _panel.AddThemeStyleboxOverride("panel", style);
        AddChild(_panel);
        var padding = new MarginContainer { Name = "Padding" };
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            padding.AddThemeConstantOverride("margin_" + side, 18);
        _panel.AddChild(padding);
        var body = new VBoxContainer { Name = "Content" };
        body.AddThemeConstantOverride("separation", 12);
        padding.AddChild(body);
        var title = Text("Ajustes de la partida", 26);
        title.Name = "Title";
        title.AddThemeColorOverride("font_color", new Color(.94f, .79f, .47f));
        body.AddChild(title);
        _hint = Text("", 16); body.AddChild(_hint);
        _scroll = new ScrollContainer
        {
            Name = "Scroll", HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill, FollowFocus = true
        };
        body.AddChild(_scroll);
        _fields = new VBoxContainer { Name = "Fields", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _fields.AddThemeConstantOverride("separation", 10);
        _scroll.AddChild(_fields);
        _fields.AddChild(Text("Tiempos · segundos", 20));
        AddTime("CardChoice", "Elegir carta de la oferta o del pozo", "De 1 a 120 segundos", 1, 120);
        AddTime("CardPreparation", "Armar y confirmar el juego", "De 5 a 300 segundos", 5, 300);
        AddTime("Pasa", "Denunciar con Pasa", "De 1 a 30 segundos", 1, 30);
        AddTime("Rematch", "Volver a jugar en multijugador", "De 5 a 120 segundos", 5, 120);
        _fields.AddChild(new HSeparator());
        _fields.AddChild(Text("Victorias al inicio", 20));
        _fields.AddChild(Text("Activa las formas de ganar permitidas para todos los jugadores.", 16));
        AddWin("LowStraight", "Desmoche · A–2–3–4", "Cuatro cartas consecutivas del mismo palo.");
        AddWin("HighStraight", "Desmoche · J–Q–K–A", "Cuatro cartas consecutivas del mismo palo.");
        AddWin("Cuarta", "Desmoche · Cuarta", "Cuatro cartas del mismo rango.");
        AddWin("Color", "Color", "Las nueve cartas rojas o negras, ordenadas por rango.");
        AddWin("Peladilla", "Peladilla", "Sin juegos ni proyectos que complete una sola carta más.");
        _fields.AddChild(Text("La victoria habitual con diez cartas combinadas sigue activa.", 15));
        _reset = new Button { Name = "DefaultsButton", Text = "Restablecer valores", CustomMinimumSize = new Vector2(0, 44) };
        _fields.AddChild(_reset);
        _reset.Pressed += () => LoadValues(MatchSettings.Default);
        var footer = new HBoxContainer { Name = "Footer" };
        footer.AddThemeConstantOverride("separation", 12);
        body.AddChild(footer);
        _cancel = new Button { Name = "CancelButton", Text = "Cancelar", SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 46) };
        _apply = new Button { Name = "ApplyButton", Text = "Aplicar", ThemeTypeVariation = "PrimaryButton",
            SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 46) };
        footer.AddChild(_cancel); footer.AddChild(_apply);
        _cancel.Pressed += Close;
        _apply.Pressed += Apply;
        Resized += Arrange;
        _panel.MinimumSizeChanged += QueueArrange;
        Hide();
        Arrange();
    }

    public static Button AddAccess(VBoxContainer parent, Label summary, int index)
    {
        var row = new HBoxContainer { Name = "MatchSettingsAccess" };
        row.AddThemeConstantOverride("separation", 16);
        summary.Name = "Summary";
        summary.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        summary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        summary.AddThemeFontSizeOverride("font_size", 16);
        row.AddChild(summary);
        var button = new Button { Name = "SettingsButton", Text = "Ajustes",
            Icon = GD.Load<Texture2D>("res://Assets/UI/Salon/Gear.svg"),
            TooltipText = "Tiempos y victorias iniciales de la partida", CustomMinimumSize = new Vector2(144, 46) };
        button.AddThemeConstantOverride("icon_max_width", 26);
        button.AddThemeFontSizeOverride("font_size", 18);
        row.AddChild(button);
        parent.AddChild(row); parent.MoveChild(row, index);
        return button;
    }

    public static string Summary(MatchSettings settings) =>
        $"Elegir: {settings.CardChoiceSeconds} s · Armar: {settings.CardPreparationSeconds} s\n" +
        $"Victorias iniciales: {Enum.GetValues<InitialVictoryKind>().Count(settings.Allows)}/5 activas";

    public void Open(MatchSettings settings, bool editable, Control returnFocus, Action<MatchSettings>? save = null)
    {
        _save = save; _returnFocus = returnFocus; _editable = editable;
        _hint.Text = editable ? "Se aplican a toda la mesa y se conservan al volver a jugar."
            : "Estos son los ajustes elegidos por el creador de la sala.";
        LoadValues(settings);
        foreach (SpinBox spin in _times) spin.Editable = editable;
        foreach (CheckButton check in _wins) check.Disabled = !editable;
        _apply.Visible = _reset.Visible = editable;
        _cancel.Text = editable ? "Cancelar" : "Cerrar";
        Control[] focus = editable ? _times.Select(s => (Control)s.GetLineEdit()).Concat(_wins)
            .Concat(new Control[] { _reset, _cancel, _apply }).ToArray() : new Control[] { _cancel };
        for (int i = 0; i < focus.Length; i++)
        {
            focus[i].FocusNext = focus[i].GetPathTo(focus[(i + 1) % focus.Length]);
            focus[i].FocusPrevious = focus[i].GetPathTo(focus[(i + focus.Length - 1) % focus.Length]);
        }
        Show(); MoveToFront(); Arrange(); _scroll.ScrollVertical = 0;
        _cancel.GrabFocus();
    }

    public void RefreshReadOnly(MatchSettings settings)
    {
        if (Visible && !_editable) LoadValues(settings);
    }

    public void Close()
    {
        Hide(); _save = null;
        if (IsInstanceValid(_returnFocus) && _returnFocus!.IsVisibleInTree()) _returnFocus.GrabFocus();
    }

    public override void _Input(InputEvent @event)
    {
        if (IsVisibleInTree() && @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        { Close(); GetViewport().SetInputAsHandled(); }
    }

    private void Apply()
    {
        if (!_editable) return;
        foreach (SpinBox spin in _times) spin.Apply();
        var settings = new MatchSettings((int)_times[0].Value, (int)_times[1].Value,
            (int)_times[2].Value, (int)_times[3].Value, _wins[0].ButtonPressed,
            _wins[1].ButtonPressed, _wins[2].ButtonPressed, _wins[3].ButtonPressed, _wins[4].ButtonPressed);
        Action<MatchSettings>? save = _save;
        Close(); save?.Invoke(settings);
    }

    private void LoadValues(MatchSettings settings)
    {
        int[] seconds = { settings.CardChoiceSeconds, settings.CardPreparationSeconds, settings.PasaSeconds, settings.RematchSeconds };
        bool[] enabled = { settings.LowStraightEnabled, settings.HighStraightEnabled, settings.CuartaEnabled, settings.ColorEnabled, settings.PeladillaEnabled };
        for (int i = 0; i < _times.Count; i++) _times[i].Value = seconds[i];
        for (int i = 0; i < _wins.Count; i++) _wins[i].SetPressedNoSignal(enabled[i]);
    }

    private void AddTime(string name, string caption, string help, int min, int max)
    {
        var row = new HBoxContainer { Name = name + "Row" };
        row.AddThemeConstantOverride("separation", 12);
        var labels = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        labels.AddChild(Text(caption, 18)); labels.AddChild(Text(help, 14));
        row.AddChild(labels);
        var spin = new SpinBox { Name = name, MinValue = min, MaxValue = max, Step = 1,
            SelectAllOnFocus = true, CustomMinimumSize = new Vector2(122, 46), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        spin.AddThemeFontSizeOverride("font_size", 19);
        row.AddChild(spin); _times.Add(spin); _fields.AddChild(row);
    }

    private void AddWin(string name, string caption, string help)
    {
        var button = new CheckButton { Name = name, Text = caption, TooltipText = help,
            CustomMinimumSize = new Vector2(0, 44) };
        button.AddThemeFontSizeOverride("font_size", 18);
        _wins.Add(button); _fields.AddChild(button);
    }

    private static Label Text(string value, int fontSize)
    {
        var label = new Label { Text = value, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }

    private void Arrange()
    {
        if (_panel is null) return;
        _panel.Size = new Vector2(Math.Min(640, Math.Max(280, Size.X - 32)), Math.Min(720, Math.Max(260, Size.Y - 32)));
        _panel.Position = (Size - _panel.Size) / 2;
    }

    private void QueueArrange()
    {
        if (_arrangeQueued) return;
        _arrangeQueued = true;
        Callable.From(() => { _arrangeQueued = false; if (IsInsideTree()) Arrange(); }).CallDeferred();
    }
}
