using System;
using System.Linq;
using Desmoche.Application;
using Desmoche.Domain;
using Desmoche.Networking.Core;
using Desmoche.Presentation;
using Godot;

namespace Desmoche.Diagnostics;

// La escena normal no referencia este script. Los datos sintéticos son solo fixtures visuales.
public partial class MesaVisualSmoke : Main
{
    protected override bool ImmediateInitialization => true;
    private Mesa _mesa = null!;
    private SnapshotDto? _shown;
    private bool _auto, _done;
    private int _case, _checks;
    private ulong _nextAt;
    private string _before = "";
    private const string Root = Mesa.ContentPath;
    private static readonly string[] Names =
    {
        "Reparto de nueve cartas", "Juegos públicos y turno", "Carta ofrecida",
        "Pago pendiente", "Jugador desconectado/eliminado", "Victoria y revelación",
        "Empate sin revelación", "Limpieza de estado", "Tres jugadores y sus juegos",
        "Cuatro jugadores desde un cliente", "Escalera pública larga", "Sin estado sincronizado"
    };

    public override void _Ready()
    {
        base._Ready();
        _mesa = GetNode<Mesa>("Margin/Screens/Mesa");
        GetNode<Control>("Margin/Screens/Lobby").Visible = false;
        _mesa.Visible = true;
        _auto = OS.GetCmdlineUserArgs().Contains("--auto");
        try
        {
            if (_auto) CheckCardFaces();
            ShowCase(0);
        }
        catch (Exception ex) { Finish(false, ex.Message); }
    }

    public override void _Process(double delta)
    {
        if (_done || Time.GetTicksMsec() < _nextAt) return;
        try
        {
            if (_auto)
            {
                ValidateCase();
                if (_case == Names.Length - 1) { Finish(true, "Estados y privacidad representados correctamente."); return; }
                ShowCase(_case + 1);
            }
            else ShowCase((_case + 1) % Names.Length);
        }
        catch (Exception ex) { Finish(false, ex.Message); }
    }

    private void ShowCase(int index)
    {
        _case = index;
        _shown = Fixture(index);
        _before = _shown is null ? "" : NetworkProtocol.Encode(_shown);
        _mesa.DisplaySnapshot(_shown, "Prueba visual · " + Names[index] + " · " + (index + 1) + "/" + Names.Length);
        _nextAt = Time.GetTicksMsec() + (_auto ? 120UL : 6000UL);
    }

    private void ValidateCase()
    {
        SnapshotDto? view = _shown;
        var hand = _mesa.GetNode<HFlowContainer>(Root + "HandZone/Padding/Content/Cards");
        var deck = _mesa.GetNode<Carta>(Mesa.CenterPath + "Mazo/Content/Carta");
        var discard = _mesa.GetNode<Carta>(Mesa.CenterPath + "Pozo/Content/Carta");
        var offer = _mesa.GetNode<Carta>(Mesa.CenterPath + "Oferta/Content/Carta");
        var revealed = _mesa.GetNode<Control>(Root + "RevealedZone");
        if (view is null)
        {
            Check(hand.GetChildCount() == 0 && _mesa.RenderedPlayerCount == 0, "Sin vista no quedan jugadores ni mano.");
            Check(!deck.IsBack && !deck.IsFaceUp && discard.FaceCardId is null && offer.FaceCardId is null,
                "Sin vista se limpian mazo, pozo y oferta.");
            Check(VisibleFaceIds(_mesa).Length == 0 && !revealed.Visible, "Sin vista no quedan identidades visibles.");
            return;
        }
        Check(NetworkProtocol.Encode(view) == _before, "La UI no modifica el snapshot.");
        Check(_mesa.RenderedPlayerCount == view.Players.Length, "Panel por jugador público.");
        int[] visualHand = hand.GetChildren().Cast<Carta>().Select(c => c.FaceCardId!.Value).ToArray();
        Check(visualHand.SequenceEqual(_mesa.LocalHandOrder) &&
            visualHand.OrderBy(id => id).SequenceEqual(view.OwnHand.OrderBy(id => id)),
            "Mano propia conserva identidades autorizadas y el orden visual local.");
        Check(deck.FaceCardId is null && deck.IsBack == (view.DeckCount > 0), "Mazo sin identidades.");
        Check(discard.FaceCardId == view.DiscardTop, "Solo se muestra la carta superior del pozo.");
        Check(offer.FaceCardId == view.OfferedCard, "Oferta separada del pozo.");
        var players = _mesa.GetNode<HFlowContainer>(Root + "PlayerZone/Padding/Players");
        var self = _mesa.GetNode<HFlowContainer>(Mesa.SelfPlayerPath);
        Check(players.GetChildCount() == view.Players.Length - 1 && self.GetChildCount() == 1,
            "Rivales arriba y un jugador propio en la bandeja.");
        Check(VisibleFaceIds(players).Length == 0 && VisibleFaceIds(self).Length == 0,
            "Los paneles de identidad no contienen caras de cartas.");
        var rivalGames = _mesa.GetNode<HFlowContainer>(Mesa.RivalGamesPath);
        var rivals = view.Players.Where(p => p.Id != view.RecipientId).OrderBy(p => p.SeatIndex).ToArray();
        Check(rivalGames.GetChildCount() == rivals.Length, "Zona pública por cada rival.");
        for (int i = 0; i < rivals.Length; i++)
        {
            Check(players.GetChild(i).Name == "Jugador" + rivals[i].Id &&
                rivalGames.GetChild(i).Name == "JuegosJugador" + rivals[i].Id,
                "Encabezados y juegos respetan el mismo orden de asientos.");
            CheckMelds(rivalGames.GetChild(i).GetNode<Control>("Rows"),
                view.Melds.Where(m => m.OwnerId == rivals[i].Id).OrderBy(m => m.Id).ToArray());
        }
        CheckMelds(_mesa.GetNode<Control>(Mesa.OwnGamesPath),
            view.Melds.Where(m => m.OwnerId == view.RecipientId).OrderBy(m => m.Id).ToArray());
        bool winning = view.Phase == GamePhase.Finished && view.WinnerPlayerId is not null &&
            view.Result is MatchResult.Desmoche or MatchResult.InitialVictory;
        int[] allowed = view.OwnHand.Concat(view.Melds.SelectMany(m => m.Cards))
            .Concat(view.DiscardTop is int top ? new[] { top } : Array.Empty<int>())
            .Concat(view.OfferedCard is int card ? new[] { card } : Array.Empty<int>())
            .Concat(winning && view.InitialVictory is { } proof ? proof.Cards
                : Array.Empty<int>()).OrderBy(id => id).ToArray();
        Check(VisibleFaceIds(_mesa).OrderBy(id => id).SequenceEqual(allowed),
            "No hay identidades adicionales a las autorizadas.");
        Check(!revealed.Visible, "Las manos finales se consultan por rival, sin revelación al pie.");
        foreach (var rival in rivals)
            Check(_mesa.HasNode(Mesa.FinalHandButtonPath(rival.Id)) ==
                (view.Phase == GamePhase.Finished && view.RevealedHands.Any(h => h.PlayerId == rival.Id)),
                "Mostrar juego solo aparece con revelación autorizada.");
        Check(_mesa.GetNode<Label>(Mesa.CenterPath + "Mazo/Content/CountLabel").Text == view.DeckCount + " cartas",
            "Contador refleja dato del servidor.");
        int? active = view.Phase == GamePhase.AwaitingDraw ? view.CurrentPlayerId :
            view.Phase == GamePhase.AwaitingPayment ? view.PaymentPlayerId : null;
        foreach (PublicPlayerDto player in view.Players)
        {
            var panel = (player.Id == view.RecipientId ? self : players).GetNode<PanelContainer>("Jugador" + player.Id);
            var style = (StyleBoxFlat)panel.GetThemeStylebox("panel");
            bool highlighted = style.BorderColor == new Color(1f, 0.75f, 0.28f);
            Check(highlighted == (active == player.Id), "Indicador de turno/pago correcto para " + player.Name + ".");
        }
        GD.Print("MESA CASO OK: " + Names[_case]);
    }

    private void CheckMelds(Control rows, MeldDto[] expected)
    {
        HFlowContainer[] groups = rows.GetChildren().OfType<HFlowContainer>().ToArray();
        Check(groups.Length == expected.Length, "Combinaciones separadas según el propietario.");
        for (int i = 0; i < expected.Length; i++)
        {
            Check(groups[i].Name == "Juego" + expected[i].Id, "Identidad visual de cada juego.");
            Check(groups[i].GetChildren().Cast<Carta>().Select(c => c.FaceCardId!.Value)
                .SequenceEqual(expected[i].Cards), "Orden público de cartas conservado.");
        }
    }

    private void CheckCardFaces()
    {
        var card = GD.Load<PackedScene>("res://Scenes/Carta.tscn").Instantiate<Carta>();
        AddChild(card);
        for (int id = 0; id < 52; id++)
        {
            card.ShowFace(id);
            Check(card.IsFaceUp && card.FaceCardId == Card.FromId(id).Id, "Identidad visual de carta " + id + ".");
            bool red = Card.FromId(id).Suit is Suit.Diamonds or Suit.Hearts;
            Color expected = red ? new Color(0.76f, 0.12f, 0.19f) : new Color(0.08f, 0.12f, 0.17f);
            Check(card.GetNode<Label>("Padding/Content/Corner/Rank").GetThemeColor("font_color") == expected,
                "Color visual de carta " + id + ".");
        }
        card.ShowBack();
        Check(card.IsBack && card.FaceCardId is null &&
            card.GetNode<Label>("Padding/Content/Corner/Rank").Text == "", "Dorso elimina identidad y rango.");
        card.ShowEmpty();
        Check(!card.IsFaceUp && !card.IsBack && card.FaceCardId is null, "Carta vacía elimina identidad.");
        RemoveChild(card); card.QueueFree();
    }

    private static int[] VisibleFaceIds(Node root)
    {
        var result = new System.Collections.Generic.List<int>();
        Collect(root, result);
        return result.ToArray();
    }
    private static void Collect(Node node, System.Collections.Generic.List<int> result)
    {
        if (node is Carta card && card.IsVisibleInTree() && card.FaceCardId is int id) result.Add(id);
        foreach (Node child in node.GetChildren()) Collect(child, result);
    }

    private static SnapshotDto? Fixture(int index)
    {
        if (index is 7 or 11) return null;
        var baseView = new SnapshotDto(NetworkProtocol.Version, new string('a', 32), index + 1, 1, 1,
            GamePhase.InitialExchange, MatchResult.None, 1, null, null,
            new[] { new PublicPlayerDto(1, "Ana", 0, 9, true, false),
                new PublicPlayerDto(2, "Luis", 1, 9, true, false) },
            Enumerable.Range(0, 9).ToArray(), Array.Empty<MeldDto>(), 34, 0, null, null,
            Array.Empty<RevealedHandDto>(), false);
        if (index == 0) return baseView;
        if (index == 8)
            return baseView with
            {
                Phase = GamePhase.AwaitingDraw, CurrentPlayerId = 3, DeckCount = 25,
                OwnHand = Enumerable.Range(3, 6).ToArray(),
                Players = new[] { baseView.Players[0] with { CardCount = 6 },
                    baseView.Players[1] with { CardCount = 6 },
                    new PublicPlayerDto(3, "María", 2, 6, true, false) },
                Melds = new[] { new MeldDto(1, 1, new[] { 0, 1, 2 }),
                    new MeldDto(2, 2, new[] { 13, 14, 15 }),
                    new MeldDto(3, 3, new[] { 26, 27, 28 }) }
            };
        if (index == 9)
            return baseView with
            {
                RecipientId = 3, Phase = GamePhase.AwaitingPayment, CurrentPlayerId = 1, PaymentPlayerId = 3,
                DeckCount = 16, OwnHand = Enumerable.Range(29, 6).ToArray(),
                Players = new[] { baseView.Players[0] with { CardCount = 6 },
                    baseView.Players[1] with { CardCount = 6 },
                    new PublicPlayerDto(3, "María", 2, 6, true, false),
                    new PublicPlayerDto(4, "Pedro", 3, 9, true, false) },
                Melds = new[] { new MeldDto(1, 1, new[] { 0, 1, 2 }),
                    new MeldDto(2, 2, new[] { 13, 14, 15 }),
                    new MeldDto(3, 3, new[] { 26, 27, 28 }) }
            };
        // Fixture de tamaño máximo para envolver cartas; no ejecuta acciones del motor.
        if (index == 10)
            return baseView with
            {
                Phase = GamePhase.AwaitingDraw, CurrentPlayerId = 1, DeckCount = 21,
                OwnHand = Enumerable.Range(26, 9).ToArray(),
                Melds = new[] { new MeldDto(1, 2, Enumerable.Range(0, 13).ToArray()) }
            };
        SnapshotDto played = baseView with
        {
            Phase = GamePhase.AwaitingDraw, CurrentPlayerId = 2,
            OwnHand = Enumerable.Range(3, 6).ToArray(),
            Players = new[] { baseView.Players[0] with { CardCount = 6 }, baseView.Players[1] },
            Melds = new[] { new MeldDto(1, 1, new[] { 0, 1, 2 }) }
        };
        if (index == 1) return played;
        if (index == 2) return played with
        { Phase = GamePhase.AwaitingClaims, DeckCount = 33, OfferedCard = 9, OwnResponseSubmitted = true };
        if (index == 3) return played with
        { Phase = GamePhase.AwaitingPayment, CurrentPlayerId = 1, PaymentPlayerId = 2,
            DeckCount = 33, DiscardCount = 1, DiscardTop = 51 };
        if (index == 4) return played with
        { CurrentPlayerId = 1, Players = new[] { played.Players[0], played.Players[1] with { Connected = false, Eliminated = true } } };
        if (index == 5) return played with
        {
            Phase = GamePhase.Finished, CurrentPlayerId = null, Result = MatchResult.Desmoche, WinnerPlayerId = 1,
            OwnHand = Array.Empty<int>(), Players = new[] { played.Players[0] with { CardCount = 0 }, played.Players[1] },
            Melds = new[] { new MeldDto(1, 1, Enumerable.Range(0, 10).ToArray()) }, DeckCount = 33,
            RevealedHands = new[] { new RevealedHandDto(1, Array.Empty<int>()),
                new RevealedHandDto(2, Enumerable.Range(13, 9).ToArray()) }
        };
        // Empate final también autoriza la consulta individual de las manos.
        return played with
        { Phase = GamePhase.Finished, CurrentPlayerId = null, Result = MatchResult.Draw, DeckCount = 0,
            DiscardCount = 34, DiscardTop = 51,
            RevealedHands = new[] { new RevealedHandDto(1, played.OwnHand.ToArray()),
                new RevealedHandDto(2, Enumerable.Range(13, 9).ToArray()) }, FinalDeck = Array.Empty<int>() };
    }

    private void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }

    private void Finish(bool success, string message)
    {
        if (_done) return;
        _done = true;
        GD.Print((success ? "MESA VISUAL OK: " : "MESA VISUAL ERROR: ") + message + " Comprobaciones=" + _checks);
        if (_auto) GetTree().Quit(success ? 0 : 1);
        else GD.PushError(message);
    }
}
