using System;
using System.Linq;
using Desmoche.Application;
using Desmoche.Domain;
using Desmoche.Sessions;
using Desmoche.Singleplayer;
using Desmoche.Networking.Core;
using Godot;

namespace Desmoche.Presentation;

// Solo representa la vista por destinatario; no instancia un motor de partida.
public partial class Mesa : Control
{
    public const string ContentPath = "Scroll/Padding/Content/";
    public const string CenterPath = ContentPath + "Table/Padding/Surface/Center/";
    public const string RivalGamesPath = ContentPath + "Table/Padding/Surface/OpponentGames";
    public const string OwnGamesPath = ContentPath + "Table/Padding/Surface/MyGame/Rows";
    public const string SelfPlayerPath = ContentPath + "HandZone/Padding/Content/HandHeader/SelfPlayers";
    private IGameSession? _session;
    private PackedScene _cardScene = null!;
    private Label _connection = null!, _phase = null!, _turn = null!, _result = null!, _response = null!;
    private Label _deckCount = null!, _discardCount = null!, _handTitle = null!, _handEmpty = null!, _meldEmpty = null!;
    private Carta _deck = null!, _discard = null!, _offered = null!;
    private Control _offerZone = null!, _revealedZone = null!;
    private HFlowContainer _players = null!, _selfPlayer = null!, _hand = null!, _rivalGames = null!;
    private Control _ownGames = null!;
    private VBoxContainer _revealed = null!;
    private Button _leave = null!;
    private Button _rematch = null!;
    private bool _ready;

    public int RenderedPlayerCount => _ready ? _players.GetChildCount() + _selfPlayer.GetChildCount() : 0;

    public override void _Ready()
    {
        _cardScene = GD.Load<PackedScene>("res://Scenes/Carta.tscn");
        _connection = GetNode<Label>(ContentPath + "Header/ConnectionLabel");
        _phase = GetNode<Label>(ContentPath + "Header/PhaseLabel");
        _turn = GetNode<Label>(ContentPath + "Header/TurnLabel");
        _result = GetNode<Label>(ContentPath + "Header/ResultLabel");
        _response = GetNode<Label>(ContentPath + "Header/ResponseLabel");
        _players = GetNode<HFlowContainer>(ContentPath + "PlayerZone/Padding/Players");
        _selfPlayer = GetNode<HFlowContainer>(SelfPlayerPath);
        _rivalGames = GetNode<HFlowContainer>(RivalGamesPath);
        _ownGames = GetNode<Control>(OwnGamesPath);
        _meldEmpty = GetNode<Label>(ContentPath + "Table/Padding/Surface/MyGame/EmptyLabel");
        _deckCount = GetNode<Label>(CenterPath + "Mazo/Content/CountLabel");
        _discardCount = GetNode<Label>(CenterPath + "Pozo/Content/CountLabel");
        _deck = GetNode<Carta>(CenterPath + "Mazo/Content/Carta");
        _discard = GetNode<Carta>(CenterPath + "Pozo/Content/Carta");
        _offerZone = GetNode<Control>(CenterPath + "Oferta");
        _offered = GetNode<Carta>(CenterPath + "Oferta/Content/Carta");
        _handTitle = GetNode<Label>(ContentPath + "HandZone/Padding/Content/HandHeader/TitleLabel");
        _hand = GetNode<HFlowContainer>(ContentPath + "HandZone/Padding/Content/Cards");
        _handEmpty = GetNode<Label>(ContentPath + "HandZone/Padding/Content/EmptyLabel");
        _revealedZone = GetNode<Control>(ContentPath + "RevealedZone");
        _revealed = GetNode<VBoxContainer>(ContentPath + "RevealedZone/Rows");
        _leave = GetNode<Button>(ContentPath + "TopBar/LeaveButton");
        _leave.Pressed += LeaveRoom;
        _rematch = new Button { Name = "RematchButton", Text = "Otra partida", Visible = false };
        _leave.GetParent().AddChild(_rematch);
        _leave.GetParent().MoveChild(_rematch, _leave.GetIndex());
        _rematch.Pressed += RestartLocal;
        InitializeInteraction();
        InitializeCasino();
        InitializeCountdowns();
        InitializeDealAnimation();
        InitializeFinalHands();
        InitializeHandDrag();
        InitializeTableEditing();
        InitializeTableActions();
        _offered.SelectionRequested += ToggleExternal;
        _discard.SelectionRequested += ToggleExternal;
        _ready = true;
        RefreshNetworkView();
    }

    public void Bind(IGameSession session)
    {
        if (!_ready) throw new InvalidOperationException("Mesa debe estar lista antes de Bind.");
        Unbind();
        _session = session;
        session.ViewChanged += OnViewChanged;
        session.SessionChanged += OnSessionChanged;
        session.ActionCompleted += OnActionCompleted;
        RefreshNetworkView();
    }

    public void Unbind()
    {
        CancelDealAnimation();
        if (_session is not null)
        {
            _session.ViewChanged -= OnViewChanged;
            _session.SessionChanged -= OnSessionChanged;
            _session.ActionCompleted -= OnActionCompleted;
            _session = null;
        }
        if (_ready) RefreshNetworkView();
    }

    public override void _ExitTree()
    {
        Unbind();
        if (!_ready) return;
        _leave.Pressed -= LeaveRoom;
        _rematch.Pressed -= RestartLocal;
        _offered.SelectionRequested -= ToggleExternal;
        _discard.SelectionRequested -= ToggleExternal;
        _finalHandClose.Pressed -= CloseFinalHand;
        _finalDeckButton.Pressed -= ShowFinalDeck;
        AbortHandDrag();
        DisposeDynamicInteraction();
        DisposeCasino();
        DisposeDealAnimation();
        _ready = false;
    }

    public void FocusDefault()
    {
        if (_ready && Visible) _tableMenu.GrabFocus();
    }

    private void OnViewChanged(SnapshotDto view) => RefreshNetworkView();
    private void OnSessionChanged() => RefreshNetworkView();
    private void RestartLocal()
    {
        if (_session is LocalGameSession local) local.Restart();
        else RequestAction(ActionKind.JoinRematch);
    }

    private void LeaveRoom()
    {
        if (_session is null || !_session.IsActive) return;
        _session.Close(_session.IsLocal ? "Partida local cerrada." :
            _session.CanManageMatch ? "Sala cerrada por el host." : "Has salido de la sala.");
    }

    private void RefreshNetworkView()
    {
        if (!_ready) return;
        _leave.Disabled = _session?.IsActive != true;
        _leave.Text = _session?.IsLocal == true ? "Salir de partida" :
            _session?.CanManageMatch == true ? "Cerrar sala" : "Salir de sala";
        DisplaySnapshot(_session?.View, _session?.Status ?? "Esperando estado de la partida.");
    }

    // En producción se recibe IGameSession.View. Los diagnósticos usan fixtures aislados.
    public void DisplaySnapshot(SnapshotDto? view, string connectionStatus)
    {
        if (!_ready) throw new InvalidOperationException("Mesa debe estar lista antes de representar datos.");
        if (QueueDealSnapshot(view, connectionStatus)) return;
        SnapshotDto? previous = _interactionView;
        bool beginDeal = ShouldAnimateDeal(previous, view);
        SynchronizeInteraction(view);
        SynchronizeCountdowns(view);
        SynchronizeFinalHands(view);
        _connection.Text = connectionStatus;
        _challengedMeld = null;
        GetNode<Label>(ContentPath + "Table/Padding/Surface/MyGame/TitleLabel").Text = "Mi juego";
        Clear(_players); Clear(_selfPlayer); Clear(_hand);
        _dealPlayerCounts.Clear();
        Clear(_rivalGames); Clear(_ownGames); Clear(_revealed);
        _tableEditButtons.Clear();
        _offerZone.Visible = false; _revealedZone.Visible = false;
        _result.Text = ""; _result.Visible = false;
        _response.Text = ""; _response.Visible = false;
        if (view is null)
        {
            _phase.Text = "Sin partida sincronizada"; _turn.Text = "Sin turno";
            _deckCount.Text = "0 cartas"; _discardCount.Text = "0 cartas";
            _deck.ShowEmpty("SIN MAZO"); _discard.ShowEmpty(); _offered.ShowEmpty();
            _handTitle.Text = "Mis cartas · 0 cartas";
            _handEmpty.Visible = true; _handEmpty.Text = "Esperando reparto.";
            _meldEmpty.Visible = true;
            RenderDrafts(); RefreshSelection();
            QueueCasinoLayout();
            return;
        }
        _phase.Text = view.OrderChallenge?.Reason == PasaReason.UnusedOffer ? "Oferta obligatoria sin jugar" : PhaseName(view.Phase);
        _turn.Text = TurnText(view);
        _deckCount.Text = view.DeckCount + " cartas";
        _discardCount.Text = view.DiscardCount + " cartas";
        if (view.DeckCount > 0) _deck.ShowBack(); else _deck.ShowEmpty("AGOTADO");
        if (view.DiscardTop is int top) _discard.ShowFace(top); else _discard.ShowEmpty();
        if (view.OfferedCard is int offered)
        { _offered.ShowFace(offered); _offerZone.Visible = true; }
        else _offered.ShowEmpty();

        // Mismo orden de asientos para los encabezados y los juegos de rivales.
        foreach (PublicPlayerDto player in view.Players.OrderBy(p => p.SeatIndex))
        {
            AddPlayer(player.Id == view.RecipientId ? _selfPlayer : _players, player, view);
            if (player.Id != view.RecipientId) AddRivalGames(player, view);
        }
        RenderHand();
        _handTitle.Text = "Mis cartas · " + view.OwnHand.Length + " cartas";
        _handEmpty.Visible = view.OwnHand.Length == 0;
        _handEmpty.Text = "No tienes cartas en la mano.";
        if (view.OwnSpectating)
        { _handTitle.Text = "Observando la partida"; _handEmpty.Text = "Sigues alojando la sala como espectador."; }
        MeldDto[] own = view.Melds.Where(m => m.OwnerId == view.RecipientId).OrderBy(m => m.Id).ToArray();
        bool initialWinner = IsInitialWinner(view, view.RecipientId);
        _meldEmpty.Visible = own.Length == 0 && !initialWinner;
        if (initialWinner)
        {
            AddInitialVictory(_ownGames, view.InitialVictory!);
            GetNode<Label>(ContentPath + "Table/Padding/Surface/MyGame/TitleLabel").Text =
                "Mi juego · " + InitialVictoryName(view.InitialVictory!.Kind);
        }
        else foreach (MeldDto meld in own) AddMeld(_ownGames, meld);

        if (view.Phase is GamePhase.InitialExchange or GamePhase.InitialDeclarations or GamePhase.AwaitingClaims)
        {
            _response.Visible = true;
            _response.Text = view.OwnResponseSubmitted ? "No tienes respuestas pendientes." : "Tu respuesta está pendiente.";
            if (view.OwnSpectating) _response.Text = "Observas esta partida.";
        }
        if (view.Phase == GamePhase.Finished)
        {
            _result.Visible = true;
            _result.Text = view.Result == MatchResult.Draw ? "Partida finalizada · Empate"
                : view.WinnerPlayerId is int winner ? "Ganó " + PlayerName(view, winner) +
                  (view.Result == MatchResult.InitialVictory ? " · " +
                      (view.InitialVictory is { } proof ? InitialVictoryName(proof.Kind) : "Victoria inicial") : " · Desmoche")
                : "Partida finalizada";
        }
        RenderDrafts(); RefreshSelection();
        QueueCasinoLayout();
        if (beginDeal) BeginDealAnimation(view!, connectionStatus);
    }

    private void AddPlayer(HFlowContainer parent, PublicPlayerDto player, SnapshotDto view)
    {
        bool active = view.Phase == GamePhase.AwaitingDraw && view.CurrentPlayerId == player.Id ||
            view.Phase == GamePhase.AwaitingPayment && view.PaymentPlayerId == player.Id;
        bool self = player.Id == view.RecipientId;
        var panel = new PanelContainer { Name = "Jugador" + player.Id,
            CustomMinimumSize = self ? new Vector2(330, 76) : new Vector2(612, 96) };
        panel.AddThemeStyleboxOverride("panel", PanelStyle(active));
        var padding = Padding(self ? 8 : 6);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", self ? 12 : 20);
        panel.AddChild(padding); padding.AddChild(row);
        row.AddChild(CasinoPortrait(player, active, self));
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 2); row.AddChild(content);
        string name = player.Name + (player.Id == view.RecipientId ? " · Tú" : "") +
            (_session?.IsLocal != true && player.Id == 1 ? " · Host" : "");
        Label nameLabel = Text(name, self ? 18 : 28, new Color(0.97f, 0.95f, 0.89f));
        nameLabel.AddThemeFontOverride("font", _casinoSerif);
        nameLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        nameLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        content.AddChild(nameLabel);
        string state = !player.Connected ? "Desconectado" : player.Eliminated ? "Eliminado" : "Conectado";
        if (_session?.IsLocal == true && !player.Eliminated) state = self ? "Partida local" : "Listo";
        if (!player.Connected && player.Eliminated) state += " · Eliminado";
        if (active) state += view.Phase == GamePhase.AwaitingPayment ? " · Debe pagar" : " · Turno";
        Label stateLabel = Text(state, self ? 13 : 16, active ? new Color(1f, 0.82f, 0.40f) : new Color(0.66f, 0.77f, 0.78f));
        stateLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        stateLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        content.AddChild(stateLabel);
        Label countLabel = Text(player.CardCount + " cartas en mano", self ? 13 : 16);
        countLabel.AutowrapMode = TextServer.AutowrapMode.Off;
        content.AddChild(countLabel);
        _dealPlayerCounts.Add(player.Id, countLabel);
        parent.AddChild(panel); // Este panel nunca contiene identidades de cartas.
    }

    private void AddRivalGames(PublicPlayerDto player, SnapshotDto view)
    {
        var column = new VBoxContainer { Name = "JuegosJugador" + player.Id,
            CustomMinimumSize = new Vector2(RivalWidth, 136) };
        column.AddThemeConstantOverride("separation", 8);
        bool initialWinner = IsInitialWinner(view, player.Id);
        column.AddChild(CasinoHeading("Juego " + player.Name +
            (initialWinner ? " · " + InitialVictoryName(view.InitialVictory!.Kind) : "")));
        var rows = new Control { Name = "Rows", CustomMinimumSize = new Vector2(RivalWidth, 100),
            MouseFilter = MouseFilterEnum.Ignore };
        column.AddChild(rows);
        MeldDto[] melds = view.Melds.Where(m => m.OwnerId == player.Id).OrderBy(m => m.Id).ToArray();
        if (initialWinner) AddInitialVictory(rows, view.InitialVictory!);
        else foreach (MeldDto meld in melds) AddMeld(rows, meld);
        if (melds.Length == 0 && !initialWinner)
        {
            Label empty = Text("Sin juegos publicados", 12, new Color(0.52f, 0.70f, 0.62f));
            empty.HorizontalAlignment = HorizontalAlignment.Center; rows.AddChild(empty);
            empty.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        }
        _rivalGames.AddChild(column);
        AddFinalHandButton(player, view, rows);
    }

    private void AddMeld(Control parent, MeldDto meld)
    {
        var cards = new HFlowContainer { Name = "Juego" + meld.Id, Alignment = FlowContainer.AlignmentMode.Center };
        cards.AddThemeConstantOverride("h_separation", 5);
        cards.AddThemeConstantOverride("v_separation", 5);
        parent.AddChild(cards);
        for (int i = 0; i < meld.Cards.Length; i++) AddCard(cards, meld.Cards[i], "Carta" + i);
        if (_interactionView?.OrderChallenge is { } challenge && challenge.PlayerId == meld.OwnerId && _challengedMeld is null &&
            (challenge.Reason == PasaReason.UnusedOffer ? challenge.MeldId == meld.Id :
                !RulesValidator.HasAlternatingSetOrder(meld.Cards.Select(Card.FromId).ToArray())))
            _challengedMeld = cards;
        if (parent == _ownGames) { BindOwnMeldDrop(cards, meld.Id); RegisterTableMeld(meld.Id); }
        else cards.MouseFilter = MouseFilterEnum.Ignore;
    }

    private static bool IsInitialWinner(SnapshotDto view, int playerId) =>
        view.Phase == GamePhase.Finished && view.Result == MatchResult.InitialVictory &&
        view.WinnerPlayerId == playerId && view.InitialVictory is not null;

    private static string InitialVictoryName(InitialVictoryKind kind) => kind switch
    {
        InitialVictoryKind.Color => "Color", InitialVictoryKind.Peladilla => "Peladilla",
        InitialVictoryKind.LowStraight => "Desmoche A-2-3-4",
        InitialVictoryKind.HighStraight => "Desmoche J-Q-K-A",
        InitialVictoryKind.Cuarta => "Cuarta", _ => "Victoria inicial"
    };

    private void AddInitialVictory(Control parent, InitialVictoryDto proof)
    {
        var cards = new HFlowContainer { Name = "VictoriaInicial", MouseFilter = MouseFilterEnum.Ignore,
            Alignment = FlowContainer.AlignmentMode.Center, TooltipText = InitialVictoryName(proof.Kind) };
        cards.AddThemeConstantOverride("h_separation", 5);
        parent.AddChild(cards);
        for (int i = 0; i < proof.Cards.Length; i++) AddCard(cards, proof.Cards[i], "Carta" + i);
    }

    private void AddCardRow(VBoxContainer parent, string title, int[] cards)
    {
        var row = new VBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(Text(title, 16));
        var flow = new HFlowContainer { Alignment = FlowContainer.AlignmentMode.Center };
        flow.AddThemeConstantOverride("h_separation", 8);
        flow.AddThemeConstantOverride("v_separation", 8);
        row.AddChild(flow); parent.AddChild(row);
        for (int i = 0; i < cards.Length; i++) AddCard(flow, cards[i], "Carta" + i);
        if (cards.Length == 0) row.AddChild(Text("Sin cartas", 14));
    }

    private void AddCard(HFlowContainer parent, int id, string name)
    {
        Carta card = _cardScene.Instantiate<Carta>();
        card.Name = name; card.ShowFace(id); parent.AddChild(card);
    }

    private static void Clear(Node parent)
    {
        foreach (Node child in parent.GetChildren()) { parent.RemoveChild(child); child.QueueFree(); }
    }

    private static MarginContainer Padding(int pixels)
    {
        var padding = new MarginContainer();
        foreach (string side in new[] { "left", "top", "right", "bottom" })
            padding.AddThemeConstantOverride("margin_" + side, pixels);
        return padding;
    }

    private static Label Text(string text, int size, Color? color = null)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        if (color is Color value) label.AddThemeColorOverride("font_color", value);
        return label;
    }

    private static string Initials(string name)
    {
        string[] parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "?";
        string first = System.Globalization.StringInfo.GetNextTextElement(parts[0]);
        string last = parts.Length > 1 ? System.Globalization.StringInfo.GetNextTextElement(parts[^1]) : "";
        return (first + last).ToUpperInvariant();
    }

    private static StyleBoxFlat PanelStyle(bool active) => new()
    {
        BgColor = new Color(0.027f, 0.068f, 0.077f, 0.96f),
        BorderColor = active ? new Color(1f, 0.75f, 0.28f) : new Color(0.16f, 0.29f, 0.32f),
        BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
        CornerRadiusTopLeft = 13, CornerRadiusTopRight = 13,
        CornerRadiusBottomLeft = 13, CornerRadiusBottomRight = 13
    };

    private static string PlayerName(SnapshotDto view, int? id) =>
        view.Players.FirstOrDefault(p => p.Id == id)?.Name ?? "—";

    private static string TurnText(SnapshotDto view) => view.Phase switch
    {
        GamePhase.Preparing => "Preparando la mesa",
        GamePhase.InitialExchange => "Intercambio inicial · Participan todos",
        GamePhase.InitialDeclarations => "Declaraciones iniciales · Participan todos",
        GamePhase.AwaitingDraw => view.CurrentPlayerId == view.RecipientId ? "Tu turno · Esperando robo"
            : "Turno de " + PlayerName(view, view.CurrentPlayerId),
        GamePhase.AwaitingClaims => "Carta ofrecida · Esperando respuestas",
        GamePhase.AwaitingPayment => "Pago pendiente · " + PlayerName(view, view.PaymentPlayerId),
        GamePhase.AwaitingOrderChallenge => "Pasa · " + (view.Settings ?? MatchSettings.Default).PasaSeconds + " segundos · " + PlayerName(view, view.OrderChallenge?.PlayerId) +
            (view.OrderChallenge?.Reason == PasaReason.UnusedOffer ? " dejó pasar una carta que entra en su juego" : " no alternó los colores"),
        GamePhase.Finished => "No hay turno activo",
        _ => "Esperando estado"
    };

    private static string PhaseName(GamePhase phase) => phase switch
    {
        GamePhase.Preparing => "Preparación", GamePhase.InitialExchange => "Intercambio inicial",
        GamePhase.InitialDeclarations => "Declaraciones iniciales", GamePhase.AwaitingDraw => "Esperando robo",
        GamePhase.AwaitingClaims => "Esperando reclamos", GamePhase.AwaitingPayment => "Esperando pago",
        GamePhase.AwaitingOrderChallenge => "Comprobando orden de colores",
        GamePhase.Finished => "Partida finalizada", _ => "Estado desconocido"
    };
}
