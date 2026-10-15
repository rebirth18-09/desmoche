using System;
using System.Collections.Generic;
using System.Linq;
using Desmoche.Application;
using Desmoche.Networking.Core;
using Godot;

namespace Desmoche.Presentation;

public partial class Mesa
{
    public const string FinalHandPanelPath = ContentPath + "FinalHandPanel";
    public const string FinalHandClosePath = ContentPath + "CloseFinalHandButton";
    public const string FinalDeckButtonPath = ContentPath + "ShowDeckButton";
    public static string FinalHandButtonPath(int playerId) => ContentPath + "ShowHandButton" + playerId;

    private readonly Dictionary<int, (Button Button, Control Rows)> _finalHandButtons = new();
    private PanelContainer _finalHandPanel = null!;
    private Label _finalHandTitle = null!, _finalHandEmpty = null!;
    private HFlowContainer _finalHandCards = null!;
    private Button _finalHandClose = null!;
    private Button _finalDeckButton = null!;
    private ScrollContainer _finalDeckScroll = null!;
    private GridContainer _finalDeckCards = null!;
    private Label _finalDeckOrder = null!;
    private bool _shownFinalDeck;
    private int? _shownFinalPlayer;
    private int[] _shownFinalCards = Array.Empty<int>();
    private string _shownFinalSession = "";
    private long _shownFinalWindow;

    private void InitializeFinalHands()
    {
        _finalHandPanel = new PanelContainer { Name = "FinalHandPanel", Visible = false,
            MouseFilter = MouseFilterEnum.Stop, ZIndex = 10 };
        _finalHandPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.025f, 0.10f, 0.085f, 0.84f),
            BorderColor = new Color(0.78f, 0.64f, 0.35f, 0.9f),
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12,
            CornerRadiusBottomLeft = 12, CornerRadiusBottomRight = 12,
            ContentMarginLeft = 0, ContentMarginRight = 0, ContentMarginTop = 0, ContentMarginBottom = 0,
            ShadowColor = new Color(0, 0, 0, 0.3f), ShadowSize = 6
        });
        _casinoCanvas.AddChild(_finalHandPanel);
        var padding = Padding(12); padding.Name = "Padding";
        _finalHandPanel.AddChild(padding);
        var content = new VBoxContainer { Name = "Content", MouseFilter = MouseFilterEnum.Ignore };
        content.AddThemeConstantOverride("separation", 8); padding.AddChild(content);
        _finalHandTitle = Text("", 26); _finalHandTitle.Name = "TitleLabel";
        _finalHandTitle.AddThemeFontOverride("font", _casinoSerif);
        _finalHandTitle.AutowrapMode = TextServer.AutowrapMode.Off;
        _finalHandTitle.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _finalHandTitle.CustomMinimumSize = new Vector2(0, 32);
        content.AddChild(_finalHandTitle);
        _finalHandEmpty = Text("Sin cartas restantes en la mano", 22); _finalHandEmpty.Name = "EmptyLabel";
        content.AddChild(_finalHandEmpty);
        _finalHandCards = new HFlowContainer { Name = "Cards", MouseFilter = MouseFilterEnum.Ignore,
            Alignment = FlowContainer.AlignmentMode.Center };
        _finalHandCards.AddThemeConstantOverride("h_separation", 6);
        _finalHandCards.AddThemeConstantOverride("v_separation", 6);
        content.AddChild(_finalHandCards);
        _finalDeckOrder = Text("1 = próxima carta en salir", 18); _finalDeckOrder.Name = "DeckOrderLabel";
        _finalDeckOrder.Visible = false; content.AddChild(_finalDeckOrder);
        _finalDeckScroll = new ScrollContainer { Name = "DeckScroll", Visible = false,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto, SizeFlagsVertical = SizeFlags.ExpandFill };
        content.AddChild(_finalDeckScroll);
        _finalDeckCards = new GridContainer { Name = "Cards", Columns = 4,
            MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _finalDeckCards.AddThemeConstantOverride("h_separation", 6);
        _finalDeckCards.AddThemeConstantOverride("v_separation", 6);
        _finalDeckScroll.AddChild(_finalDeckCards);
        _finalHandClose = new Button { Name = "CloseFinalHandButton", Text = "Cerrar", Visible = false,
            CustomMinimumSize = new Vector2(100, 44), ZIndex = 11 };
        _finalHandClose.AddThemeFontSizeOverride("font_size", 16);
        _finalHandClose.Pressed += CloseFinalHand;
        _casinoCanvas.AddChild(_finalHandClose);
        _finalDeckButton = new Button { Name = "ShowDeckButton", Text = "Mostrar mazo", Visible = false,
            CustomMinimumSize = new Vector2(0, 44), ZIndex = 5,
            TooltipText = "Ver las cartas restantes en el orden en que habrían salido" };
        _finalDeckButton.AddThemeFontSizeOverride("font_size", 16);
        _finalDeckButton.Pressed += ShowFinalDeck;
        _casinoCanvas.AddChild(_finalDeckButton);
    }

    private static bool FinalHandsAllowed(SnapshotDto? view) => view is
        { Phase: GamePhase.Finished, Result: MatchResult.Desmoche or MatchResult.InitialVictory or MatchResult.Draw };

    private static bool FinalDeckAllowed(SnapshotDto? view) => view is
        { Phase: GamePhase.Finished, Result: not MatchResult.None, FinalDeck: not null } && view.FinalDeck.Length == view.DeckCount;

    private void AddFinalHandButton(PublicPlayerDto player, SnapshotDto view, Control rows)
    {
        if (!FinalHandsAllowed(view) || !view.RevealedHands.Any(hand => hand.PlayerId == player.Id)) return;
        var button = new Button { Name = "ShowHandButton" + player.Id, Text = "Mostrar juego",
            CustomMinimumSize = new Vector2(0, 44), ZIndex = 5,
            TooltipText = "Ver las cartas restantes de " + player.Name };
        button.AddThemeFontSizeOverride("font_size", 16);
        button.Pressed += () => ShowFinalHand(player.Id);
        _casinoCanvas.AddChild(button);
        _finalHandButtons.Add(player.Id, (button, rows));
    }

    private void SynchronizeFinalHands(SnapshotDto? view)
    {
        foreach (var pair in _finalHandButtons.Values)
        { _casinoCanvas.RemoveChild(pair.Button); pair.Button.QueueFree(); }
        _finalHandButtons.Clear();
        _finalDeckButton.Visible = FinalDeckAllowed(view);
        if (_shownFinalDeck)
        {
            if (!FinalDeckAllowed(view) || view!.Session != _shownFinalSession || view.Window != _shownFinalWindow ||
                !view.FinalDeck!.SequenceEqual(_shownFinalCards)) CloseFinalHand();
            return;
        }
        if (_shownFinalPlayer is not int player) return;
        RevealedHandDto? hand = FinalHandsAllowed(view)
            ? view!.RevealedHands.FirstOrDefault(h => h.PlayerId == player) : null;
        if (hand is null || view!.Session != _shownFinalSession || view.Window != _shownFinalWindow ||
            !hand.Cards.SequenceEqual(_shownFinalCards)) CloseFinalHand();
    }

    private void ShowFinalHand(int playerId)
    {
        SnapshotDto? view = _interactionView;
        if (!FinalHandsAllowed(view) || playerId == view!.RecipientId) return;
        RevealedHandDto? hand = view.RevealedHands.FirstOrDefault(h => h.PlayerId == playerId);
        if (hand is null) return;
        Clear(_finalDeckCards); _shownFinalDeck = false;
        _finalDeckOrder.Hide(); _finalDeckScroll.Hide();
        Clear(_finalHandCards);
        _shownFinalPlayer = playerId; _shownFinalSession = view.Session; _shownFinalWindow = view.Window;
        _shownFinalCards = hand.Cards.ToArray();
        _finalHandTitle.Text = PlayerName(view, playerId) + " · " + hand.Cards.Length + " cartas";
        _finalHandTitle.TooltipText = _finalHandTitle.Text;
        _finalHandEmpty.Visible = hand.Cards.Length == 0;
        _finalHandEmpty.Text = "Sin cartas restantes en la mano";
        _finalHandCards.Visible = hand.Cards.Length != 0;
        for (int index = 0; index < hand.Cards.Length; index++)
        {
            Carta card = _cardScene.Instantiate<Carta>(); card.Name = "Carta" + index;
            card.ShowFace(hand.Cards[index]); _finalHandCards.AddChild(card);
            card.SetInteraction(false, false);
        }
        _finalHandPanel.Show(); _finalHandClose.Show();
        QueueCasinoLayout(); _finalHandClose.GrabFocus();
    }

    private void ShowFinalDeck()
    {
        SnapshotDto? view = _interactionView;
        if (!FinalDeckAllowed(view)) return;
        Clear(_finalHandCards); Clear(_finalDeckCards);
        _shownFinalPlayer = null; _shownFinalDeck = true;
        _shownFinalSession = view!.Session; _shownFinalWindow = view.Window;
        _shownFinalCards = view.FinalDeck!.ToArray();
        _finalHandTitle.Text = "Mazo · " + _shownFinalCards.Length + " cartas";
        _finalHandTitle.TooltipText = _finalHandTitle.Text;
        _finalHandCards.Hide(); _finalHandEmpty.Text = "No quedaban cartas en el mazo";
        _finalHandEmpty.Visible = _shownFinalCards.Length == 0;
        _finalDeckOrder.Visible = _shownFinalCards.Length != 0;
        _finalDeckScroll.Visible = _shownFinalCards.Length != 0;
        _finalDeckScroll.ScrollVertical = 0;
        for (int index = 0; index < _shownFinalCards.Length; index++)
        {
            var cell = new VBoxContainer { Name = "Posicion" + index, MouseFilter = MouseFilterEnum.Ignore };
            cell.AddThemeConstantOverride("separation", 2);
            Label number = Text((index + 1).ToString(), 18); number.Name = "OrderLabel";
            number.HorizontalAlignment = HorizontalAlignment.Center;
            number.CustomMinimumSize = new Vector2(0, 24); cell.AddChild(number);
            Carta card = _cardScene.Instantiate<Carta>(); card.Name = "Carta";
            card.ShowFace(_shownFinalCards[index]); cell.AddChild(card); card.SetInteraction(false, false);
            card.MouseFilter = MouseFilterEnum.Ignore; _finalDeckCards.AddChild(cell);
        }
        _finalHandPanel.Show(); _finalHandClose.Show();
        QueueCasinoLayout(); _finalHandClose.GrabFocus();
    }

    private void CloseFinalHand()
    {
        _shownFinalPlayer = null; _shownFinalDeck = false; _shownFinalSession = ""; _shownFinalCards = Array.Empty<int>();
        _finalHandPanel.Hide(); _finalHandClose.Hide(); Clear(_finalHandCards);
        Clear(_finalDeckCards); _finalDeckOrder.Hide(); _finalDeckScroll.Hide();
    }

    private void LayoutFinalHands()
    {
        float lastButtonBottom = 0;
        foreach (var entry in _finalHandButtons.Values)
        {
            Rect2 rows = entry.Rows.GetGlobalRect();
            Vector2 global = new(rows.Position.X + rows.Size.X / 2, rows.Position.Y + 72 * CasinoScale + 8);
            float width = Math.Max(entry.Button.GetCombinedMinimumSize().X, Math.Min(190, rows.Size.X - 8));
            entry.Button.Scale = Vector2.One / CasinoScale;
            entry.Button.Size = new Vector2(width, 44);
            global.X -= width / 2;
            entry.Button.Position = _casinoCanvas.GetGlobalTransform().AffineInverse() * global;
            lastButtonBottom = Math.Max(lastButtonBottom, entry.Button.Position.Y + 44 / CasinoScale);
        }
        // Al finalizar, separar también el centro de los nuevos botones.
        if (_finalHandButtons.Count > 0 || _finalDeckButton.Visible)
        {
            Control center = GetNode<Control>(CenterPath.TrimEnd('/'));
            float y = Math.Max(170, lastButtonBottom + 6 / CasinoScale - 112);
            // El margen izquierdo deja libres también los rótulos del juego
            // propio; el margen derecho se reserva para la mano consultada.
            if (!_offerZone.Visible && !_paymentZone.Visible)
            { center.Position = new Vector2(32, y); center.Size = new Vector2(380, 184); }
            else center.Position = new Vector2(594, y);
        }
        if (_finalDeckButton.Visible)
        {
            Rect2 countRect = _deckCount.GetGlobalRect();
            float width = Math.Max(150, _finalDeckButton.GetCombinedMinimumSize().X);
            _finalDeckButton.Scale = Vector2.One / CasinoScale;
            _finalDeckButton.Size = new Vector2(width, 44);
            Vector2 at = new(countRect.Position.X + countRect.Size.X / 2 - width / 2, countRect.End.Y + 6);
            _finalDeckButton.Position = _casinoCanvas.GetGlobalTransform().AffineInverse() * at;
        }
        if (!_finalHandPanel.Visible) return;

        // Franja libre a la derecha: los juegos propios terminan en x=1568,
        // los rivales en y<=244 y la mano empieza en y=640. No se tapa ninguno.
        float top = Math.Max(280, lastButtonBottom + 12 / CasinoScale);
        float availableHeight = Math.Max(60, 616 - top);
        const float panelWidth = 420, innerWidth = panelWidth - 24;
        int count = _shownFinalCards.Length;
        int columns = _shownFinalDeck ? 4 : Math.Max(1, Math.Min(5, count)), rowsCount = (count + columns - 1) / columns;
        float cardWidth = Math.Min(90, (innerWidth - (columns - 1) * 6 - 0.5f) / columns);
        float cardHeight = Math.Max(46, cardWidth * 1.35f);
        foreach (Carta card in _finalHandCards.GetChildren().OfType<Carta>())
            card.SetVisualSize(new Vector2(cardWidth, cardHeight));
        float cardsHeight = count == 0 ? 36 : rowsCount * cardHeight + Math.Max(0, rowsCount - 1) * 6;
        _finalHandCards.CustomMinimumSize = new Vector2(innerWidth, cardsHeight);
        foreach (Carta card in _finalDeckCards.GetChildren().SelectMany(cell => cell.GetChildren().OfType<Carta>()))
            card.SetVisualSize(new Vector2(90, 121.5f));
        _finalDeckScroll.CustomMinimumSize = new Vector2(innerWidth, Math.Max(60, availableHeight - 96));
        float naturalHeight = _shownFinalDeck && count > 0 ? availableHeight : Math.Max(96, 24 + 32 + 8 + cardsHeight);
        _finalHandPanel.Size = new Vector2(panelWidth, naturalHeight);
        float fit = Math.Min(1, availableHeight / Math.Max(naturalHeight, _finalHandPanel.GetCombinedMinimumSize().Y));
        _finalHandPanel.Scale = Vector2.One * fit;
        _finalHandPanel.Position = new Vector2(1580 + (panelWidth - panelWidth * fit) / 2, top);
        _finalHandClose.Scale = Vector2.One / CasinoScale;
        _finalHandClose.Size = new Vector2(100, 44);
        _finalHandClose.Position = new Vector2(2000 - 100 / CasinoScale,
            top + _finalHandPanel.Size.Y * fit + 6 / CasinoScale);
    }

    private bool CloseFinalHandOnInput(InputEvent input)
    {
        if (!_finalHandPanel.Visible) return false;
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        { CloseFinalHand(); return true; }
        if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } mouse &&
            !ContainsPointer(_finalHandPanel, mouse.Position) && !ContainsPointer(_finalHandClose, mouse.Position))
            CloseFinalHand();
        return false;
    }
}
