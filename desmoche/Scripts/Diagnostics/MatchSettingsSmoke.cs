using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Desmoche.Domain;
using Desmoche.Networking;
using Desmoche.Presentation;
using Desmoche.Singleplayer;
using Godot;

namespace Desmoche.Diagnostics;

public partial class MatchSettingsSmoke : Control
{
    private const string Content = "Scroll/Center/Panel/Padding/Content/";
    private SubViewport _viewport = null!;
    private Main _main = null!;
    private int _checks;
    private string _capture = "";

    public override void _Ready()
    {
        _capture = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--capture="))?[10..] ?? "";
        _viewport = new SubViewport { Size = new Vector2I(1280,720), HandleInputLocally = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        AddChild(_viewport);
        _main = GD.Load<PackedScene>("res://Scenes/Main.tscn").Instantiate<Main>(); _viewport.AddChild(_main);
        var display = new TextureRect { Texture = _viewport.GetTexture(), MouseFilter = MouseFilterEnum.Ignore,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale };
        AddChild(display); display.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Callable.From(Run).CallDeferred();
    }

    private async void Run()
    {
        try
        {
            while (!_main.ScreensReady) await Frames();
            var menu = _main.GetNode<Control>("Margin/Screens/MenuPrincipal");
            await Click(menu.GetNode<Button>("Scroll/Center/Content/Buttons/SingleplayerButton"));
            var setup = _main.GetNode<SingleplayerSetup>("Margin/Screens/Singleplayer");
            var access = setup.GetNode<Button>(Content + "MatchSettingsAccess/SettingsButton");
            var panel = setup.GetNode<MatchSettingsPanel>("MatchSettingsOverlay");
            var summary = setup.GetNode<Label>(Content + "MatchSettingsAccess/Summary");
            string defaults = summary.Text;
            foreach (Vector2I size in new[] { new Vector2I(640,360), new(844,390), new(1280,720), new(1920,1080),
                new(2400,1080), new(1024,768), new(3440,1440) })
            {
                _viewport.Size = size; await Frames();
                await Click(access);
                Check(panel.IsVisibleInTree(), "Engranaje abre panel en " + size);
                Control frame = panel.GetNode<Control>("Panel");
                Check(panel.GetGlobalRect().Grow(1).Encloses(frame.GetGlobalRect()), "Panel dentro del área segura en " + size + " root="+panel.GetGlobalRect()+" panel="+frame.GetGlobalRect()+" minimum="+frame.GetCombinedMinimumSize());
                foreach (string name in new[] { "ApplyButton", "CancelButton" })
                    Check(frame.GetGlobalRect().Encloses(Find<Button>(panel,name).GetGlobalRect()), "Pie accesible en " + size);
                SpinBox spin = Find<SpinBox>(panel,"CardPreparation");
                spin.Value = 60;
                var color = Find<CheckButton>(panel,"Color"); await Click(color);
                Check(!color.ButtonPressed, "Interruptor recibe clic nativo");
                if (size == new Vector2I(1280,720) || size == new Vector2I(844,390))
                { panel.GetNode<ScrollContainer>("Panel/Padding/Content/Scroll").ScrollVertical=0; await Frames(); await Capture("Ajustes-"+size.X+"x"+size.Y+".png"); }
                await Click(Find<Button>(panel,"CancelButton"));
                Check(!panel.Visible && summary.Text == defaults, "Cancelar descarta cambios sin modificar opciones");
            }
            _viewport.Size = new Vector2I(1280,720); await Frames(); await Click(access);
            Find<SpinBox>(panel,"CardChoice").Value=12;
            Find<SpinBox>(panel,"CardPreparation").GetLineEdit().Text="45";
            Find<SpinBox>(panel,"Pasa").Value=8;
            Find<SpinBox>(panel,"Rematch").Value=30;
            await Click(Find<CheckButton>(panel,"Peladilla"));
            await Click(Find<Button>(panel,"ApplyButton"));
            Check(summary.Text.Contains("12 s") && summary.Text.Contains("45 s") && summary.Text.Contains("4/5"), "Aplicar acepta texto pendiente y actualiza resumen");
            await Click(access); await Click(Find<Button>(panel,"DefaultsButton"));
            Check(Find<SpinBox>(panel,"CardChoice").Value==5 && Find<CheckButton>(panel,"Peladilla").ButtonPressed, "Restablecer carga valores originales");
            _viewport.PushInput(new InputEventKey { Keycode=Key.Escape,Pressed=true },true); await Frames();
            Check(!panel.Visible && _main.CurrentScreen=="Singleplayer" && summary.Text.Contains("12 s"), "Escape cancela sin navegar ni aplicar valores");
            await Click(setup.GetNode<Button>(Content+"StartButton"));
            var local=_main.GetNode<LocalGameSession>("LocalGameSession");
            Check(_main.CurrentScreen=="Mesa" && local.View?.Settings==new MatchSettings(12,45,8,30,PeladillaEnabled:false),
                "Empezar crea partida real con los ajustes elegidos");
            local.Close(); await Frames();
            await Click(setup.GetNode<Button>(Content+"BackButton"));
            await Click(menu.GetNode<Button>("Scroll/Center/Content/Buttons/MultiplayerButton"));
            var lobby=_main.GetNode<Lobby>("Margin/Screens/Lobby");
            await Click(lobby.GetNode<Button>(Content+"OptionsPanel/CreateOption"));
            var createAccess=lobby.GetNode<Button>(Content+"CreatePanel/MatchSettingsAccess/SettingsButton");
            var roomPanel=lobby.GetNode<MatchSettingsPanel>("MatchSettingsOverlay");
            await Click(createAccess);
            Find<SpinBox>(roomPanel,"CardChoice").Value=15;
            await Click(Find<CheckButton>(roomPanel,"LowStraight"));
            await Click(Find<Button>(roomPanel,"ApplyButton"));
            lobby.GetNode<LineEdit>(Content+"CreatePanel/NameInput").Text="Host de prueba";
            await Click(lobby.GetNode<Button>(Content+"CreatePanel/Buttons/CreateButton"));
            var network=_main.GetNode<NetworkManager>("NetworkManager");
            Check(network.IsHost && network.View?.Settings==new MatchSettings(CardChoiceSeconds:15,LowStraightEnabled:false),
                "Crear sala entrega opciones al host real");
            var roomAccess=lobby.GetNode<Button>(Content+"SessionPanel/MatchSettingsAccess/SettingsButton");
            await Click(roomAccess);
            Find<SpinBox>(roomPanel,"CardPreparation").Value=90;
            await Click(Find<Button>(roomPanel,"ApplyButton"));
            await Frames();
            Check(network.View?.Settings?.CardPreparationSeconds==90, "Host actualiza ajustes en la sala antes de iniciar");
            roomPanel.Open(network.View!.Settings!,false,roomAccess);
            await Frames();
            Check(!Find<Button>(roomPanel,"ApplyButton").Visible && !Find<SpinBox>(roomPanel,"CardChoice").Editable &&
                Find<CheckButton>(roomPanel,"Color").Disabled, "Vista de invitado permite consultar sin editar");
            await Click(Find<Button>(roomPanel,"CancelButton"));
            await Click(lobby.GetNode<Button>(Content+"SessionPanel/RoomButtons/LeaveButton"));
            Check(network.View is null && !roomPanel.Visible, "Cerrar sala limpia sesión y panel");
            GD.Print("MATCH SETTINGS PASS: Comprobaciones="+_checks);
            _viewport.QueueFree(); await Frames(); GetTree().Quit();
        }
        catch(Exception e) { await Capture("Fallo.png"); GD.PushError("MATCH SETTINGS FAIL: "+e); GetTree().Quit(1); }
    }

    private static T Find<T>(Node node,string name) where T:Node => (T)node.FindChild(name,true,false);
    private void Check(bool okay,string text) { if(!okay)throw new InvalidOperationException(text);_checks++; }
    private async Task Frames() { for(int i=0;i<6;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame); }
    private async Task Click(Control control)
    {
        for(Node? p=control.GetParent();p is not null;p=p.GetParent())
            if(p is ScrollContainer scroll) { scroll.EnsureControlVisible(control); await Frames(); }
        Check(control.IsVisibleInTree() && (control is not BaseButton b || !b.Disabled), "Control activo: "+control.Name);
        Vector2 point=control.GetGlobalTransformWithCanvas()*(control.Size/2);
        Check(new Rect2(Vector2.Zero,_viewport.Size).HasPoint(point),"Control en pantalla: "+control.Name);
        _viewport.PushInput(new InputEventMouseMotion { Position=point,GlobalPosition=point },true); await Frames();
        _viewport.PushInput(new InputEventMouseButton { Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=true,ButtonMask=MouseButtonMask.Left },true);
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        _viewport.PushInput(new InputEventMouseButton { Position=point,GlobalPosition=point,ButtonIndex=MouseButton.Left,Pressed=false },true);
        await Frames();
    }
    private async Task Capture(string name)
    {
        if(_capture.Length==0)return;
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        Directory.CreateDirectory(_capture);using Image image=_viewport.GetTexture().GetImage();image.SavePng(Path.Combine(_capture,name));
    }
}
