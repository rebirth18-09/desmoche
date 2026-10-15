using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Desmoche.Application;
using Desmoche.Networking.Core;
using Desmoche.Presentation;
using Godot;

namespace Desmoche.Diagnostics;

// Ventana real: incluye el stretch de project.godot, no solo el tamaño de Mesa.
public partial class ResponsiveLayoutSmoke : Main
{
    protected override bool ImmediateInitialization => true;
    private int _checks;
    private string _capture = "";
    private Mesa _mesa = null!;
    public override void _Ready()
    {
        base._Ready();
        _capture = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--capture="))?[10..] ?? "";
        _mesa = GetNode<Mesa>("Margin/Screens/Mesa");
        Callable.From(Run).CallDeferred();
    }

    private async void Run()
    {
        try
        {
            foreach (Vector2I resolution in new[] { new Vector2I(640, 360), new(844, 390),
                new(1280, 720), new(1920, 1080), new(2400, 1080), new(1024, 768), new(3440, 1440) })
            {
                GetWindow().Size = resolution;
                ReturnToStart();
                await Settle();
                var menu = GetNode<StartupMenu>("Margin/Screens/MenuPrincipal");
                CheckContained(menu.GetNode<Control>("Scroll/Center/Content/Buttons"), menu, "Menú");
                menu.GetNode<Button>("Scroll/Center/Content/Buttons/RulesButton").EmitSignal(BaseButton.SignalName.Pressed);
                await Settle();
                var helpScroll = menu.GetNode<ScrollContainer>("HelpOverlay/Scroll");
                Check(helpScroll.ScrollVertical == 0, "Ayuda comienza por el principio");
                Control help = menu.GetNode<Control>("HelpOverlay/Scroll/Center/Panel");
                CheckHorizontal(help, menu, "Ayuda no se corta horizontalmente");
                helpScroll.EnsureControlVisible(help.GetNode<Control>("Padding/Content/CloseButton"));
                await Settle();
                CheckContained(help.GetNode<Control>("Padding/Content/CloseButton"), helpScroll, "Volver de ayuda accesible");
                menu.GetNode<Button>("HelpOverlay/Scroll/Center/Panel/Padding/Content/CloseButton").EmitSignal(BaseButton.SignalName.Pressed);
                OpenSingleplayer();
                await Settle();
                var setup = GetNode<SingleplayerSetup>("Margin/Screens/Singleplayer");
                CheckHorizontal(setup.GetNode<Control>("Scroll/Center/Panel"), setup, "Opciones de bots");
                ReturnToStart(); OpenMultiplayer();
                await Settle();
                var lobby = GetNode<Lobby>("Margin/Screens/Lobby");
                CheckHorizontal(lobby.GetNode<Control>("Scroll/Center/Panel"), lobby, "Lobby");
                lobby.Hide(); _mesa.Show();
                GetNode<Control>("SalonBackground").Hide(); GetNode<Control>("MenuFrame").Hide();
                foreach (bool full in new[] { false, true })
                {
                    SnapshotDto fixture = Fixture(full);
                    string original = NetworkProtocol.Encode(fixture);
                    _mesa.DisplaySnapshot(fixture, "Partida de prueba");
                    _mesa.GetNode<ScrollContainer>("Scroll").ScrollVertical = 0;
                    await Settle();
                    ValidateTable();
                    Check(NetworkProtocol.Encode(fixture) == original, "Resolución no altera la vista autorizada");
                    if (full) await Capture("Mesa-" + resolution.X + "x" + resolution.Y + ".png");
                    _mesa.DisplaySnapshot(fixture with { Phase = GamePhase.Finished, Result = MatchResult.Draw,
                        Rematch = new RematchDto(new[] { 1, 2, 3, 4 }, new[] { 2 }, 15000, false) }, "Fin");
                    await Settle();
                    Control footer = _mesa.GetNode<Control>(Mesa.ContentPath + "TopBar");
                    CheckHorizontal(footer, _mesa, "Revancha dentro del ancho");
                    Control[] children = footer.GetChildren().OfType<Control>().Where(c => c.Visible).ToArray();
                    for (int a = 0; a < children.Length; a++)
                        for (int b = a + 1; b < children.Length; b++)
                            Check(!Overlap(children[a].GetGlobalRect(), children[b].GetGlobalRect()), "Revancha, contador y salida no se solapan");
                    var scroll = _mesa.GetNode<ScrollContainer>("Scroll");
                    scroll.EnsureControlVisible(_mesa.GetNode<Control>(Mesa.ContentPath + "TopBar/RematchButton"));
                    await Settle();
                    CheckContained(_mesa.GetNode<Control>(Mesa.ContentPath + "TopBar/RematchButton"), scroll, "Revancha accesible");
                }
                GD.Print("RESPONSIVE CASE: " + resolution + " viewport=" + GetViewportRect().Size);
            }
            GD.Print("RESPONSIVE LAYOUT PASS: Comprobaciones=" + _checks);
            GetTree().Quit();
        }
        catch (Exception e) { GD.PushError("RESPONSIVE LAYOUT FAIL: " + e); GetTree().Quit(1); }
    }

    private void ValidateTable()
    {
        var board = _mesa.GetNode<Control>(Mesa.ContentPath + "Material");
        CheckContained(board, _mesa, "Mesa completa");
        Vector2 scale = board.GetGlobalTransform().Scale;
        Check(Math.Abs(scale.X - scale.Y) < .001f, "Escala uniforme");
        Carta[] cards = Descendants(_mesa.GetNode<Control>(Mesa.ContentPath))
            .OfType<Carta>().Where(c => c.IsVisibleInTree() && c.FaceCardId is not null).ToArray();
        foreach (Carta card in cards) CheckContained(card, board, "Carta dentro de la mesa");
        Rect2 handTitle = _mesa.GetNode<Control>(Mesa.ContentPath + "HandZone/Padding/Content/HandHeader/TitleLabel").GetGlobalRect();
        foreach (Carta card in _mesa.GetNode<Control>(Mesa.ContentPath + "HandZone/Padding/Content/Cards").GetChildren().OfType<Carta>())
            Check(!Overlap(handTitle, card.GetGlobalRect()), "Título de mano no tapa las cartas");
        for (int a = 0; a < cards.Length; a++)
            for (int b = a + 1; b < cards.Length; b++)
                Check(!Overlap(cards[a].GetGlobalRect(), cards[b].GetGlobalRect()), "Cartas sin superposición");
        var actions = _mesa.GetNode<Control>(Mesa.ContentPath + "ContextActions");
        CheckContained(actions, _mesa, "Acciones integradas en la pantalla");
        Check(_mesa.GetNode<ScrollContainer>("Scroll").GetVScrollBar().MaxValue <= _mesa.Size.Y + 1,
            "No hay una segunda pantalla vacía debajo");
        foreach (Button button in actions.GetChildren().OfType<Button>().Where(b => b.Visible))
        {
            CheckHorizontal(button, _mesa, "Acción dentro del ancho");
            Check(button.Size.Y >= 44, "Acción con altura lógica mínima 44");
        }
        Control[] contextual = actions.GetChildren().OfType<Button>().Cast<Control>()
            .Concat(new Control[] { _mesa.GetNode<MesaActionClock>(Mesa.ChoiceClockPath),
                _mesa.GetNode<MesaActionClock>(Mesa.PreparationClockPath), _mesa.GetNode<MesaActionClock>(Mesa.PasaClockPath) })
            .Where(c => c.IsVisibleInTree()).ToArray();
        foreach (Control control in contextual)
        {
            CheckContained(control, board, "Control contextual dentro de la mesa");
            foreach (Carta card in cards) Check(!Overlap(control.GetGlobalRect(), card.GetGlobalRect()), "Control contextual no tapa cartas");
        }
        for (int a = 0; a < contextual.Length; a++) for (int b = a + 1; b < contextual.Length; b++)
            Check(!Overlap(contextual[a].GetGlobalRect(), contextual[b].GetGlobalRect()), "Controles contextuales no se solapan");
    }
    private void CheckHorizontal(Control child, Control parent, string message)
    {
        Rect2 a = child.GetGlobalRect(), b = parent.GetGlobalRect();
        Check(a.Position.X >= b.Position.X - 1 && a.End.X <= b.End.X + 1, message + " " + a + " padre=" + b);
    }
    private void CheckContained(Control child, Control parent, string message)
    {
        CheckHorizontal(child, parent, message);
        Rect2 a = child.GetGlobalRect(), b = parent.GetGlobalRect();
        Check(a.Position.Y >= b.Position.Y - 1 && a.End.Y <= b.End.Y + 1, message + " " + a + " padre=" + b);
    }
    private static bool Overlap(Rect2 a, Rect2 b) => a.Position.X < b.End.X - 1 && b.Position.X < a.End.X - 1 &&
        a.Position.Y < b.End.Y - 1 && b.Position.Y < a.End.Y - 1;
    private void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); _checks++; }
    private async Task Settle() { for (int i = 0; i < 12; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private async Task Capture(string file)
    {
        if (_capture.Length == 0) return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Directory.CreateDirectory(_capture);
        using Image image = GetViewport().GetTexture().GetImage();
        Check(image.SavePng(Path.Combine(_capture, file)) == Error.Ok, "Captura");
    }
    private static IEnumerable<Node> Descendants(Node root)
    { foreach (Node child in root.GetChildren()) { yield return child; foreach (Node sub in Descendants(child)) yield return sub; } }
    private static SnapshotDto Fixture(bool full)
    {
        int[] counts = full ? new[] { 10, 10, 10 } : new[] { 9, 7, 2 };
        MeldDto[] melds = Enumerable.Range(0, 3).Select(i => new MeldDto(i + 1, i + 2, Enumerable.Range(i * 10, counts[i]).ToArray())).ToArray();
        return new SnapshotDto(NetworkProtocol.Version, new string('a', 32), 1, 1, 1,
            GamePhase.AwaitingClaims, MatchResult.None, 2, null, null,
            Enumerable.Range(1, 4).Select(i => new PublicPlayerDto(i, "Jugador " + i, i - 1, i == 1 ? 9 : Math.Max(0, 9 - counts[i - 2]), true, false)).ToArray(),
            Enumerable.Range(30, 9).ToArray(), melds, 52 - counts.Sum() - 11, 1, 50, 51,
            Array.Empty<RevealedHandDto>(), false,
            OfferTiming: new OfferTimingDto(51, 5000, Array.Empty<CardReservationDto>(), false));
    }
}
