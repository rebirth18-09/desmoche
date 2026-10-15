using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Desmoche.Application;
using Desmoche.Networking.Core;
using Desmoche.Presentation;
using Godot;

namespace Desmoche.Diagnostics;

// GUI nativa aislada: posición elevada, clicks, arrastre, resizes y snapshot.
public partial class HandSelectionSmoke : Control
{
    private SubViewport _viewport = null!;
    private Mesa _mesa = null!;
    private SnapshotDto _view = null!;
    private string _capture = "";
    private int _checks;
    private const string HandPath = Mesa.ContentPath + "HandZone/Padding/Content/Cards";

    public override void _Ready()
    {
        _capture = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--capture="))?[10..] ?? "";
        _viewport = new SubViewport { Name = "SelectionViewport", Size = new Vector2I(1600, 900),
            HandleInputLocally = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(_viewport);
        _mesa = GD.Load<PackedScene>("res://Scenes/Mesa.tscn").Instantiate<Mesa>();
        _viewport.AddChild(_mesa);
        var render = new TextureRect { Texture = _viewport.GetTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore };
        AddChild(render); render.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _view = new SnapshotDto(NetworkProtocol.Version, new string('e', 32), 1, 2, 1,
            GamePhase.InitialDeclarations, MatchResult.None, null, null, null,
            new[] { new PublicPlayerDto(1, "Tú", 0, 9, true, false), new PublicPlayerDto(2, "Rival", 1, 9, true, false) },
            new[] { 2, 0, 1, 13, 26, 39, 10, 11, 12 }, Array.Empty<MeldDto>(), 34, 0, null, null,
            Array.Empty<RevealedHandDto>(), false);
        _mesa.DisplaySnapshot(_view, "Prueba de selección");
        Callable.From(Run).CallDeferred();
    }

    private async void Run()
    {
        try
        {
            if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("Este diagnóstico requiere render real.");
            await Settle();
            string before = NetworkProtocol.Encode(_view);
            int[] order = _mesa.LocalHandOrder;
            float originalY = Card(2).GetGlobalRect().Position.Y;
            await Click(Card(2));
            Check(Card(2).Selected && Card(2).OffsetTransformPositionRatio.Y < -.179f, "Click eleva una carta");
            Check(Card(2).GetGlobalRect().Position.Y < originalY - 4, "La geometría sigue la elevación");
            Check(!Card(2).HasGameIndicator, "Una carta no se marca verde");
            await Click(Card(0));
            Check(Selected() == 2 && !Hand().Any(c => c.HasGameIndicator), "Dos cartas solo se elevan");
            await Click(Card(1));
            Check(Selected() == 3 && Hand().Where(c => c.Selected).All(c => c.HasGameIndicator), "Escalera desordenada verde");
            Check(_mesa.LocalHandOrder.SequenceEqual(order), "La selección no ordena cartas");
            var green = (StyleBoxFlat)Card(1).GetThemeStylebox("panel");
            Check(green.BorderColor.G > green.BorderColor.R && green.BorderWidthTop >= 3, "Contorno verde visible");
            var continueButton = _mesa.GetNode<Button>(Mesa.ContextActionsPath + "ContinueButton");
            await ClickAt(continueButton.GetGlobalTransformWithCanvas() * (continueButton.Size / 2));
            Check(Selected() == 3 && Card(1).HasGameIndicator, "Pulsar un botón no suelta la selección");
            await Capture("Escalera-verde.png");
            await Click(Card(13));
            Check(Selected() == 4 && Hand().Where(c => c.Selected).All(c => !c.HasGameIndicator), "Carta sobrante quita el verde");
            Check(Hand().Where(c => c.Selected).All(c => c.OffsetTransformPositionRatio.Y < -.179f), "Selección inválida sigue elevada");
            await Capture("Seleccion-sin-juego.png");
            await Click(Card(13));
            Check(Card(13).OffsetTransformPositionRatio == Vector2.Zero && Card(1).HasGameIndicator, "Quitar carta restaura el verde");

            // Toca la porción que sobresale de la fila original.
            Vector2 raisedTop = Card(2).GetGlobalTransformWithCanvas() * new Vector2(Card(2).Size.X / 2, 3);
            await ClickAt(raisedTop);
            Check(!Card(2).Selected && Selected() == 2, "La parte elevada recibe el click");
            await ClearOnBlank();
            Check(Hand().All(c => !c.Selected && !c.HasGameIndicator && c.OffsetTransformPositionRatio == Vector2.Zero), "Espacio vacío baja toda la selección");
            foreach (int id in new[] { 26, 0, 13 }) await Click(Card(id));
            Check(Hand().Where(c => c.Selected).All(c => c.HasGameIndicator), "Tercia desordenada verde");
            await Click(Card(39));
            Check(Selected() == 4 && Hand().Where(c => c.Selected).All(c => c.HasGameIndicator), "Cuarta verde");
            _viewport.Size = new Vector2I(844, 390);
            await Settle();
            Check(Selected() == 4 && Hand().Where(c => c.Selected).All(c => c.OffsetTransformPositionRatio.Y < -.179f && c.HasGameIndicator), "Resize conserva elevación y verde");
            await Capture("Seleccion-movil.png");
            await ClearOnBlank();
            foreach (int id in new[] { 0, 12, 11 }) await Click(Card(id));
            Check(Hand().Where(c => c.Selected).All(c => c.HasGameIndicator), "As alto verde");
            await ClearOnBlank();
            await Click(Card(0));
            Vector2 from = Card(0).GetGlobalTransformWithCanvas() * (Card(0).Size / 2);
            Carta target = Card(12);
            Vector2 to = target.GetGlobalTransformWithCanvas() * new Vector2(target.Size.X - 3, target.Size.Y / 2);
            Feed(new InputEventMouseButton { Position = from, GlobalPosition = from, ButtonIndex = MouseButton.Left, ButtonMask = MouseButtonMask.Left, Pressed = true });
            await Frame();
            Feed(new InputEventMouseMotion { Position = to, GlobalPosition = to, Relative = to - from, ButtonMask = MouseButtonMask.Left });
            await Frame();
            Check(_viewport.GuiIsDragging(), "Carta elevada inicia arrastre");
            Feed(new InputEventMouseMotion { Position = to, GlobalPosition = to, ButtonMask = MouseButtonMask.Left });
            await Frame();
            Feed(new InputEventMouseButton { Position = to, GlobalPosition = to, ButtonIndex = MouseButton.Left, Pressed = false });
            await Settle();
            Check(!_viewport.GuiIsDragging() && _mesa.LocalHandOrder.Last() == 0, "Arrastre elevado reordena sin click adicional");
            Check(NetworkProtocol.Encode(_view) == before, "Los gestos conservan la vista autoritativa");
            _mesa.DisplaySnapshot(_view with { Window = 3, Phase = GamePhase.AwaitingDraw, CurrentPlayerId = 1 }, "Nueva fase");
            await Settle();
            Check(Hand().All(c => !c.Selected && !c.HasGameIndicator && c.OffsetTransformPositionRatio == Vector2.Zero), "Cambiar fase limpia el indicio y la elevación");
            _mesa.DisplaySnapshot(null, "Salir"); await Settle();
            Check(Hand().Length == 0, "Salir elimina cartas y animaciones");
            GD.Print("HAND SELECTION PASS: Comprobaciones=" + _checks);
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError("HAND SELECTION FAIL: " + error); GetTree().Quit(1); }
    }
    private Carta Card(int id) => _mesa.GetNode<Carta>(HandPath + "/Carta" + id);
    private Carta[] Hand() => _mesa.GetNode<Control>(HandPath).GetChildren().OfType<Carta>().ToArray();
    private int Selected() => Hand().Count(c => c.Selected);
    private async Task Click(Carta card) => await ClickAt(card.GetGlobalTransformWithCanvas() * (card.Size / 2));
    private async Task ClickAt(Vector2 position)
    {
        Feed(new InputEventMouseMotion { Position = position, GlobalPosition = position }); await Frame();
        Feed(new InputEventMouseButton { Position = position, GlobalPosition = position, ButtonIndex = MouseButton.Left, ButtonMask = MouseButtonMask.Left, Pressed = true }); await Frame();
        Feed(new InputEventMouseButton { Position = position, GlobalPosition = position, ButtonIndex = MouseButton.Left, Pressed = false });
        await Settle();
    }
    private async Task ClearOnBlank()
    {
        Control canvas = _mesa.GetNode<Control>(Mesa.ContentPath.TrimEnd('/'));
        await ClickAt(canvas.GetGlobalTransformWithCanvas() * new Vector2(220, 360));
    }
    private void Feed(InputEventMouse input) => _viewport.PushInput(input, true);
    private async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    private async Task Settle()
    { await Frame(); await Frame(); await ToSignal(GetTree().CreateTimer(.22), Timer.SignalName.Timeout); }
    private void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); _checks++; }
    private async Task Capture(string name)
    {
        if (_capture.Length == 0) return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Directory.CreateDirectory(_capture);
        using Image image = _viewport.GetTexture().GetImage();
        Check(image.SavePng(Path.Combine(_capture, name)) == Error.Ok, "Captura");
    }
}
