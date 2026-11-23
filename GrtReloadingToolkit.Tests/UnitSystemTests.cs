using GrtReloadingToolkit.Measuring;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>Grains or grams, millimetres or inches, metres or yards, MOA or mrad, and the clicks of the scope.</summary>
public sealed class UnitSystemTests
{
    [Theory]
    [InlineData(100, "gr", 100)]
    [InlineData(100, "g", 6.479891)]                // 100 grains are 6.48 grams
    [InlineData(25.4, "in", 1)]
    [InlineData(25.4, "mm", 25.4)]
    [InlineData(914.4, "m", 914.4)]
    [InlineData(914.4, "yd", 1000)]                 // 914.4 m are a thousand yards
    [InlineData(3.437746771, "mrad", 1)]            // a milliradian is 3.4377 minutes of angle
    [InlineData(1, "moa", 1)]
    public void FromTheBaseUnitToTheShownOne(double baseValue, string code, double shown)
        => Assert.Equal(shown, UnitSystem.FromBase(baseValue, code), 6);

    [Theory]
    [InlineData("gr")] [InlineData("g")] [InlineData("mm")] [InlineData("in")] [InlineData("m")] [InlineData("yd")] [InlineData("moa")] [InlineData("mrad")]
    public void WhatIsTypedComesBackToTheStoredValueWithoutLosingAnything(string code)
    {
        foreach (double v in new[] { 0.001, 1, 40.3, 175, 5000.123456 })
            Assert.Equal(v, UnitSystem.ToBase(UnitSystem.FromBase(v, code), code), 9);
    }

    [Fact]
    public void EveryKindHasItsUnitsAndTheBaseComesFirst()
    {
        Assert.Equal(new[] { "gr", "g" }, UnitSystem.Of(UnitKind.Mass).Select(u => u.Code));
        Assert.Equal(new[] { "mm", "in" }, UnitSystem.Of(UnitKind.Length).Select(u => u.Code));
        Assert.Equal(new[] { "m", "yd" }, UnitSystem.Of(UnitKind.Distance).Select(u => u.Code));
        Assert.Equal(new[] { "moa", "mrad" }, UnitSystem.Of(UnitKind.Angle).Select(u => u.Code));
        Assert.All(new[] { UnitKind.Mass, UnitKind.Length, UnitKind.Distance, UnitKind.Angle }, k => Assert.Equal(1.0, UnitSystem.BaseOf(k).PerBase));
        Assert.Throws<ArgumentException>(() => UnitSystem.Find("furlong"));
        Assert.True(UnitSystem.IsKnown("YD"));                                   // written in capitals it is still the yard
    }

    [Fact]
    public void ShowWritesTheNumberWithTheDecimalsOfTheUnit()
    {
        Assert.Equal("11.33981", UnitSystem.Show(175, "g"));                      // 175 gr = 11.33981 g, five decimals at most
        Assert.Equal("175", UnitSystem.Show(175, "gr"));
        Assert.Equal("0.3937", UnitSystem.Show(10, "in"));                        // 0.39370 without the trailing zero
        Assert.Equal("10", UnitSystem.Show(10, "mm"));
        Assert.Equal("1093.61", UnitSystem.Show(1000, "yd"));
        Assert.Equal("0.1", UnitSystem.Show(0.34377, "mrad", 2));
        Assert.Equal("0.5", UnitSystem.Show(0.5, "moa", 3));
        Assert.Equal("49.454", UnitSystem.Show(49.4538, "mm"));                   // as the grids showed it before the choice existed (three decimals)
        Assert.Equal("97.34", UnitSystem.Show(97.34, "gr"));
    }

    [Fact]
    public void AFieldUnitSaysWhichKindItBelongsTo()
    {
        Assert.Equal(UnitKind.Mass, UnitSystem.KindOfFieldUnit("gr"));
        Assert.Equal(UnitKind.Length, UnitSystem.KindOfFieldUnit("mm"));
        Assert.Equal(UnitKind.Distance, UnitSystem.KindOfFieldUnit("m"));
        Assert.Equal(UnitKind.Angle, UnitSystem.KindOfFieldUnit("MOA"));
        Assert.Null(UnitSystem.KindOfFieldUnit("N"));                             // a force has no choice
        Assert.Null(UnitSystem.KindOfFieldUnit("gn H2O"));
        Assert.Null(UnitSystem.KindOfFieldUnit(""));
    }

    [Fact]
    public void OnlyTheNameOfTheUnitOfAnAngleChangesInAFinishedReport()
    {
        string text = "Shots 0.35 MOA high; with 1/4 MOA clicks: 1 down. Worth (MOA): 0.12 and 0.06 MOA.";
        Assert.Equal(text, UnitText.Angle(text, "moa"));
        Assert.Equal("Shots 0.35 mrad high; with 1/4 MOA clicks: 1 down. Worth (mrad): 0.12 and 0.06 mrad.", UnitText.Angle(text, "mrad"));   // a click keeps its name
        Assert.Equal("1/10 mrad clicks", UnitText.Angle("1/10 mrad clicks", "mrad"));
    }

    // ---- the clicks of the scope ------------------------------------------------------------------------------------------------

    [Fact]
    public void TheClicksAreWorthWhatTheirNamesSay()
    {
        Assert.Equal(0.125, ScopeClicks.InMoa("1/8 MOA"));
        Assert.Equal(0.25, ScopeClicks.InMoa("1/4 MOA"));
        Assert.Equal(0.5, ScopeClicks.InMoa("1/2 MOA"));
        Assert.Equal(0.05 * UnitSystem.MoaPerMrad, ScopeClicks.InMoa("1/20 mrad")!.Value, 9);   // 0.17 MOA
        Assert.Equal(0.1 * UnitSystem.MoaPerMrad, ScopeClicks.InMoa("1/10 mrad")!.Value, 9);    // 0.34 MOA
        Assert.Equal(new[] { "1/8 MOA", "1/4 MOA", "1/2 MOA", "1/20 mrad", "1/10 mrad" }, ScopeClicks.All);
    }

    [Theory]
    [InlineData("0.1 mrad", "1/10 mrad")]
    [InlineData("0,05 mrad", "1/20 mrad")]
    [InlineData("1/4 moa", "1/4 MOA")]
    [InlineData(" 1/8 MOA ", "1/8 MOA")]
    [InlineData("0.25 MOA", "1/4 MOA")]
    public void TheOldAndTheLooseNamesOfAClickAreRead(string written, string canonical) => Assert.Equal(canonical, ScopeClicks.Canonical(written));

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("1/3 MOA")] [InlineData("boh")]
    public void ANameThatIsNotAClickIsNotOne(string? written)
    {
        Assert.Null(ScopeClicks.Canonical(written));
        Assert.Null(ScopeClicks.InMoa(written));
    }

    [Fact]
    public void AClickIsWrittenInMoaOrInMrad()
    {
        Assert.Equal("moa", ScopeClicks.UnitOf("1/4 MOA"));
        Assert.Equal("mrad", ScopeClicks.UnitOf("1/10 mrad"));
    }

    // ---- the choices of the shooter -----------------------------------------------------------------------------------------------

    [Fact]
    public void WithNoChoiceEveryKindStartsInTheUnitTheArchiveKeeps()
    {
        var p = new UnitPrefs(new MemoryUnitStore());
        Assert.Equal(("gr", "mm", "m", "moa"), (p.Default(UnitKind.Mass), p.Default(UnitKind.Length), p.Default(UnitKind.Distance), p.Default(UnitKind.Angle)));
        Assert.Equal("1/4 MOA", p.Click);
        Assert.Equal("gr", p.For("case_weight_gr", UnitKind.Mass));
    }

    [Fact]
    public void AStartingChoiceCoversEveryFieldOfItsKindAndAFieldCanHaveItsOwn()
    {
        var p = new UnitPrefs(new MemoryUnitStore());
        p.SetDefault(UnitKind.Mass, "g");
        p.SetDefault(UnitKind.Length, "in");
        Assert.Equal("g", p.For("case_weight_gr", UnitKind.Mass));
        Assert.Equal("g", p.For("bullet_weight_gr", UnitKind.Mass));
        p.SetField("case_length_mm", UnitKind.Length, "mm");                         // this one stays in millimetres
        Assert.Equal("mm", p.For("case_length_mm", UnitKind.Length));
        Assert.True(p.HasOwnChoice("case_length_mm", UnitKind.Length));
        Assert.Equal("in", p.For("bullet_bto_mm", UnitKind.Length));                 // the others follow the starting choice
        Assert.False(p.HasOwnChoice("bullet_bto_mm", UnitKind.Length));
        p.ClearField("case_length_mm");
        Assert.Equal("in", p.For("case_length_mm", UnitKind.Length));
    }

    [Fact]
    public void AChoiceThatIsNotAUnitOfThatKindIsRefusedAndOneFoundInTheFileIsIgnored()
    {
        var store = new MemoryUnitStore();
        var p = new UnitPrefs(store);
        Assert.Throws<ArgumentException>(() => p.SetDefault(UnitKind.Mass, "mm"));
        Assert.Throws<ArgumentException>(() => p.SetField("x", UnitKind.Length, "yd"));
        Assert.Throws<ArgumentException>(() => p.Click = "1/3 MOA");
        store.Set("unit.default.mass", "furlong");                                    // a hand-edited file
        store.Set("unit.field.case_length_mm", "gr");
        store.Set("unit.click", "boh");
        Assert.Equal("gr", p.Default(UnitKind.Mass));
        Assert.Equal("mm", p.For("case_length_mm", UnitKind.Length));
        Assert.Equal("1/4 MOA", p.Click);
    }

    [Fact]
    public void TheFieldsWithAChoiceOfTheirOwnAreListedAndCanAllBeClearedAtOnce()
    {
        var store = new MemoryUnitStore();
        var p = new UnitPrefs(store);
        p.SetDefault(UnitKind.Mass, "g");
        Assert.Empty(p.FieldChoices());
        p.SetField("measure.case_length_mm", UnitKind.Length, "in");
        p.SetField("reading.distance", UnitKind.Distance, "yd");
        store.Set("unit.field.broken", "furlong");                                       // a hand-edited entry is not listed
        Assert.Equal(new[] { ("measure.case_length_mm", "in"), ("reading.distance", "yd") }, p.FieldChoices());
        p.ClearAllFields();
        Assert.Empty(p.FieldChoices());
        Assert.Equal("g", p.Default(UnitKind.Mass));                                      // the starting choices stay
        Assert.Equal("mm", p.For("measure.case_length_mm", UnitKind.Length));
    }

    [Fact]
    public void TheClickIsRememberedAndAnOldNameIsKeptAsTheNewOne()
    {
        var store = new MemoryUnitStore();
        var p = new UnitPrefs(store) { Click = "1/10 mrad" };
        Assert.Equal("1/10 mrad", new UnitPrefs(store).Click);                        // another window reads the same
        store.Set("unit.click", "0.05 mrad");
        Assert.Equal("1/20 mrad", p.Click);
    }
}

/// <summary>The texts the program prints and shows follow the units chosen, and keep GRT's for a kind that was never chosen.</summary>
public sealed class ProgramUnitViewTests
{
    private static GrtPluginKit.Grt.GrtUnits Grt => GrtPluginKit.Grt.GrtUnits.From(null);

    [Fact]
    public void LengthsAndDistancesInATextFollowTheChosenUnits()
    {
        var p = new UnitPrefs(new MemoryUnitStore());
        var v = new ProgramUnitView(p, Grt);
        const string t = "at 300 m the shots spread 25.4 mm sideways and 12.7 mm in height (3.0 / 6.0 mm), 0.5 m/s, 100 m, mm";
        Assert.Equal(t, v.DistanceAll(v.LengthAll(t)));                                    // metric: untouched
        p.SetDefault(UnitKind.Length, "in");
        p.SetDefault(UnitKind.Distance, "yd");
        string r = v.DistanceAll(v.LengthAll(t));
        Assert.Contains("1.000 in sideways", r);
        Assert.Contains("0.500 in in height", r);
        Assert.Contains("(0.118 / 0.236 in)", r);
        Assert.Contains("at 328 yd", r);
        Assert.Contains("0.5 m/s", r);                                                     // a velocity is not a distance
        Assert.Contains("109 yd", r);
    }

    [Fact]
    public void NeckTensionIsTypedInMmUntilToldOtherwiseAndShownInTheChosenLength()
    {
        var p = new UnitPrefs(new MemoryUnitStore());
        var v = new ProgramUnitView(p, Grt);
        Assert.Equal(0.05, v.TensionToMm(0.05), 12);
        Assert.Equal("0.050", v.TensionShown(0.05));
        Assert.Equal("mm", v.TensionShownName);
        p.SetField(ProgramUnitView.TensionTypedField, UnitKind.Length, "in");
        Assert.Equal(0.0508, v.TensionToMm(0.002), 9);                       // 0.002" typed
        Assert.Equal("0.0020", v.TensionShown(0.0508).Replace("0.0020", "0.0020"));
        Assert.Equal("in", v.TensionShownName);                               // nothing chosen for length: shown as typed
        p.SetDefault(UnitKind.Length, "mm");
        Assert.Equal("mm", v.TensionShownName);
        Assert.Equal("0.051", v.TensionShown(0.0508));
    }

    [Fact]
    public void ATextWrittenInMoaIsConvertedNumbersAndWord()
    {
        var p = new UnitPrefs(new MemoryUnitStore());
        var v = new ProgramUnitView(p, Grt);
        const string t = "Mean radius 0.42 MOA · spread 1 MOA · clicks of 1/4 MOA · Group MOA";
        Assert.Equal(t, v.AngleAll(t));                                                    // MOA chosen (or nothing): untouched
        p.SetDefault(UnitKind.Angle, "mrad");
        Assert.Equal("Mean radius 0.12 mrad · spread 0.29 mrad · clicks of 1/4 MOA · Group mrad", v.AngleAll(t));
    }

    [Fact]
    public void SeatingNumbersAreShownAsTypedUntilTheShooterSaysWhichUnitHeWritesIn()
    {
        var p = new UnitPrefs(new MemoryUnitStore());
        var mm = GrtPluginKit.Grt.GrtUnits.From(null);                       // GRT's own length unit is mm here
        var v = new ProgramUnitView(p, mm);
        Assert.Equal(0.508, v.SeatingValue(0.508), 12);                      // nothing chosen: as typed
        p.SetField(ProgramUnitView.SeatingTypedField, UnitKind.Length, "in");
        Assert.Equal("in", v.SeatingTypedCode);
        Assert.Equal(0.508, v.SeatingValue(0.020), 9);                       // typed in inches, shown in mm
        p.SetDefault(UnitKind.Length, "in");
        Assert.Equal(0.020, v.SeatingValue(0.020), 12);                      // typed and shown in inches: untouched
        p.SetField(ProgramUnitView.SeatingTypedField, UnitKind.Length, "mm");
        Assert.Equal(0.020, v.SeatingValue(0.508), 9);
    }

    [Fact]
    public void AnglesStayInMoaUntilOneIsChosenThenFollowTheChoice()
    {
        var p = new UnitPrefs(new MemoryUnitStore());
        var v = new ProgramUnitView(p, Grt);
        Assert.Equal("moa", v.AngleCode);
        Assert.Equal(0.5, v.AngleValue(0.5), 9);
        Assert.Equal("Group MOA", v.AngleText("Group MOA"));
        p.SetDefault(UnitKind.Angle, "mrad");
        Assert.Equal("mrad", v.AngleUnitName);
        Assert.Equal(1.0, v.AngleValue(3.4377467707849396), 9);                           // 1 mrad = 3.4377 MOA
        Assert.Equal("Group mrad", v.AngleText("Group MOA"));
        Assert.Equal(3.4377467707849396, v.AngleToMoa(1.0), 9);                           // and back: what is typed in mrad is stored in MOA
    }

    [Fact]
    public void AFieldWithAChoiceOfItsOwnKeepsItWhateverTheOthersAre()
    {
        var p = new UnitPrefs(new MemoryUnitStore());
        var v = new ProgramUnitView(p, Grt);
        p.SetDefault(UnitKind.Mass, "gr");
        p.SetField("athlon.charge", UnitKind.Mass, "g");
        var own = v.ForField("athlon.charge");
        Assert.Equal("g", own.ChargeUnitName);
        Assert.Equal("gr", v.ChargeUnitName);
        Assert.Equal("g", own.CodeOf(UnitKind.Mass));
        Assert.Equal("gr", v.CodeOf(UnitKind.Mass));
        p.ClearField("athlon.charge");
        Assert.Equal("gr", v.ForField("athlon.charge").ChargeUnitName);
    }

    [Fact]
    public void WithNothingChosenTheTextsAreGrts()
    {
        var v = new ProgramUnitView(new UnitPrefs(new MemoryUnitStore()), Grt);
        Assert.Equal(Grt.Charge(40.3), v.Charge(40.3));
        Assert.Equal(Grt.BulletMass(140), v.BulletMass(140));
        Assert.Equal(Grt.Length(49.5), v.Length(49.5));
        Assert.Equal(Grt.Distance(300), v.Distance(300));
        Assert.Equal(Grt.Charge(40.3), new ProgramUnitView(null, Grt).Charge(40.3));          // no preferences at all (a command line)
    }

    [Fact]
    public void AKindThatWasChosenIsShownInTheChosenUnitAndTheOthersStayGrts()
    {
        var p = new UnitPrefs(new MemoryUnitStore());
        p.SetDefault(UnitKind.Mass, "g");
        p.SetDefault(UnitKind.Length, "in");
        p.SetDefault(UnitKind.Distance, "yd");
        var v = new ProgramUnitView(p, Grt);
        Assert.Equal("2.592 g".Replace("2.592", "2.5920"), v.Charge(40));                    // 40 gr = 2.5920 g, four decimals
        Assert.Equal("9.07 g", v.BulletMass(140));                                           // 140 gr = 9.07 g
        Assert.Equal("1.9488 in", v.Length(49.5));
        Assert.Equal("328.08 yd", v.Distance(300));
        p.SetDefault(UnitKind.Mass, "gr");
        Assert.Equal("40.3 gr", v.Charge(40.3));
        Assert.Equal("140 gr", v.BulletMass(140));
        Assert.Equal(Grt.Velocity(830), v.Velocity(830));                                    // never part of the choice
    }

    [Fact]
    public void OnlyTheKindThatWasChosenChanges()
    {
        var p = new UnitPrefs(new MemoryUnitStore());
        p.SetDefault(UnitKind.Length, "in");
        var v = new ProgramUnitView(p, Grt);
        Assert.Equal(Grt.Charge(40.3), v.Charge(40.3));                                      // weights never chosen: GRT's
        Assert.Equal("1.9488 in", v.Length(49.5));
    }
}
