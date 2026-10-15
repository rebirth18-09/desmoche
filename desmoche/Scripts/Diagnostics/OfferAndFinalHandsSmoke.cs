using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Desmoche.Application;
using Desmoche.Domain;
using Desmoche.Networking.Core;
using Desmoche.Presentation;
using Desmoche.Sessions;
using Godot;

namespace Desmoche.Diagnostics;

public partial class OfferAndFinalHandsSmoke : Main
{
    protected override bool ImmediateInitialization => true;
    private Mesa _mesa = null!;
    private SubViewport _input = null!;
    private int _checks;
    private string _capture = "";
    private const string HandPath = Mesa.ContentPath + "HandZone/Padding/Content/Cards";

    // Solo puente de presentación para probar el botón y Reply diferido.
    // La aceptación y las reglas de ofertas se verifican en las consolas reales.
    private sealed class OfferSession : IGameSession
    {
        public bool IsActive => true;
        public bool IsLocal => false;
        public bool CanManageMatch => false;
        public SnapshotDto? View { get; private set; }
        public string Status => "Oferta de prueba";
        public event Action<SnapshotDto>? ViewChanged;
        public event Action<ReplyDto>? ActionCompleted;
        public event Action? SessionChanged;
        public OfferSession(SnapshotDto view) { View = view; }
        public long Submit(ActionKind kind, int? cardId = null, PlayDto[]? plays = null,
            bool declareWin = false, InitialVictoryKind? initialKind = null, int[]? cards = null, int? sourceMeldId = null)
        {
            if (kind != ActionKind.ChooseOffer) throw new InvalidOperationException("Acción inesperada en fixture de oferta.");
            Callable.From(() =>
            {
                View = View! with { Revision = 2, OfferTiming = new OfferTimingDto(1, 4000,
                    new[] { new CardReservationDto(1, 20000) }, false) };
                ViewChanged?.Invoke(View);
                ActionCompleted?.Invoke(new ReplyDto(101, true, "Elegida", false, 2));
            }).CallDeferred();
            return 101;
        }
        public void Close(string reason = "Partida cerrada.") { SessionChanged?.Invoke(); }
    }

    public override void _Ready()
    {
        base._Ready();
        _capture = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--capture="))?[10..] ?? "";
        GetNode<Control>("Margin").Hide();
        _input = new SubViewport { Name = "OfferInput", Size = new Vector2I(1280, 720),
            HandleInputLocally = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(_input);
        Main tested = GD.Load<PackedScene>("res://Scenes/Diagnostics/DiagnosticMain.tscn").Instantiate<Main>();
        _input.AddChild(tested);
        tested.GetNode<Control>("Margin/Screens/Lobby").Hide();
        tested.GetNode<Control>("SalonBackground").Hide(); tested.GetNode<Control>("MenuFrame").Hide();
        _mesa = tested.GetNode<Mesa>("Margin/Screens/Mesa"); _mesa.Show();
        var render = new TextureRect { Texture = _input.GetTexture(), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(render); render.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Callable.From(Run).CallDeferred();
    }

    private async void Run()
    {
        try
        {
            if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("Requiere render real para GUI nativa.");
            SnapshotDto offer = WinFixture(2) with { Phase = GamePhase.AwaitingClaims, Result = MatchResult.None,
                WinnerPlayerId = null, OwnHand = new[] { 2, 0, 4, 13, 26, 39, 10, 11, 12 }, Melds = Array.Empty<MeldDto>(),
                RevealedHands = Array.Empty<RevealedHandDto>(), FinalDeck = null, OfferedCard = 1,
                OfferTiming = new OfferTimingDto(1, 5000, Array.Empty<CardReservationDto>(), false),
                Players = new[] { new PublicPlayerDto(1, "Tú", 0, 9, true, false), new PublicPlayerDto(2, "Rival", 1, 9, true, false) } };
            var session = new OfferSession(offer); _mesa.Bind(session); await Settle();
            await Click(Card(2)); await Click(Card(0));
            Check(!Card(2).HasGameIndicator && !Card(0).HasGameIndicator, "Oferta sin elegir no completa la pareja");
            await Click(_mesa.GetNode<Button>(Mesa.ContextActionsPath + "ChooseOfferButton"));
            Carta offered = _mesa.GetNode<Carta>(Mesa.CenterPath + "Oferta/Content/Carta");
            Check(session.View!.OfferTiming!.Reservations.Length == 1, "Quiero esta carta atraviesa Submit y Reply");
            Check(Card(2).HasGameIndicator && Card(0).HasGameIndicator && offered.HasGameIndicator,
                "Dos propias y oferta elegida se marcan verde");
            Check(offered.OffsetTransformPositionRatio == Vector2.Zero, "La oferta conserva su posición");
            await Capture("Oferta-y-dos-cartas-verdes.png");
            await Click(Card(4));
            Check(!Card(2).HasGameIndicator && !offered.HasGameIndicator, "Carta incompatible quita el verde combinado");
            await Click(Card(4));
            Check(offered.HasGameIndicator, "Quitar sobrante restaura el verde de la oferta");
            string before = NetworkProtocol.Encode(session.View!);
            await Click(offered);
            Check(!offered.HasGameIndicator && !Card(2).HasGameIndicator, "Desmarcar la oferta recalcula el indicador");
            Check(NetworkProtocol.Encode(session.View!) == before, "El indicador no cambia la reserva autoritativa");
            _mesa.Unbind();

            foreach (Vector2I resolution in new[] { new Vector2I(640, 360), new(844, 390), new(1280, 720),
                new(1920, 1080), new(2400, 1080), new(1024, 768), new(3440, 1440) })
            {
                GetWindow().Size = resolution; await Settle();
                _input.Size = new Vector2I((int)GetViewportRect().Size.X, (int)GetViewportRect().Size.Y);
                for (int count = 2; count <= 4; count++)
                foreach (bool draw in new[] { false, true })
                {
                    SnapshotDto view = draw ? DrawFixture(count) : WinFixture(count);
                    Check(StateProjection.IsValidPublic(StateProjection.Public(view)), "Final de prueba coherente con la réplica");
                    string original = NetworkProtocol.Encode(view);
                    _mesa.DisplaySnapshot(view, "Partida finalizada"); await Settle();
                    Check(!_mesa.GetNode<Control>(Mesa.ContentPath + "RevealedZone").Visible,
                        "No hay manos finales al pie");
                    Check(!_mesa.GetNode<Control>(Mesa.FinalHandPanelPath).Visible, "Ventana cerrada hasta pulsar");
                    Check(Cards(_mesa.GetNode(Mesa.FinalHandPanelPath)).Length == 0, "No se instancian cartas antes de consultar");
                    Check(!_mesa.HasNode(Mesa.FinalHandButtonPath(1)), "Sin botón de rival para el jugador propio");
                    foreach (string countPath in new[] { Mesa.CenterPath + "Mazo/Content/CountLabel", Mesa.CenterPath + "Pozo/Content/CountLabel" })
                        Check(PublicCards().All(card => !Overlap(_mesa.GetNode<Control>(countPath).GetGlobalRect(), card.GetGlobalRect())),
                            "Contadores finales no tapan cartas publicadas");
                    foreach (int rival in Enumerable.Range(2, count - 1))
                    {
                        Button button = _mesa.GetNode<Button>(Mesa.FinalHandButtonPath(rival));
                        Check(button.Size.Y >= 44 && button.Text == "Mostrar juego", "Botón accesible bajo cada rival");
                        var published = PublicCards();
                        Check(published.All(c => !Overlap(button.GetGlobalRect(), c.GetGlobalRect())), "Botón no tapa juegos publicados");
                        await Click(button);
                        Control panel = _mesa.GetNode<Control>(Mesa.FinalHandPanelPath);
                        Check(panel.Visible, "Click abre la ventana");
                        Check(Cards(panel).Select(c => c.FaceCardId!.Value).SequenceEqual(
                            view.RevealedHands.Single(h => h.PlayerId == rival).Cards), "Solo cartas restantes del rival elegido");
                        Check(published.All(c => c.IsVisibleInTree() && !Overlap(panel.GetGlobalRect(), c.GetGlobalRect())),
                            "Todos los juegos siguen visibles y sin superposición");
                        foreach (Carta card in Cards(panel)) Check(Contains(panel.GetGlobalRect(), card.GetGlobalRect()), "Cartas dentro de la ventana");
                        Check(_mesa.GetNode<Control>(Mesa.FinalHandClosePath).Size.Y >= 44, "Cerrar mantiene altura de interfaz");
                        if (count == 4 && rival == 4 && resolution.X is 1280 or 2400)
                            await Capture((draw ? "Mano-empate-" : "Mano-final-") + resolution.X + "x" + resolution.Y + ".png");
                    }
                    await Click(_mesa.GetNode<Button>(Mesa.FinalHandClosePath));
                    Check(!_mesa.GetNode<Control>(Mesa.FinalHandPanelPath).Visible && Cards(_mesa.GetNode(Mesa.FinalHandPanelPath)).Length == 0,
                        "Cerrar retira ventana y sus cartas");
                    Check(NetworkProtocol.Encode(view) == original, "Consultar conserva el snapshot y juegos");
                    Button deckButton = _mesa.GetNode<Button>(Mesa.FinalDeckButtonPath);
                    Check(deckButton.Visible && deckButton.Size.Y >= 44 && deckButton.Text == "Mostrar mazo", "Botón de mazo disponible solo al terminar");
                    Check(PublicCards().All(card => !Overlap(deckButton.GetGlobalRect(), card.GetGlobalRect())), "Mostrar mazo no tapa juegos publicados");
                    await Click(deckButton);
                    Control deckPanel = _mesa.GetNode<Control>(Mesa.FinalHandPanelPath);
                    Check(deckPanel.Visible && Cards(deckPanel).Select(card => card.FaceCardId!.Value).SequenceEqual(view.FinalDeck!),
                        "El mazo aparece en el orden autorizado de extracción");
                    Label[] numbers = Descendants(deckPanel).OfType<Label>().Where(label => label.Name == "OrderLabel").ToArray();
                    Check(numbers.Select(label => label.Text).SequenceEqual(Enumerable.Range(1, view.DeckCount).Select(n => n.ToString())), "Cada carta indica su puesto de salida");
                    Check(PublicCards().All(card => !Overlap(deckPanel.GetGlobalRect(), card.GetGlobalRect())), "La consulta del mazo conserva los juegos visibles");
                    if (count == 4 && resolution == new Vector2I(1280, 720)) await Capture((draw ? "Mazo-empate-" : "Mazo-final-") + "1280x720.png");
                    if (count == 4 && resolution == new Vector2I(2400, 1080)) await Capture((draw ? "Mazo-empate-" : "Mazo-final-") + "2400x1080.png");
                    var deckScroll = _mesa.GetNode<ScrollContainer>(Mesa.FinalHandPanelPath + "/Padding/Content/DeckScroll");
                    deckScroll.ScrollVertical = 10000; await Settle();
                    if (view.DeckCount > 0)
                        Check(deckScroll.ScrollVertical > 0 && Overlap(deckScroll.GetGlobalRect(), Cards(deckPanel)[^1].GetGlobalRect()), "El desplazamiento permite llegar a la última carta");
                    else Check(Cards(deckPanel).Length == 0 && _mesa.GetNode<Control>(Mesa.FinalHandPanelPath + "/Padding/Content/EmptyLabel").Visible,
                        "Mazo agotado conserva la consulta de manos y explica su ausencia de cartas");
                    await Click(_mesa.GetNode<Button>(Mesa.FinalHandButtonPath(2)));
                    Check(Cards(deckPanel).Select(card => card.FaceCardId!.Value).SequenceEqual(view.RevealedHands.Single(h => h.PlayerId == 2).Cards),
                        "Mostrar juego sustituye la consulta del mazo en la misma ventana");
                    await Click(deckButton);
                    Check(deckScroll.ScrollVertical == 0, "Cada consulta del mazo comienza por la próxima carta");
                    _mesa.DisplaySnapshot(view with { Revision = 2 }, "Mismo final"); await Settle();
                    Check(deckPanel.Visible, "Una actualización del mismo final conserva el mazo consultado");
                    await Click(_mesa.GetNode<Button>(Mesa.FinalHandClosePath));
                    Check(Cards(deckPanel).Length == 0 && !deckPanel.Visible, "Cerrar elimina las cartas de consulta del mazo");
                    Check(NetworkProtocol.Encode(view) == original, "Consultar el mazo no cambia el snapshot");
                }
                GD.Print("OFFER FINAL CASE: " + resolution);
            }
            SnapshotDto empty = WinFixture(4) with { RevealedHands = new[] { new RevealedHandDto(1, Array.Empty<int>()),
                new RevealedHandDto(2, Array.Empty<int>()), new RevealedHandDto(3, new[] { 33, 34 }),
                new RevealedHandDto(4, Enumerable.Range(39, 9).ToArray()) }, Melds = WinFixture(4).Melds.Where(m => m.OwnerId != 4).ToArray() };
            _mesa.DisplaySnapshot(empty, "Fin"); await Settle();
            await Click(_mesa.GetNode<Button>(Mesa.FinalHandButtonPath(2)));
            Check(Cards(_mesa.GetNode(Mesa.FinalHandPanelPath)).Length == 0 &&
                _mesa.GetNode<Control>(Mesa.FinalHandPanelPath + "/Padding/Content/EmptyLabel").Visible, "Mano vacía explícita");
            await Click(_mesa.GetNode<Button>(Mesa.FinalHandButtonPath(4)));
            Check(Cards(_mesa.GetNode(Mesa.FinalHandPanelPath)).Length == 9, "Nueve restantes al cambiar de rival");
            Control fullPanel = _mesa.GetNode<Control>(Mesa.FinalHandPanelPath);
            Check(Cards(fullPanel).All(card => Contains(fullPanel.GetGlobalRect(), card.GetGlobalRect())) &&
                PublicCards().All(card => !Overlap(fullPanel.GetGlobalRect(), card.GetGlobalRect())), "Nueve cartas caben sin tapar juegos");
            _input.PushInput(new InputEventKey { Pressed = true, Keycode = Key.Escape }, true); await Settle();
            Check(!_mesa.GetNode<Control>(Mesa.FinalHandPanelPath).Visible, "Escape cierra la ventana final");
            await Click(_mesa.GetNode<Button>(Mesa.FinalHandButtonPath(4)));
            _mesa.DisplaySnapshot(offer, "Siguiente partida"); await Settle();
            Check(!_mesa.GetNode<Control>(Mesa.FinalHandPanelPath).Visible && !_mesa.HasNode(Mesa.FinalHandButtonPath(2)) &&
                Cards(_mesa.GetNode(Mesa.FinalHandPanelPath)).Length == 0, "Siguiente partida borra la revelación anterior");
            Check(!_mesa.GetNode<Button>(Mesa.FinalDeckButtonPath).Visible, "Partida activa no permite consultar el mazo");
            _mesa.DisplaySnapshot(offer with { RevealedHands = empty.RevealedHands }, "Datos de final fuera de fase"); await Settle();
            Check(!_mesa.HasNode(Mesa.FinalHandButtonPath(2)), "Una mano revelada fuera de Finished no habilita la consulta");
            SnapshotDto exhausted = DrawFixture(4);
            _mesa.DisplaySnapshot(exhausted, "Mazo agotado"); await Settle();
            await Click(_mesa.GetNode<Button>(Mesa.FinalDeckButtonPath));
            Check(Cards(_mesa.GetNode(Mesa.FinalHandPanelPath)).Length == 0 &&
                _mesa.GetNode<Label>(Mesa.FinalHandPanelPath + "/Padding/Content/EmptyLabel").Text == "No quedaban cartas en el mazo",
                "Mazo vacío al final se explica sin inventar cartas");
            _mesa.DisplaySnapshot(null, "Salir"); await Settle();
            Check(Cards(_mesa.GetNode(Mesa.FinalHandPanelPath)).Length == 0, "Salir limpia datos de la ventana");
            // Este diagnóstico crea y destruye muchas mesas/cartas al cambiar
            // ventanas. Dejar terminar QueueFree y sus wrappers .NET antes de
            // apagar el motor evita referencias pendientes en su finalización.
            _input.QueueFree(); _mesa = null!; _input = null!;
            await Frame(); await Frame(); await Frame();
            GC.Collect(); GC.WaitForPendingFinalizers();
            await Frame();
            GD.Print("OFFER AND FINAL HANDS PASS: Comprobaciones=" + _checks); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError("OFFER AND FINAL HANDS FAIL: " + error); GetTree().Quit(1); }
    }

    private Carta Card(int id) => _mesa.GetNode<Carta>(HandPath + "/Carta" + id);
    private Carta[] PublicCards() => Cards(_mesa.GetNode(Mesa.OwnGamesPath)).Concat(Cards(_mesa.GetNode(Mesa.RivalGamesPath))).ToArray();
    private static IEnumerable<Node> Descendants(Node node)
    { foreach (Node child in node.GetChildren()) { yield return child; foreach (Node next in Descendants(child)) yield return next; } }
    private static Carta[] Cards(Node node) => Descendants(node).OfType<Carta>().Where(c => c.IsVisibleInTree() && c.FaceCardId is not null).ToArray();
    private static bool Overlap(Rect2 a, Rect2 b) => a.Position.X < b.End.X - 1 && b.Position.X < a.End.X - 1 && a.Position.Y < b.End.Y - 1 && b.Position.Y < a.End.Y - 1;
    private static bool Contains(Rect2 outer, Rect2 inner) => inner.Position.X >= outer.Position.X - 1 && inner.End.X <= outer.End.X + 1 && inner.Position.Y >= outer.Position.Y - 1 && inner.End.Y <= outer.End.Y + 1;
    private void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); _checks++; }
    private async Task Click(Control control)
    {
        Vector2 p = control.GetGlobalTransformWithCanvas() * (control.Size / 2);
        _input.PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true); await Frame();
        _input.PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, ButtonMask = MouseButtonMask.Left, Pressed = true }, true); await Frame();
        _input.PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = false }, true); await Settle();
    }
    private async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    private async Task Settle() { for (int i = 0; i < 3; i++) await Frame(); await ToSignal(GetTree().CreateTimer(.18), Timer.SignalName.Timeout); }
    private async Task Capture(string name)
    {
        if (_capture.Length == 0) return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Directory.CreateDirectory(_capture);
        using Image image = _input.GetTexture().GetImage(); Check(image.SavePng(Path.Combine(_capture, name)) == Error.Ok, "Captura");
    }
    private static SnapshotDto WinFixture(int count)
    {
        SnapshotDto view = new(NetworkProtocol.Version, new string('f', 32), 1, 1, 1,
        GamePhase.Finished, MatchResult.Desmoche, null, null, 1,
        Enumerable.Range(1, count).Select(id => new PublicPlayerDto(id, "Jugador " + id, id - 1, id == 1 ? 0 : id == 2 ? 3 : id == 3 ? 2 : 6, true, false)).ToArray(),
        Array.Empty<int>(), new[] { new MeldDto(1, 1, Enumerable.Range(0, 10).ToArray()), new(2, 2, Enumerable.Range(13, 6).ToArray()),
            new(3, 3, Enumerable.Range(26, 7).ToArray()), new(4, 4, Enumerable.Range(39, 3).ToArray()) }.Where(m => m.OwnerId <= count).ToArray(),
        52 - 10 - (count - 1) * 9, 0, null, null,
        Enumerable.Range(1, count).Select(id => new RevealedHandDto(id, id == 1 ? Array.Empty<int>() : id == 2 ? Enumerable.Range(19, 3).ToArray() :
            id == 3 ? new[] { 33, 34 } : Enumerable.Range(42, 6).ToArray())).ToArray(), false);
        return view with { FinalDeck = Enumerable.Range(0, 52).Except(view.Melds.SelectMany(m => m.Cards))
            .Except(view.RevealedHands.SelectMany(h => h.Cards)).Reverse().ToArray() };
    }

    // Reproduce los conteos de la captura del usuario: mazo agotado, 5 propias,
    // 0/6/6 en los rivales y todos los juegos publicados aún visibles.
    private static SnapshotDto DrawFixture(int count)
    {
        int[][] hands = { new[] { 32, 43, 17, 35, 36 }, Array.Empty<int>(),
            new[] { 7, 8, 11, 12, 16, 18 }, new[] { 19, 20, 21, 26, 30, 31 } };
        MeldDto[] melds = { new(1, 1, new[] { 0, 1, 2, 3 }), new(2, 2, new[] { 13, 14, 15 }),
            new(3, 2, new[] { 23, 24, 25 }), new(4, 2, new[] { 27, 28, 29 }),
            new(5, 3, new[] { 9, 22, 48 }), new(6, 4, new[] { 4, 5, 6 }) };
        melds = melds.Where(m => m.OwnerId <= count).ToArray();
        return new SnapshotDto(NetworkProtocol.Version, new string('d', 32), 1, 1, 1,
            GamePhase.Finished, MatchResult.Draw, null, null, null,
            Enumerable.Range(1, count).Select(id => new PublicPlayerDto(id, "Jugador " + id, id - 1, hands[id - 1].Length, true, false)).ToArray(),
            hands[0], melds, 0, 52 - hands.Take(count).Sum(h => h.Length) - melds.Sum(m => m.Cards.Length), 10, null,
            Enumerable.Range(1, count).Select(id => new RevealedHandDto(id, hands[id - 1])).ToArray(), false, FinalDeck: Array.Empty<int>());
    }
}
