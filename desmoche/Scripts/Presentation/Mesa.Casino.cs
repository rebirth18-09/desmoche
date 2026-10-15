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
        GetNode<Control>(ContentPath + "TopBar").MinimumSizeChanged += QueueCasinoLayout;
        GetNode<Control>(ContentPath + "Header").MinimumSizeChanged += QueueCasinoLayout;
        _rematch.CustomMinimumSize = new Vector2(0, 44);
        _casinoInitialized = true;
        QueueCasinoLayout();
    }

    private void DisposeCasino()
    {
        if (!_casinoInitialized) return;
        Resized -= QueueCasinoLayout;
        VisibilityChanged -= QueueCasinoLayout;
        _casinoActions.MinimumSizeChanged -= QueueCasinoLayout;
        GetNode<Control>(ContentPath + "TopBar").MinimumSizeChanged -= QueueCasinoLayout;
        GetNode<Control>(ContentPath + "Header").MinimumSizeChanged -= QueueCasinoLayout;
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
        // Una sola pantalla: la mesa conserva escala uniforme y los controles
        // tienen tamaño de interfaz independiente. No queda contenido al bajar.
        float availableWidth = Math.Max(200, Size.X - 16);
        float headerHeight = LayoutTableChrome(availableWidth);
        // Los controles pertenecen a sus cartas/juegos. La escala del tablero
        // es estable al seleccionar: no aparece una fila debajo de la mano.
        float availableHeight = Math.Max(100, Size.Y - headerHeight - 12);
        CasinoScale = Math.Max(0.1f, Math.Min(availableWidth / CanvasWidth,
            availableHeight / CanvasHeight));
        float width = CanvasWidth * CasinoScale;
        _casinoCanvas.Size = new Vector2(CanvasWidth, CanvasHeight);
        _casinoCanvas.Scale = Vector2.One * CasinoScale;
        _casinoCanvas.Position = new Vector2((Size.X - width) / 2,
            headerHeight + (availableHeight - CanvasHeight * CasinoScale) / 2);
        _casinoDeckLayers.Visible = _deck.IsBack;
        foreach (VBoxContainer rival in _rivalGames.GetChildren().OfType<VBoxContainer>())
            LayoutPublicCards(rival.GetNode<Control>("Rows"), RivalWidth, new Vector2(50.3f, 72), 5);
        LayoutTableChrome(availableWidth);

        int count = VisibleHandCards().Length;
        float cardWidth = count == 0 ? 124 : Math.Min(124, (1296f - Math.Max(0, count - 1) * 12) / count);
        foreach (Carta card in _hand.GetChildren().OfType<Carta>())
            card.SetVisualSize(new Vector2(cardWidth, 128 * cardWidth / 124));
        _hand.Size = new Vector2(1296, 132);
        // El rótulo queda en el espacio lateral de la bandeja, libre incluso
        // cuando las cartas seleccionadas suben por encima de su fila.
        _handTitle.Position = new Vector2(64, 20);
        _handTitle.Scale = Vector2.One / CasinoScale;
        _handTitle.Size = new Vector2(148 * CasinoScale, 100 * CasinoScale);
        _handTitle.AddThemeFontSizeOverride("font_size", 12);
        _handTitle.AutowrapMode = TextServer.AutowrapMode.Off;
        _handTitle.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _handTitle.Text = _interactionView?.OwnSpectating == true ? "Observando" : "Tú\n" + (_interactionView?.OwnHand.Length ?? 0) + " cartas";
        _tableHandPortrait.Position = new Vector2(130, 676); _tableHandPortrait.Size = new Vector2(54, 54);
        _tableHandPortrait.TooltipText = _interactionView is { } current ? PlayerName(current, current.RecipientId) : "Tú";

        Control center = GetNode<Control>(CenterPath.TrimEnd('/'));
        center.Position = new Vector2(594, 142);
        center.Size = new Vector2(810, 160);
        foreach (Carta pile in new[] { _deck, _discard, _offered, _stagedCard }) pile.SetVisualSize(new Vector2(78, 97));
        foreach (Carta back in _casinoDeckLayers.GetChildren().OfType<Carta>()) back.SetVisualSize(new Vector2(78, 97));
        foreach (string zone in new[] { "Mazo", "Pozo", "Oferta", "Pago" })
        {
            Label caption = GetNode<Label>(CenterPath + zone + "/Content/TitleLabel");
            caption.AddThemeFontSizeOverride("font_size", 22);
            GetNode<Label>(CenterPath + zone + "/Content/CountLabel").AddThemeFontSizeOverride("font_size", 16);
        }
        Control own = GetNode<Control>(ContentPath + "Table/Padding/Surface/MyGame");
        float rowHeight = Math.Max(96, 44 / CasinoScale);
        own.Position = new Vector2(448, 616 - rowHeight - 18 / CasinoScale - 112);
        Label ownTitle = own.GetNode<Label>("TitleLabel");
        ownTitle.Scale = Vector2.One / CasinoScale; ownTitle.Size = new Vector2(1120 * CasinoScale, 18);
        ownTitle.AddThemeFontSizeOverride("font_size", 12);
        own.GetNode<Control>("LineLeft").Hide(); own.GetNode<Control>("LineRight").Hide();
        _ownGames.Position = new Vector2(0, 18 / CasinoScale);
        LayoutTableGames();
        LayoutInTableActions();
        Callable.From(LayoutInTableActions).CallDeferred();
        _casinoPadding.CustomMinimumSize = Vector2.Zero;
        _casinoPadding.Size = Size;
        _revealedZone.Hide();
        PositionPasaButton();
        LayoutFinalHands();
    }

    private void PositionPasaButton() => LayoutPasaAction();

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
