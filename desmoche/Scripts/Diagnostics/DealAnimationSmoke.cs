using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Desmoche.Application;
using Desmoche.Networking.Core;
using Desmoche.Presentation;
using Godot;

namespace Desmoche.Diagnostics;

public partial class DealAnimationSmoke : Main
{
    protected override bool ImmediateInitialization => true;
    private Mesa _mesa = null!;
    private int _checks;
    private string _captures = "";
    private bool _auto;

    public override void _Ready()
    {
        base._Ready();
        string[] args = OS.GetCmdlineUserArgs();
        _auto = args.Contains("--auto");
        _captures = args.FirstOrDefault(a => a.StartsWith("--capture=", StringComparison.Ordinal))?[10..] ?? "";
        _mesa = GetNode<Mesa>("Margin/Screens/Mesa");
        GetNode<Control>("Margin/Screens/Lobby").Hide(); _mesa.Show();
        Callable.From(Run).CallDeferred();
    }

    private async void Run()
    {
        try
        {
            GetWindow().Size = new Vector2I(1600, 900);
            for (int count = 2; count <= 4; count++)
            {
                SnapshotDto view = Fixture(count, count);
                _mesa.DisplaySnapshot(view with { Phase = GamePhase.Preparing, Window = 1 }, "Preparando");
                _mesa.DisplaySnapshot(view, "Partida de prueba");
                ulong[] handNodes = Hand().Select(c => c.GetInstanceId()).ToArray();
                Check(_mesa.IsDealing, "Comienza al recibir el reparto confirmado.");
                Check(Hand().All(c => !c.Interactive && c.Modulate.A == 0), "El cambio propio espera el reparto.");
                await Until(() => _mesa.AnimatedDealCardCount == count * 9, 1500);
                Check(Flights().Length == count * 9 && Flights().All(c => c.IsBack && c.FaceCardId is null),
                    "Todas las entregas comienzan como dorsos sin identidad.");
                await Delay(0.9);
                Check(Flights().Where(c => c.IsFaceUp).All(c => view.OwnHand.Contains(c.FaceCardId!.Value)),
                    "Solo se revelan cartas de la mano propia.");
                if (count == 4) await Capture("Reparto-en-curso.png");
                PublicPlayerDto[] players = view.Players.Select(p => p.Id == 1 ? p with { CardCount = 8 } : p).ToArray();
                SnapshotDto latest = view with { Revision = 3, Players = players, ExchangeCount = 1 };
                string before = NetworkProtocol.Encode(latest);
                _mesa.DisplaySnapshot(latest, "Otro jugador confirmo el cambio");
                Check(_mesa.IsDealing, "Una confirmacion ajena no reinicia ni interrumpe el reparto.");
                if (count == 4) GetWindow().Size = new Vector2I(1100, 820);
                await Until(() => !_mesa.IsDealing, 6000);
                Check(Flights().Length == 0 && Hand().Length == 9 && Hand().All(c => c.Modulate.A == 1 && c.Interactive),
                    "Al terminar quedan nueve cartas propias y se habilita seleccion.");
                Check(handNodes.SequenceEqual(Hand().Select(c => c.GetInstanceId())),
                    "El cierre conserva las cartas y sus posiciones sin reconstruir la mano.");
                Check(NetworkProtocol.Encode(latest) == before, "La animacion no cambia el estado recibido.");
                Check(Descendants(_mesa.GetNode<Control>(Mesa.ContentPath + "PlayerZone")).OfType<Label>()
                    .Any(l => l.Text == "8 cartas en mano"), "Se dibuja la ultima confirmacion del rival al terminar.");
                _mesa.DisplaySnapshot(latest with { Revision = 4 }, "Actualizacion");
                Check(!_mesa.IsDealing, "No se repite por revisiones de la misma partida.");
                if (count == 4) await Capture("Reparto-completo.png");
                _mesa.DisplaySnapshot(latest with { Phase = GamePhase.Finished, Result = MatchResult.Draw, Window = 3 }, "Fin");
                _mesa.DisplaySnapshot(view with { Revision = 6, Window = 4 }, "Revancha");
                Check(_mesa.IsDealing, "La siguiente partida vuelve a animarse.");
                _mesa.DisplaySnapshot(null, "Desconectado");
                Check(!_mesa.IsDealing && Flights().Length == 0 && Hand().Length == 0, "Salir cancela vuelos y cartas privadas.");
            }
            SnapshotDto changed = Fixture(4, 1);
            _mesa.DisplaySnapshot(changed, "Inicio");
            _mesa.DisplaySnapshot(changed with { Revision = 3, OwnResponseSubmitted = true,
                OwnHand = changed.OwnHand.Skip(1).ToArray(), ExchangeCount = 1,
                Players = changed.Players.Select(p => p.Id == 1 ? p with { CardCount = 8 } : p).ToArray() }, "Cambio confirmado");
            Check(!_mesa.IsDealing && Hand().Length == 8 && Flights().Length == 0,
                "Un cambio propio confirmado cancela la animacion y conserva ocho cartas reales.");
            if (_captures.Length > 0) await CaptureSuits();
            GD.Print($"DEAL ANIMATION PASS: reparto, privacidad, cambio, revancha y cancelacion. Comprobaciones={_checks}");
            if (_auto) GetTree().Quit();
        }
        catch (Exception error) { GD.PushError("DEAL ANIMATION FAIL: " + error.Message); if (_auto) GetTree().Quit(1); }
    }

    private Carta[] Flights() => _mesa.GetNode<Control>(Mesa.ContentPath + "DealAnimation").GetChildren().OfType<Carta>().ToArray();
    private Carta[] Hand() => _mesa.GetNode<Control>(Mesa.ContentPath + "HandZone/Padding/Content/Cards").GetChildren().OfType<Carta>().ToArray();
    private void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); _checks++; }
    private async Task Delay(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), Timer.SignalName.Timeout);
    private async Task Until(Func<bool> predicate, int milliseconds)
    {
        ulong end = Time.GetTicksMsec() + (ulong)milliseconds;
        while (!predicate())
        { if (Time.GetTicksMsec() >= end) throw new InvalidOperationException("Tiempo de prueba agotado."); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    }
    private async Task Capture(string name)
    {
        if (_captures.Length == 0) return;
        Directory.CreateDirectory(_captures);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using Image image = GetViewport().GetTexture().GetImage();
        if (image is null || image.IsEmpty()) throw new InvalidOperationException("La captura requiere render real.");
        if (image.SavePng(Path.Combine(_captures, name)) != Error.Ok) throw new InvalidOperationException("No se pudo guardar captura.");
    }
    private async Task CaptureSuits()
    {
        _mesa.Hide();
        var panel = new ColorRect { Color = new Color(0.04f, 0.16f, 0.11f), Size = new Vector2(1100, 820), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(panel);
        var scene = GD.Load<PackedScene>("res://Scenes/Carta.tscn");
        for (int size = 0; size < 3; size++)
            for (int column = 0; column < 4; column++)
            {
                Carta card = scene.Instantiate<Carta>();
                card.SetVisualSize(size == 0 ? new Vector2(180, 220) : size == 1 ? new Vector2(90, 112) : new Vector2(50.3f, 72));
                card.ShowFace(column < 2 ? column == 0 ? 0 : 39 : column == 2 ? 10 : 49);
                card.Position = new Vector2(160 + column * 210, 80 + size * 250);
                panel.AddChild(card);
            }
        await Delay(0.1);
        await Capture("Trebol-y-pica.png");
        panel.QueueFree();
    }
    private static System.Collections.Generic.IEnumerable<Node> Descendants(Node root)
    { foreach (Node child in root.GetChildren()) { yield return child; foreach (Node descendant in Descendants(child)) yield return descendant; } }
    private static SnapshotDto Fixture(int count, int recipient) => new(NetworkProtocol.Version,
        new string((char)('a' + count), 32), 2, 2, recipient, GamePhase.InitialExchange, MatchResult.None, null, null, null,
        Enumerable.Range(1, count).Select(id => new PublicPlayerDto(id, "Jugador " + id, id - 1, 9, true, false)).ToArray(),
        Enumerable.Range((recipient - 1) * 9, 9).ToArray(), Array.Empty<MeldDto>(),
        52 - count * 9, 0, null, null, Array.Empty<RevealedHandDto>(), false);
}
