using System;
using System.Collections.Generic;
using System.Linq;
using Desmoche.Application;
using Desmoche.Networking.Core;

namespace Desmoche.Presentation.Interaction;

// Borrador local. Sus cartas permanecen en el estado del servidor hasta recibir
// una respuesta autoritativa; este tipo no decide si forman un juego valido.
public sealed class VisualGroupDraft
{
    private readonly int[] _cards;
    public int? MeldId { get; }
    public IReadOnlyList<int> Cards => Array.AsReadOnly((int[])_cards.Clone());

    internal VisualGroupDraft(IEnumerable<int> cards, int? meldId)
    {
        _cards = cards.ToArray();
        MeldId = meldId;
    }

    internal int[] CopyCards() => (int[])_cards.Clone();
}

// Estado de presentacion exclusivamente local: orden, seleccion y borradores.
// No contiene GameManager, RPC, cartas rivales ni reglas de combinaciones.
public sealed class HandInteractionState
{
    private readonly List<int> _order = new();
    private readonly HashSet<int> _selected = new();
    private readonly List<VisualGroupDraft> _groups = new();
    private readonly Dictionary<int, int[]> _ownMelds = new();
    private string? _session;
    private int _recipient;
    private long _window;
    private GamePhase _phase;
    private bool _responseSubmitted;
    private bool _eligible;
    private int[] _previousHand = Array.Empty<int>();
    private int? _external;
    private bool _externalSelected;

    public IReadOnlyList<int> Order => Array.AsReadOnly(_order.ToArray());
    public IReadOnlyList<int> SelectedIds =>
        Array.AsReadOnly(_order.Where(_selected.Contains).ToArray());
    public IReadOnlyList<VisualGroupDraft> Groups =>
        Array.AsReadOnly(_groups.ToArray());
    public bool ExternalSelected => _externalSelected;
    public int? ExternalCardId => _external;

    public void Synchronize(SnapshotDto? view)
    {
        if (view is null)
        {
            Reset();
            return;
        }
        int[] hand = CopyUniqueCards(view.OwnHand);
        var melds = new Dictionary<int, int[]>();
        foreach (MeldDto meld in view.Melds.Where(m => m.OwnerId == view.RecipientId))
        {
            if (meld.Id <= 0 || !melds.TryAdd(meld.Id, CopyUniqueCards(meld.Cards)))
                throw new ArgumentException("Identificador de juego propio invalido.", nameof(view));
        }
        PublicPlayerDto? own = view.Players.FirstOrDefault(p => p.Id == view.RecipientId);
        bool eligible = own is { Connected: true, Eliminated: false };
        int? external = null;
        if (eligible && !view.OwnResponseSubmitted &&
            view.Phase == GamePhase.AwaitingClaims)
            external = view.OfferedCard;
        else if (eligible && view.Phase == GamePhase.AwaitingDraw &&
            view.CurrentPlayerId == view.RecipientId)
            external = view.DiscardTop;
        if (external is < 0 or > 51 || external is int source && hand.Contains(source))
            throw new ArgumentException("Carta externa invalida.", nameof(view));

        bool newIdentity = _session != view.Session || _recipient != view.RecipientId;
        bool newContext = _window != view.Window || _phase != view.Phase ||
            _responseSubmitted != view.OwnResponseSubmitted || _eligible != eligible;
        bool handChanged = !_previousHand.OrderBy(id => id).SequenceEqual(hand.OrderBy(id => id));
        bool meldsChanged = !SameMelds(_ownMelds, melds);
        bool sourceChanged = _external != external;
        if (newIdentity)
        {
            Reset();
            _order.AddRange(hand);
        }
        else
        {
            var remaining = hand.ToHashSet();
            _order.RemoveAll(id => !remaining.Contains(id));
            foreach (int id in hand)
                if (!_order.Contains(id)) _order.Add(id);
            _selected.RemoveWhere(id => !remaining.Contains(id));
            if (newContext) ClearSelection();
            if (newContext || handChanged || meldsChanged || sourceChanged)
                ClearGroups();
            if (sourceChanged) _externalSelected = false;
        }
        _ownMelds.Clear();
        foreach (var entry in melds) _ownMelds.Add(entry.Key, entry.Value);
        _session = view.Session;
        _recipient = view.RecipientId;
        _window = view.Window;
        _phase = view.Phase;
        _responseSubmitted = view.OwnResponseSubmitted;
        _eligible = eligible;
        _previousHand = hand;
        _external = external;
    }

    public bool Toggle(int cardId)
    {
        if (!_order.Contains(cardId) || Assigned(cardId)) return false;
        if (!_selected.Remove(cardId)) _selected.Add(cardId);
        return true;
    }

    // beforeIndex se refiere a los huecos de la lista antes de retirar la carta.
    // Count representa el hueco final; arrastrar no mueve cartas del servidor.
    public bool Move(int cardId, int beforeIndex) =>
        MoveCard(_order, cardId, beforeIndex);

    public bool SelectExternal(bool selected)
    {
        if (!selected)
        {
            _externalSelected = false;
            return true;
        }
        if (_external is not int card || Assigned(card)) return false;
        _externalSelected = true;
        return true;
    }

    public bool PrepareGroup(int? targetMeldId = null)
    {
        if (_groups.Count >= 17 || targetMeldId is <= 0) return false;
        var cards = new List<int>();
        if (targetMeldId is int meldId)
        {
            if (!_ownMelds.TryGetValue(meldId, out int[]? existing) ||
                _groups.Any(group => group.MeldId == meldId)) return false;
            cards.AddRange(existing);
        }
        int[] selected = _order.Where(_selected.Contains).ToArray();
        if (targetMeldId is not null && selected.Length == 0 && !_externalSelected)
            return false;
        cards.AddRange(selected);
        if (_externalSelected && _external is int external) cards.Add(external);
        if (cards.Count is < 3 or > 13 || cards.Distinct().Count() != cards.Count ||
            cards.Any(Assigned)) return false;
        _groups.Add(new VisualGroupDraft(cards, targetMeldId));
        ClearSelection();
        return true;
    }

    // Prepara una colocacion local por arrastre, incluso si todavia no hay tres
    // cartas. Las reglas de juego se comprueban al enviar la solicitud.
    // beforeIndex indica un hueco de la lista de destino antes de la insercion.
    public bool StageCards(int draggedId, int? targetMeldId = null,
        int? groupIndex = null, int? beforeIndex = null)
    {
        if (draggedId is < 0 or > 51 || targetMeldId is <= 0 ||
            groupIndex is < 0 || groupIndex >= _groups.Count) return false;
        bool fromHand = _order.Contains(draggedId);
        bool fromExternal = _external == draggedId;
        if ((!fromHand && !fromExternal) || Assigned(draggedId)) return false;

        var additions = new List<int>();
        if (fromExternal || _selected.Contains(draggedId))
            additions.AddRange(_order.Where(_selected.Contains));
        else
            additions.Add(draggedId);
        if (fromExternal || _externalSelected)
        {
            if (_external is not int external) return false;
            additions.Add(external);
        }
        if (additions.Count == 0 || additions.Distinct().Count() != additions.Count ||
            additions.Any(id => (!_order.Contains(id) && _external != id) || Assigned(id)))
            return false;

        int index = groupIndex ?? -1;
        int? meldId = targetMeldId;
        var destination = new List<int>();
        if (index >= 0)
        {
            VisualGroupDraft group = _groups[index];
            if (targetMeldId is not null && group.MeldId != targetMeldId) return false;
            meldId = group.MeldId;
            destination.AddRange(group.CopyCards());
        }
        else if (targetMeldId is int target)
        {
            if (!_ownMelds.TryGetValue(target, out int[]? published)) return false;
            index = _groups.FindIndex(group => group.MeldId == target);
            if (index >= 0)
                destination.AddRange(_groups[index].CopyCards());
            else
            {
                if (published.Any(Assigned)) return false;
                destination.AddRange(published);
            }
        }
        if (index < 0 && _groups.Count >= 17) return false;
        int insertion = beforeIndex ?? destination.Count;
        if (insertion < 0 || insertion > destination.Count ||
            destination.Count + additions.Count > 13) return false;
        destination.InsertRange(insertion, additions);
        if (destination.Distinct().Count() != destination.Count) return false;

        var draft = new VisualGroupDraft(destination, meldId);
        if (index < 0) _groups.Add(draft);
        else _groups[index] = draft;
        ClearSelection();
        return true;
    }

    // Deshacer una colocacion no devuelve cartas del servidor: libera solo la
    // carta privada o externa que se habia preparado localmente. Una extension
    // conserva siempre todas las cartas del juego ya publicado.
    public bool ReturnToHand(int groupIndex, int cardId)
    {
        if (groupIndex < 0 || groupIndex >= _groups.Count ||
            (!_order.Contains(cardId) && _external != cardId)) return false;
        VisualGroupDraft group = _groups[groupIndex];
        int[] published = Array.Empty<int>();
        if (group.MeldId is int target)
        {
            if (!_ownMelds.TryGetValue(target, out int[]? existing)) return false;
            published = existing;
            if (published.Contains(cardId)) return false;
        }
        var cards = group.CopyCards().ToList();
        if (!cards.Remove(cardId)) return false;
        if (cards.Count == 0 || (group.MeldId is not null &&
                cards.All(published.Contains)))
            _groups.RemoveAt(groupIndex);
        else
            _groups[groupIndex] = new VisualGroupDraft(cards, group.MeldId);
        return true;
    }

    public bool MoveInGroup(int groupIndex, int cardId, int beforeIndex)
    {
        if (groupIndex < 0 || groupIndex >= _groups.Count) return false;
        VisualGroupDraft group = _groups[groupIndex];
        var cards = group.CopyCards().ToList();
        if (!MoveCard(cards, cardId, beforeIndex)) return false;
        _groups[groupIndex] = new VisualGroupDraft(cards, group.MeldId);
        return true;
    }

    public bool RemoveGroup(int groupIndex)
    {
        if (groupIndex < 0 || groupIndex >= _groups.Count) return false;
        _groups.RemoveAt(groupIndex);
        return true;
    }

    public void ClearSelection()
    {
        _selected.Clear();
        _externalSelected = false;
    }

    public void ClearGroups() => _groups.Clear();

    public PlayDto[] BuildPlays() =>
        _groups.Select(group => new PlayDto(group.CopyCards(), group.MeldId)).ToArray();

    private bool Assigned(int cardId) =>
        _groups.Any(group => group.Cards.Contains(cardId));

    private static bool MoveCard(List<int> cards, int cardId, int beforeIndex)
    {
        int oldIndex = cards.IndexOf(cardId);
        if (oldIndex < 0 || beforeIndex < 0 || beforeIndex > cards.Count) return false;
        cards.RemoveAt(oldIndex);
        if (oldIndex < beforeIndex) beforeIndex--;
        cards.Insert(beforeIndex, cardId);
        return true;
    }

    private static int[] CopyUniqueCards(int[]? cards)
    {
        if (cards is null || cards.Any(id => id is < 0 or > 51) ||
            cards.Distinct().Count() != cards.Length)
            throw new ArgumentException("Las cartas deben tener IDs unicos entre 0 y 51.");
        return (int[])cards.Clone();
    }

    private static bool SameMelds(Dictionary<int, int[]> left,
        Dictionary<int, int[]> right) => left.Count == right.Count &&
        left.All(entry => right.TryGetValue(entry.Key, out int[]? cards) &&
            entry.Value.SequenceEqual(cards));

    private void Reset()
    {
        _order.Clear();
        ClearSelection();
        ClearGroups();
        _ownMelds.Clear();
        _session = null;
        _recipient = 0;
        _window = 0;
        _phase = GamePhase.Preparing;
        _responseSubmitted = false;
        _eligible = false;
        _previousHand = Array.Empty<int>();
        _external = null;
    }
}
