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

public partial class HandDragSmoke : Control
{
    private SubViewport _viewport = null!;
    private Mesa _mesa = null!;
    private SnapshotDto _view = null!;
    private string _capture = "";
    private int _checks;
    private const string HandPath = Mesa.ContentPath + "HandZone/Padding/Content/Cards";
    private static readonly int[] InitialOrder = { 0, 1, 2, 13, 26, 39, 10, 11, 12 };

    public override void _Ready()
    {
        _capture = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--capture="))?[10..] ?? "";
        _viewport = new SubViewport { Name = "HandDragViewport", Size = new Vector2I(1280, 720),
            HandleInputLocally = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always }; AddChild(_viewport);
        _mesa = GD.Load<PackedScene>("res://Scenes/Mesa.tscn").Instantiate<Mesa>(); _viewport.AddChild(_mesa);
        var render = new TextureRect { Texture = _viewport.GetTexture(), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(render); render.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _view = new SnapshotDto(NetworkProtocol.Version, new string('a', 32), 1, 2, 1,
            GamePhase.InitialDeclarations, MatchResult.None, null, null, null,
            new[] { new PublicPlayerDto(1, "Tú", 0, 9, true, false), new PublicPlayerDto(2, "Rival", 1, 9, true, false) },
            InitialOrder.ToArray(), Array.Empty<MeldDto>(), 34, 0, null, null, Array.Empty<RevealedHandDto>(), false);
        Callable.From(Run).CallDeferred();
    }

    private async void Run()
    {
        try
        {
            if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("Requiere render real.");
            foreach (Vector2I resolution in new[] { new Vector2I(640, 360), new(844, 390), new(1280, 720), new(1920, 1080),
                new(2400, 1080), new(1024, 768), new(3440, 1440) })
            {
                _viewport.Size = resolution; await Reset();
                string authority = NetworkProtocol.Encode(_view);
                var objects = Cards().ToDictionary(c => c.FaceCardId!.Value);
                Vector2[] slots = Centers(); Carta source = Card(0);
                await Begin(source, slots[0], slots[4]);
                Check(_viewport.GuiIsDragging(), "Arrastre nativo activo");
                Check(source.Modulate.A == 0, "El origen no sigue dibujado en su posición anterior");
                Panel marker = _mesa.GetNode<Panel>(Mesa.HandDragSlotPath);
                Check(marker.Visible && marker.MouseFilter == MouseFilterEnum.Ignore, "Hueco dorado visible sin interceptar el gesto");
                Check(marker.GetGlobalRect().GetCenter().DistanceTo(slots[4]) < 3, "El hueco coincide con el destino bajo el cursor");
                Check(_mesa.LocalHandOrder.SequenceEqual(InitialOrder), "La previsualización no confirma el orden antes de soltar");
                Carta preview = Descendants(_viewport).OfType<Carta>().Single(c => c.FaceCardId == 0 && c != source);
                Check(preview.GetGlobalRect().GetCenter().DistanceTo(slots[4]) < 3,
                    "La carta visible sigue el cursor: " + preview.GetGlobalRect() + " destino=" + slots[4]);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using (Image rendered = _viewport.GetTexture().GetImage())
                {
                    Rect2 rect = preview.GetGlobalRect(); Vector2 sample = rect.Position + rect.Size * new Vector2(.85f, .12f);
                    Color pixel = rendered.GetPixel((int)sample.X, (int)sample.Y);
                    Check(pixel.R > .7f && pixel.G > .7f && pixel.B > .65f, "La carta flotante se renderiza aunque su origen esté oculto");
                }
                await Settle();
                Check(Card(26).OffsetTransformPosition.X < -1 && Card(1).OffsetTransformPosition.X < -1,
                    "Las vecinas se deslizan para abrir el espacio señalado");
                Check(Cards().All(c => ReferenceEquals(objects[c.FaceCardId!.Value], c)), "El hover reutiliza todos los nodos");
                if (resolution == new Vector2I(1280, 720)) await Capture("Arrastre-hueco-dorado-1280x720.png");
                if (resolution == new Vector2I(844, 390)) await Capture("Arrastre-hueco-dorado-844x390.png");
                await Release(slots[4]);
                int[] expected = InitialOrder.Where(id => id != 0).ToList().InsertAt(4, 0);
                Check(_mesa.LocalHandOrder.SequenceEqual(expected) && !marker.Visible && source.Modulate.A == 1,
                    "Soltar confirma exactamente el lugar marcado y restaura la carta");
                Check(Cards().Select(c => c.FaceCardId!.Value).SequenceEqual(expected) &&
                    Cards().All(c => ReferenceEquals(objects[c.FaceCardId!.Value], c)), "El orden visual cambia sin reconstruir cartas");
                Check(Cards().All(c => c.OffsetTransformPosition.IsZeroApprox()), "El aterrizaje termina sin desplazamientos residuales");
                Check(source.Selected && source.OffsetTransformPositionRatio.Y < -.179f, "La selección elevada se conserva después de ordenar");
                Check(NetworkProtocol.Encode(_view) == authority, "El arrastre no modifica el snapshot ni cantidades");

                // De extremo a extremo y retorno a la primera posición.
                await Reset(); slots = Centers(); source = Card(12);
                await Begin(source, slots[^1], slots[0]); await Release(slots[0]);
                Check(_mesa.LocalHandOrder.SequenceEqual(new[] { 12 }.Concat(InitialOrder.Where(id => id != 12))),
                    "La última carta se puede colocar al principio");
                slots = Centers(); source = Card(12); Vector2 at = source.GetGlobalRect().GetCenter();
                await Begin(source, at, slots[^1]); await Release(slots[^1]);
                Check(_mesa.LocalHandOrder.SequenceEqual(InitialOrder), "También se puede insertar al final");

                await Reset(); slots = Centers(); source = Card(13);
                await Begin(source, slots[3], slots[6]);
                Feed(new InputEventKey { Pressed = true, Keycode = Key.Escape }); await Settle();
                Check(!_viewport.GuiIsDragging() && _mesa.LocalHandOrder.SequenceEqual(InitialOrder) &&
                    !marker.Visible && Cards().All(c => c.Modulate.A == 1 && c.OffsetTransformPosition.IsZeroApprox()),
                    "Escape devuelve todas las cartas y no confirma el destino");

                await Reset(); slots = Centers(); source = Card(13);
                await Begin(source, slots[3], slots[5]);
                Vector2 outside = CanvasPoint(new Vector2(240, 360));
                await Motion(outside); await Settle();
                Check(!marker.Visible && Cards().All(c => c.OffsetTransformPosition.IsZeroApprox()),
                    "Fuera de la mano desaparece el destino y las vecinas regresan");
                await Release(outside);
                Check(_mesa.LocalHandOrder.SequenceEqual(InitialOrder) && Cards().All(c => c.Modulate.A == 1),
                    "Soltar en vacío cancela la reorganización");

                await Reset(); await Click(Card(1)); await Click(Card(2));
                slots = Centers(); source = Card(0);
                await Begin(source, source.GetGlobalRect().GetCenter(), slots[5]); await Settle();
                preview = Descendants(_viewport).OfType<Carta>().Single(c => c.FaceCardId == 0 && c != source);
                Check(preview.HasGameIndicator && new[] { 0, 1, 2 }.All(id => Card(id).HasGameIndicator),
                    "El arrastre mantiene el verde de una selección válida");
                await Release(slots[5]);
                Check(new[] { 0, 1, 2 }.All(id => Card(id).HasGameIndicator && Card(id).Selected),
                    "Aterrizar no borra la selección ni su indicio de juego");

                await Reset(); slots = Centers(); source = Card(0);
                await Begin(source, slots[0], slots[4]);
                _mesa.DisplaySnapshot(_view with { Window = 3, Phase = GamePhase.AwaitingDraw, CurrentPlayerId = 1 }, "Cambio de fase");
                await Settle();
                Check(!_viewport.GuiIsDragging() && !marker.Visible && Cards().All(c => c.Modulate.A == 1 && c.OffsetTransformPosition.IsZeroApprox()),
                    "Un cambio de contexto cancela el gesto y limpia las animaciones");
                GD.Print("HAND DRAG CASE: " + resolution);
            }

            await ValidateTableEditing();
            await ValidatePaymentPreparation();

            // El destino de juegos se conserva: salir de la mano permite preparar.
            _viewport.Size = new Vector2I(1280, 720); await Reset();
            Vector2[] lastSlots = Centers(); Carta draftSource = Card(0);
            await Begin(draftSource, lastSlots[0], lastSlots[4]);
            Control game = _mesa.GetNode<Control>(Mesa.OwnGamesPath);
            Vector2 dropGame = game.GetGlobalRect().GetCenter(); await Motion(dropGame); await Release(dropGame);
            Check(_mesa.LocalDraftCount == 1 && _mesa.LocalHandOrder.SequenceEqual(InitialOrder) && draftSource.Modulate.A == 1,
                "Arrastrar a un juego prepara un borrador sin perder ni mover cartas de la mano");
            await Reset(); lastSlots = Centers();
            await Begin(Card(0), lastSlots[0], lastSlots[4]); _viewport.Size = new Vector2I(844, 390); await Settle();
            Check(!_viewport.GuiIsDragging() && !_mesa.GetNode<Control>(Mesa.HandDragSlotPath).Visible &&
                Cards().All(c => c.Modulate.A == 1 && c.OffsetTransformPosition.IsZeroApprox()), "Redimensionar cancela sin dejar cartas invisibles");
            await Reset(); lastSlots = Centers(); await Begin(Card(0), lastSlots[0], lastSlots[4]);
            _mesa.DisplaySnapshot(null, "Salir"); await Settle();
            Check(!_viewport.GuiIsDragging() && Cards().Length == 0 && !_mesa.GetNode<Control>(Mesa.HandDragSlotPath).Visible,
                "Salir limpia el preview, el hueco y las cartas");
            _viewport.QueueFree(); _mesa = null!; _viewport = null!;
            await Frame(); await Frame(); await Frame(); GC.Collect(); GC.WaitForPendingFinalizers(); await Frame();
            GD.Print("HAND DRAG PASS: Comprobaciones=" + _checks); GetTree().Quit();
        }
        catch (Exception e)
        {
            GD.PushError("HAND DRAG FAIL: " + e);
            _viewport?.QueueFree(); _mesa = null!; _viewport = null!;
            await Frame(); await Frame(); await Frame(); GC.Collect(); GC.WaitForPendingFinalizers(); await Frame();
            GetTree().Quit(1);
        }
    }
    private async Task Reset()
    {
        // Cada caso empieza una sesión distinta: una actualización ordinaria
        // conserva deliberadamente el orden que el jugador haya elegido.
        _view = _view with { Session = Guid.NewGuid().ToString("N") };
        _mesa.DisplaySnapshot(_view, "Prueba de arrastre"); await Settle();
    }
    private Carta Card(int id) => _mesa.GetNode<Carta>(HandPath + "/Carta" + id);
    private Carta[] Cards() => _mesa.GetNode<Control>(HandPath).GetChildren().OfType<Carta>().ToArray();
    private Vector2[] Centers() => Cards().Select(c => _mesa.GetNode<Control>(HandPath).GetGlobalTransformWithCanvas() * (c.Position + c.Size / 2)).ToArray();
    private Vector2 CanvasPoint(Vector2 point) => _mesa.GetNode<Control>(Mesa.ContentPath.TrimEnd('/')).GetGlobalTransformWithCanvas() * point;
    private void Feed(InputEvent input) => _viewport.PushInput(input, true);
    private async Task Motion(Vector2 point)
    { Feed(new InputEventMouseMotion { Position = point, GlobalPosition = point, ButtonMask = MouseButtonMask.Left }); await Frame(); }
    private async Task Begin(Carta source, Vector2 start, Vector2 end)
    {
        Feed(new InputEventMouseMotion { Position = start, GlobalPosition = start }); await Frame();
        Feed(new InputEventMouseButton { Position = start, GlobalPosition = start, ButtonIndex = MouseButton.Left, ButtonMask = MouseButtonMask.Left, Pressed = true }); await Frame();
        Vector2 next = start.Lerp(end,.18f);
        Feed(new InputEventMouseMotion { Position = next, GlobalPosition = next, Relative = next - start, ButtonMask = MouseButtonMask.Left }); await Frame();
        for (int i = 2; i <= 8; i++) await Motion(start.Lerp(end,i/8f));
        await Frame();
    }
    private async Task Release(Vector2 point)
    { Feed(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = false }); await Settle(); }
    private async Task Click(Control control)
    {
        Vector2 point = control.GetGlobalRect().GetCenter();
        Feed(new InputEventMouseMotion { Position = point, GlobalPosition = point }); await Frame();
        Feed(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, ButtonMask = MouseButtonMask.Left, Pressed = true }); await Frame();
        await Release(point);
    }
    private async Task Frame() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    private async Task Settle() { await Frame(); await Frame(); await ToSignal(GetTree().CreateTimer(.25), Timer.SignalName.Timeout); }
    private static IEnumerable<Node> Descendants(Node node)
    { foreach (Node child in node.GetChildren()) { yield return child; foreach (Node next in Descendants(child)) yield return next; } }
    private void Check(bool valid, string text) { if (!valid) throw new InvalidOperationException(text); _checks++; }
    private async Task Capture(string name)
    {
        if (_capture.Length == 0) return;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Directory.CreateDirectory(_capture); using Image image = _viewport.GetTexture().GetImage();
        Check(image.SavePng(Path.Combine(_capture, name)) == Error.Ok, "Captura de arrastre");
    }
}

internal static class HandDragTestList
{
    public static int[] InsertAt(this List<int> cards, int index, int id)
    { cards.Insert(index,id); return cards.ToArray(); }
}
