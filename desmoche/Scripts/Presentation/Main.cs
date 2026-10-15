using System;
using Desmoche.Application;
using Desmoche.Networking;
using Desmoche.Networking.Core;
using Desmoche.Singleplayer;
using Desmoche.Singleplayer.Core;
using Godot;

namespace Desmoche.Presentation;

// La raíz y NetworkManager no se reemplazan al navegar: conservan sus rutas de RPC.
public partial class Main : Control
{
    private static readonly string[] StartupResources =
    {
        "res://Scenes/MenuPrincipal.tscn", "res://Scenes/Lobby.tscn", "res://Scenes/Mesa.tscn",
        "res://Scenes/Singleplayer.tscn",
        "res://Assets/Casino/Courts.png", "res://Assets/Casino/Portraits.png",
        "res://Assets/Casino/PortraitCircle.gdshader"
    };
    // Solo permite ejecutar los diagnósticos anteriores con preparación síncrona.
    protected virtual bool ImmediateInitialization => false;
    [Export(PropertyHint.Range, "0,3,0.1")]
    public double MinimumLoadingSeconds { get; set; } = 0.8;
    public bool ScreensReady { get; private set; }
    public string CurrentScreen { get; private set; } = "Carga";
    public event Action? ScreensInitialized;

    private NetworkManager _network = null!;
    private LocalGameSession _local = null!;
    private Control _screens = null!;
    private LoadingScreen _loading = null!;
    private StartupMenu _menu = null!;
    private Lobby _lobby = null!;
    private Mesa _mesa = null!;
    private SingleplayerSetup _singleplayer = null!;
    private readonly Resource?[] _loaded = new Resource?[StartupResources.Length];
    private bool _bound, _requested, _failed, _multiplayerEntered;
    private bool _singleplayerEntered;
    private const int ScreenCount = 4;
    private int _initializedScreens;
    private ulong _loadingStarted;

    public override void _Ready()
    {
        GetWindow().MinSize = new Vector2I(640, 360);
        InitializeResponsiveWindow();
        _network = GetNode<NetworkManager>("NetworkManager");
        _local = GetNode<LocalGameSession>("LocalGameSession");
        _screens = GetNode<Control>("Margin/Screens");
        _loading = GetNode<LoadingScreen>("Margin/Screens/Carga");
        _loading.RetryRequested += BeginLoading;
        _loading.QuitRequested += QuitGame;
        _network.ViewChanged += OnViewChanged;
        _network.ConnectionChanged += OnConnectionChanged;
        _local.SessionChanged += UpdateScreen;
        _local.ViewChanged += OnViewChanged;
        _bound = true;
        if (ImmediateInitialization)
        {
            for (int i = 0; i < StartupResources.Length; i++)
                _loaded[i] = ResourceLoader.Load(StartupResources[i]);
            for (int i = 0; i < ScreenCount; i++) InitializeNextScreen();
            _multiplayerEntered = true;
            FinishLoading();
        }
        else _loadingStarted = Time.GetTicksMsec();
    }

    public override void _Process(double delta)
    {
        UpdateMobileInsets(delta);
        if (ScreensReady || _failed) return;
        if (!_requested) { BeginLoading(); return; }
        if (Time.GetTicksMsec() - _loadingStarted > 60000)
        {
            FailLoading("La preparación de recursos superó el tiempo de espera.");
            return;
        }
        double progress = 0;
        bool resourcesReady = true;
        for (int i = 0; i < StartupResources.Length; i++)
        {
            if (_loaded[i] is not null) { progress++; continue; }
            using var resourceProgress = new Godot.Collections.Array();
            ResourceLoader.ThreadLoadStatus status =
                ResourceLoader.LoadThreadedGetStatus(StartupResources[i], resourceProgress);
            if (status is ResourceLoader.ThreadLoadStatus.Failed or ResourceLoader.ThreadLoadStatus.InvalidResource)
            {
                FailLoading("No se pudo cargar " + StartupResources[i]);
                return;
            }
            if (status == ResourceLoader.ThreadLoadStatus.Loaded)
            {
                // Get se llama únicamente al terminar; no espera al hilo de carga.
                _loaded[i] = ResourceLoader.LoadThreadedGet(StartupResources[i]);
                if (_loaded[i] is null) { FailLoading("Recurso vacío: " + StartupResources[i]); return; }
                progress++;
            }
            else
            {
                resourcesReady = false;
                if (resourceProgress.Count > 0) progress += resourceProgress[0].AsDouble();
            }
        }
        _loading.SetProgress(0.75 * progress / StartupResources.Length + 0.25 * _initializedScreens / ScreenCount,
            resourcesReady ? "Preparando las pantallas…" : "Cargando recursos…");
        if (!resourcesReady) return;
        try
        {
            // Nodos en el hilo principal; una pantalla por fotograma.
            if (_initializedScreens < ScreenCount) { InitializeNextScreen(); return; }
            _loading.SetProgress(1, "Listo");
            if (Time.GetTicksMsec() - _loadingStarted >= Math.Max(0, MinimumLoadingSeconds) * 1000)
                FinishLoading();
        }
        catch (Exception exception) { FailLoading(exception.Message); }
    }

    private void BeginLoading()
    {
        if (ScreensReady) return;
        bool retrying = _requested;
        _failed = false;
        _requested = true;
        CurrentScreen = "Carga";
        _loadingStarted = Time.GetTicksMsec();
        _loading.Reset();
        for (int i = 0; i < StartupResources.Length; i++)
        {
            if (_loaded[i] is not null) continue;
            if (retrying)
            {
                ResourceLoader.ThreadLoadStatus previous = ResourceLoader.LoadThreadedGetStatus(StartupResources[i]);
                // No duplica solicitudes activas; consume las fallidas antes de volver a pedirlas.
                if (previous is ResourceLoader.ThreadLoadStatus.InProgress or ResourceLoader.ThreadLoadStatus.Loaded)
                    continue;
                if (previous == ResourceLoader.ThreadLoadStatus.Failed)
                    _ = ResourceLoader.LoadThreadedGet(StartupResources[i]);
            }
            Error error = ResourceLoader.LoadThreadedRequest(StartupResources[i]);
            if (error != Error.Ok)
            {
                FailLoading("No se pudo solicitar " + StartupResources[i] + ": " + error);
                return;
            }
        }
    }

    private void InitializeNextScreen()
    {
        int index = _initializedScreens;
        if (_loaded[index] is not PackedScene packed)
            throw new InvalidOperationException("Pantalla no válida: " + StartupResources[index]);
        Control screen = packed.Instantiate<Control>();
        bool validType = index switch { 0 => screen is StartupMenu, 1 => screen is Lobby,
            2 => screen is Mesa, 3 => screen is SingleplayerSetup, _ => false };
        if (!validType)
        {
            screen.Free();
            _loaded[index] = null;
            throw new InvalidOperationException("Script incorrecto en " + StartupResources[index]);
        }
        try
        {
            screen.Visible = false;
            _screens.AddChild(screen);
            switch (index)
            {
                case 0:
                    _menu = (StartupMenu)screen;
                    _menu.MultiplayerRequested += OpenMultiplayer;
                    _menu.SingleplayerRequested += OpenSingleplayer;
                    _menu.QuitRequested += QuitGame;
                    break;
                case 1:
                    _lobby = (Lobby)screen;
                    _lobby.Bind(_network);
                    _lobby.BackRequested += ReturnToStart;
                    break;
                case 2:
                    _mesa = (Mesa)screen;
                    _mesa.Bind(_network);
                    break;
                case 3:
                    _singleplayer = (SingleplayerSetup)screen;
                    _singleplayer.StartRequested += StartSingleplayer;
                    _singleplayer.BackRequested += ReturnToStart;
                    break;
            }
            _initializedScreens++;
        }
        catch
        {
            if (screen.GetParent() is Node parent) parent.RemoveChild(screen);
            screen.Free();
            _loaded[index] = null;
            throw;
        }
    }

    private void FinishLoading()
    {
        ScreensReady = true;
        UpdateScreen();
        ScreensInitialized?.Invoke();
        CallDeferred(MethodName.FocusScreen);
    }

    private void FailLoading(string detail)
    {
        _failed = true;
        CurrentScreen = "Error";
        GD.PushError("Inicio: " + detail);
        _loading.ShowError();
    }

    public void OpenMultiplayer()
    {
        if (!ScreensReady || _network.State != ConnectionState.Offline || _local.IsActive) return;
        _singleplayerEntered = false;
        _multiplayerEntered = true;
        _mesa.Bind(_network);
        _lobby.ShowOptions();
        UpdateScreen();
    }

    public void ReturnToStart()
    {
        if (!ScreensReady || _network.State != ConnectionState.Offline || _local.IsActive) return;
        _multiplayerEntered = false;
        _singleplayerEntered = false;
        UpdateScreen();
    }

    public void OpenSingleplayer()
    {
        if (!ScreensReady || _network.State != ConnectionState.Offline || _local.IsActive) return;
        _multiplayerEntered = false;
        _singleplayerEntered = true;
        _singleplayer.ShowOptions();
        UpdateScreen();
    }

    public void StartSingleplayer(LocalMatchOptions options)
    {
        if (!ScreensReady || _network.State != ConnectionState.Offline || _local.IsActive) return;
        try
        {
            _singleplayerEntered = true;
            _multiplayerEntered = false;
            _mesa.Bind(_local);
            _local.Start(options);
            UpdateScreen();
        }
        catch (ArgumentException exception) { _singleplayer.ShowError(exception.Message); }
        catch (InvalidOperationException exception) { _singleplayer.ShowError(exception.Message); }
    }

    private void QuitGame()
    {
        if (_network.State != ConnectionState.Offline) _network.Close("Juego cerrado.");
        if (_local.IsActive) _local.Close();
        GetTree().Quit();
    }
    private void OnViewChanged(SnapshotDto view) => UpdateScreen();
    private void OnConnectionChanged(ConnectionState state, string message) => UpdateScreen();

    private void UpdateScreen()
    {
        if (!ScreensReady) return;
        bool inMatch = _network.State is ConnectionState.Hosting or ConnectionState.Connected &&
            _network.View is { Phase: not GamePhase.Preparing };
        if (_network.State != ConnectionState.Offline || inMatch) _multiplayerEntered = true;
        string next = _local.IsActive || inMatch ? "Mesa" : _singleplayerEntered ? "Singleplayer" :
            _multiplayerEntered ? "Lobby" : "Inicio";
        bool changed = CurrentScreen != next;
        CurrentScreen = next;
        _loading.Visible = false;
        _menu.Visible = next == "Inicio";
        _lobby.Visible = next == "Lobby";
        _mesa.Visible = next == "Mesa";
        _singleplayer.Visible = next == "Singleplayer";
        // El salón pertenece a la navegación. Mesa conserva su fondo anterior.
        GetNode<Control>("SalonBackground").Visible = next != "Mesa";
        GetNode<Control>("MenuFrame").Visible = next != "Mesa";
        if (changed) CallDeferred(MethodName.FocusScreen);
    }

    private void FocusScreen()
    {
        if (!IsInsideTree() || !ScreensReady) return;
        if (_mesa.Visible) _mesa.FocusDefault();
        else if (_lobby.Visible) _lobby.FocusDefault();
        else if (_singleplayer.Visible) _singleplayer.FocusDefault();
        else _menu.FocusDefault();
    }

    public override void _ExitTree()
    {
        GetViewport().SizeChanged -= UpdateSafeMargins;
        if (!_bound) return;
        _network.ViewChanged -= OnViewChanged;
        _network.ConnectionChanged -= OnConnectionChanged;
        _local.SessionChanged -= UpdateScreen;
        _local.ViewChanged -= OnViewChanged;
        _loading.RetryRequested -= BeginLoading;
        _loading.QuitRequested -= QuitGame;
        if (_initializedScreens >= 1)
        {
            _menu.MultiplayerRequested -= OpenMultiplayer;
            _menu.SingleplayerRequested -= OpenSingleplayer;
            _menu.QuitRequested -= QuitGame;
        }
        if (_initializedScreens >= 2)
        {
            _lobby.BackRequested -= ReturnToStart;
            _lobby.Unbind();
        }
        if (_initializedScreens >= 3) _mesa.Unbind();
        if (_initializedScreens >= 4)
        {
            _singleplayer.StartRequested -= StartSingleplayer;
            _singleplayer.BackRequested -= ReturnToStart;
        }
        _bound = false;
    }
}
