using Jega.Core;

namespace Jega.Core.Tests;

public class ProjectOptionsTests
{
    [Fact]
    public void WhatIsSavedIsReadBack()
    {
        var o = new ProjectOptions { ContextIncludesLot = true, ToposolidThicknessMm = 300, SiteplanMarginM = 25, SetbackM = 5,
            ImportSiteplan = false, ImportViewOnly = false, SiteplanPaperStraight = false, SiteAnchor = ProjectOptions.AnchorShared,
            LabelTextMm = 2.5, ZeroLevel = "a-level", ZeroTerrainAt = ProjectOptions.LevelAverage, HiddenLayers = "KOTER,SKELMAAL",
            FloorDvr90Mm = 10130 };
        var back = ProjectOptions.FromJson(o.ToJson());
        Assert.Equal(o, back);
        Assert.Empty(back.Problems());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    public void NothingOrBrokenGivesTheDefaults(string? json)
    {
        var o = ProjectOptions.FromJson(json);
        Assert.False(o.ContextIncludesLot);   // the existing house is modelled by hand
        Assert.Equal(200, o.ToposolidThicknessMm);
        Assert.Equal(800, o.InteriorStepMm);
        Assert.Equal(400, o.EdgeStepMm);
        Assert.Equal(10, o.SiteplanMarginM);
        Assert.Equal(2.5, o.SetbackM);
        Assert.True(o.ImportSiteplan);
        Assert.True(o.ImportViewOnly);        // the site plan only in the plan it's imported in
        Assert.True(o.SiteplanPaperStraight); // its paper straight in its view, only the map turns
        Assert.Equal(ProjectOptions.AnchorShared, o.SiteAnchor);  // real coordinates by default
        Assert.Equal(3, o.LabelTextMm);
        Assert.Equal("", o.ZeroLevel);        // no terrain point put on a level
        Assert.Equal(ProjectOptions.LevelLowest, o.ZeroTerrainAt);
        Assert.Empty(o.HiddenLayerList);      // every layer drawn
        Assert.Null(o.FloorDvr90Mm);          // Revit 0's DVR90 is the Survey Point's elevation
        Assert.Empty(o.Problems());
    }

    [Fact]
    public void AFloorKeptAt0IsNotTheSurveyPoints()
    {
        // Absolute elevations kept in Jega (a project with links) stay 0, not "read the Survey Point".
        Assert.Equal(0, ProjectOptions.FromJson(new ProjectOptions { FloorDvr90Mm = 0 }.ToJson()).FloorDvr90Mm);
    }

    [Fact]
    public void TheHiddenLayersAreReadAsNames()
    {
        Assert.Equal(["KOTER", "SKELMAAL"], new ProjectOptions { HiddenLayers = "KOTER, SKELMAAL," }.HiddenLayerList);
        // The same layers are the same options, so Settings sees no change (a list would compare by reference).
        Assert.Equal(new ProjectOptions { HiddenLayers = "KOTER" }, ProjectOptions.FromJson(new ProjectOptions { HiddenLayers = "KOTER" }.ToJson()));
    }

    [Fact]
    public void HiddenLayersInAnotherFormAreTheSameAsSettingsWritesThem()
    {
        // Another order, spaces, a layer Config doesn't have and one that can't be left out: Config's names in Config's
        // order, as the window writes them, so an untouched OK in Settings sees no change.
        var stored = new ProjectOptions { HiddenLayers = " KOTER,GRUND, NOSUCH,SKELMAAL " };
        Assert.Equal("SKELMAAL,KOTER", stored.WithLayersAsSettingsWrites().HiddenLayers);
        Assert.Equal(new ProjectOptions { HiddenLayers = "SKELMAAL,KOTER" }, stored.WithLayersAsSettingsWrites());
        Assert.Equal("", new ProjectOptions().WithLayersAsSettingsWrites().HiddenLayers);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(10, true)]
    [InlineData(0.9, false)]
    [InlineData(10.5, false)]
    public void LabelTextSizeFrom1To10Mm(double mm, bool fine) =>
        Assert.Equal(fine, new ProjectOptions { LabelTextMm = mm }.Problems().Count == 0);

    [Fact]
    public void TheLowestTerrainOnALevelAboveRevitsZero()
    {
        // The lowest terrain 1.23 m DVR90 on a level at +2.00: Revit's 0 is at -0.77 m DVR90.
        Assert.Equal(-770, ProjectOptions.Dvr90OnLevel(1230, 2000), 6);
        // On a level at Revit's 0: Revit's 0 is the terrain's own height.
        Assert.Equal(1230, ProjectOptions.Dvr90OnLevel(1230, 0), 6);
    }

    [Fact]
    public void ASettingAnOlderVersionDidntKnowTakesItsDefault()
    {
        // An unknown setting is ignored, a removed one too ("Point on the site" at the lot's centre); a missing one takes
        // its default.
        var o = ProjectOptions.FromJson("""{"ToposolidThicknessMm":250,"SomethingRemoved":"x","ShowSpotLevels":false,"OrientFromSiteplan":true,"SiteReference":"lot"}""");
        Assert.Equal(250, o.ToposolidThicknessMm);
        Assert.Equal(2.5, o.SetbackM);
        Assert.Empty(o.Problems());
    }

    // The stored names are a contract with every model: a renamed property would leave every project's setting behind
    // without a word. A new setting is added here; a renamed one keeps its old name with [JsonPropertyName].
    [Fact]
    public void TheStoredNamesStay()
    {
        using var json = System.Text.Json.JsonDocument.Parse(new ProjectOptions().ToJson());
        string[] names =
        [
            "SiteAnchor", "TerrainLevelAt", "ToposolidThicknessMm",
            "InteriorStepMm", "EdgeStepMm", "LevelCrossMm", "LabelTextMm", "ZeroLevel", "ZeroTerrainAt", "SiteplanMarginM",
            "SetbackM", "ContextIncludesLot", "Language", "ImportSiteplan", "ImportViewOnly", "HiddenLayers", "WrittenBy",
            "RoofMinAreaM2", "RoofRidgeOnly", "RoofSkipCrowded", "FloorDvr90Mm", "SiteplanPaperStraight",
        ];
        Assert.Equal(names.Order(), json.RootElement.EnumerateObject().Select(p => p.Name).Order());
    }

    [Fact]
    public void TheJegaThatWroteThemIsKept()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+(-[\w.]+)?$", Build.Version);
        using var json = System.Text.Json.JsonDocument.Parse(new ProjectOptions().ToJson());
        Assert.Equal(Build.Version, json.RootElement.GetProperty("WrittenBy").GetString());
    }

    [Fact]
    public void ATextSettingOfNullTakesItsDefault()
    {
        var o = ProjectOptions.FromJson("""
            {"SiteAnchor":null,"TerrainLevelAt":null,"ZeroLevel":null,"ZeroTerrainAt":null,"Language":null,"HiddenLayers":null}
            """);
        Assert.Equal(new ProjectOptions(), o);
        Assert.Empty(o.Problems());
    }

    [Fact]
    public void ProblemsAreNamed()
    {
        var o = new ProjectOptions { InteriorStepMm = 100, SiteplanMarginM = 500, SetbackM = -1, SiteAnchor = "frame", LabelTextMm = 0.5, ZeroTerrainAt = "roof" };
        Assert.Equal(6, o.Problems().Count);
    }

    [Fact]
    public void ALanguageIsOneConfigOffers()
    {
        // Saved before the language was: Danish, Config's default.
        Assert.Equal("da-DK", ProjectOptions.FromJson("""{"ToposolidThicknessMm":250}""").Language);
        Assert.Empty(new ProjectOptions { Language = "en-GB" }.Problems());
        Assert.Empty(new ProjectOptions { Language = "da-dk" }.Problems());
        Assert.Single(new ProjectOptions { Language = "sv-SE" }.Problems());
    }

    [Theory]
    [InlineData("da-DK", "da-DK")]
    [InlineData("da-dk", "da-DK")]
    [InlineData("EN-gb", "en-GB")]
    [InlineData("sv-SE", "da-DK")]  // not offered: the default
    [InlineData(null, "da-DK")]
    [InlineData("", "da-DK")]
    public void A_stored_language_means_one_Config_offers(string? stored, string spoken) =>
        Assert.Equal(spoken, Language.Of(stored));
}
