using System;
using System.Linq;
using Desmoche.Networking.Core;
using Godot;

namespace Desmoche.Presentation;

// Composición visual. No contiene reglas, acciones de partida ni estado privado adicional.
public partial class Mesa
{
    private const float CanvasWidth = 2016, CanvasHeight = 780, RivalWidth = 548;
    private readonly SystemFont _casinoSerif = new()
    {
        FontNames = new[] { "Georgia", "Times New Roman", "Liberation Serif", "Noto Serif" }
    };
    private Control _casinoPadding = null!, _casinoCanvas = null!;
    private VBoxContainer _casinoActions = null!;
    private Texture2D? _casinoPortraits;
    private Shader? _casinoPortraitMask;
    private Node2D _casinoDeckLayers = null!;
    private bool _casinoQueued, _casinoInitialized;

    public float CasinoScale { get; private set; } = 1;

    private void InitializeCasino()
    {
        _casinoPadding = GetNode<Control>("Scroll/Padding");
        _casinoCanvas = GetNode<Control>(ContentPath.TrimEnd('/'));
        _casinoActions = GetNode<VBoxContainer>(InteractionPath.TrimEnd('/'));
        _casinoPortraits = GD.Load<Texture2D>("res://Assets/Casino/Portraits.png");
        _casinoPortraitMask = GD.Load<Shader>("res://Assets/Casino/PortraitCircle.gdshader");
        foreach (string path in new[]
        {
            CenterPath + "Mazo/Content/TitleLabel", CenterPath + "Pozo/Content/TitleLabel",
            CenterPath + "Oferta/Content/TitleLabel",
            ContentPath + "Table/Padding/Surface/MyGame/TitleLabel",
            ContentPath + "HandZone/Padding/Content/HandHeader/TitleLabel"
        }) GetNode<Label>(path).AddThemeFontOverride("font", _casinoSerif);
        _deck.SetVisualSize(new Vector2(90, 112));
        _discard.SetVisualSize(new Vector2(90, 112));
        _offered.SetVisualSize(new Vector2(90, 112));
        // Dorsos decorativos, sin identidades: volumen de la pila, no cartas del mazo real.
        _casinoDeckLayers = new Node2D { Name = "PilaVisual", ShowBehindParent = true };
        _deck.AddChild(_casinoDeckLayers);
        for (int layer = 2; layer >= 1; layer--)
        {
            Carta back = _cardScene.Instantiate<Carta>();
            back.SetVisualSize(new Vector2(90, 112));
            back.ShowBack();
            back.Position = new Vector2(layer * 2, layer * 3);
            _casinoDeckLayers.AddChild(back);
        }
        Resized += QueueCasinoLayout;
        VisibilityChanged += QueueCasinoLayout;
        _casinoActions.MinimumSizeChanged += QueueCasinoLayout;
        _casinoInitialized = true;
        QueueCasinoLayout();
    }

    private void DisposeCasino()
    {
        if (!_casinoInitialized) return;
        Resized -= QueueCasinoLayout;
        VisibilityChanged -= QueueCasinoLayout;
        _casinoActions.MinimumSizeChanged -= QueueCasinoLayout;
        _casinoInitialized = false;
    }

    private void QueueCasinoLayout()
    {
        if (!_casinoInitialized || _casinoQueued || !IsInsideTree()) return;
        _casinoQueued = true;
        Callable.From(ReflowCasino).CallDeferred();
    }

    private void ReflowCasino()
    {
        _casinoQueued = false;
        if (!_casinoInitialized || !IsInsideTree()) return;
        // La imagen nunca se deforma. El pie conserva tamaños legibles al reducir el tablero.
        float availableWidth = Math.Max(200, Size.X - 16);
        // Reservar el pie para que nombre y estado tambien se vean en ventanas apaisadas.
        CasinoScale = Math.Max(0.1f, Math.Min(availableWidth / CanvasWidth,
            Math.Max(120, Size.Y - 152) / CanvasHeight));
        float width = CanvasWidth * CasinoScale;
        _casinoCanvas.Size = new Vector2(CanvasWidth, CanvasHeight);
        _casinoCanvas.Scale = Vector2.One * CasinoScale;
        _casinoCanvas.Position = new Vector2((availableWidth - width) / 2, 0);
        _casinoDeckLayers.Visible = _deck.IsBack;
        foreach (VBoxContainer rival in _rivalGames.GetChildren().OfType<VBoxContainer>())
            LayoutPublicCards(rival.GetNode<Control>("Rows"), RivalWidth, new Vector2(50.3f, 72), 5);
        LayoutPublicCards(_ownGames, 1120, new Vector2(90, 96), 8);

        PositionPasaButton();

        int count = _hand.GetChildCount();
        float cardWidth = count == 0 ? 124 : Math.Min(124, (1296f - Math.Max(0, count - 1) * 12) / count);
        foreach (Carta card in _hand.GetChildren().OfType<Carta>())
            card.SetVisualSize(new Vector2(cardWidth, 128 * cardWidth / 124));
        _hand.Size = new Vector2(1296, 132);
        // Cuando hay muchas cartas, el rótulo pasa al borde superior de la bandeja.
        _handTitle.Position = count > 7 ? new Vector2(220, -20) : new Vector2(220, 38);
        _handTitle.Size = new Vector2(250, 40);

        // Controles fuera de la superficie de juego: no tapan cartas ni pierden funcionalidad.
        float inverse = 1 / CasinoScale;
        Control top = GetNode<Control>(ContentPath + "TopBar");
        Control header = GetNode<Control>(ContentPath + "Header");
        top.Position = new Vector2(0, 788);
        top.Scale = Vector2.One * inverse;
        top.Size = new Vector2(width, 36);
        _selfPlayer.Position = new Vector2(0, 148);
        _selfPlayer.Scale = Vector2.One * inverse;
        _selfPlayer.Size = new Vector2(330, 80);
        float headerLeft = 130 * CasinoScale + 354;
        header.Position = new Vector2(headerLeft / CasinoScale, 788);
        header.Scale = Vector2.One * inverse;
        header.Size = new Vector2(Math.Max(100, width - headerLeft - (_rematch.Visible ? 290 : 152)), header.GetCombinedMinimumSize().Y);
        float headerBottom = 788 * CasinoScale + Math.Max(80, header.Size.Y) + 16;
        float controlsTop = headerBottom;
        // La oferta nunca cambia de fila. Acciones a su derecha, con texto legible.
        Control center = GetNode<Control>(CenterPath.TrimEnd('/'));
        center.Position = new Vector2(594, 170);
        center.Size = new Vector2(810, 184);
        _contextActions.Position = new Vector2(1592, 282);
        _contextActions.Scale = Vector2.One * inverse;
        _contextActions.Size = new Vector2(Math.Max(180, width - 1592 * CasinoScale - 18),
            _contextActions.GetCombinedMinimumSize().Y);
        _casinoActions.Position = new Vector2(-130, controlsTop / CasinoScale - 640);
        _casinoActions.Scale = Vector2.One * inverse;
        _casinoActions.Size = new Vector2(width, _casinoActions.GetCombinedMinimumSize().Y);
        float end = controlsTop + _casinoActions.Size.Y + 24;
        _revealedZone.Position = new Vector2(0, end / CasinoScale);
        _revealedZone.Scale = Vector2.One * inverse;
        _revealedZone.Size = new Vector2(width, _revealedZone.GetCombinedMinimumSize().Y);
        if (_revealedZone.Visible) end += _revealedZone.Size.Y + 24;
        _casinoPadding.CustomMinimumSize = new Vector2(0, end);
    }

    private void PositionPasaButton()
    {
        if (!_pasaButton.Visible || _challengedMeld is null || !IsInstanceValid(_challengedMeld)) return;
        _pasaButton.Scale = Vector2.One / CasinoScale;
        _pasaButton.Size = new Vector2(76, 34);
        Rect2 group = VisibleCardsRect(_challengedMeld);
        Rect2 board = _casinoCanvas.GetGlobalRect();
        Vector2 position = new(group.End.X + 8, group.Position.Y + (group.Size.Y - _pasaButton.Size.Y) / 2);
        var rows = _rivalGames.GetChildren().OfType<VBoxContainer>()
            .Select(rival => rival.GetNode<Control>("Rows")).Append(_ownGames);
        Rect2[] otherGroups = rows.SelectMany(row => row.GetChildren().OfType<HFlowContainer>())
            .Where(groupNode => groupNode != _challengedMeld).Select(VisibleCardsRect).ToArray();
        bool Occupied(Vector2 candidate) => candidate.X < board.Position.X + 8 ||
            candidate.X + _pasaButton.Size.X > board.End.X - 8 ||
            otherGroups.Any(rect => new Rect2(candidate, _pasaButton.Size).Intersects(rect));
        if (Occupied(position))
            position = new Vector2(group.Position.X - _pasaButton.Size.X - 8, position.Y);
        if (Occupied(position))
            position = new Vector2(Math.Max(board.Position.X + 8, group.End.X - _pasaButton.Size.X), group.End.Y + 6);
        _pasaButton.Position = _casinoCanvas.GetGlobalTransform().AffineInverse() * position;
    }

    private static Rect2 VisibleCardsRect(Control group)
    {
        Carta[] cards = group.GetChildren().OfType<Carta>().ToArray();
        if (cards.Length == 0) return group.GetGlobalRect();
        Rect2 first = cards[0].GetGlobalRect();
        Vector2 minimum = first.Position, maximum = first.End;
        foreach (Carta card in cards.Skip(1))
        {
            Rect2 rect = card.GetGlobalRect();
            minimum = new Vector2(Math.Min(minimum.X, rect.Position.X), Math.Min(minimum.Y, rect.Position.Y));
            maximum = new Vector2(Math.Max(maximum.X, rect.End.X), Math.Max(maximum.Y, rect.End.Y));
        }
        return new Rect2(minimum, maximum - minimum);
    }

    private static void LayoutPublicCards(Control rows, float width, Vector2 maximum, float gap)
    {
        HFlowContainer[] groups = rows.GetChildren().OfType<HFlowContainer>().ToArray();
        int count = groups.Sum(group => group.GetChildCount());
        if (count == 0) return;
        // Diez caben siempre; cantidades mayores se representan sin truncarlas ni alterar reglas.
        float cardWidth = Math.Min(maximum.X, (width - (count - 1) * gap) / count);
        Vector2 cardSize = maximum * (cardWidth / maximum.X);
        float x = (width - count * cardWidth - (count - 1) * gap) / 2;
        foreach (HFlowContainer group in groups)
        {
            int groupCount = group.GetChildCount();
            foreach (Carta card in group.GetChildren().OfType<Carta>()) card.SetVisualSize(cardSize);
            float groupWidth = groupCount * cardWidth + Math.Max(0, groupCount - 1) * gap;
            group.CustomMinimumSize = new Vector2(groupWidth, cardSize.Y);
            group.Size = new Vector2(groupWidth + 0.1f, cardSize.Y);
            group.Position = new Vector2(x, 0);
            x += groupWidth + gap;
        }
    }

    private PanelContainer CasinoPortrait(PublicPlayerDto player, bool active, bool self)
    {
        int side = self ? 54 : 76;
        var frame = new PanelContainer
        {
            Name = "Avatar", CustomMinimumSize = new Vector2(side, side),
            SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore
        };
        frame.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.1f, 0.11f),
            BorderColor = active ? new Color(1f, 0.75f, 0.28f) : new Color(0.57f, 0.68f, 0.70f),
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = side / 2, CornerRadiusTopRight = side / 2,
            CornerRadiusBottomLeft = side / 2, CornerRadiusBottomRight = side / 2,
            ContentMarginLeft = 3, ContentMarginTop = 3, ContentMarginRight = 3, ContentMarginBottom = 3
        });
        if (_casinoPortraits is not null && _casinoPortraitMask is not null)
        {
            int index = self ? 3 : Math.Clamp(player.SeatIndex - 1, 0, 2);
            int col = index % 2, row = index / 2;
            float half = _casinoPortraits.GetWidth() / 2f;
            var texture = new AtlasTexture
            {
                Atlas = _casinoPortraits, Region = new Rect2(col * half, row * half, half, half), FilterClip = true
            };
            var material = new ShaderMaterial { Shader = _casinoPortraitMask };
            material.SetShaderParameter("region_origin", new Vector2(col * 0.5f, row * 0.5f));
            var image = new TextureRect
            {
                Texture = texture, Material = material, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = MouseFilterEnum.Ignore,
                TextureFilter = TextureFilterEnum.LinearWithMipmaps
            };
            frame.AddChild(image);
        }
        else
        {
            Label initials = Text(Initials(player.Name), 20);
            initials.HorizontalAlignment = HorizontalAlignment.Center;
            initials.VerticalAlignment = VerticalAlignment.Center;
            frame.AddChild(initials);
        }
        return frame;
    }

    private HBoxContainer CasinoHeading(string text)
    {
        var heading = new HBoxContainer { CustomMinimumSize = new Vector2(0, 30), MouseFilter = MouseFilterEnum.Ignore };
        heading.AddThemeConstantOverride("separation", 18);
        for (int index = 0; index < 3; index++)
        {
            if (index == 1)
            {
                Label label = Text(text, 24);
                label.AddThemeFontOverride("font", _casinoSerif);
                label.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
                label.AutowrapMode = TextServer.AutowrapMode.Off;
                heading.AddChild(label);
            }
            else
            {
                var line = new HSeparator { SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    SizeFlagsVertical = SizeFlags.ShrinkCenter, MouseFilter = MouseFilterEnum.Ignore };
                line.AddThemeStyleboxOverride("separator", new StyleBoxLine { Color = new Color(0.7f, 0.52f, 0.23f), Thickness = 1 });
                heading.AddChild(line);
            }
        }
        return heading;
    }
}
