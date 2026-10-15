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

public partial class TableActionsSmoke : Control
{
    private sealed class PreviewSession : IGameSession
    {
        public bool IsActive => true;
        public bool IsLocal => false;
        public bool CanManageMatch => false;
        public SnapshotDto? View { get; private set; }
        public string Status => "Prueba de acciones en mesa";
        public event Action<SnapshotDto>? ViewChanged;
        public event Action<ReplyDto>? ActionCompleted;
        public event Action? SessionChanged;
        public ActionKind? LastKind { get; private set; }
        public long Requests { get; private set; }
        public PreviewSession(SnapshotDto view) { View = view; }
        public long Submit(ActionKind kind, int? cardId = null, PlayDto[]? plays = null,
            bool declareWin = false, InitialVictoryKind? initialKind = null, int[]? cards = null, int? sourceMeldId = null)
        { LastKind = kind; return ++Requests; }
        public void Publish(SnapshotDto view) { View = view; ViewChanged?.Invoke(view); }
        public void Accept(SnapshotDto view)
        { Publish(view); ActionCompleted?.Invoke(new ReplyDto(Requests, true, "Confirmado", false, view.Revision)); }
        public void Close(string reason = "") { SessionChanged?.Invoke(); }
    }
    private SubViewport _viewport = null!;
    private Mesa _mesa = null!;
    private int _checks;
    private string _capture = "";

    public override void _Ready()
    {
        _capture = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--capture="))?[10..] ?? "";
        _viewport = new SubViewport { Size = new Vector2I(1280, 720), HandleInputLocally = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always }; AddChild(_viewport);
        _mesa = GD.Load<PackedScene>("res://Scenes/Mesa.tscn").Instantiate<Mesa>(); _viewport.AddChild(_mesa);
        var rendered = new TextureRect { Texture = _viewport.GetTexture(), MouseFilter = MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale };
        AddChild(rendered); rendered.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Callable.From(Run).CallDeferred();
    }
    private Button Button(string name) => _mesa.GetNode<Button>(Mesa.ContextActionsPath + name);
    private MesaActionClock Clock(string path) => _mesa.GetNode<MesaActionClock>(path);
    private Carta Card(int id) => _mesa.GetNode<Control>(Mesa.ContentPath + "HandZone/Padding/Content/Cards")
        .GetChildren().OfType<Carta>().Single(c => c.FaceCardId == id);
    private async void Run()
    {
        try
        {
            foreach (Vector2I size in new[] { new Vector2I(640,360), new(844,390), new(1280,720), new(1920,1080),
                new(2400,1080), new(1024,768), new(3440,1440) })
            {
                _viewport.Size = size;
                SnapshotDto fixture = Fixture(); var session = new PreviewSession(fixture); _mesa.Bind(session); await Settle();
                string before = NetworkProtocol.Encode(fixture);
                Check(Clock(Mesa.ChoiceClockPath).Visible && Clock(Mesa.ChoiceClockPath).SecondsRemaining is > 0 and <= 5,
                    "Oferta muestra cinco segundos en círculo: visible="+Clock(Mesa.ChoiceClockPath).Visible+" segundos="+Clock(Mesa.ChoiceClockPath).SecondsRemaining);
                Adjacent(Button("ChooseOfferButton"), Clock(Mesa.ChoiceClockPath)); ValidateGeometry("oferta");
                Check(Button("ChooseOfferButton").GetGlobalRect().GetCenter().DistanceTo(
                    _mesa.GetNode<Carta>(Mesa.CenterPath+"Oferta/Content/Carta").GetGlobalRect().GetCenter()) <
                    Math.Max(220,_mesa.Size.X*.14f),"Tomar permanece cerca de la carta ofrecida");
                if (size == new Vector2I(1280,720) || size == new Vector2I(844,390)) await Capture("Oferta-"+size.X+"x"+size.Y+".png");
                await Click(Button("ChooseOfferButton"));
                Check(session.LastKind == ActionKind.ChooseOffer, "Tomar junto a la carta conserva su intención");
                SnapshotDto reserved = fixture with { Revision = 2, OfferTiming = fixture.OfferTiming! with {
                    Reservations = new[] { new CardReservationDto(1,20000) } } };
                session.Accept(reserved); await Settle();
                Check(!Button("ChooseOfferButton").Visible && !Clock(Mesa.ChoiceClockPath).Visible &&
                    Clock(Mesa.PreparationClockPath).Visible && Clock(Mesa.PreparationClockPath).SecondsRemaining is > 0 and <= 20,
                    "Reserva sustituye el círculo de elección por el de preparación");
                await Click(Card(26));
                // La oferta se marca al confirmar la reserva; A de mano + A de
                // juego publicado prepara el trasmoche completo existente.
                var donor = _mesa.GetNode<Control>(Mesa.OwnGamesPath+"/Juego42").GetChildren().OfType<Carta>().Single(c=>c.FaceCardId==0);
                await Click(donor); await Settle();
                Check(Button("ClaimButton").Visible, "La confirmación aparece al preparar un juego con oferta");
                Adjacent(Button("ClaimButton"), Clock(Mesa.PreparationClockPath)); ValidateGeometry("preparación");
                if (size == new Vector2I(1280,720) || size == new Vector2I(844,390)) await Capture("Preparacion-"+size.X+"x"+size.Y+".png");
                Check(NetworkProtocol.Encode(fixture)==before, "Preparación y círculos no modifican la vista original");
                await Click(Button("ClaimButton")); Check(session.LastKind==ActionKind.Claim,"Confirmar en Mi juego conserva el reclamo");

                SnapshotDto challenge = Fixture() with { Phase=GamePhase.AwaitingOrderChallenge, OfferTiming=null,
                    OrderChallenge=new OrderChallengeDto(2,1,3000), Melds=new[] { new MeldDto(1,2,new[] { 10,23,36 }) }, DeckCount=34 };
                session.Publish(challenge); await Settle();
                Check(Clock(Mesa.PasaClockPath).Visible && Clock(Mesa.PasaClockPath).SecondsRemaining is > 0 and <= 3,"Pasa muestra tres segundos");
                Adjacent(_mesa.GetNode<Button>(Mesa.PasaButtonPath), Clock(Mesa.PasaClockPath)); ValidateGeometry("Pasa");
                if (size==new Vector2I(1280,720)) await Capture("Pasa-1280x720.png");
                session.Accept(challenge with { Revision=3 }); await Settle();
                await Click(_mesa.GetNode<Button>(Mesa.PasaButtonPath)); Check(session.LastKind==ActionKind.SayPasa,"Pasa junto al círculo llega a la sesión");

                SnapshotDto discard = Fixture() with { Phase=GamePhase.AwaitingDraw, CurrentPlayerId=1, OfferedCard=null,
                    OfferTiming=null, DiscardCount=1, DiscardTop=13, DiscardTiming=new DiscardTimingDto(1,13,5000) };
                session.Accept(discard); await Settle();
                Adjacent(Button("ChooseDiscardButton"),Clock(Mesa.ChoiceClockPath)); ValidateGeometry("pozo");
                await Click(Button("ChooseDiscardButton")); Check(session.LastKind==ActionKind.ChooseDiscard,"Elegir pozo mantiene su intención");
                SnapshotDto payment = Fixture() with { Phase=GamePhase.AwaitingPayment, PaymentPlayerId=1, OfferedCard=null, OfferTiming=null };
                session.Accept(payment); await Settle();
                var zone=_mesa.GetNode<CardDropZone>(Mesa.CenterPath+"Pago");
                zone._DropData(Vector2.Zero, CardDragData.Create(payment,26,CardDragOrigin.Hand)); await Settle();
                Check(Button("ConfirmCardButton").Visible && Button("CancelCardButton").Visible,"Aceptar y Cambiar acompañan al pago preparado");
                ValidateGeometry("pago");
                if (size==new Vector2I(1280,720)) await Capture("Pago-1280x720.png");
                await Click(Button("ConfirmCardButton")); Check(session.LastKind==ActionKind.Pay,"Aceptar en la mesa conserva el pago");
                _mesa.Unbind(); GD.Print("TABLE ACTIONS CASE: "+size);
            }
            _viewport.Size=new Vector2I(1280,720);
            var expired=new PreviewSession(Fixture() with { OfferTiming=new OfferTimingDto(13,650,Array.Empty<CardReservationDto>(),false) });
            _mesa.Bind(expired); await Settle();
            string state=NetworkProtocol.Encode(expired.View!);
            await ToSignal(GetTree().CreateTimer(.9),Timer.SignalName.Timeout);
            Check(Clock(Mesa.ChoiceClockPath).SecondsRemaining==0 && Button("ChooseOfferButton").Disabled,
                "Al llegar a cero, el círculo deshabilita elección sin resolver la partida");
            Check(expired.Requests==0 && NetworkProtocol.Encode(expired.View!)==state,"El reloj visual no genera solicitudes ni cambia autoridad");
            var custom = new MatchSettings(12, 60, 10, 30);
            var configurable = new PreviewSession(Fixture() with { Settings=custom,
                OfferTiming=new OfferTimingDto(13,6000,Array.Empty<CardReservationDto>(),false) });
            _mesa.Bind(configurable); await Settle();
            Check(Clock(Mesa.ChoiceClockPath).RemainingFraction is > .40f and < .51f,
                "El aro de elección mide la fracción de los 12 s configurados");
            configurable.Publish(configurable.View! with { Revision=2, OfferTiming=new OfferTimingDto(13,0,
                new[] { new CardReservationDto(1,30000) },false) }); await Settle();
            Check(Clock(Mesa.PreparationClockPath).RemainingFraction is > .45f and < .51f,
                "El aro de preparación usa 60 s en lugar de 20");
            configurable.Publish(configurable.View! with { Revision=3, Phase=GamePhase.AwaitingOrderChallenge,
                OfferTiming=null, OfferedCard=null, OrderChallenge=new OrderChallengeDto(2,1,5000),
                Melds=new[] { new MeldDto(1,2,new[] {10,23,36}) } }); await Settle();
            Check(Clock(Mesa.PasaClockPath).RemainingFraction is > .40f and < .51f,
                "El aro de Pasa usa 10 s en lugar de 3");
            _mesa.Unbind(); await Cleanup();
            GD.Print("TABLE ACTIONS PASS: Comprobaciones="+_checks); GetTree().Quit();
        }
        catch(Exception e) { GD.PushError("TABLE ACTIONS FAIL: "+e); await Cleanup(); GetTree().Quit(1); }
    }
    private void ValidateGeometry(string phase)
    {
        Control board=_mesa.GetNode<Control>(Mesa.ContentPath+"Material");
        Control[] controls=_mesa.GetNode<Control>(Mesa.ContextActionsPath.TrimEnd('/')).GetChildren().OfType<Button>()
            .Cast<Control>().Concat(new Control[] { Clock(Mesa.ChoiceClockPath),Clock(Mesa.PreparationClockPath),Clock(Mesa.PasaClockPath),
                _mesa.GetNode<Button>(Mesa.PasaButtonPath) }).Where(c=>c.IsVisibleInTree()).ToArray();
        foreach(Control control in controls)
        {
            Check(board.GetGlobalRect().Grow(1).Encloses(control.GetGlobalRect()),phase+": "+control.Name+" dentro de la mesa "+control.GetGlobalRect());
            Check(control.GetGlobalRect().Size.Y>=43.9f,phase+": tamaño legible y pulsable");
            foreach(Carta card in Descendants(_mesa).OfType<Carta>().Where(c=>c.IsVisibleInTree()))
            {
                Rect2 rect=card.GetGlobalRect();
                for(Node? p=card.GetParent();p is not null;p=p.GetParent())
                    if(p is Control { ClipContents:true } clip) rect=rect.Intersection(clip.GetGlobalRect());
                if(rect.Size.X>0 && rect.Size.Y>0)
                    Check(!Overlap(control.GetGlobalRect(),rect),phase+": "+control.Name+" no tapa carta "+card.FaceCardId);
            }
        }
        for(int a=0;a<controls.Length;a++) for(int b=a+1;b<controls.Length;b++)
            Check(!Overlap(controls[a].GetGlobalRect(),controls[b].GetGlobalRect()),phase+": controles y círculos no se solapan");
    }
    private void Adjacent(Control button,Control clock) => Check(
        Math.Abs(button.GetGlobalRect().GetCenter().Y-clock.GetGlobalRect().GetCenter().Y)<1 &&
        Math.Abs(clock.GetGlobalRect().Position.X-button.GetGlobalRect().End.X-8)<1,"Círculo al lado de "+button.Name);
    private static bool Overlap(Rect2 a,Rect2 b)=>a.Position.X<b.End.X-1 && b.Position.X<a.End.X-1 && a.Position.Y<b.End.Y-1 && b.Position.Y<a.End.Y-1;
    private static IEnumerable<Node> Descendants(Node n) { foreach(Node c in n.GetChildren()) { yield return c; foreach(Node d in Descendants(c)) yield return d; } }
    private void Check(bool okay,string message) { if(!okay) throw new InvalidOperationException(message); _checks++; }
    private async Task Settle() { for(int i=0;i<10;i++) await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame); }
    private async Task Click(Control c)
    {
        Check(c.IsVisibleInTree() && (c is not BaseButton b || !b.Disabled),"Control activo: "+c.Name);
        Vector2 p=c.GetGlobalTransformWithCanvas()*(c.Size/2);
        _viewport.PushInput(new InputEventMouseMotion { Position=p,GlobalPosition=p },true); await Settle();
        _viewport.PushInput(new InputEventMouseButton { Position=p,GlobalPosition=p,ButtonIndex=MouseButton.Left,ButtonMask=MouseButtonMask.Left,Pressed=true },true);
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        _viewport.PushInput(new InputEventMouseButton { Position=p,GlobalPosition=p,ButtonIndex=MouseButton.Left,Pressed=false },true); await Settle();
    }
    private async Task Capture(string file)
    {
        if(_capture.Length==0)return; await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        Directory.CreateDirectory(_capture); using Image i=_viewport.GetTexture().GetImage(); i.SavePng(Path.Combine(_capture,file));
    }
    private async Task Cleanup() { _viewport.QueueFree(); await Settle(); GC.Collect(); GC.WaitForPendingFinalizers(); await Settle(); }
    private static SnapshotDto Fixture()=>new(NetworkProtocol.Version,Guid.NewGuid().ToString("N"),1,1,1,
        GamePhase.AwaitingClaims,MatchResult.None,1,null,null,
        new[] {new PublicPlayerDto(1,"Tú",0,5,true,false),new PublicPlayerDto(2,"Rival",1,9,true,false)},
        new[] {26,7,8,9,50},new[] {new MeldDto(42,1,new[] {0,1,2,3})},33,0,null,13,Array.Empty<RevealedHandDto>(),false,
        OfferTiming:new OfferTimingDto(13,5000,Array.Empty<CardReservationDto>(),false));
}
