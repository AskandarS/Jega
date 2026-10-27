using System.Text.Json;
using Jega.Core.Resources;
using static Jega.Core.Texts;

namespace Jega.Core;

/// <summary>
/// Jega's settings for this PC, not the project: the user's own keys, which must never travel with a .rvt that gets
/// sent on. In %APPDATA%\Jega\settings.json. The Datafordeler key is stored protected
/// for this Windows user (DPAPI, done on the Revit side: Secret.cs), so the file doesn't give it away.
/// </summary>
public sealed record MachineOptions
{
    /// <summary>The user's Datafordeler API key, protected with DPAPI for this Windows user (Base64); empty = none yet.</summary>
    public string DatafordelerKeyProtected { get; init; } = "";

    /// <summary>Adressevælger's token ("Advanced"); empty = its example token (SiteFetcher.DefaultAddressToken).</summary>
    public string AddressToken { get; init; } = "";

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Jega", "settings.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>From the file; missing or unreadable gives the defaults (never fails a command).</summary>
    public static MachineOptions Load(string? path = null) => Load(out _, path);

    /// <summary>As Load; $problem says why an existing file couldn't be read (null when missing or fine).</summary>
    public static MachineOptions Load(out string? problem, string? path = null)
    {
        problem = null;
        path ??= DefaultPath;
        try
        {
            var o = File.Exists(path) ? JsonSerializer.Deserialize<MachineOptions>(File.ReadAllText(path).TrimStart('﻿'), Json) ?? new() : new();
            // A field written as null is read as null whatever its type says (System.Text.Json): none, as when missing.
            return o with { DatafordelerKeyProtected = o.DatafordelerKeyProtected ?? "", AddressToken = o.AddressToken ?? "" };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            problem = Fill(Strings.Machine_Unreadable, path, e.Message);
            return new();
        }
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".part", JsonSerializer.Serialize(this, Json));
        File.Move(path + ".part", path, overwrite: true);
    }
}
