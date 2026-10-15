using System;
using Desmoche.Domain;
using Godot;

namespace Desmoche.Presentation;

// Solo representa una identidad autorizada; toda interacción produce una intención visual.
public partial class Carta : PanelContainer
{
    [Signal] public delegate void SelectionRequestedEventHandler(int cardId);

    private enum DisplayMode { Empty, Back, Face }
    private DisplayMode _mode;
    private Card? _card;
    private string _emptyText = "VACÍO";
    private Label _rank = null!, _corner = null!, _reverseRank = null!, _reverseSuit = null!;
    private Label _symbol = null!, _caption = null!;
    private Control _reverseCorner = null!, _cornerRoot = null!, _content = null!;
    private MarginContainer _padding = null!;
    private CartaArtwork _artwork = null!;
    private StyleBoxFlat _faceStyle = null!, _selectedStyle = null!, _backStyle = null!, _emptyStyle = null!;
    private StyleBoxFlat _raisedStyle = null!, _greenStyle = null!;
    private Tween? _selectionTween;
    private Tween? _reorderTween;
    private Vector2 _reorderTarget;
    private bool _handSelected, _greenSelection;
    private bool _ready, _interactive, _selected, _pressed, _dragStarted;
    private bool _updatingMetrics;

    public int? FaceCardId => _mode == DisplayMode.Face ? _card?.Id : null;
    public bool IsFaceUp => _mode == DisplayMode.Face;
    public bool IsBack => _mode == DisplayMode.Back;
    public bool HasGameIndicator => _greenSelection && Selected;

    public bool Interactive
    {
        get => _interactive && FaceCardId.HasValue;
        set { _interactive = value; ApplyInteraction(); }
    }
    public bool Selected
    {
        get => _selected && FaceCardId.HasValue;
        set
        {
            bool selected = value && FaceCardId.HasValue;
            if (_selected == selected) return;
            _selected = selected;
            Refresh();
        }
    }

    // Son callbacks locales configurados por Mesa; no viajan por la red.
    public Func<Variant>? DragDataRequested { get; set; }
    public Func<Vector2, Variant, bool>? DropAllowed { get; set; }
    public Action<Vector2, Variant>? DropReceived { get; set; }
    public Action<Carta>? DragPreviewCreated { get; set; }
    public Action<bool>? DragCompleted { get; set; }

    public override void _Ready()
    {
        _rank = GetNode<Label>("Padding/Content/Corner/Rank");
        _corner = GetNode<Label>("Padding/Content/Corner/Suit");
        _reverseRank = GetNode<Label>("Padding/Content/ReverseCorner/Rank");
        _reverseSuit = GetNode<Label>("Padding/Content/ReverseCorner/Suit");
        _reverseCorner = GetNode<Control>("Padding/Content/ReverseCorner");
        _cornerRoot = GetNode<Control>("Padding/Content/Corner");
        _content = GetNode<Control>("Padding/Content");
        _padding = GetNode<MarginContainer>("Padding");
        _symbol = GetNode<Label>("Padding/Content/Symbol");
        _caption = GetNode<Label>("Padding/Content/Caption");
        _artwork = GetNode<CartaArtwork>("Padding/Content/Artwork");
        var serif = new SystemFont { FontNames = new[] { "Georgia", "Times New Roman", "Noto Serif" }, FontWeight = 700 };
        _rank.AddThemeFontOverride("font", serif);
        _reverseRank.AddThemeFontOverride("font", serif);
        _caption.AddThemeFontOverride("font", new SystemFont { FontNames = new[] { "Georgia", "Times New Roman", "Noto Serif" } });
        _faceStyle = Style(new Color(0.993f, 0.981f, 0.951f), new Color(0.68f, 0.56f, 0.38f));
        _selectedStyle = (StyleBoxFlat)_faceStyle.Duplicate();
        _selectedStyle.BorderColor = new Color(1f, 0.77f, 0.20f);
        _selectedStyle.BorderWidthLeft = _selectedStyle.BorderWidthTop = 3;
        _selectedStyle.BorderWidthRight = _selectedStyle.BorderWidthBottom = 3;
        _raisedStyle = (StyleBoxFlat)_faceStyle.Duplicate();
        _raisedStyle.ShadowSize = 7;
        _raisedStyle.ShadowOffset = new Vector2(1, 5);
        _greenStyle = (StyleBoxFlat)_raisedStyle.Duplicate();
        _greenStyle.BorderColor = new Color(0.25f, 0.86f, 0.43f);
        _greenStyle.BorderWidthLeft = _greenStyle.BorderWidthTop = 3;
        _greenStyle.BorderWidthRight = _greenStyle.BorderWidthBottom = 3;
        _backStyle = Style(new Color(0.045f, 0.095f, 0.22f), new Color(0.89f, 0.88f, 0.75f));
        _emptyStyle = Style(new Color(0.015f, 0.09f, 0.07f, 0.38f), new Color(0.28f, 0.45f, 0.36f, 0.6f));
        _ready = true;
        Resized += UpdateMetrics;
        _content.Resized += UpdateMetrics;
        UpdateMetrics();
        Refresh();
        ApplyInteraction();
    }

    public void SetInteraction(bool enabled, bool selected)
    {
        Interactive = enabled;
        Selected = selected;
    }

    public void SetHandSelection(bool selected, bool formsGame)
    {
        selected &= Interactive;
        bool green = selected && formsGame;
        bool changed = _handSelected != selected;
        bool recolor = _greenSelection != green;
        _handSelected = selected; _greenSelection = green;
        OffsetTransformEnabled = true;
        // Godot 4.7: desplazamiento independiente del layout y del tamaño.
        // El área de input acompaña a la carta elevada.
        OffsetTransformVisualOnly = false;
        if (changed)
        {
            _selectionTween?.Kill();
            _selectionTween = CreateTween().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
            _selectionTween.TweenProperty(this, "offset_transform_position_ratio",
                new Vector2(0, selected ? -0.18f : 0), 0.16);
        }
        if (changed || recolor) Refresh();
    }

    public void SetGameIndicator(bool formsGame)
    {
        bool green = formsGame && Selected;
        if (_greenSelection == green) return;
        _greenSelection = green;
        Refresh();
    }

    // Independiente de la elevación proporcional de selección. El contenedor
    // conserva sus posiciones; esta transformación mueve también el input.
    public void SetReorderOffset(Vector2 offset, bool animate = true)
    {
        OffsetTransformEnabled = true; OffsetTransformVisualOnly = false;
        if (animate && _reorderTarget.IsEqualApprox(offset)) return;
        _reorderTarget = offset; _reorderTween?.Kill(); _reorderTween = null;
        if (!animate || !IsInsideTree()) { OffsetTransformPosition = offset; return; }
        _reorderTween = CreateTween().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _reorderTween.TweenProperty(this, "offset_transform_position", offset, 0.16);
    }

    private void ResetHandSelection()
    {
        _selectionTween?.Kill(); _selectionTween = null;
        _handSelected = _greenSelection = false;
        OffsetTransformPositionRatio = Vector2.Zero;
        _reorderTween?.Kill(); _reorderTween = null; _reorderTarget = Vector2.Zero;
        OffsetTransformPosition = Vector2.Zero;
    }

    // Cambia dimensiones reales del Control; índices y arte mantienen su proporción sin Scale.
    public void SetVisualSize(Vector2 size)
    {
        Vector2 target = new(Mathf.Max(32, size.X), Mathf.Max(46, size.Y));
        CustomMinimumSize = target;
        Size = target;
        UpdateMetrics();
    }

    private void UpdateMetrics()
    {
        if (!_ready || _updatingMetrics) return;
        _updatingMetrics = true;
        float width = Mathf.Max(32, Size.X), height = Mathf.Max(46, Size.Y);
        int margin = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(width, height) * 0.032f), 2, 5);
        foreach (string side in new[] { "margin_left", "margin_top", "margin_right", "margin_bottom" })
            _padding.AddThemeConstantOverride(side, margin);
        int fontSize = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(width * 0.28f, height * 0.19f)), 13, 31);
        float indexWidth = Mathf.Max(18, fontSize * 1.42f), rankHeight = fontSize * 1.1f;
        float suitRadius = Mathf.Max(4, fontSize * 0.30f);
        Vector2 indexSize = new(indexWidth, rankHeight + suitRadius * 2 + 1);
        Vector2 contentSize = new(Mathf.Max(1, width - margin * 2), Mathf.Max(1, height - margin * 2));
        _cornerRoot.SetAnchorsPreset(LayoutPreset.TopLeft);
        _cornerRoot.Position = Vector2.Zero;
        _cornerRoot.Size = indexSize;
        _reverseCorner.SetAnchorsPreset(LayoutPreset.TopLeft);
        _reverseCorner.Position = contentSize - indexSize;
        _reverseCorner.Size = indexSize;
        _reverseCorner.PivotOffset = indexSize * 0.5f;
        _reverseCorner.Rotation = Mathf.Pi;
        foreach (Label label in new[] { _rank, _reverseRank })
        {
            label.Position = Vector2.Zero;
            label.Size = new Vector2(indexWidth, rankHeight);
            label.AddThemeFontSizeOverride("font_size", fontSize);
        }
        _caption.AddThemeFontSizeOverride("font_size", Mathf.Clamp(Mathf.RoundToInt(width * 0.13f), 10, 15));
        _artwork.SetIndexMetrics(indexWidth, rankHeight, suitRadius);
        _updatingMetrics = false;
    }

    public void ShowFace(int cardId)
    {
        if (FaceCardId != cardId) { _selected = false; ResetHandSelection(); }
        _card = Card.FromId(cardId);
        _mode = DisplayMode.Face;
        Refresh();
        ApplyInteraction();
    }

    public void ShowBack()
    {
        _card = null;
        _mode = DisplayMode.Back;
        ClearInteraction();
        Refresh();
    }

    public void ShowEmpty(string caption = "VACÍO")
    {
        _card = null;
        _mode = DisplayMode.Empty;
        _emptyText = caption;
        ClearInteraction();
        Refresh();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (!Interactive) return;
        if (@event is InputEventMouseButton mouse && mouse.ButtonIndex == MouseButton.Left)
        {
            if (mouse.Pressed)
            {
                _pressed = true;
                _dragStarted = false;
                GrabFocus();
            }
            else
            {
                bool click = _pressed && !_dragStarted && !GetViewport().GuiIsDragging()
                    && new Rect2(Vector2.Zero, Size).HasPoint(mouse.Position);
                _pressed = false;
                if (click) EmitSignal(SignalName.SelectionRequested, FaceCardId!.Value);
            }
            AcceptEvent();
        }
        else if (@event is InputEventKey key && key.Pressed && !key.Echo
            && key.Keycode is Key.Space or Key.Enter or Key.KpEnter)
        {
            EmitSignal(SignalName.SelectionRequested, FaceCardId!.Value);
            AcceptEvent();
        }
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (!Interactive || DragDataRequested is null) return default;
        int cardId = FaceCardId!.Value;
        Variant data = DragDataRequested();
        if (data.VariantType == Variant.Type.Nil) return default;

        // Mesa oculta visualmente el origen de mano conservando su espacio.
        // Godot libera este preview al acabar el arrastre.
        var previewRoot = new Control { MouseFilter = MouseFilterEnum.Ignore, ZIndex = 50 };
        Carta preview = GD.Load<PackedScene>("res://Scenes/Carta.tscn").Instantiate<Carta>();
        Vector2 previewScale = GetGlobalTransform().Scale.Abs();
        // Mantener las métricas del original y escalar todo su arte. Reducir
        // el tamaño lógico activaría los mínimos de Carta en pantallas chicas.
        preview.SetVisualSize(Size);
        preview.Scale = previewScale;
        preview.ShowFace(cardId);
        preview.SetInteraction(false, Selected);
        preview.SetGameIndicator(HasGameIndicator);
        preview.Position = -Size * previewScale * 0.5f;
        previewRoot.AddChild(preview);
        _dragStarted = true;
        SetDragPreview(previewRoot);
        DragPreviewCreated?.Invoke(preview);
        return data;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        Interactive && DropAllowed?.Invoke(atPosition, data) == true;

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (_CanDropData(atPosition, data)) DropReceived?.Invoke(atPosition, data);
    }

    public override void _Notification(int what)
    {
        // La notificación puede llegar antes del release. No confundir soltar con seleccionar.
        if (what == NotificationDragEnd && _dragStarted)
        {
            _pressed = false; _dragStarted = false;
            DragCompleted?.Invoke(GetViewport().GuiIsDragSuccessful());
        }
    }

    public override void _ExitTree()
    {
        Resized -= UpdateMetrics;
        if (_content is not null) _content.Resized -= UpdateMetrics;
        ClearInteraction();
    }

    private void ClearInteraction()
    {
        ResetHandSelection();
        _interactive = _selected = _pressed = _dragStarted = false;
        DragDataRequested = null;
        DropAllowed = null;
        DropReceived = null;
        DragPreviewCreated = null; DragCompleted = null;
        ApplyInteraction();
    }

    private void ApplyInteraction()
    {
        MouseFilter = Interactive ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        FocusMode = Interactive ? FocusModeEnum.All : FocusModeEnum.None;
        MouseDefaultCursorShape = Interactive ? CursorShape.PointingHand : CursorShape.Arrow;
        if (!Interactive) _pressed = false;
    }

    private void Refresh()
    {
        if (!_ready) return;
        bool face = _mode == DisplayMode.Face && _card is not null;
        Color ink;
        if (face)
        {
            Card card = _card!;
            ink = card.Suit is Suit.Diamonds or Suit.Hearts
                ? new Color(0.76f, 0.12f, 0.19f) : new Color(0.08f, 0.12f, 0.17f);
            _rank.Text = _reverseRank.Text = RankText(card.Rank);
            _corner.Text = _reverseSuit.Text = "";
            _symbol.Text = "";
            _caption.Text = "";
            TooltipText = RankText(card.Rank) + " de " + SuitName(card.Suit).ToLowerInvariant();
            AddThemeStyleboxOverride("panel", HasGameIndicator ? _greenStyle :
                _handSelected ? _raisedStyle : Selected ? _selectedStyle : _faceStyle);
            _artwork.ShowFace(card.Suit, card.Rank, ink);
        }
        else
        {
            ink = new Color(0.64f, 0.72f, 0.66f);
            _rank.Text = _reverseRank.Text = _corner.Text = _reverseSuit.Text = "";
            _symbol.Text = "";
            _caption.Text = _mode == DisplayMode.Back ? "" : _emptyText;
            TooltipText = _mode == DisplayMode.Back ? "Carta boca abajo" : _emptyText;
            AddThemeStyleboxOverride("panel", _mode == DisplayMode.Back ? _backStyle : _emptyStyle);
            if (_mode == DisplayMode.Back) _artwork.ShowBack();
            else _artwork.ShowEmpty();
        }
        _reverseCorner.Visible = face;
        _caption.Visible = _mode == DisplayMode.Empty;
        foreach (Label label in new[] { _rank, _corner, _reverseRank, _reverseSuit, _symbol, _caption })
            label.AddThemeColorOverride("font_color", ink);
    }

    private static string RankText(Rank rank) => rank switch
    {
        Rank.Ace => "A", Rank.Jack => "J", Rank.Queen => "Q", Rank.King => "K",
        _ => ((int)rank).ToString(System.Globalization.CultureInfo.InvariantCulture)
    };
    private static string SuitName(Suit suit) => suit switch
    {
        Suit.Clubs => "TRÉBOLES", Suit.Diamonds => "DIAMANTES",
        Suit.Hearts => "CORAZONES", Suit.Spades => "PICAS", _ => ""
    };
    private static StyleBoxFlat Style(Color background, Color border) => new()
    {
        BgColor = background, BorderColor = border,
        BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
        CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        ShadowColor = new Color(0, 0, 0, 0.40f), ShadowSize = 4, ShadowOffset = new Vector2(1, 3),
        ContentMarginLeft = 0, ContentMarginRight = 0, ContentMarginTop = 0, ContentMarginBottom = 0
    };
}
