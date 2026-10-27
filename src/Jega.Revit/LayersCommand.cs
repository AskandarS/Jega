using Autodesk.Revit.Attributes;
using Jega.Revit.Resources;

namespace Jega.Revit;

/// <summary>
/// "Layers" in the ribbon: Settings opened on its Layers tab, so the site plan's layers are a click away, as a family's
/// switches would be (a DXF import can't carry parameters of its own). Tick, OK, and the site plan is drawn again;
/// the rest is Settings as ever.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class LayersCommand : SettingsCommand
{
    protected override string Button => Strings.Ribbon_Layers;

    protected override bool OnLayers => true;
}
