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

// GUI nativa vinculada a la autoridad real, sin sustituir las reglas por fixtures de UI.
public partial class MandatoryOfferSmoke : Main
{
    protected override bool ImmediateInitialization => true;
    private SubViewport _input = null!;
    private Mesa _mesa = null!;
    private int _checks;
    private string _capture = "";

    private sealed class AuthoritySession : IGameSession
    {
        private readonly ServerSession _server;
        private readonly Dictionary<int, long> _requests = new();
        private long _now = 1000;
        public bool IsActive => true;
        public bool IsLocal => true;
        public bool CanManageMatch => false;
        public SnapshotDto? View => _server.ViewFor(1);
        public string Status => "Diagnóstico de oferta obligatoria";
        public event Action<SnapshotDto>? ViewChanged;
        public event Action<ReplyDto>? ActionCompleted;
        public event Action? SessionChanged;
        public AuthoritySession()
        {
            int[][] desired = { Enumerable.Range(13, 9).ToArray(), new[] { 2, 3, 4, 26, 27, 28, 29, 30, 31 } };
            int[][] before = desired.Select((h, i) => h.Skip(1).Append(desired[(i + 1) % 2][0]).ToArray()).ToArray();
            var draws = new List<int>();
            for (int round = 0; round < 9; round++) foreach (int[] hand in before) draws.Add(hand[round]);
            draws.Add(5); draws.AddRange(Enumerable.Range(0, 52).Except(draws).ToArray());
            _server = new ServerSession("Tú", () => new Deck(draws.AsEnumerable().Reverse().Select(Card.FromId)));
            if (!_server.Join(2, NetworkProtocol.Version, "Rival", out string error)) throw new InvalidOperationException(error);
            Send(1, ActionKind.StartMatch);
            Send(1, ActionKind.ExchangeCard, View!.OwnHand[^1]); Send(2, ActionKind.ExchangeCard, _server.ViewFor(2).OwnHand[^1]);
            Send(1, ActionKind.FinishInitial); Send(2, ActionKind.FinishInitial);
            Send(2, ActionKind.Publish, plays: new[] { new PlayDto(new[] { 2, 3, 4 }) });
            Send(1, ActionKind.DrawDeck); Send(2, ActionKind.Pass);
        }
        public long Submit(ActionKind kind, int? cardId = null, PlayDto[]? plays = null, bool declareWin = false,
            InitialVictoryKind? initialKind = null, int[]? cards = null, int? sourceMeldId = null)
        {
            long request = _requests.GetValueOrDefault(1) + 1; _requests[1] = request;
            SnapshotDto current = View!;
            var command = new CommandDto(NetworkProtocol.Version, current.Session, request, current.Window, kind,
                CardId: cardId, Plays: plays, DeclareWin: declareWin, InitialKind: initialKind, Cards: cards, SourceMeldId: sourceMeldId);
            Callable.From(() =>
            {
                ReplyDto reply = _server.Receive(1, NetworkProtocol.Encode(command), ++_now);
                ViewChanged?.Invoke(View!); ActionCompleted?.Invoke(reply);
            }).CallDeferred();
            return request;
        }
        private void Send(int actor, ActionKind kind, int? card = null, PlayDto[]? plays = null)
        {
            long request = _requests.GetValueOrDefault(actor) + 1; _requests[actor] = request;
            ReplyDto reply = _server.Receive(actor, NetworkProtocol.Encode(new CommandDto(NetworkProtocol.Version,
                _server.SessionId, request, _server.Window, kind, CardId: card, Plays: plays)), ++_now);
            if (!reply.Success || reply.Penalized) throw new InvalidOperationException(reply.Message);
        }
        public void Expire()
        { _now += 3000; _server.Tick(_now); ViewChanged?.Invoke(View!); }
        public void Close(string reason = "") { SessionChanged?.Invoke(); }
    }

    public override void _Ready()
    {
        base._Ready(); _capture = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--capture="))?[10..] ?? "";
        GetNode<Control>("Margin").Hide();
        _input = new SubViewport { Name = "MandatoryOfferInput", Size = new Vector2I(1280, 720),
            HandleInputLocally = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always }; AddChild(_input);
        Main tested = GD.Load<PackedScene>("res://Scenes/Diagnostics/DiagnosticMain.tscn").Instantiate<Main>(); _input.AddChild(tested);
        tested.GetNode<Control>("Margin/Screens/Lobby").Hide(); tested.GetNode<Control>("SalonBackground").Hide();
        tested.GetNode<Control>("MenuFrame").Hide(); _mesa = tested.GetNode<Mesa>("Margin/Screens/Mesa"); _mesa.Show();
        var render = new TextureRect { Texture = _input.GetTexture(), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore }; AddChild(render);
        render.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); Callable.From(Run).CallDeferred();
    }

    private async void Run()
    {
        try
        {
            if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("Requiere render real para GUI nativa.");
            foreach (Vector2I size in new[] { new Vector2I(640, 360), new(844, 390), new(1280, 720), new(1920, 1080),
                new(2400, 1080), new(1024, 768), new(3440, 1440) })
            {
                GetWindow().Size = size; await Settle();
                _input.Size = new Vector2I((int)GetViewportRect().Size.X, (int)GetViewportRect().Size.Y);
                var session = new AuthoritySession(); _mesa.Bind(session); await Settle();
                Check(!_mesa.GetNode<Button>(Mesa.PasaButtonPath).Visible && !_mesa.GetNode<Button>(Mesa.FinalDeckButtonPath).Visible,
                    "La oferta pendiente no tiene denuncia ni consulta del mazo.");
                Button reject = _mesa.GetNode<Button>(Mesa.ContextActionsPath + "PassButton");
                await Click(reject);
                Check(session.View!.OrderChallenge is { PlayerId: 2, Reason: PasaReason.UnusedOffer },
                    "Rechazar en la GUI abre la denuncia en la autoridad. fase=" + session.View.Phase +
                    " botón=" + reject.GetGlobalRect() + " disabled=" + reject.Disabled + " viewport=" + _input.Size);
                Button pasa = _mesa.GetNode<Button>(Mesa.PasaButtonPath);
                Check(pasa.Visible && !pasa.Disabled && pasa.Size.Y >= 44 && pasa.TooltipText.Contains("oferta"), "Pasa de oferta visible y accesible.");
                var group = _mesa.GetNode<Control>(Mesa.RivalGamesPath + "/JuegosJugador2/Rows/Juego1");
                Rect2 button = pasa.GetGlobalRect(); Rect2 cards = Bounds(group);
                Check(!button.Intersects(cards) && Math.Abs(button.GetCenter().Y - cards.GetCenter().Y) < 90, "Pasa apunta al juego correcto sin cubrirlo.");
                Check(!_mesa.GetNode<Label>(Mesa.ContentPath + "Header/TurnLabel").Text.Contains("colores"), "El texto distingue la falta de oferta del orden de colores.");
                if (size == new Vector2I(1280, 720)) await Capture("Pasa-oferta-1280x720.png");
                await Click(pasa);
                Check(session.View!.Players.Single(p => p.Id == 2).Eliminated && session.View.DiscardTop == 5 && session.View.OfferedCard is null,
                    "Pulsar Pasa elimina al infractor y lleva la carta al pozo.");
                Check(!_mesa.GetNode<Button>(Mesa.PasaButtonPath).Visible &&
                    _mesa.GetNode<Carta>(Mesa.CenterPath + "Pozo/Content/Carta").FaceCardId == 5, "La GUI retira la denuncia y muestra el pozo confirmado.");
                _mesa.Unbind();
                var ignored = new AuthoritySession(); _mesa.Bind(ignored); await Settle();
                await Click(_mesa.GetNode<Button>(Mesa.ContextActionsPath + "PassButton")); ignored.Expire(); await Settle();
                Check(ignored.View!.Players.All(p => !p.Eliminated) && ignored.View.DiscardTop == 5 && !_mesa.GetNode<Button>(Mesa.PasaButtonPath).Visible,
                    "Sin pulsar Pasa, vencen los tres segundos y el jugador continúa.");
                _mesa.Unbind(); GD.Print("MANDATORY OFFER CASE: " + size);
            }
            _input.QueueFree(); _mesa = null!; _input = null!;
            await Frame(); await Frame(); await Frame(); GC.Collect(); GC.WaitForPendingFinalizers(); await Frame();
            GD.Print("MANDATORY OFFER PASS: Comprobaciones=" + _checks); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError("MANDATORY OFFER FAIL: " + error); GetTree().Quit(1); }
    }
    private void Check(bool valid, string text) { if (!valid) throw new InvalidOperationException(text); _checks++; }
    private static Rect2 Bounds(Node group)
    {
        Rect2[] rectangles = group.GetChildren().OfType<Carta>().Select(card => card.GetGlobalRect()).ToArray();
        return new Rect2(new Vector2(rectangles.Min(r => r.Position.X), rectangles.Min(r => r.Position.Y)),
            new Vector2(rectangles.Max(r => r.End.X) - rectangles.Min(r => r.Position.X), rectangles.Max(r => r.End.Y) - rectangles.Min(r => r.Position.Y)));
    }
    private async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    private async Task Settle() { for (int i = 0; i < 3; i++) await Frame(); await ToSignal(GetTree().CreateTimer(.18), Timer.SignalName.Timeout); }
    private async Task Click(Control target)
    {
        Vector2 p = target.GetGlobalTransformWithCanvas() * (target.Size / 2);
        _input.PushInput(new InputEventMouseMotion { Position = p, GlobalPosition = p }, true); await Frame();
        _input.PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left,
            ButtonMask = MouseButtonMask.Left, Pressed = true }, true); await Frame();
        _input.PushInput(new InputEventMouseButton { Position = p, GlobalPosition = p, ButtonIndex = MouseButton.Left, Pressed = false }, true); await Settle();
    }
    private async Task Capture(string filename)
    {
        if (_capture.Length == 0) return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Directory.CreateDirectory(_capture); using Image image = _input.GetTexture().GetImage();
        Check(image.SavePng(Path.Combine(_capture, filename)) == Error.Ok, "Captura de Pasa.");
    }
}
