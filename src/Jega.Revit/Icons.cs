using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Jega.Revit;

/// <summary>
/// The ribbon icons, drawn in code on a 32 × 32 grid, so there are no image files and they stay sharp at both
/// sizes Revit uses (32 px on the ribbon, 16 px small). Mid-tone colours that read on Revit's light and dark
/// themes: terrain green and arrow blue, as on the site plan.
/// </summary>
internal static class Icons
{
    private static readonly Brush Terrain = Frozen(Color.FromRgb(0x2f, 0x8f, 0x6e));
    private static readonly Brush Arrow = Frozen(Color.FromRgb(0x2f, 0x6c, 0xd8));
    private static readonly Brush Lot = Frozen(Color.FromRgb(0xd6, 0x45, 0x35));
    private static readonly Brush Line = Frozen(Color.FromRgb(0xd9, 0x7a, 0x1f));

    /// <summary>Three sliders, knobs at different places: the project's settings.</summary>
    public static BitmapSource Settings(int px) => Render(px, dc =>
    {
        var pen = new Pen(Terrain, 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        foreach (var (y, knob) in new[] { (8.0, 20.0), (16.0, 10.0), (24.0, 17.0) })
        {
            dc.DrawLine(pen, new Point(4, y), new Point(28, y));
            dc.DrawEllipse(Arrow, null, new Point(knob, y), 3.5, 3.5);
        }
    });

    /// <summary>A house square in a turning arc: the whole model turned to work square to a building.</summary>
    public static BitmapSource WorkDirection(int px) => Render(px, dc =>
    {
        dc.DrawGeometry(null, new Pen(Arrow, 2.5) { StartLineCap = PenLineCap.Round }, Geometry.Parse("M16,4 A12,12 0 1 1 5.6,10"));
        dc.DrawGeometry(Arrow, null, Geometry.Parse("M2.5,6 L9.5,8.5 L4.5,13.5 Z"));
        dc.DrawGeometry(Brushes.White, new Pen(Terrain, 2) { LineJoin = PenLineJoin.Round }, Geometry.Parse("M11,12 L21,9 L24,19 L14,22 Z"));
    });

    /// <summary>Two arrows, one turned from the other: the view between Project North and True North.</summary>
    public static BitmapSource ViewNorth(int px) => Render(px, dc =>
    {
        dc.DrawGeometry(null, new Pen(Terrain, 2) { LineJoin = PenLineJoin.Round }, Geometry.Parse("M3,3 H29 V29 H3 Z"));
        dc.DrawGeometry(Arrow, null, Geometry.Parse("M11,6 L15,20 L11,17 L7,20 Z"));
        dc.DrawGeometry(Lot, null, Geometry.Parse("M23.5,8.5 L20.6,22.7 L18.6,19.5 L14.9,21.1 Z"));
    });

    /// <summary>Three sheets stacked, the top one the site plan's red: the site plan's layers.</summary>
    public static BitmapSource Layers(int px) => Render(px, dc =>
    {
        foreach (var (y, brush) in new[] { (22.0, Terrain), (16.0, Arrow), (10.0, Lot) })
        {
            dc.DrawGeometry(Brushes.White, new Pen(brush, 2) { LineJoin = PenLineJoin.Round },
                Geometry.Parse(FormattableString.Invariant($"M16,{y - 6} L29,{y} L16,{y + 6} L3,{y} Z")));
        }
    });

    /// <summary>A map pin (the address point, in the site plan's red) over a terrain profile: load from an address.</summary>
    public static BitmapSource Address(int px) => Render(px, dc =>
    {
        dc.DrawGeometry(Terrain, null, Geometry.Parse("M2,30 L2,24 8,21 14,23 21,18 30,22 30,30 Z"));
        dc.DrawGeometry(Lot, null, Geometry.Parse("M16,26 C12,19 8,15.5 8,10.5 A8,8 0 1 1 24,10.5 C24,15.5 20,19 16,26 Z"));
        dc.DrawEllipse(Brushes.White, null, new Point(16, 10.5), 3.2, 3.2);
    });

    /// <summary>A terrain profile with an arrow down into it: load the terrain.</summary>
    public static BitmapSource UpdateTerrain(int px) => Render(px, dc =>
    {
        dc.DrawGeometry(Terrain, null, Geometry.Parse("M2,30 L2,22 8,18 14,21 21,14 30,19 30,30 Z"));
        dc.DrawGeometry(Arrow, null, Geometry.Parse("M9.5,2 H12.5 V9 H16 L11,15 L6,9 H9.5 Z"));
    });

    /// <summary>A circular arrow around a small terrain: redraw from what is stored.</summary>
    public static BitmapSource Refresh(int px) => Render(px, dc =>
    {
        // Clockwise from the top round to the upper left (300° of a circle about 16,15), head pointing on.
        var pen = new Pen(Arrow, 3) { StartLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, pen, Geometry.Parse("M16,6 A9,9 0 1 1 8.21,10.5"));
        dc.DrawGeometry(Arrow, null, Geometry.Parse("M9.96,7.47 L4.94,9.77 L10.48,12.97 Z"));
        dc.DrawGeometry(Terrain, null, Geometry.Parse("M10,21 L13,17.5 16,19.5 19,15.5 22,21 Z"));
    });

    /// <summary>The lot outline in the site plan's red, with a level marker at a corner: levels on the lot.</summary>
    public static BitmapSource LotEdge(int px) => Render(px, dc =>
    {
        dc.DrawGeometry(null, new Pen(Lot, 2.5) { LineJoin = PenLineJoin.Round }, Geometry.Parse("M3,10 L19,4 L29,12 L26,28 L5,25 Z"));
        dc.DrawGeometry(Arrow, null, Geometry.Parse("M12,12 L20,12 L16,18 Z"));
        dc.DrawRectangle(Arrow, null, new Rect(10, 18.5, 12, 2.5));
    });

    /// <summary>A terrain profile inside the lot outline in the site plan's red: the Toposolid on the lot.</summary>
    public static BitmapSource LotTerrain(int px) => Render(px, dc =>
    {
        dc.DrawGeometry(Terrain, null, Geometry.Parse("M5,26 L5,20 10,16 15,19 21,12 27,17 27,26 Z"));
        dc.DrawGeometry(null, new Pen(Lot, 2.5) { LineJoin = PenLineJoin.Round }, Geometry.Parse("M3,8 L19,3 L29,10 L28,29 L4,28 Z"));
    });

    /// <summary>An orange line over a terrain profile with a fall arrow: levels along a drawn line.</summary>
    public static BitmapSource TerrainLine(int px) => Render(px, dc =>
    {
        dc.DrawGeometry(Terrain, null, Geometry.Parse("M2,30 L2,25 9,22 16,24 23,19 30,21 30,30 Z"));
        dc.DrawGeometry(null, new Pen(Line, 2.5) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, Geometry.Parse("M4,6 L14,12 L28,8"));
        dc.DrawGeometry(Arrow, null, Geometry.Parse("M8,14 H18 V12 L23,15.5 L18,19 V17 H8 Z"));
    });

    /// <summary>Three plain blocks of different heights on a terrain line: the buildings around the lot.</summary>
    public static BitmapSource Context(int px) => Render(px, dc =>
    {
        dc.DrawGeometry(Terrain, null, Geometry.Parse("M2,30 L2,26 10,24.5 18,25.5 30,24 30,30 Z"));
        var edge = new Pen(Arrow, 1.6) { LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(Brushes.White, edge, Geometry.Parse("M3,25 V14 H11 V24.6 Z"));
        dc.DrawGeometry(Brushes.White, edge, Geometry.Parse("M12,24.4 V6 H21 V25.2 Z"));
        dc.DrawGeometry(Brushes.White, edge, Geometry.Parse("M22,25 V17 H29 V24.2 Z"));
    });

    /// <summary>A sheet with a folded corner and an arrow out of it: write the files.</summary>
    public static BitmapSource Export(int px) => Render(px, dc =>
    {
        dc.DrawGeometry(Brushes.White, new Pen(Terrain, 2) { LineJoin = PenLineJoin.Round }, Geometry.Parse("M5,3 H17 L23,9 V29 H5 Z M17,3 V9 H23"));
        dc.DrawGeometry(Terrain, null, Geometry.Parse("M7,27 L7,22 11,19 15,21 21,17 21,27 Z"));
        dc.DrawGeometry(Arrow, null, Geometry.Parse("M14,11.5 H22 V8 L29,13.5 L22,19 V15.5 H14 Z"));
    });

    /// <summary>An "i" in a ring: about Jega.</summary>
    public static BitmapSource About(int px) => Render(px, dc =>
    {
        dc.DrawEllipse(null, new Pen(Terrain, 2.5), new Point(16, 16), 12.5, 12.5);
        dc.DrawEllipse(Arrow, null, new Point(16, 9.5), 2.2, 2.2);
        dc.DrawRectangle(Arrow, null, new Rect(14, 13.5, 4, 11));
    });

    private static BitmapSource Render(int px, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(px / 32.0, px / 32.0));
            draw(dc);
            dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
