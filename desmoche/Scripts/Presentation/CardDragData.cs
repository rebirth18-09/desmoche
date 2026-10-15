using Desmoche.Networking.Core;
using Godot;
using Godot.Collections;

namespace Desmoche.Presentation;

public enum CardDragOrigin { Hand = 1, Draft = 2, External = 3, Published = 4 }
public readonly record struct LocalCardDrag(int CardId, CardDragOrigin Origin, int GroupIndex);

// Token de un gesto local. No es un comando de red ni sustituye la validacion del servidor.
public static class CardDragData
{
    public static Variant Create(SnapshotDto view, int cardId, CardDragOrigin origin, int group = -1) =>
        new Dictionary
        {
            { "type", "desmoche-card-v1" }, { "session", view.Session },
            { "revision", view.Revision }, { "window", view.Window },
            { "recipient", view.RecipientId }, { "origin", (int)origin },
            { "group", group }, { "card", cardId }
        };

    public static bool TryRead(Variant data, SnapshotDto? view, out LocalCardDrag drag)
    {
        drag = default;
        if (view is null || data.VariantType != Variant.Type.Dictionary) return false;
        Dictionary map = data.AsGodotDictionary();
        if (map.Count != 8 || !String(map, "type", out string type) || type != "desmoche-card-v1" ||
            !String(map, "session", out string session) || session != view.Session ||
            !Number(map, "revision", out long revision) || revision != view.Revision ||
            !Number(map, "window", out long window) || window != view.Window ||
            !Number(map, "recipient", out long recipient) || recipient != view.RecipientId ||
            !Number(map, "origin", out long origin) || origin is < 1 or > 4 ||
            !Number(map, "group", out long group) || group < -1 || group > int.MaxValue ||
            !Number(map, "card", out long card) || card is < 0 or > 51 ||
            (origin == (long)CardDragOrigin.Draft ? group is < 0 or > 16 :
                origin == (long)CardDragOrigin.Published ? group <= 0 : group != -1)) return false;
        if (origin == (long)CardDragOrigin.Published && !System.Linq.Enumerable.Any(view.Melds,
            m => m.Id == group && m.OwnerId == view.RecipientId && System.Linq.Enumerable.Contains(m.Cards, (int)card))) return false;
        drag = new LocalCardDrag((int)card, (CardDragOrigin)origin, (int)group);
        return true;
    }

    private static bool Number(Dictionary map, string key, out long value)
    {
        value = 0;
        if (!map.TryGetValue(key, out Variant item) || item.VariantType != Variant.Type.Int) return false;
        value = item.AsInt64();
        return true;
    }

    private static bool String(Dictionary map, string key, out string value)
    {
        value = "";
        if (!map.TryGetValue(key, out Variant item) || item.VariantType != Variant.Type.String) return false;
        value = item.AsString();
        return true;
    }
}
