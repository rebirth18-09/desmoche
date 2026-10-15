using System;
using System.Collections.Generic;
using System.Linq;
using Desmoche.Application;
using Desmoche.Networking.Core;
using Godot;

namespace Desmoche.Presentation;

// Presentación integrada. Conserva las intenciones y la autoridad existentes.
public partial class Mesa
{
    public const string TableStatusPath = ContentPath + "TableStatus";
    public const string TableInfoPath = ContentPath + "TableInfo";
    private HBoxContainer _tableStatus = null!;
    private Label _tableStatusText = null!, _tableInfoText = null!;
    private PanelContainer _tableInfo = null!;
    private Panel _tableMenuBackground = null!;
    private Control _tableHandPortrait = null!;
    private Button _tableMenu = null!, _tableDetails = null!, _tablePrepare = null!;
    private Button _tablePrevious = null!, _tableNext = null!;
    private readonly Dictionary<int, Button> _tableEditButtons = new();
    private bool _tableEditingReady, _tableMenuOpen;
    private float _tableGameOffset, _tableGameMaximum;
    private int? _tableEditingMeld;
    private float _tableHeaderHeight = 48;

    private void InitializeTableEditing()
    {
        GetNode<ScrollContainer>("Scroll").VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
        GetNode<Control>(ContentPath + "Header").Hide();
        _selfPlayer.Hide(); _interactionMessage.Hide();
        GetNode<Control>(InteractionPath + "HintLabel").Hide(); _selectionLabel.Hide();
        GetNode<Control>(InteractionPath + "DraftZone/Content/TitleLabel").Hide();
        _draftEmpty.Hide();
        _draftZone.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        _draftZone.CustomMinimumSize = Vector2.Zero;
        _casinoActions.MouseFilter = MouseFilterEnum.Ignore;
        _draftZone.MouseFilter = MouseFilterEnum.Ignore;
        GetNode<Control>(InteractionPath + "DraftZone/Content").MouseFilter = MouseFilterEnum.Ignore;
        _draftRows.MouseFilter = MouseFilterEnum.Ignore;
        _ownGames.ClipContents = true; _draftRows.ClipContents = true;
        _tableHandPortrait = CasinoPortrait(new PublicPlayerDto(1, "Tú", 0, 0, true, false), false, true);
        _tableHandPortrait.Name = "HandPortrait"; _casinoCanvas.AddChild(_tableHandPortrait);

        _tableStatus = new HBoxContainer { Name = "TableStatus", ZIndex = 12, MouseFilter = MouseFilterEnum.Pass };
        _tableStatus.AddThemeConstantOverride("separation", 8); _casinoCanvas.AddChild(_tableStatus);
        _tableStatusText = Text("", 15, new Color(1, .82f, .4f));
        _tableStatusText.Name = "StatusLabel"; _tableStatusText.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _tableStatusText.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _tableStatusText.AutowrapMode = TextServer.AutowrapMode.Off;
        _tableStatusText.VerticalAlignment = VerticalAlignment.Center; _tableStatus.AddChild(_tableStatusText);
        _cardCountdown.GetParent().RemoveChild(_cardCountdown); _tableStatus.AddChild(_cardCountdown);
        _cardCountdown.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _cardCountdown.AutowrapMode = TextServer.AutowrapMode.Off;
        _cardCountdown.CustomMinimumSize = new Vector2(170, 0);
        _tableDetails = TableButton("InfoButton", "Info"); _tableStatus.AddChild(_tableDetails);
        _tableMenu = TableButton("MenuButton", "Menú"); _tableStatus.AddChild(_tableMenu);
        _tableDetails.Pressed += () => { _tableInfo.Visible = !_tableInfo.Visible; _tableMenuOpen = false; QueueCasinoLayout(); };
        _tableMenu.Pressed += () => { _tableMenuOpen = !_tableMenuOpen; _tableInfo.Hide(); QueueCasinoLayout(); };
        _tableMenuBackground = new Panel { Name = "MenuPanel", ZIndex = 29, Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _tableMenuBackground.AddThemeStyleboxOverride("panel", PanelStyle(false)); _casinoCanvas.AddChild(_tableMenuBackground);

        _tableInfo = new PanelContainer { Name = "TableInfo", ZIndex = 30, Visible = false, MouseFilter = MouseFilterEnum.Stop };
        _tableInfo.AddThemeStyleboxOverride("panel", PanelStyle(false)); _casinoCanvas.AddChild(_tableInfo);
        var column = new VBoxContainer { Name = "Content" }; _tableInfo.AddChild(column);
        column.AddChild(Text("Información de partida", 18));
        var scroll = new ScrollContainer { Name = "Scroll", SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; column.AddChild(scroll);
        _tableInfoText = Text("", 14); _tableInfoText.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _tableInfoText.AutowrapMode = TextServer.AutowrapMode.WordSmart; scroll.AddChild(_tableInfoText);
        Button close = TableButton("CloseButton", "Cerrar"); column.AddChild(close); close.Pressed += () => _tableInfo.Hide();

        _tablePrepare = ContextButton("PrepareSelectionButton", "Preparar juego");
        _tablePrepare.Pressed += PrepareSelected;
        _tablePrevious = TableButton("PreviousGamesButton", "‹"); _tableNext = TableButton("NextGamesButton", "›");
        _tablePrevious.TooltipText = "Ver juegos anteriores"; _tableNext.TooltipText = "Ver juegos siguientes";
        _casinoCanvas.AddChild(_tablePrevious); _casinoCanvas.AddChild(_tableNext);
        _tablePrevious.Pressed += () => { _tableGameOffset = Math.Max(0, _tableGameOffset - 900); QueueCasinoLayout(); };
        _tableNext.Pressed += () => { _tableGameOffset = Math.Min(_tableGameMaximum, _tableGameOffset + 900); QueueCasinoLayout(); };
        // ZIndex solo ordena el dibujo. La barra debe recibir input antes que
        // los paneles y controles de mesa que quedan visualmente debajo.
        GetNode<Control>(ContentPath + "TopBar").MoveToFront();
        _tableEditingReady = true;
    }

    private static Button TableButton(string name, string caption)
    {
        var button = new Button { Name = name, Text = caption, CustomMinimumSize = new Vector2(44, 44), ZIndex = 12 };
        button.AddThemeFontSizeOverride("font_size", 15); return button;
    }

    private static StyleBoxFlat PendingGameStyle() => new()
    {
        BgColor = new Color(.025f, .12f, .09f, .9f), BorderColor = new Color(.94f, .74f, .31f),
        BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
        CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6
    };

    private void RegisterTableMeld(int id)
    {
        var edit = TableButton("EditGame" + id, "Editar");
        edit.TooltipText = "Selecciona cartas y prepara una ampliación, o arrástralas sobre este juego";
        edit.Pressed += () =>
        {
            if (!CanEdit) return;
            _tableEditingMeld = _tableEditingMeld == id ? null : id;
            _targetGame.Select(_targetGame.GetItemIndex(_tableEditingMeld ?? 0));
            Message(_tableEditingMeld is null ? "Preparando un juego nuevo." : "Selecciona cartas para ampliar el juego o arrástralas sobre él.");
            RefreshSelection();
        };
        _ownGames.AddChild(edit); _tableEditButtons[id] = edit;
    }

    private void RefreshTableEditing()
    {
        if (!_tableEditingReady) return;
        if (_interactionView is null || !_interactionView.Melds.Any(m => m.OwnerId == _interactionView.RecipientId && m.Id == _tableEditingMeld))
            _tableEditingMeld = null;
        _tablePrepare.Text = _tableEditingMeld is null ? "Preparar" : "Ampliar";
        _tablePrepare.TooltipText = _tableEditingMeld is null ? "Preparar un juego con las cartas seleccionadas" : "Ampliar el juego elegido con las cartas seleccionadas";
        int selectionCount = _handState.SelectedIds.Count + (_handState.ExternalSelected ? 1 : 0);
        _tablePrepare.Visible = CanEdit && selectionCount > 0;
        _tablePrepare.Disabled = !CanEdit;
        foreach (var pair in _tableEditButtons)
        {
            pair.Value.Disabled = !CanEdit;
            pair.Value.Text = _tableEditingMeld == pair.Key ? "Nuevo" : "Editar";
        }
        _contextActions.Visible = _contextActions.GetChildren().OfType<Control>().Any(c => c.Visible);
        RefreshTableStatus(); QueueCasinoLayout();
    }

    private void RefreshTableStatus()
    {
        if (!_tableEditingReady) return;
        string text = IsDealing ? "Repartiendo cartas…" : _result.Visible ? _result.Text :
            _interactionMessage.Text.Length > 0 ? _interactionMessage.Text : _turn.Text;
        _tableStatusText.Text = text; _tableStatusText.TooltipText = text;
        _cardCountdown.TooltipText = _cardCountdown.Text;
        if (_tableInfo.Visible)
        {
            string own = string.Join(" · ", _selfPlayer.GetChildren().SelectMany(SelectionControls).OfType<Label>().Select(l => l.Text));
            _tableInfoText.Text = string.Join("\n\n", new[] { own, _connection.Text, _phase.Text, _turn.Text,
                _result.Text, _response.Text, _interactionMessage.Text, _selectionLabel.Text, _cardCountdown.Text, _rematchCountdown.Text }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
        }
    }

    private void PlaceScreenControl(Control control, Vector2 screenPosition, Vector2 size)
    {
        Vector2 global = GetGlobalTransformWithCanvas() * screenPosition;
        control.Scale = Vector2.One / CasinoScale;
        control.Position = ((Control)control.GetParent()).GetGlobalTransformWithCanvas().AffineInverse() * global;
        control.Size = size;
    }

    private float LayoutTableChrome(float availableWidth)
    {
        if (!_tableEditingReady) return 48;
        RefreshTableStatus();
        PlaceScreenControl(_tableStatus, new Vector2(8, 4), new Vector2(availableWidth, 44));
        Control top = GetNode<Control>(ContentPath + "TopBar");
        top.ZIndex = 31; _leave.Visible = _tableMenuOpen;
        top.Visible = _tableMenuOpen || _rematch.Visible;
        float topHeight = top.Visible ? Math.Max(44, top.GetCombinedMinimumSize().Y) : 0;
        PlaceScreenControl(top, new Vector2(8, 50), new Vector2(availableWidth, topHeight));
        _tableMenuBackground.Visible = _tableMenuOpen;
        PlaceScreenControl(_tableMenuBackground, new Vector2(4, 46), new Vector2(availableWidth + 8, topHeight + 8));
        _tableHeaderHeight = 50 + (_rematch.Visible ? topHeight + 6 : 0);
        float infoWidth = Math.Min(520, availableWidth), infoHeight = Math.Min(330, Math.Max(180, Size.Y - 64));
        PlaceScreenControl(_tableInfo, new Vector2((Size.X - infoWidth) / 2, 52), new Vector2(infoWidth, infoHeight));
        _selfPlayer.Hide(); GetNode<Control>(ContentPath + "Header").Hide();
        return _tableHeaderHeight;
    }

    private void LayoutTableGames()
    {
        if (!_tableEditingReady) return;
        const float width = 1120;
        float height = Math.Max(96, 44 / CasinoScale), gap = 12;
        var published = _ownGames.GetChildren().OfType<HFlowContainer>().ToArray();
        var pending = _draftRows.GetChildren().OfType<Control>().ToArray();
        var items = new List<(Control Root, HFlowContainer Flow, Button? Action, bool Pending)>();
        foreach (HFlowContainer flow in published)
        {
            int? id = _interactionView?.Melds.FirstOrDefault(m => "Juego" + m.Id == flow.Name.ToString() && m.OwnerId == _interactionView.RecipientId)?.Id;
            bool replaced = id is int value && _handState.Groups.Any(g => g.MeldId == value);
            flow.Visible = !replaced;
            if (id is int key && _tableEditButtons.TryGetValue(key, out Button? edit)) edit.Visible = !replaced && CanEdit;
            if (!replaced) items.Add((flow, flow, id is int k && _tableEditButtons.TryGetValue(k, out Button? button) && button.Visible ? button : null, false));
        }
        foreach (Control row in pending) items.Add((row, row.GetNode<HFlowContainer>("Cards"), row.GetNode<Button>("RemoveButton"), true));
        int count = items.Sum(i => i.Flow.GetChildren().OfType<Carta>().Count());
        float ActionWidth(Button? button) => button is null ? 0 : (Math.Max(44, button.GetCombinedMinimumSize().X) + 6) / CasinoScale;
        float reserved = items.Sum(i => ActionWidth(i.Action)) + Math.Max(0, items.Count - 1) * gap;
        float cardWidth = count == 0 ? 90 : Math.Clamp((width - reserved - count * 5) / count, 50, 90);
        float cardHeight = 96 * cardWidth / 90;
        float total = items.Sum(i => i.Flow.GetChildren().OfType<Carta>().Count() * (cardWidth + 5) - 5 + ActionWidth(i.Action)) + Math.Max(0, items.Count - 1) * gap;
        _tableGameMaximum = Math.Max(0, total - width);
        _tableGameOffset = Math.Clamp(_tableGameOffset, 0, _tableGameMaximum);
        float x = Math.Max(0, (width - total) / 2) - _tableGameOffset;
        _ownGames.Size = new Vector2(width, height);
        // Posicionar el contenedor de borradores sobre la misma zona, sin
        // cambiar sus rutas ni los datos de preparación.
        _casinoActions.Scale = Vector2.One;
        _casinoActions.Position = ((Control)_casinoActions.GetParent()).GetGlobalTransformWithCanvas().AffineInverse() * _ownGames.GetGlobalRect().Position;
        _casinoActions.Size = new Vector2(width, height);
        _draftRows.Size = new Vector2(width, height);
        _draftRows.CustomMinimumSize = new Vector2(width, height);
        _interactionMessage.Hide(); _selectionLabel.Hide(); _draftEmpty.Hide();
        foreach (var item in items)
        {
            int n = item.Flow.GetChildren().OfType<Carta>().Count();
            float cardsWidth = n * (cardWidth + 5) - 5, itemWidth = cardsWidth + ActionWidth(item.Action);
            item.Flow.AddThemeConstantOverride("h_separation", 5);
            foreach (Carta card in item.Flow.GetChildren().OfType<Carta>()) card.SetVisualSize(new Vector2(cardWidth, cardHeight));
            item.Flow.CustomMinimumSize = new Vector2(cardsWidth, cardHeight); item.Flow.Size = new Vector2(cardsWidth + .1f, cardHeight);
            if (item.Pending)
            {
                item.Root.Position = new Vector2(x, 0); item.Root.Size = new Vector2(itemWidth, height);
                item.Flow.Position = new Vector2(4, (height - cardHeight) / 2);
                item.Root.GetNode<Control>("PendingFrame").Size = item.Root.Size;
                item.Action!.Scale = Vector2.One / CasinoScale;
                item.Action.Position = new Vector2(cardsWidth + 6, (height - 44 / CasinoScale) / 2);
                item.Action.Size = new Vector2(Math.Max(44, item.Action.GetCombinedMinimumSize().X), 44);
            }
            else
            {
                item.Flow.Position = new Vector2(x, (height - cardHeight) / 2);
                if (item.Action is { } action)
                {
                    action.Scale = Vector2.One / CasinoScale; action.Size = new Vector2(Math.Max(44, action.GetCombinedMinimumSize().X), 44);
                    action.Position = new Vector2(x + cardsWidth + 6, (height - 44 / CasinoScale) / 2);
                }
            }
            x += itemWidth + gap;
        }
        Label title = GetNode<Label>(ContentPath + "Table/Padding/Surface/MyGame/TitleLabel");
        if (_interactionView?.Phase != GamePhase.Finished)
            title.Text = "Mi juego" + (_handState.Groups.Count > 0 ? " · " + _handState.Groups.Count + " sin confirmar" : _tableEditingMeld is not null ? " · Editando" : "");
        _tablePrevious.Visible = _tableNext.Visible = _tableGameMaximum > 0;
        _tablePrevious.Disabled = _tableGameOffset <= 0; _tableNext.Disabled = _tableGameOffset >= _tableGameMaximum;
        Rect2 gameRect = _ownGames.GetGlobalRect();
        Vector2 global = gameRect.Position + new Vector2(0, (gameRect.Size.Y - 44) / 2);
        _tablePrevious.Scale = _tableNext.Scale = Vector2.One / CasinoScale;
        _tablePrevious.Position = _casinoCanvas.GetGlobalTransformWithCanvas().AffineInverse() * (global - new Vector2(52, 0));
        _tableNext.Position = _casinoCanvas.GetGlobalTransformWithCanvas().AffineInverse() * (global + new Vector2(width * CasinoScale + 8, 0));
        _tablePrevious.Size = _tableNext.Size = new Vector2(44, 44);
        _meldEmpty.Visible = items.Count == 0;
    }

    private bool CloseTablePopupOnInput(InputEvent input)
    {
        if (!_tableEditingReady) return false;
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape } && (_tableMenuOpen || _tableInfo.Visible))
        { _tableMenuOpen = false; _tableInfo.Hide(); QueueCasinoLayout(); return true; }
        if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mouse)
        {
            if (_tableInfo.Visible && !ContainsPointer(_tableInfo, mouse.Position) && !ContainsPointer(_tableDetails, mouse.Position)) _tableInfo.Hide();
            if (_tableMenuOpen && !ContainsPointer(_tableMenu, mouse.Position) && !ContainsPointer(GetNode<Control>(ContentPath + "TopBar"), mouse.Position))
            { _tableMenuOpen = false; QueueCasinoLayout(); }
        }
        return false;
    }
}
