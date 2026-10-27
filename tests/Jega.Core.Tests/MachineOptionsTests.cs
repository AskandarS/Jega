using Jega.Core;

namespace Jega.Core.Tests;

public class MachineOptionsTests : IDisposable
{
    private readonly string _temp = Directory.CreateTempSubdirectory("jega-machine-").FullName;

    public void Dispose() => Directory.Delete(_temp, recursive: true);

    [Fact]
    public void WhatIsSavedIsReadBack()
    {
        var path = Path.Combine(_temp, "settings.json");
        new MachineOptions { DatafordelerKeyProtected = "AQAAANCMnd8B", AddressToken = "mytoken" }.Save(path);
        var back = MachineOptions.Load(path);
        Assert.Equal("AQAAANCMnd8B", back.DatafordelerKeyProtected);
        Assert.Equal("mytoken", back.AddressToken);
        Assert.Equal(new MachineOptions(), MachineOptions.Load(Path.Combine(_temp, "missing.json")));
    }

    [Fact]
    public void TheFileIsInJegasOwnFolder() =>
        Assert.EndsWith(Path.Combine("Jega", "settings.json"), MachineOptions.DefaultPath);

    [Fact]
    public void ABrokenFileGivesTheDefaults()
    {
        var path = Path.Combine(_temp, "broken.json");
        File.WriteAllText(path, "{ not json");
        Assert.Equal(new MachineOptions(), MachineOptions.Load(path));
    }

    [Fact]
    public void NullIsNone()
    {
        var path = Path.Combine(_temp, "nulls.json");
        File.WriteAllText(path, """{"DatafordelerKeyProtected":null,"AddressToken":null}""");
        Assert.Equal(new MachineOptions(), MachineOptions.Load(path));
    }
}
