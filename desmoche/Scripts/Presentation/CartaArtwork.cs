using System;
using Desmoche.Domain;
using Godot;

namespace Desmoche.Presentation;

// Palos vectoriales y figuras del atlas; los índices nunca dependen de glifos de la fuente.
public partial class CartaArtwork : Control
{
    private enum ArtworkMode { Empty, Back, Face }
    private ArtworkMode _mode;
    private Suit? _suit;
    private Rank? _rank;
    private Color _ink;
    private float _indexWidth = 24, _rankHeight = 23, _indexRadius = 5.5f;
    private AtlasTexture[]? _courts;
    private static readonly Color Gold = new(0.77f, 0.58f, 0.23f);
    private static readonly Color Pale = new(0.85f, 0.88f, 0.89f);

    public override void _Ready()
    {
        Resized += QueueRedraw;
        const string path = "res://Assets/Casino/Courts.png";
        if (ResourceLoader.Exists(path))
        {
            Texture2D atlas = GD.Load<Texture2D>(path);
            Vector2 imageSize = atlas.GetSize();
            float cellWidth = imageSize.X / 3;
            _courts = new AtlasTexture[3];
            for (int i = 0; i < _courts.Length; i++)
                _courts[i] = new AtlasTexture { Atlas = atlas,
                    Region = new Rect2(i * cellWidth, 0, cellWidth, imageSize.Y), FilterClip = true };
        }
    }

    public void SetIndexMetrics(float width, float rankHeight, float radius)
    {
        _indexWidth = width; _rankHeight = rankHeight; _indexRadius = radius;
        QueueRedraw();
    }

    public void ShowFace(Suit suit, Rank rank, Color ink)
    {
        _suit = suit; _rank = rank; _ink = ink; _mode = ArtworkMode.Face; QueueRedraw();
    }
    public void ShowBack()
    {
        _suit = null; _rank = null; _mode = ArtworkMode.Back; QueueRedraw();
    }
    public void ShowEmpty()
    {
        _suit = null; _rank = null; _mode = ArtworkMode.Empty; QueueRedraw();
    }

    public override void _Draw()
    {
        if (Size.X < 1 || Size.Y < 1) return;
        if (_mode == ArtworkMode.Back) { DrawBack(); return; }
        if (_mode == ArtworkMode.Empty) { DrawEmpty(); return; }
        if (_suit is not Suit suit || _rank is not Rank rank) return;
        DrawPaper();
        bool court = (int)rank > 10;
        if (court) DrawCourt(rank, suit);
        else DrawSuit(suit, new Vector2(Size.X * 0.54f, Size.Y * 0.52f),
            Mathf.Min(Size.X * 0.30f, Size.Y * 0.26f), _ink);
        DrawCornerSuits(suit, court);
    }

    private void DrawPaper()
    {
        Color grain = new(0.43f, 0.31f, 0.16f, 0.016f);
        for (float y = 2; y < Size.Y - 1; y += 4)
            DrawLine(new Vector2(1, y), new Vector2(Size.X - 1, y + 0.15f), grain, 0.5f, true);
        DrawLine(new Vector2(1, 1), new Vector2(Size.X - 1, 1), new Color(1, 1, 1, 0.45f), 0.7f, true);
    }

    private void DrawCornerSuits(Suit suit, bool clearCourt)
    {
        Vector2 indexCenter = new(_indexWidth * 0.5f, _rankHeight + _indexRadius + 0.5f);
        if (clearCourt)
        {
            Vector2 indexSize = new(_indexWidth, _rankHeight + _indexRadius * 2 + 1);
            Color paper = new(0.993f, 0.981f, 0.951f);
            DrawRect(new Rect2(Vector2.Zero, indexSize), paper);
            DrawRect(new Rect2(Size - indexSize, indexSize), paper);
        }
        DrawSuit(suit, indexCenter, _indexRadius, _ink);
        DrawSetTransform(Size - indexCenter, Mathf.Pi);
        DrawSuit(suit, Vector2.Zero, _indexRadius, _ink);
        DrawSetTransform(Vector2.Zero);
    }

    private void DrawSuit(Suit suit, Vector2 center, float r, Color ink)
    {
        if (suit == Suit.Diamonds)
        {
            Vector2[] diamond = { center + new Vector2(0, -r), center + new Vector2(r * 0.68f, 0),
                center + new Vector2(0, r), center + new Vector2(-r * 0.68f, 0) };
            DrawColoredPolygon(diamond, ink);
            DrawOutline(diamond, ink);
            return;
        }
        if (suit == Suit.Clubs)
        {
            DrawCircle(center + new Vector2(0, -r * 0.42f), r * 0.51f, ink, antialiased: true);
            DrawCircle(center + new Vector2(-r * 0.45f, r * 0.12f), r * 0.51f, ink, antialiased: true);
            DrawCircle(center + new Vector2(r * 0.45f, r * 0.12f), r * 0.51f, ink, antialiased: true);
            DrawStem(center, r, ink);
            return;
        }
        var points = new Vector2[48];
        float direction = suit == Suit.Spades ? -1 : 1;
        float size = suit == Suit.Spades ? r * 0.82f : r;
        for (int i = 0; i < points.Length; i++)
        {
            float t = i * Mathf.Tau / points.Length;
            float sin = Mathf.Sin(t);
            float x = sin * sin * sin * size;
            float y = -(13 * Mathf.Cos(t) - 5 * Mathf.Cos(2 * t) - 2 * Mathf.Cos(3 * t) - Mathf.Cos(4 * t)) * size / 17;
            points[i] = center + new Vector2(x, y * direction);
        }
        DrawColoredPolygon(points, ink);
        DrawOutline(points, ink);
        if (suit == Suit.Spades) DrawStem(center, r, ink);
    }

    private void DrawOutline(Vector2[] points, Color ink)
    {
        var outline = new Vector2[points.Length + 1];
        Array.Copy(points, outline, points.Length);
        outline[^1] = points[0];
        DrawPolyline(outline, ink, 0.7f, true);
    }

    private void DrawStem(Vector2 center, float r, Color ink) =>
        DrawColoredPolygon(new[] { center + new Vector2(0, -r * 0.1f),
            center + new Vector2(r * 0.32f, r * 0.95f), center + new Vector2(-r * 0.32f, r * 0.95f) }, ink);

    private void DrawBack()
    {
        Rect2 outer = new(new Vector2(2, 2), Size - new Vector2(4, 4));
        DrawRect(outer, Pale, false, 1);
        Rect2 inner = new(new Vector2(5, 5), Size - new Vector2(10, 10));
        DrawRect(inner, new Color(0.12f, 0.22f, 0.39f));
        DrawRect(inner, new Color(0.56f, 0.66f, 0.76f), false, 1);
        Color pattern = new(0.46f, 0.58f, 0.75f, 0.55f);
        const float step = 8;
        for (float y = inner.Position.Y + 4; y < inner.End.Y - 3; y += step)
        for (float x = inner.Position.X + 4; x < inner.End.X - 3; x += step)
        {
            Vector2 p = new(x, y);
            DrawPolyline(new[] { p + new Vector2(0, -2.5f), p + new Vector2(2.5f, 0),
                p + new Vector2(0, 2.5f), p + new Vector2(-2.5f, 0), p + new Vector2(0, -2.5f) }, pattern, 0.75f, true);
        }
        Vector2 c = Size * 0.5f;
        float w = Mathf.Max(8, Size.X * 0.23f), h = Mathf.Max(16, Size.Y * 0.26f);
        Vector2[] diamond = { c + new Vector2(0, -h), c + new Vector2(w, 0),
            c + new Vector2(0, h), c + new Vector2(-w, 0), c + new Vector2(0, -h) };
        DrawColoredPolygon(new[] { diamond[0], diamond[1], diamond[2], diamond[3] }, new Color(0.06f, 0.13f, 0.28f));
        DrawPolyline(diamond, Pale, 1.3f, true);
        Vector2[] second = { c + new Vector2(0, -h * 0.78f), c + new Vector2(w * 0.76f, 0),
            c + new Vector2(0, h * 0.78f), c + new Vector2(-w * 0.76f, 0), c + new Vector2(0, -h * 0.78f) };
        DrawPolyline(second, new Color(0.63f, 0.74f, 0.86f), 0.8f, true);
        DrawSuit(Suit.Spades, c, Mathf.Min(8, Size.X * 0.12f), Pale);
        DrawCircle(c + new Vector2(0, -h * 0.57f), 1.3f, Pale, antialiased: true);
        DrawCircle(c + new Vector2(0, h * 0.57f), 1.3f, Pale, antialiased: true);
    }

    private void DrawEmpty()
    {
        Color line = new(0.39f, 0.57f, 0.46f, 0.56f);
        Vector2 topLeft = new(3, 3), bottomRight = Size - new Vector2(3, 3);
        DrawDashedLine(topLeft, new Vector2(bottomRight.X, topLeft.Y), line, 1, 4);
        DrawDashedLine(new Vector2(bottomRight.X, topLeft.Y), bottomRight, line, 1, 4);
        DrawDashedLine(bottomRight, new Vector2(topLeft.X, bottomRight.Y), line, 1, 4);
        DrawDashedLine(new Vector2(topLeft.X, bottomRight.Y), topLeft, line, 1, 4);
        DrawSuit(Suit.Spades, new Vector2(Size.X * 0.5f, Size.Y * 0.41f),
            Mathf.Min(Size.X * 0.19f, Size.Y * 0.15f), new Color(0.31f, 0.50f, 0.39f, 0.5f));
    }

    private void DrawCourt(Rank rank, Suit suit)
    {
        if (_courts is not null)
        {
            AtlasTexture texture = _courts[(int)rank - (int)Rank.Jack];
            Vector2 sourceSize = texture.GetSize();
            Vector2 area = new(Size.X * 0.83f, Size.Y * 0.87f);
            float factor = Mathf.Min(area.X / sourceSize.X, area.Y / sourceSize.Y);
            Vector2 targetSize = sourceSize * factor;
            Rect2 atlasFrame = new((Size - targetSize) * 0.5f, targetSize);
            DrawTextureRect(texture, atlasFrame, false);
            DrawRect(atlasFrame, new Color(0.62f, 0.48f, 0.24f, 0.58f), false, 0.7f, true);
            return;
        }
        float scale = Mathf.Min(Size.X / 62, Size.Y / 91);
        Vector2 center = Size * 0.5f;
        Rect2 frame = new(center - new Vector2(17, 33) * scale, new Vector2(34, 66) * scale);
        DrawRect(frame, new Color(0.91f, 0.88f, 0.75f));
        DrawRect(frame, Gold, false, 1);
        DrawSetTransform(center + new Vector2(0, -15) * scale, 0, new Vector2(scale, scale));
        DrawCourtHalf(rank);
        DrawSetTransform(center + new Vector2(0, 15) * scale, Mathf.Pi, new Vector2(scale, scale));
        DrawCourtHalf(rank);
        DrawSetTransform(Vector2.Zero);
        DrawLine(center + new Vector2(-17, 0) * scale, center + new Vector2(17, 0) * scale, Gold, 2);
        DrawSuit(suit, center, 5 * scale, _ink);
    }

    private void DrawCourtHalf(Rank rank)
    {
        Color navy = new(0.11f, 0.20f, 0.32f);
        Color skin = new(0.90f, 0.68f, 0.42f);
        DrawColoredPolygon(new[] { new Vector2(-15, 17), new Vector2(-13, 3),
            new Vector2(0, -1), new Vector2(13, 3), new Vector2(15, 17) }, navy);
        DrawColoredPolygon(new[] { new Vector2(-12, 3), new Vector2(0, 14), new Vector2(12, 3),
            new Vector2(10, 17), new Vector2(-10, 17) }, _ink);
        DrawLine(new Vector2(-13, 3), new Vector2(12, 16), Gold, 2);
        DrawLine(new Vector2(13, 3), new Vector2(-12, 16), Gold, 2);
        DrawCircle(new Vector2(0, -6), 5.5f, skin, antialiased: true);
        DrawArc(new Vector2(0, -7), 5.5f, Mathf.Pi, Mathf.Tau, 16, navy, 3, true);
        DrawCircle(new Vector2(-2, -6), 0.65f, navy, antialiased: true);
        DrawCircle(new Vector2(2, -6), 0.65f, navy, antialiased: true);
        DrawLine(new Vector2(-1.5f, -2.5f), new Vector2(1.5f, -2.5f), navy, 0.6f, true);
        Vector2[] crown = rank == Rank.Jack
            ? new[] { new Vector2(-6, -11), new Vector2(-4, -16), new Vector2(6, -12), new Vector2(5, -9) }
            : new[] { new Vector2(-6, -10), new Vector2(-7, -16), new Vector2(-3, -13),
                new Vector2(0, -18), new Vector2(3, -13), new Vector2(7, -16), new Vector2(6, -10) };
        DrawColoredPolygon(crown, Gold);
        DrawLine(new Vector2(-6, -10), new Vector2(6, -10), navy, 1);
        if (rank == Rank.Queen) DrawCircle(new Vector2(0, -14), 1.8f, _ink, antialiased: true);
        if (rank == Rank.King) DrawLine(new Vector2(0, -19), new Vector2(0, -15), Gold, 1.5f);
        for (int i = -1; i <= 1; i++) DrawCircle(new Vector2(i * 7, 9), 1.4f, Gold, antialiased: true);
    }
}
