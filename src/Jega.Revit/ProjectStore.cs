using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace Jega.Revit;

/// <summary>The site from Load from Address: the lot's corners (mm from the site's 0,0, true north), the highest
/// terrain along the house (mm DVR90), the address and its point (ETRS89/UTM32, m); null where not known.</summary>
internal sealed record SiteData(string Address, List<Core.Point2> Lot, double? HouseGroundMm, double? AddressE, double? AddressN);

/// <summary>What Jega keeps in the model, so the project doesn't depend on a file path.</summary>
internal sealed record ProjectSettings(
    string Csv, string FileName, string FilePath, string ImportedUtc, int LastOffsetMm, int SamplingMm);

/// <summary>
/// Extensible Storage on one DataStorage element: the terrain points themselves (the CSV text, parsed again with
/// TerrainGrid on use, so there is one reader), where they came from, and the last used settings. Also marks
/// the elements Jega draws, so a new run replaces them instead of adding more.
/// </summary>
internal static class ProjectStore
{
    // Fixed forever: a new layout gets a new GUID and SchemaVersion, never a changed schema under the same GUID.
    // Every schema is readable by anyone and written only by Jega (VendorId, the .addin's): another add-in or a
    // script can read what Jega keeps, not change it.
    private static readonly Guid SettingsGuid = new("629cadf7-f6d3-44f6-9fb7-43d609f9558f");
    private static readonly Guid MarkGuid = new("ebc7dc20-447c-4c69-8b5d-9d32f778d837");
    // The context file (buildings around the lot) on a DataStorage of its own, so the settings schema stays as it is.
    private static readonly Guid ContextGuid = new("3ec034ae-fb35-495e-8a49-d9e0ede1479f");
    // What Load from Address fetched around the lot for the site plan (Core.Surroundings, JSON), to draw it again.
    private static readonly Guid SurroundingsGuid = new("ec4fbd4c-c87b-4dde-96e3-5770d089ecd3");
    // The Settings window's options (Core.ProjectOptions) as one JSON field: new options need no new schema.
    private static readonly Guid OptionsGuid = new("1824e7c3-73d0-4a0d-9a6c-981333f878dd");
    // The site: what Load from Address knows (the lot, the house's terrain, the address point in UTM32) and
    // where the site plan import was last (Placement.Remember).
    private static readonly Guid SiteGuid = new("d97cbc99-2292-4224-a94a-fb07e100c303");
    // On the Toposolid: what it was made from and at which DVR90 of Revit's 0, so a new run can keep or move it.
    private static readonly Guid BuiltGuid = new("b9862aa3-2091-475b-b5d8-d4e4b9491752");
    // On each terrain line label: the lines it is for (their UniqueIds), so the updater finds the labels of a line
    // deleted or taken out of Jega's styles, whoever drew it and whenever.
    private static readonly Guid LabelLinesGuid = new("c882cb01-b098-4bc6-95f9-489bf8988ed2");
    // Beside the mark: the element it was put on (its UniqueId). Revit copies both with the element, so a copy the user
    // made (a Toposolid for the design) has another's id and isn't taken for Jega's, nor deleted when Jega's is made again.
    private static readonly Guid OwnerGuid = new("cccaf877-a02a-44cc-955b-4dab6684b7cd");
    // On the site plan import: the turn its drawing was given (SiteplanDxf.Write's turnDeg, a plan drawn in the
    // project's working direction), so the site's north is the import's own turn plus this (Placement.NorthOf).
    private static readonly Guid DrawnTurnGuid = new("5b0a7c1e-3d2f-4a86-9c41-7e2d6f8b1a93");
    private const int SchemaVersion = 1;
    // The .addin's VendorId (Jega.Revit.csproj): Revit lets only an add-in with this id write Jega's schemas.
    private const string VendorId = "JEGA";

    public static ProjectSettings? Read(Document doc) => Read(doc, withCsv: true)?.Settings;

    /// <summary>
    /// As Read, with the DataStorage it is on, but the CSV left out (""): a terrain can be up to about 30 MB of text,
    /// too much to take out on every edit (StoredTerrain keeps its grid by the DataStorage and ImportedUtc instead).
    /// Never Write these settings back: without their CSV, the terrain would be gone.
    /// </summary>
    public static (ProjectSettings Settings, ElementId Storage)? ReadWithoutCsv(Document doc) => Read(doc, withCsv: false);

    private static (ProjectSettings Settings, ElementId Storage)? Read(Document doc, bool withCsv)
    {
        var schema = Schema.Lookup(SettingsGuid);
        if (schema is null || Storage(doc, schema) is not { } ds)
        {
            return null;
        }
        var e = ds.GetEntity(schema);
        return (new ProjectSettings(withCsv ? e.Get<string>("Csv") : "", e.Get<string>("FileName"), e.Get<string>("FilePath"), e.Get<string>("ImportedUtc"),
            e.Get<int>("LastOffsetMm"), e.Get<int>("SamplingMm")), ds.Id);
    }

    /// <summary>Inside an open transaction.</summary>
    public static void Write(Document doc, ProjectSettings s)
    {
        var schema = SettingsSchema();
        var ds = Storage(doc, schema) ?? DataStorage.Create(doc);
        var e = new Entity(schema);
        e.Set("SchemaVersion", SchemaVersion);
        e.Set("Csv", s.Csv);
        e.Set("FileName", s.FileName);
        // The name only, never where it was on this PC: the .rvt gets sent on (a model from before kept the whole
        // path here; written again, it has the name). The field stays: a schema's layout is fixed.
        e.Set("FilePath", s.FileName);
        e.Set("ImportedUtc", s.ImportedUtc);
        e.Set("LastOffsetMm", s.LastOffsetMm);
        e.Set("SamplingMm", s.SamplingMm);
        // Unused (ProjectOptions.FloorDvr90Mm keeps it, also without a terrain); the field stays.
        e.Set("Dvr90OverrideMm", "");
        ds.SetEntity(e);
    }

    /// <summary>
    /// Inside an open transaction. The project's offset (mm, level = terrain + offset), and the terrain line labels
    /// made again with it. Returns why the labels couldn't be updated (TerrainLines.Update), or null.
    /// </summary>
    public static string? WriteOffset(Document doc, int offsetMm)
    {
        if (Read(doc) is not { } stored || stored.LastOffsetMm == offsetMm)
        {
            return null;
        }
        Write(doc, stored with { LastOffsetMm = offsetMm });
        return TerrainLines.Update(doc);
    }

    /// <summary>
    /// Inside an open transaction. A new terrain (the CSV's text, from $source: the file's name, never its folder, or
    /// the address it was fetched for), keeping the project's offset.
    /// </summary>
    public static void WriteTerrain(Document doc, string csv, string source)
    {
        var previous = Read(doc);
        Write(doc, new ProjectSettings(csv, source, source, DateTime.UtcNow.ToString("o"),
            previous?.LastOffsetMm ?? 0, (int)Core.EdgeSampler.DefaultStep));
    }

    /// <summary>The context stored in the model (Context button, Context.Parse reads it), or null if none has been fetched.</summary>
    public static string? ReadContext(Document doc) =>
        Schema.Lookup(ContextGuid) is { } schema && Storage(doc, schema) is { } ds ? ds.GetEntity(schema).Get<string>("Json") : null;

    /// <summary>Inside an open transaction. The stored context goes (its own DataStorage with it): it was for another address.</summary>
    public static void ClearContext(Document doc)
    {
        if (Schema.Lookup(ContextGuid) is { } schema && Storage(doc, schema) is { } ds)
        {
            doc.Delete(ds.Id);
        }
    }

    /// <summary>Inside an open transaction. Keeps the context file's text in the model, on its own DataStorage.</summary>
    public static void WriteContext(Document doc, string json, string fileName)
    {
        var schema = ContextSchema();
        var ds = Storage(doc, schema) ?? DataStorage.Create(doc);
        var e = new Entity(schema);
        e.Set("Json", json);
        e.Set("FileName", fileName);
        e.Set("ImportedUtc", DateTime.UtcNow.ToString("o"));
        ds.SetEntity(e);
    }

    /// <summary>The surroundings Load from Address fetched (Core.Surroundings JSON), or null (never: a terrain of the user's own).</summary>
    public static string? ReadSurroundings(Document doc) =>
        Schema.Lookup(SurroundingsGuid) is { } schema && Storage(doc, schema) is { } ds ? ds.GetEntity(schema).Get<string>("Json") : null;

    /// <summary>Inside an open transaction. Keeps the surroundings in the model, on their own DataStorage.</summary>
    public static void WriteSurroundings(Document doc, string json)
    {
        var schema = SurroundingsSchema();
        var ds = Storage(doc, schema) ?? DataStorage.Create(doc);
        var e = new Entity(schema);
        e.Set("Json", json);
        ds.SetEntity(e);
    }

    private static Schema SurroundingsSchema()
    {
        if (Schema.Lookup(SurroundingsGuid) is { } existing)
        {
            return existing;
        }
        var b = new SchemaBuilder(SurroundingsGuid);
        b.SetSchemaName("JegaSurroundings");
        b.SetVendorId(VendorId);
        b.SetReadAccessLevel(AccessLevel.Public);
        b.SetWriteAccessLevel(AccessLevel.Vendor);
        b.SetDocumentation("Jega: the lots, buildings and road edges fetched around the lot for the site plan (JSON, UTM32 m), to draw it again for another area.");
        b.AddSimpleField("Json", typeof(string));
        return b.Finish();
    }

    /// <summary>The project's Jega settings (the Settings window); the defaults when none are saved.</summary>
    public static Core.ProjectOptions ReadOptions(Document doc) => ReadOptions(doc, out _);

    /// <summary>As ReadOptions; $unreadable: settings are stored but couldn't be read, so these are the defaults.</summary>
    public static Core.ProjectOptions ReadOptions(Document doc, out bool unreadable) =>
        Core.ProjectOptions.FromJson(Schema.Lookup(OptionsGuid) is { } schema && Storage(doc, schema) is { } ds
            ? ds.GetEntity(schema).Get<string>("Json") : null, out unreadable);

    /// <summary>Inside an open transaction.</summary>
    public static void WriteOptions(Document doc, Core.ProjectOptions options)
    {
        var schema = OptionsSchema();
        var ds = Storage(doc, schema) ?? DataStorage.Create(doc);
        var e = new Entity(schema);
        e.Set("Json", options.ToJson());
        ds.SetEntity(e);
    }

    private static Schema OptionsSchema()
    {
        if (Schema.Lookup(OptionsGuid) is { } existing)
        {
            return existing;
        }
        var b = new SchemaBuilder(OptionsGuid);
        b.SetSchemaName("JegaOptions");
        b.SetVendorId(VendorId);
        b.SetReadAccessLevel(AccessLevel.Public);
        b.SetWriteAccessLevel(AccessLevel.Vendor);
        b.SetDocumentation("Jega: the project's settings from the Settings window, as JSON (Jega.Core.ProjectOptions).");
        b.AddSimpleField("Json", typeof(string));
        return b.Finish();
    }

    /// <summary>The site from Load from Address; null if it hasn't run here.</summary>
    public static SiteData? ReadSite(Document doc)
    {
        if (SiteEntity(doc) is not { } e || e.Get<string>("Address").Length == 0)
        {
            return null;
        }
        var lot = e.Get<string>("Lot").Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split(','))
            .Where(p => p.Length == 2 && Number(p[0]) is not null && Number(p[1]) is not null)
            .Select(p => new Core.Point2(Number(p[0])!.Value, Number(p[1])!.Value)).ToList();
        return new SiteData(e.Get<string>("Address"), lot, Number(e.Get<string>("HouseGroundMm")), Number(e.Get<string>("AddressE")), Number(e.Get<string>("AddressN")));
    }

    /// <summary>Inside an open transaction. $lotMm: mm from the site's 0,0; $e, $n: the address point, UTM32 m.</summary>
    public static void WriteSite(Document doc, string address, double e, double n, IEnumerable<(double X, double Y)> lotMm, double? houseGroundMm) =>
        UpdateSite(doc, entity =>
        {
            entity.Set("Address", address);
            entity.Set("AddressE", Text(e));
            entity.Set("AddressN", Text(n));
            entity.Set("Lot", string.Join(";", lotMm.Select(p => $"{Text(p.X)},{Text(p.Y)}")));
            entity.Set("HouseGroundMm", houseGroundMm is { } h ? Text(h) : "");
        });

    /// <summary>Where the site plan import was last: its 0,0 (internal feet) and north (radians
    /// counter-clockwise from project north); null if never kept.</summary>
    public static (double X, double Y, double Angle)? ReadFrame(Document doc) =>
        SiteEntity(doc) is { } e && Number(e.Get<string>("FrameX")) is { } x && Number(e.Get<string>("FrameY")) is { } y
            && Number(e.Get<string>("FrameAngle")) is { } a ? (x, y, a) : null;

    /// <summary>Inside an open transaction. Said in jega.log with who wrote it ($by, filled in by the compiler), so a
    /// frame that turns back (0° under an import at 11°) can be traced.</summary>
    public static void WriteFrame(Document doc, double x, double y, double angle, [System.Runtime.CompilerServices.CallerMemberName] string by = "")
    {
        JegaLog.Note(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"Frame written by {by}: {Units.MmFromFeet(x):0},{Units.MmFromFeet(y):0} mm, {Core.Angles.ToDegrees(angle):0.###}° (was {(ReadFrame(doc) is { } f ? $"{Core.Angles.ToDegrees(f.Angle):0.###}°" : "none")})"));
        UpdateSite(doc, entity =>
        {
            entity.Set("FrameX", Text(x));
            entity.Set("FrameY", Text(y));
            entity.Set("FrameAngle", Text(angle));
        });
    }

    private static Entity? SiteEntity(Document doc) =>
        Schema.Lookup(SiteGuid) is { } schema && Storage(doc, schema) is { } ds ? ds.GetEntity(schema) : null;

    // The site and the frame are written at different times: each keeps the other's fields.
    private static void UpdateSite(Document doc, Action<Entity> edit)
    {
        var schema = SiteSchema();
        var ds = Storage(doc, schema) ?? DataStorage.Create(doc);
        var e = ds.GetEntity(schema);
        if (!e.IsValid())
        {
            e = new Entity(schema);
            foreach (var field in schema.ListFields())
            {
                e.Set(field.FieldName, "");
            }
        }
        edit(e);
        ds.SetEntity(e);
    }

    private static Schema SiteSchema()
    {
        if (Schema.Lookup(SiteGuid) is { } existing)
        {
            return existing;
        }
        var b = new SchemaBuilder(SiteGuid);
        b.SetSchemaName("JegaSite");
        b.SetVendorId(VendorId);
        b.SetReadAccessLevel(AccessLevel.Public);
        b.SetWriteAccessLevel(AccessLevel.Vendor);
        b.SetDocumentation("Jega: the site from Load from Address (lot in mm from the address point, house terrain mm DVR90, " +
                           "address point UTM32 m) and the situationsplan import's last place (internal feet, radians). Numbers as text.");
        foreach (var field in new[] { "Address", "AddressE", "AddressN", "Lot", "HouseGroundMm", "FrameX", "FrameY", "FrameAngle" })
        {
            b.AddSimpleField(field, typeof(string));
        }
        return b.Finish();
    }

    // Retired: JegaOrientationBefore, 7c1e4b2a-5f83-4d6e-9a17-2b8c0d3e6f41, the project's orientation kept while the
    // site plan set true north at Refresh (gone: Jega never sets true north now); JegaImportOrigin,
    // af005244-bbdc-47e4-952f-51661c55c2a5, the site plan import's own 0,0 when it was drawn from the lot's centre (gone:
    // the drawing's 0,0 is always the site's). Left in older models, unread; never reuse the GUIDs.

    // The project's own shared coordinates before Jega gave it real ones ("real coordinates", Placement.Georeference),
    // so leaving that anchor can put them back (Settings). Its own schema: a schema can't get new fields once it's in
    // a model.
    private static readonly Guid CoordinatesGuid = new("b0aad07a-258f-4622-ac88-923444aaac6e");

    /// <summary>The shared coordinates kept when Jega gave the project real ones (shared east/west and north/south of
    /// Revit's 0 in feet, the angle to true north in radians); null if none.</summary>
    public static (double EastWest, double NorthSouth, double Angle)? ReadCoordinatesBefore(Document doc) => ReadPosition(doc, CoordinatesGuid);

    /// <summary>Inside an open transaction. Null forgets it.</summary>
    public static void WriteCoordinatesBefore(Document doc, (double EastWest, double NorthSouth, double Angle)? before) =>
        WritePosition(doc, CoordinatesGuid, "JegaCoordinatesBefore", "the project's shared coordinates before Jega gave it real ones", before);

    private static (double EastWest, double NorthSouth, double Angle)? ReadPosition(Document doc, Guid guid) =>
        Schema.Lookup(guid) is { } schema && Storage(doc, schema) is { } ds && ds.GetEntity(schema) is var e
        && Number(e.Get<string>("EastWest")) is { } ew && Number(e.Get<string>("NorthSouth")) is { } ns && Number(e.Get<string>("Angle")) is { } a
            ? (ew, ns, a) : null;

    private static void WritePosition(Document doc, Guid guid, string name, string what, (double EastWest, double NorthSouth, double Angle)? before)
    {
        var schema = Schema.Lookup(guid) ?? PositionSchema(guid, name, what);
        var ds = Storage(doc, schema) ?? DataStorage.Create(doc);
        var e = new Entity(schema);
        e.Set("EastWest", before is { } b ? Text(b.EastWest) : "");
        e.Set("NorthSouth", before is { } n ? Text(n.NorthSouth) : "");
        e.Set("Angle", before is { } a ? Text(a.Angle) : "");
        ds.SetEntity(e);
    }

    private static Schema PositionSchema(Guid guid, string name, string what)
    {
        var b = new SchemaBuilder(guid);
        b.SetSchemaName(name);
        b.SetVendorId(VendorId);
        b.SetReadAccessLevel(AccessLevel.Public);
        b.SetWriteAccessLevel(AccessLevel.Vendor);
        b.SetDocumentation($"Jega: {what} (shared E/W and N/S of Revit's 0 in feet, angle in radians). Numbers as text; empty = none kept.");
        foreach (var field in new[] { "EastWest", "NorthSouth", "Angle" })
        {
            b.AddSimpleField(field, typeof(string));
        }
        return b.Finish();
    }

    // Where "Keep the site plan in place" holds the site's 0,0, from the Revit anchor (internal feet): a neighbour
    // loaded with "Keep the old place" lies its true offset from the first address, and Refresh mustn't pull it onto the
    // first one's place (seen with two houses on neighbouring lots). Its own schema, as above.
    private static readonly Guid AnchorOffsetGuid = new("e2f70f4d-f319-43ba-bbbb-65d58c69b38a");

    /// <summary>The site's 0,0's offset from the Revit anchor that Keep holds (internal feet); null: none, on the anchor.</summary>
    public static (double X, double Y)? ReadAnchorOffset(Document doc) =>
        Schema.Lookup(AnchorOffsetGuid) is { } schema && Storage(doc, schema) is { } ds && ds.GetEntity(schema) is var e
        && Number(e.Get<string>("X")) is { } x && Number(e.Get<string>("Y")) is { } y
            ? (x, y) : null;

    /// <summary>Inside an open transaction. Null forgets it.</summary>
    public static void WriteAnchorOffset(Document doc, (double X, double Y)? offset)
    {
        if (offset is null && Schema.Lookup(AnchorOffsetGuid) is null)
        {
            return;
        }
        var schema = Schema.Lookup(AnchorOffsetGuid) ?? AnchorOffsetSchema();
        var ds = Storage(doc, schema) ?? DataStorage.Create(doc);
        var e = new Entity(schema);
        e.Set("X", offset is { } a ? Text(a.X) : "");
        e.Set("Y", offset is { } b ? Text(b.Y) : "");
        ds.SetEntity(e);
    }

    private static Schema AnchorOffsetSchema()
    {
        var b = new SchemaBuilder(AnchorOffsetGuid);
        b.SetSchemaName("JegaAnchorOffset");
        b.SetVendorId(VendorId);
        b.SetReadAccessLevel(AccessLevel.Public);
        b.SetWriteAccessLevel(AccessLevel.Vendor);
        b.SetDocumentation("Jega: where the site plan's anchor is held, from the Revit anchor (internal feet), after a neighbour " +
                           "address kept its true offset. Numbers as text; empty = on the anchor.");
        foreach (var field in new[] { "X", "Y" })
        {
            b.AddSimpleField(field, typeof(string));
        }
        return b.Finish();
    }

    private static string Text(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    private static double? Number(string s) =>
        double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>
    /// What the element was made from (a fingerprint of its inputs) and the DVR90 of Revit's 0 then, in mm. Inside a
    /// transaction.
    /// </summary>
    public static void WriteBuilt(Element element, string inputs, double dvr90Mm)
    {
        var e = new Entity(BuiltSchema());
        e.Set("Inputs", inputs);
        e.Set("Dvr90Mm", dvr90Mm.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        element.SetEntity(e);
    }

    /// <summary>What WriteBuilt stored on the element, or null (not made by Jega).</summary>
    public static (string Inputs, double Dvr90Mm)? ReadBuilt(Element element)
    {
        if (Schema.Lookup(BuiltGuid) is not { } schema || element.GetEntity(schema) is not { } e || !e.IsValid()
            || !double.TryParse(e.Get<string>("Dvr90Mm"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mm))
        {
            return null;
        }
        return (e.Get<string>("Inputs"), mm);
    }

    private static Schema BuiltSchema()
    {
        if (Schema.Lookup(BuiltGuid) is { } existing)
        {
            return existing;
        }
        var b = new SchemaBuilder(BuiltGuid);
        b.SetSchemaName("JegaBuilt");
        b.SetVendorId(VendorId);
        b.SetReadAccessLevel(AccessLevel.Public);
        b.SetWriteAccessLevel(AccessLevel.Vendor);
        b.SetDocumentation("Jega: what a Toposolid was made from (a fingerprint) and the DVR90 of Revit's 0 then (mm, as text).");
        b.AddSimpleField("Inputs", typeof(string));
        b.AddSimpleField("Dvr90Mm", typeof(string));
        return b.Finish();
    }

    /// <summary>
    /// Marks an element as Jega's, with its role (e.g. "GridFrame"). Inside a transaction. $own: only this element is
    /// Jega's, not a copy of it (one Jega makes again and deletes: the Toposolid, the frame); without it a copy is Jega's
    /// too (a terrain line copied is a terrain line).
    /// </summary>
    public static void Mark(Element element, string role, bool own = false)
    {
        var e = new Entity(MarkSchema());
        e.Set("Role", role);
        element.SetEntity(e);
        if (own)
        {
            var owner = new Entity(OwnerSchema());
            owner.Set("Element", element.UniqueId);
            element.SetEntity(owner);
        }
    }

    /// <summary>Takes the mark off again. Inside a transaction.</summary>
    public static void Unmark(Element element)
    {
        foreach (var guid in new[] { MarkGuid, OwnerGuid, DrawnTurnGuid })
        {
            if (Schema.Lookup(guid) is { } schema)
            {
                element.DeleteEntity(schema);
            }
        }
    }

    /// <summary>Inside a transaction. The turn the site plan import's drawing was given (degrees counter-clockwise,
    /// SiteplanDxf.Write's turnDeg; 0 = drawn true north up).</summary>
    public static void WriteDrawnTurn(Element import, double degrees)
    {
        var e = new Entity(DrawnTurnSchema());
        e.Set("TurnDeg", Text(degrees));
        import.SetEntity(e);
    }

    /// <summary>The turn the site plan import's drawing was given, degrees; 0 for one drawn true north up, or from
    /// before the turn was kept, or imported by hand.</summary>
    public static double ReadDrawnTurn(Element import) =>
        Schema.Lookup(DrawnTurnGuid) is { } schema && import.GetEntity(schema) is { } e && e.IsValid()
            && Number(e.Get<string>("TurnDeg")) is { } degrees ? degrees : 0;

    private static Schema DrawnTurnSchema()
    {
        if (Schema.Lookup(DrawnTurnGuid) is { } existing)
        {
            return existing;
        }
        var b = new SchemaBuilder(DrawnTurnGuid);
        b.SetSchemaName("JegaSiteplanTurn");
        b.SetVendorId(VendorId);
        b.SetReadAccessLevel(AccessLevel.Public);
        b.SetWriteAccessLevel(AccessLevel.Vendor);
        b.SetDocumentation("Jega: the turn the site plan import's drawing was given (degrees counter-clockwise, as text): " +
                           "its north is the import's own turn plus this.");
        b.AddSimpleField("TurnDeg", typeof(string));
        return b.Finish();
    }

    /// <summary>True if the element is Jega's with this role (not a copy of one).</summary>
    public static bool Is(Element element, string role) =>
        Schema.Lookup(MarkGuid) is { } schema && element.GetEntity(schema) is { } e && e.IsValid() && e.Get<string>("Role") == role
        && !IsCopy(element);

    /// <summary>Jega's elements with this role, in any view; copies the user made of them left out.</summary>
    public static List<Element> Marked(Document doc, string role, Type ofClass) =>
        WithRole(doc, role, ofClass).Where(el => !IsCopy(el)).ToList();

    /// <summary>How many copies the user made of Jega's elements with this role: marked, but not Jega's (left alone).</summary>
    public static int Copies(Document doc, string role, Type ofClass) => WithRole(doc, role, ofClass).Count(IsCopy);

    /// <summary>
    /// For jega.log when the import with this role isn't found: every import in the model (linked CAD too) with Jega's
    /// mark, Jega's own or a copy, and in a shared model its workset, who holds it and the central's say on it; so none
    /// marked can be told from two, or from a copy. Reads only.
    /// </summary>
    public static string TraceImports(Document doc, string role)
    {
        var mark = Schema.Lookup(MarkGuid);
        var owner = Schema.Lookup(OwnerGuid);
        var imports = new FilteredElementCollector(doc).OfClass(typeof(ImportInstance)).Cast<ImportInstance>().ToList();
        var lines = new List<string>
        {
            $"{role} import not found: {imports.Count} imports, {Marked(doc, role, typeof(ImportInstance)).Count} marked " +
            $"{role}, {Copies(doc, role, typeof(ImportInstance))} copies; schemas loaded: mark {mark is not null}, " +
            $"owner {owner is not null}; shared {doc.IsWorkshared}{Central(doc)}",
        };
        foreach (var import in imports)
        {
            var marked = mark is not null && import.GetEntity(mark) is { } m && m.IsValid() ? m.Get<string>("Role") : "no mark";
            var own = owner is not null && import.GetEntity(owner) is { } o && o.IsValid()
                ? o.Get<string>("Element") == import.UniqueId ? "own" : $"copy of {o.Get<string>("Element")}"
                : "no owner";
            lines.Add($"  {import.Id} {import.UniqueId} \"{doc.GetElement(import.GetTypeId())?.Name}\": {marked}, {own}, " +
                      $"linked {import.IsLinked}, view-specific {import.ViewSpecific}{SharedState(doc, import)}");
        }
        return string.Join("\n", lines);
    }

    // The central model's path, for jega.log; "" in a model that isn't shared.
    private static string Central(Document doc)
    {
        if (!doc.IsWorkshared)
        {
            return "";
        }
        try
        {
            return $", central \"{ModelPathUtils.ConvertModelPathToUserVisiblePath(doc.GetWorksharingCentralModelPath())}\"";
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException e)
        {
            return $", central unread ({e.Message})";
        }
    }

    // An element's workset, who holds it and whether the central has a newer one, for jega.log; "" in a model that
    // isn't shared.
    private static string SharedState(Document doc, Element element)
    {
        if (!doc.IsWorkshared)
        {
            return "";
        }
        try
        {
            var workset = doc.GetWorksetTable().GetWorkset(element.WorksetId).Name;
            var status = WorksharingUtils.GetCheckoutStatus(doc, element.Id, out var holder);
            return $", workset \"{workset}\", {status}{(string.IsNullOrEmpty(holder) ? "" : $" ({holder})")}, " +
                   $"central {WorksharingUtils.GetModelUpdatesStatus(doc, element.Id)}";
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException e)
        {
            return $", shared state unread ({e.Message})";
        }
    }

    private static IEnumerable<Element> WithRole(Document doc, string role, Type ofClass)
    {
        if (Schema.Lookup(MarkGuid) is not { } schema)
        {
            return [];
        }
        // Only elements with Jega's mark are read: the updater asks on every line drawn, in models with thousands.
        return new FilteredElementCollector(doc).OfClass(ofClass).WherePasses(new ExtensibleStorageFilter(MarkGuid))
            .Where(el => el.GetEntity(schema) is { } e && e.IsValid() && e.Get<string>("Role") == role);
    }

    // Carries the mark of another element (Mark's $own): a copy of it. No owner kept: Jega's, as a copy is too.
    private static bool IsCopy(Element element) =>
        Schema.Lookup(OwnerGuid) is { } schema && element.GetEntity(schema) is { } e && e.IsValid() && e.Get<string>("Element") != element.UniqueId;

    /// <summary>Inside a transaction. A terrain line label, and the lines it is for ($lines: their UniqueIds).</summary>
    public static void MarkLines(Element label, IEnumerable<string> lines)
    {
        var e = new Entity(LabelLinesSchema());
        e.Set("Lines", string.Join(" ", lines));
        label.SetEntity(e);
    }

    /// <summary>
    /// The labels that know their lines (made since Jega kept them; older ones are made again by Refresh), in $view
    /// only, or in every view.
    /// </summary>
    public static IEnumerable<Element> LabelsWithLines(Document doc, ElementId? view = null) =>
        Schema.Lookup(LabelLinesGuid) is null
            ? []
            : (view is null ? new FilteredElementCollector(doc) : new FilteredElementCollector(doc, view))
                .WherePasses(new ExtensibleStorageFilter(LabelLinesGuid)).WhereElementIsNotElementType().ToList();

    /// <summary>The lines a label is for, by UniqueId; none for a label from before.</summary>
    public static string[] LinesOf(Element label) =>
        Schema.Lookup(LabelLinesGuid) is { } schema && label.GetEntity(schema) is { } e && e.IsValid()
            ? e.Get<string>("Lines").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            : [];

    // The one with the lowest id. Two people writing a schema for the first time in a shared model, each before the
    // other's sync, give it two DataStorage; by the lowest id everyone reads and writes the same one after syncing (a
    // collector's order isn't promised), and the other's first write is passed over.
    private static DataStorage? Storage(Document doc, Schema schema) =>
        new FilteredElementCollector(doc).OfClass(typeof(DataStorage)).Cast<DataStorage>()
            .Where(ds => ds.GetEntity(schema).IsValid())
            .MinBy(ds => ds.Id.Value);

    private static Schema SettingsSchema()
    {
        if (Schema.Lookup(SettingsGuid) is { } existing)
        {
            return existing;
        }
        var b = new SchemaBuilder(SettingsGuid);
        b.SetSchemaName("JegaSettings");
        b.SetVendorId(VendorId);
        b.SetReadAccessLevel(AccessLevel.Public);
        b.SetWriteAccessLevel(AccessLevel.Vendor);
        b.SetDocumentation("Jega: the terrain grid (CSV text, mm, DVR90) and the project's last used settings.");
        b.AddSimpleField("SchemaVersion", typeof(int));
        b.AddSimpleField("Csv", typeof(string));
        b.AddSimpleField("FileName", typeof(string));
        b.AddSimpleField("FilePath", typeof(string));
        b.AddSimpleField("ImportedUtc", typeof(string));
        b.AddSimpleField("LastOffsetMm", typeof(int));
        b.AddSimpleField("SamplingMm", typeof(int));
        // Unused, always "" (ProjectOptions.FloorDvr90Mm); kept, as a schema's layout is fixed.
        b.AddSimpleField("Dvr90OverrideMm", typeof(string));
        return b.Finish();
    }

    private static Schema ContextSchema()
    {
        if (Schema.Lookup(ContextGuid) is { } existing)
        {
            return existing;
        }
        var b = new SchemaBuilder(ContextGuid);
        b.SetSchemaName("JegaContext");
        b.SetVendorId(VendorId);
        b.SetReadAccessLevel(AccessLevel.Public);
        b.SetWriteAccessLevel(AccessLevel.Vendor);
        b.SetDocumentation("Jega: the buildings around the lot (context JSON, version 1), shown as masses by the Context button.");
        b.AddSimpleField("Json", typeof(string));
        b.AddSimpleField("FileName", typeof(string));
        b.AddSimpleField("ImportedUtc", typeof(string));
        return b.Finish();
    }

    private static Schema MarkSchema()
    {
        if (Schema.Lookup(MarkGuid) is { } existing)
        {
            return existing;
        }
        var b = new SchemaBuilder(MarkGuid);
        b.SetSchemaName("JegaElement");
        b.SetVendorId(VendorId);
        b.SetReadAccessLevel(AccessLevel.Public);
        b.SetWriteAccessLevel(AccessLevel.Vendor);
        b.AddSimpleField("Role", typeof(string));
        return b.Finish();
    }

    private static Schema OwnerSchema()
    {
        if (Schema.Lookup(OwnerGuid) is { } existing)
        {
            return existing;
        }
        var b = new SchemaBuilder(OwnerGuid);
        b.SetSchemaName("JegaOwner");
        b.SetVendorId(VendorId);
        b.SetReadAccessLevel(AccessLevel.Public);
        b.SetWriteAccessLevel(AccessLevel.Vendor);
        b.SetDocumentation("Jega: the element Jega's mark was put on (UniqueId); on another element it is a copy, not Jega's.");
        b.AddSimpleField("Element", typeof(string));
        return b.Finish();
    }

    private static Schema LabelLinesSchema()
    {
        if (Schema.Lookup(LabelLinesGuid) is { } existing)
        {
            return existing;
        }
        var b = new SchemaBuilder(LabelLinesGuid);
        b.SetSchemaName("JegaLabelLines");
        b.SetVendorId(VendorId);
        b.SetReadAccessLevel(AccessLevel.Public);
        b.SetWriteAccessLevel(AccessLevel.Vendor);
        b.SetDocumentation("Jega: the terrain lines a label is for, by UniqueId, space separated.");
        b.AddSimpleField("Lines", typeof(string));
        return b.Finish();
    }
}
