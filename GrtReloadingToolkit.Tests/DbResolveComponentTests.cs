using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// <see cref="Db.ResolvePowderId"/> and <see cref="Db.ResolveBulletId"/> map a GRT load's raw
/// propellant/projectile name string back to an inventory <see cref="Component"/> -- exact match
/// first, then a case-insensitive substring either way, since a GRT load usually only stores the
/// bullet/powder name, not the full "Brand Name [lot]" the Toolkit's own inventory uses.
/// </summary>
public sealed class DbResolveComponentTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("grt-db-resolve-tests-").FullName;

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private Db NewDb() => new(Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".db"));

    private static long AddComponent(Db db, ComponentKind kind, string name) => db.UpsertComponent(new Component
    {
        Kind = kind,
        Brand = "Test",
        Name = name,
        Unit = "pcs",
    });

    [Fact]
    public void ResolvesAnExactCaseInsensitiveNameMatch()
    {
        using Db db = NewDb();
        long id = AddComponent(db, ComponentKind.Powder, "N565");

        Assert.Equal(id, db.ResolvePowderId("n565"));
    }

    [Fact]
    public void ResolvesWhenTheStoredNameIsASubstringOfTheGrtName()
    {
        using Db db = NewDb();
        long id = AddComponent(db, ComponentKind.Bullet, "Hybrid Target");

        Assert.Equal(id, db.ResolveBulletId("Berger 108gr Hybrid Target"));
    }

    [Fact]
    public void ResolvesWhenTheGrtNameIsASubstringOfTheStoredName()
    {
        using Db db = NewDb();
        long id = AddComponent(db, ComponentKind.Powder, "Vihtavuori N565");

        Assert.Equal(id, db.ResolvePowderId("N565"));
    }

    [Fact]
    public void ReturnsNullWhenNothingMatches()
    {
        using Db db = NewDb();
        AddComponent(db, ComponentKind.Powder, "N565");

        Assert.Null(db.ResolvePowderId("H4350"));
    }

    [Fact]
    public void ReturnsNullForAnEmptyName()
    {
        using Db db = NewDb();
        AddComponent(db, ComponentKind.Powder, "N565");

        Assert.Null(db.ResolvePowderId(""));
    }

    [Fact]
    public void PowderAndBulletKindsDoNotCrossMatch()
    {
        using Db db = NewDb();
        AddComponent(db, ComponentKind.Powder, "Hybrid");

        Assert.Null(db.ResolveBulletId("Hybrid"));
    }
}
