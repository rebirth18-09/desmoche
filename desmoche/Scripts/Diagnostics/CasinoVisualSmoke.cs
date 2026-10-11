using System;
using System.Collections.Generic;
using System.Linq;
using Desmoche.Application;
using Desmoche.Domain;
using Desmoche.Networking.Core;
using Desmoche.Presentation;
using Godot;

namespace Desmoche.Diagnostics;

// Estos snapshots son pruebas de presentación: nunca se instalan en GameManager.
// El caso de tres rivales con diez cartas simultáneas es un caso de disposición,
// no una partida ordinaria: el motor terminaría al confirmar la primera victoria.
public partial class CasinoVisualSmoke : Main
{
    protected override bool ImmediateInitialization => true;
    private Mesa _mesa = null!;
    private Viewport _testViewport = null!;
    private SnapshotDto _shown = null!;
    private int[] _privateRivals = Array.Empty<int>();
    private bool _auto, _done, _captureStarted, _captureFinished, _nativeStarted, _nativeFinished;
    private int _fixture, _size, _checks;
    private ulong _nextAt;
    private string _before = "", _capturePath = "";
    private static readonly Vector2I[] Resolutions =
    {
        new(2016, 780), new(1600, 900), new(1000, 760)
    };
    private static readonly string[] Titles =
    {
        "Referencia coherente: mazo de 16 cartas",
        "Pago pendiente: siete cartas siguen en mano",
        "Disposición sintética: tres filas de diez cartas"
    };

    public override void _Ready()
    {
        base._Ready();
        string[] args = OS.GetCmdlineUserArgs();
        _auto = args.Contains("--auto");
        _capturePath = args.FirstOrDefault(a => a.StartsWith("--capture=", StringComparison.Ordinal))?[10..] ?? "";
        Main tested = this;
        _testViewport = GetViewport();
        if (_auto)
        {
            GetWindow().Size = Resolutions[0];
            GetNode<Control>("Margin").Hide();
            // El viewport aislado usa las posiciones de sus eventos de GUI,
            // sin depender del ratón del SO ni sustituir callbacks de drag.
            var isolated = new SubViewport
            {
                Name = "TestViewport", Size = Resolutions[0],
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                HandleInputLocally = true
            };
            AddChild(isolated);
            tested = GD.Load<PackedScene>("res://Scenes/Diagnostics/DiagnosticMain.tscn").Instantiate<Main>();
            isolated.AddChild(tested);
            _testViewport = isolated;
            var render = new TextureRect
            {
                Name = "RenderedTest", Texture = isolated.GetTexture(),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(render);
            render.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            GD.Print("CASINO INPUT: GUI nativa en SubViewport aislado; sin cursor del SO.");
        }
        _mesa = tested.GetNode<Mesa>("Margin/Screens/Mesa");
        tested.GetNode<Control>("Margin/Screens/Lobby").Visible = false;
        _mesa.Visible = true;
        ShowFixture(0);
    }

    public override void _Input(InputEvent @event)
    {
        if (!_auto && @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Space })
        {
            ShowFixture((_fixture + 1) % Titles.Length);
            AcceptEvent();
        }
    }

    public override void _Process(double delta)
    {
        if (_done || Time.GetTicksMsec() < _nextAt) return;
        try
        {
            if (!_captureStarted && _capturePath.Length > 0)
            {
                _captureStarted = true;
                CaptureFrame();
                return;
            }
            if (_captureStarted && !_captureFinished) return;
            if (!_auto) return;
            if (!_nativeStarted)
            {
                _nativeStarted = true;
                // La captura y el arrastre se comprueban con render normal.
                if (DisplayServer.GetName() == "headless")
                {
                    GD.Print("CASINO INPUT OMITIDO: el backend headless no ofrece un ratón nativo; usar -Capture para probar el arrastre.");
                    _nativeFinished = true;
                    return;
                }
                CheckNativeDrag();
                return;
            }
            if (!_nativeFinished) return;
            ValidateFixture();
            if (_fixture < Titles.Length - 1) ShowFixture(_fixture + 1);
            else if (_size < Resolutions.Length - 1)
            {
                _size++;
                _nativeStarted = false;
                _nativeFinished = false;
                GetWindow().Size = Resolutions[_size];
                if (_testViewport is SubViewport isolated) isolated.Size = Resolutions[_size];
                ShowFixture(0);
            }
            else Finish(true, "Tres estados y tres resoluciones; contadores, filas y privacidad correctos.");
        }
        catch (Exception ex) { Finish(false, ex.Message); }
    }

    private void ShowFixture(int index)
    {
        _fixture = index;
        _shown = CreateFixture(index, out _privateRivals);
        _before = NetworkProtocol.Encode(_shown);
        _mesa.DisplaySnapshot(_shown, "Demostración visual · " + Titles[index] + " · Espacio cambia el ejemplo");
        _nextAt = Time.GetTicksMsec() + 400UL;
    }

    private void ValidateFixture()
    {
        Check(NetworkProtocol.Encode(_shown) == _before, "La presentación conserva el snapshot intacto.");
        Check(_shown.OwnHand.Length == _shown.Players.Single(p => p.Id == _shown.RecipientId).CardCount,
            "El contador propio corresponde a la mano real de la vista.");
        int[] actualPublic = _shown.Melds.SelectMany(m => m.Cards).ToArray();
        int[] allocated = actualPublic.Concat(_shown.OwnHand).Concat(_privateRivals).ToArray();
        Check(allocated.Distinct().Count() == allocated.Length, "No se repiten cartas en los ejemplos.");
        Check(allocated.Length + _shown.DeckCount + _shown.DiscardCount == 52,
            "Cada ejemplo conserva una baraja de 52 cartas.");
        foreach (MeldDto group in _shown.Melds)
            Check(RulesValidator.ValidateMeld(group.Cards.Select(Card.FromId).ToArray()).IsValid,
                "La combinación pública " + group.Id + " es válida.");
        Check(_shown.Melds.GroupBy(m => m.OwnerId).All(g => g.Sum(m => m.Cards.Length) <= 10),
            "Los ejemplos no exceden diez cartas públicas por jugador.");
        Check(_mesa.RenderedPlayerCount == _shown.Players.Length, "Se muestran los cuatro jugadores.");
        var players = _mesa.GetNode<Control>(Mesa.ContentPath + "PlayerZone/Padding/Players");
        var self = _mesa.GetNode<Control>(Mesa.SelfPlayerPath);
        foreach (PublicPlayerDto player in _shown.Players)
        {
            Node root = player.Id == _shown.RecipientId ? self : players;
            Node panel = root.GetNode("Jugador" + player.Id);
            Check(Descendants(panel).OfType<Label>().Any(l => l.Text == player.CardCount + " cartas en mano"),
                "El panel de " + player.Name + " usa CardCount del servidor.");
            Check(Cards(panel).Length == 0, "Los paneles no contienen identidades privadas.");
        }
        if (_fixture == 1)
        {
            Check(_shown.Phase == GamePhase.AwaitingPayment && _shown.OwnHand.Length == 7,
                "La carta que se debe pagar sigue incluida en la mano.");
            Check(_shown.OwnHand.Length != Math.Max(0, 9 - _shown.Melds
                .Where(m => m.OwnerId == _shown.RecipientId).Sum(m => m.Cards.Length)),
                "El ejemplo detecta el uso incorrecto de la fórmula 9 menos juegos.");
        }
        var rivals = _shown.Players.Where(p => p.Id != _shown.RecipientId).OrderBy(p => p.SeatIndex).ToArray();
        var rivalRoot = _mesa.GetNode<Control>(Mesa.RivalGamesPath);
        Control[] zones = rivals.Select(p => rivalRoot.GetNode<Control>("JuegosJugador" + p.Id)).ToArray();
        Rect2[] bounds = zones.Select(z => z.GetGlobalRect()).ToArray();
        var publicCards = new List<Carta>();
        for (int i = 0; i < rivals.Length; i++)
        {
            Carta[] cards = Cards(zones[i]);
            int[] expected = _shown.Melds.Where(m => m.OwnerId == rivals[i].Id).OrderBy(m => m.Id)
                .SelectMany(m => m.Cards).ToArray();
            Check(cards.Select(c => c.FaceCardId!.Value).SequenceEqual(expected),
                "Se conservan las identidades y el orden de los juegos de " + rivals[i].Name + ".");
            Check(cards.All(c => Contains(bounds[i], c.GetGlobalRect())),
                "Todas las cartas están dentro de su zona independiente.");
            Check(cards.Select(c => c.GetGlobalRect().Position.Y).DefaultIfEmpty().Max() -
                cards.Select(c => c.GetGlobalRect().Position.Y).DefaultIfEmpty().Min() <= 1.5f,
                "Cada juego superior permanece en una sola fila.");
            publicCards.AddRange(cards);
        }
        for (int i = 0; i < bounds.Length; i++)
            for (int j = i + 1; j < bounds.Length; j++)
                Check(!Overlaps(bounds[i], bounds[j]), "Las zonas rivales no se superponen.");
        if (publicCards.Count > 0)
        {
            Vector2 compact = publicCards[0].GetGlobalRect().Size;
            Check(publicCards.All(c => c.GetGlobalRect().Size.DistanceTo(compact) < 1.5f),
                "Los rivales comparten un tamaño compacto consistente.");
            Carta[] hand = Cards(_mesa.GetNode(Mesa.ContentPath + "HandZone/Padding/Content/Cards"));
            Check(hand.All(c => c.GetGlobalRect().Size.X > compact.X && c.GetGlobalRect().Size.Y > compact.Y),
                "La mano propia utiliza cartas mayores que los juegos superiores.");
        }
        Carta deck = _mesa.GetNode<Carta>(Mesa.CenterPath + "Mazo/Content/Carta");
        Carta discard = _mesa.GetNode<Carta>(Mesa.CenterPath + "Pozo/Content/Carta");
        Check(publicCards.All(c => !Overlaps(c.GetGlobalRect(), deck.GetGlobalRect()) &&
            !Overlaps(c.GetGlobalRect(), discard.GetGlobalRect())),
            "Los juegos superiores no invaden el mazo ni el pozo.");
        Check(deck.IsBack && deck.FaceCardId is null, "El mazo muestra un dorso sin revelar cartas.");
        Check(discard.FaceCardId is null && !discard.IsBack, "El pozo vacío no revela identidades.");
        Check(_mesa.GetNode<Label>(Mesa.CenterPath + "Mazo/Content/CountLabel").Text == _shown.DeckCount + " cartas",
            "El contador del mazo corresponde al ejemplo coherente.");
        Check(_mesa.GetNode<Label>(Mesa.CenterPath + "Pozo/Content/CountLabel").Text == "0 cartas",
            "El pozo vacío tiene contador cero.");
        int[] allowed = actualPublic.Concat(_shown.OwnHand).OrderBy(id => id).ToArray();
        int[] visible = Cards(_mesa).Select(c => c.FaceCardId!.Value).OrderBy(id => id).ToArray();
        Check(visible.SequenceEqual(allowed), "Solo se ven la mano propia y los juegos públicos autorizados.");
        Check(!visible.Intersect(_privateRivals).Any(), "Ninguna carta privada rival aparece en pantalla.");
        GD.Print("CASINO CASO OK: " + Titles[_fixture] + " · " + Resolutions[_size]);
    }

    private async void CaptureFrame()
    {
        try
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using Image image = _testViewport.GetTexture().GetImage();
            if (image is null || image.IsEmpty()) throw new InvalidOperationException("El render no proporciona una imagen; no usar --headless para capturar.");
            Error result = image.SavePng(_capturePath);
            if (result != Error.Ok) throw new InvalidOperationException("No se pudo guardar la captura: " + result + ".");
            GD.Print("CASINO CAPTURA OK: " + _capturePath);
            _captureFinished = true;
            _nextAt = Time.GetTicksMsec() + 250UL;
        }
        catch (Exception ex) { Finish(false, ex.Message); }
    }

    private async void CheckNativeDrag()
    {
        try
        {
            var hand = _mesa.GetNode(Mesa.ContentPath + "HandZone/Padding/Content/Cards");
            Carta[] cards = Cards(hand);
            Check(cards.Length >= 2, "Hay cartas propias para verificar el arrastre nativo.");
            int[] beforeOrder = _mesa.LocalHandOrder;
            Carta source = cards[0], target = cards[^1];
            Vector2 from = source.GetGlobalTransformWithCanvas() * (source.Size / 2);
            Vector2 to = target.GetGlobalTransformWithCanvas() * new Vector2(target.Size.X - 3, target.Size.Y / 2);
            // Coordenadas locales del viewport: evita que el stretch de la ventana
            // convierta dos veces las posiciones en la ejecución sin pantalla.
            _testViewport.PushInput(new InputEventMouseMotion { Position = from, GlobalPosition = from }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            _testViewport.PushInput(new InputEventMouseButton
            {
                Position = from, GlobalPosition = from, ButtonIndex = MouseButton.Left,
                ButtonMask = MouseButtonMask.Left, Pressed = true
            }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            _testViewport.PushInput(new InputEventMouseMotion
            {
                Position = to, GlobalPosition = to, Relative = to - from,
                ButtonMask = MouseButtonMask.Left
            }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(_testViewport.GuiIsDragging(), "Godot inició el arrastre nativo con su vista previa.");
            Check(CardDragData.TryRead(_testViewport.GuiGetDragData(), _shown, out LocalCardDrag data) &&
                data.CardId == beforeOrder[0] && data.Origin == CardDragOrigin.Hand,
                "El arrastre nativo transporta una intención local de la carta propia.");
            // Un segundo movimiento deja que el hit-test actualice el destino
            // después de liberar el foco de ratón de la carta origen.
            to = target.GetGlobalTransformWithCanvas() * new Vector2(target.Size.X - 3, target.Size.Y / 2);
            _testViewport.PushInput(new InputEventMouseMotion
            {
                Position = to, GlobalPosition = to, ButtonMask = MouseButtonMask.Left
            }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            string dropTarget = _testViewport.GuiGetHoveredControl()?.GetPath().ToString() ?? "sin destino";
            _testViewport.PushInput(new InputEventMouseButton
            {
                Position = to, GlobalPosition = to, ButtonIndex = MouseButton.Left, Pressed = false
            }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(!_testViewport.GuiIsDragging(), "Soltar termina el arrastre nativo.");
            Check(_mesa.LocalHandOrder.SequenceEqual(beforeOrder.Skip(1).Append(beforeOrder[0])),
                "El arrastre real reorganiza la mano local en esta resolución. Destino=" + dropTarget +
                " Orden=" + string.Join(",", _mesa.LocalHandOrder) + " esperado=" +
                string.Join(",", beforeOrder.Skip(1).Append(beforeOrder[0])));
            Check(NetworkProtocol.Encode(_shown) == _before,
                "El arrastre real conserva todas las cartas y datos del snapshot autorizado.");
            _testViewport.PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            _testViewport.PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = false });
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            _nativeFinished = true;
            _nextAt = Time.GetTicksMsec() + 150UL;
        }
        catch (Exception ex)
        {
            _testViewport.GuiCancelDrag();
            Finish(false, ex.Message);
        }
    }

    private static SnapshotDto CreateFixture(int index, out int[] hidden)
    {
        MeldDto[] melds;
        int[] own;
        PublicPlayerDto[] players;
        if (index == 2)
        {
            melds = new[]
            {
                new MeldDto(1, 2, Enumerable.Range(0, 10).ToArray()),
                new MeldDto(2, 3, Enumerable.Range(13, 10).ToArray()),
                new MeldDto(3, 4, Enumerable.Range(26, 10).ToArray())
            };
            own = Enumerable.Range(39, 9).ToArray();
            players = new[]
            {
                new PublicPlayerDto(1, "Ricardo", 0, 9, true, false),
                new PublicPlayerDto(2, "Ana", 1, 0, true, false),
                new PublicPlayerDto(3, "Luis", 2, 0, true, false),
                new PublicPlayerDto(4, "Tercer jugador", 3, 0, true, false)
            };
            hidden = Array.Empty<int>();
        }
        else
        {
            melds = new[]
            {
                new MeldDto(1, 2, new[] { 0, 1, 2 }),
                new MeldDto(2, 2, new[] { 6, 7, 8, 9, 10, 11 }),
                new MeldDto(3, 3, Enumerable.Range(31, 7).ToArray()),
                new MeldDto(4, 4, new[] { 49, 50, 51 }),
                new MeldDto(5, 1, new[] { 17, 4, 30 })
            };
            own = new[] { 26, 14, 41, 5, 20, 38 };
            int[] remaining = Enumerable.Range(0, 52).Except(melds.SelectMany(m => m.Cards)).Except(own).ToArray();
            hidden = remaining.Take(8).ToArray();
            if (index == 1) own = own.Append(remaining[8]).ToArray();
            players = new[]
            {
                new PublicPlayerDto(1, "Ricardo", 0, own.Length, true, false),
                new PublicPlayerDto(2, "Ana", 1, 0, true, false),
                new PublicPlayerDto(3, "Luis", 2, 2, false, true),
                new PublicPlayerDto(4, "Tercer jugador", 3, 6, true, false)
            };
        }
        int deckCount = 52 - melds.Sum(m => m.Cards.Length) - players.Sum(p => p.CardCount);
        return new SnapshotDto(NetworkProtocol.Version, new string('c', 32), index + 1, index + 1, 1,
            index == 1 ? GamePhase.AwaitingPayment : GamePhase.AwaitingDraw,
            MatchResult.None, index == 1 || index == 2 ? 1 : 2, index == 1 ? 1 : null, null,
            players, own, melds, deckCount, 0, null, null, Array.Empty<RevealedHandDto>(), false);
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            yield return child;
            foreach (Node descendant in Descendants(child)) yield return descendant;
        }
    }
    private static Carta[] Cards(Node root) => Descendants(root).OfType<Carta>()
        .Where(c => c.IsVisibleInTree() && c.FaceCardId is not null).ToArray();
    private static bool Contains(Rect2 outer, Rect2 inner) => inner.Position.X >= outer.Position.X - 1.5f &&
        inner.Position.Y >= outer.Position.Y - 1.5f && inner.End.X <= outer.End.X + 1.5f && inner.End.Y <= outer.End.Y + 1.5f;
    private static bool Overlaps(Rect2 a, Rect2 b) => a.Position.X < b.End.X - 1f && b.Position.X < a.End.X - 1f &&
        a.Position.Y < b.End.Y - 1f && b.Position.Y < a.End.Y - 1f;
    private void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        _checks++;
    }
    private void Finish(bool success, string message)
    {
        if (_done) return;
        _done = true;
        GD.Print((success ? "CASINO VISUAL OK: " : "CASINO VISUAL ERROR: ") + message + " Comprobaciones=" + _checks);
        if (_auto || _capturePath.Length > 0) GetTree().Quit(success ? 0 : 1);
        else GD.PushError(message);
    }
}
