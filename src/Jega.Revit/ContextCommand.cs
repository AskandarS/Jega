using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Jega.Core;
using Jega.Revit.Resources;
using static Jega.Revit.Ui;

namespace Jega.Revit;

/// <summary>
/// "Context Buildings", an on/off switch for the buildings around the lot as masses (ContextMasses). Load from Address
/// keeps them in the model from its own calls, so on and off need no network. Only without them (a large lot or a coarser
/// grid, where the load's ring would be smaller, or no surface) it fetches them (SiteFetcher.FetchContext, the same key);
/// Shift+click fetches again. One Undo.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class ContextCommand : JegaCommand
{
    protected override string Button => Strings.Ribbon_Context;

    // The masses are placed by the frame.
    protected override bool UsesFrame => true;

    protected override Result Run(ExternalCommandData data, Document doc)
    {
        var fetchAgain = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        // Off: the masses go, the stored context stays.
        if (ContextMasses.Shown(doc) && !fetchAgain)
        {
            using var t = new Transaction(doc, Strings.Undo_ContextOff);
            t.Start();
            ContextMasses.Remove(doc);
            t.Commit();
            return Result.Succeeded;
        }

        var json = fetchAgain ? null : ProjectStore.ReadContext(doc);
        var fetched = json is null;
        if (json is null)
        {
            if (ProjectStore.ReadSite(doc) is not { AddressE: { } x, AddressN: { } y, Lot.Count: >= 3 } site)
            {
                Receipt(new ReceiptText(Strings.Context_LoadFirst) { Warning = true });
                return Result.Cancelled;
            }
            if (Keys.FetcherOrAsk(data) is not { } fetcher)
            {
                return Result.Cancelled;
            }
            var window = new FetchWindow(ct => fetcher.FetchContext(x, y, site.Lot, null, ct, ProjectOrigin.Of(x, y)));
            new WindowInteropHelper(window) { Owner = data.Application.MainWindowHandle };
            if (window.ShowDialog() != true || window.Json is null)
            {
                return Result.Cancelled;
            }
            json = window.Json;
        }

        IReadOnlyList<ContextBuilding> buildings;
        try
        {
            buildings = Context.Parse(json);
        }
        catch (ContextException e)
        {
            // Just fetched: the answer itself is wrong, so fetching again won't help. From the model: fetch again.
            Receipt(new ReceiptText(fetched ? Strings.Context_AnswerUnreadable : Strings.Context_StoredUnreadable)
            {
                Steps = [fetched ? Strings.Context_AnswerUnreadableStep : Strings.Context_StoredUnreadableStep],
                Details = [e.Message],
                Warning = true,
            });
            return Result.Failed;
        }

        // On: stored (if fetched) and built in one step, so Undo takes both back.
        var (dvr90, fromSurvey) = Dvr90.AtRevitZero(doc);
        int made, refused;
        using (var t = new Transaction(doc, Strings.Undo_ContextOn))
        {
            t.Start();
            if (fetched)
            {
                ProjectStore.WriteContext(doc, json, "Datafordeler");
            }
            (made, refused) = ContextMasses.Build(doc, buildings, Placement.Get(doc), dvr90);
            if (t.Commit() != TransactionStatus.Committed)
            {
                Receipt(new ReceiptText(Strings.Context_RevitStopped) { Warning = true });
                return Result.Failed;
            }
        }
        // Switching on from the stored context is quiet; a fetch, or anything to watch out for, gets a summary.
        if (fetched || refused > 0)
        {
            var own = buildings.Count(b => b.Own);
            Receipt(new ReceiptText(Fill(Strings.Context_Made, made))
            {
                Lines =
                [
                    refused > 0 ? Fill(Strings.Context_Skipped, refused) : null,
                    Strings.Context_Toggle,
                ],
                Details =
                [
                    Strings.Context_Heights,
                    own == 0 ? null : ProjectStore.ReadOptions(doc).ContextIncludesLot ? Fill(Strings.Context_LotIncluded, own) : Fill(Strings.Context_LotLeftOut, own),
                    Dvr90.Describe(doc, dvr90, fromSurvey),
                ],
                Footer = fetched ? Receipts.Credit : null,
            });
        }
        return Result.Succeeded;
    }

    /// <summary>Fetches off Revit's thread with Cancel (Esc), so Revit never freezes; closes with Json once fetched.</summary>
    private sealed class FetchWindow : Window
    {
        private readonly CancellationTokenSource _cts = new();
        private bool _closed;

        public string? Json { get; private set; }

        public FetchWindow(Func<CancellationToken, Task<string>> fetch)
        {
            Title = Strings.Context_WindowTitle;
            Width = 380;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var status = new TextBlock { Text = Strings.Context_Fetching, TextWrapping = TextWrapping.Wrap };
            var error = Error();
            var cancel = Button(Strings.Context_Cancel, 14);
            cancel.IsCancel = true;
            cancel.HorizontalAlignment = HorizontalAlignment.Right;
            cancel.Margin = new Thickness(0, 12, 0, 0);
            var root = new StackPanel { Margin = new Thickness(16) };
            root.Children.Add(status);
            root.Children.Add(error);
            root.Children.Add(cancel);
            Content = root;
            Closing += (_, _) =>
            {
                _closed = true;
                _cts.Cancel();
            };
            Loaded += async (_, _) =>
            {
                try
                {
                    var json = await Task.Run(() => fetch(_cts.Token), _cts.Token);
                    // Closed (Cancel) while a step that can't be cancelled finished: nothing to show it in, Json stays null.
                    if (_closed)
                    {
                        return;
                    }
                    Json = json;
                    DialogResult = true;
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception) when (_closed)
                {
                }
                catch (Exception e)
                {
                    status.Text = Strings.Context_NothingLoaded;
                    error.Text = e is FetchException ? e.Message : Fill(Strings.Context_Unexpected, e.GetType().Name, e.Message);
                    cancel.Content = Strings.Context_Close;
                }
            };
        }
    }
}
