using System;
using System.Collections.Generic;
using Godot;

namespace Desmoche.Networking;

/// <summary>
/// Replica exclusivamente publica. Las manos privadas nunca se guardan en este nodo.
/// El servidor conserva la autoridad del nodo y de su sincronizador.
/// </summary>
public partial class PublicMatchReplica : Node
{
    [Export]
    public string PublicJson { get; set; } = "";

    public event Action<string>? PublicStateReceived;

    private MultiplayerSynchronizer? _synchronizer;

    /// <summary>Se llama desde SpawnFunction, antes de insertar el nodo en el arbol.</summary>
    public void Configure(IEnumerable<int> admittedPeers)
    {
        ArgumentNullException.ThrowIfNull(admittedPeers);
        if (IsInsideTree() || _synchronizer is not null)
            throw new InvalidOperationException("La replica debe configurarse una sola vez antes de entrar al arbol.");

        var propertyPath = new NodePath(":PublicJson");
        var configuration = new SceneReplicationConfig();
        configuration.AddProperty(propertyPath);
        configuration.PropertySetSpawn(propertyPath, true);
        configuration.PropertySetReplicationMode(
            propertyPath, SceneReplicationConfig.ReplicationMode.OnChange);

        var synchronizer = new MultiplayerSynchronizer
        {
            Name = "StateSynchronizer",
            RootPath = new NodePath(".."),
            PublicVisibility = false,
            ReplicationConfig = configuration,
            DeltaInterval = 0.0
        };

        foreach (int peerId in admittedPeers)
        {
            if (peerId > 1)
                synchronizer.SetVisibilityFor(peerId, true);
        }

        SetMultiplayerAuthority(1);
        synchronizer.SetMultiplayerAuthority(1);
        _synchronizer = synchronizer;
        AddChild(synchronizer);
    }

    public void SetPeerVisible(int peerId, bool visible)
    {
        if (_synchronizer is null)
            throw new InvalidOperationException("La replica todavia no esta configurada.");
        if (peerId <= 1)
            throw new ArgumentOutOfRangeException(nameof(peerId), "Se requiere un peer remoto.");

        // Godot retira el peer de replicacion antes de emitir PeerDisconnected.
        // Limpiar su permiso no debe intentar enviarle un paquete de despawn.
        bool peerAlreadyGone = !visible && IsInsideTree() &&
            Array.IndexOf(Multiplayer.GetPeers(), peerId) < 0;
        if (!peerAlreadyGone)
        {
            _synchronizer.SetVisibilityFor(peerId, visible);
            return;
        }

        bool wasBlocking = _synchronizer.IsBlockingSignals();
        _synchronizer.SetBlockSignals(true);
        try
        {
            _synchronizer.SetVisibilityFor(peerId, false);
        }
        finally
        {
            _synchronizer.SetBlockSignals(wasBlocking);
        }
    }

    public override void _Ready()
    {
        if (_synchronizer is null)
            throw new InvalidOperationException("SpawnFunction debe configurar la replica antes de devolverla.");

        _synchronizer.DeltaSynchronized += PublishReceivedState;
        _synchronizer.Synchronized += PublishReceivedState;
        PublishReceivedState();
    }

    public override void _ExitTree()
    {
        if (_synchronizer is null)
            return;

        _synchronizer.DeltaSynchronized -= PublishReceivedState;
        _synchronizer.Synchronized -= PublishReceivedState;
    }

    private void PublishReceivedState() => PublicStateReceived?.Invoke(PublicJson);
}

