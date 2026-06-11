using System.Text;
using System.Text.RegularExpressions;
using MicGain.Core.Services;
using Xunit;

namespace MicGain.Core.Tests;

public class ApoConfigServiceTests
{
    // Fresh-install fixture [DOC] (install-ref §Configuration tutorial): default config.txt
    // is a Preamp: line plus Include: example.txt — must be preserved untouched outside markers.
    private const string FreshConfig = "Preamp: -6 dB\r\nInclude: example.txt\r\n";

    private const string GuidA = "{aaaaaaaa-1111-2222-3333-444444444444}";
    private const string GuidB = "{bbbbbbbb-5555-6666-7777-888888888888}";

    private static readonly string ConfigDir = Path.Combine("apo", "config");
    private static readonly string ConfigTxt = Path.Combine(ConfigDir, "config.txt");

    private static string IncludePath(string guid) => Path.Combine(ConfigDir, "micgain", guid + ".txt");

    private static (ApoConfigService Service, FakeFileSystem Fs) Create(string configContent = FreshConfig)
    {
        var fs = new FakeFileSystem();
        fs.Files[ConfigTxt] = configContent; // seeded, not "written" — WrittenPaths stays empty
        return (new ApoConfigService(fs, ConfigDir), fs);
    }

    /// <summary>Builds the canonical marker region exactly as the service emits it.</summary>
    private static string Region(string eol, params string[] guids)
    {
        var sb = new StringBuilder();
        sb.Append("# BEGIN micgain").Append(eol);
        foreach (var guid in guids)
        {
            sb.Append("Device: ").Append(guid).Append(eol);
            sb.Append("Include: micgain\\").Append(guid).Append(".txt").Append(eol);
        }

        sb.Append("Device: all").Append(eol).Append("# END micgain");
        return sb.ToString();
    }

    // ---- Acceptance criterion 1: round-trip ----

    [Theory]
    [InlineData(-12.0)]
    [InlineData(-6.5)]
    [InlineData(0.0)]
    [InlineData(3.0)]
    public void RoundTrip_WriteThenRead_ReturnsSameValue(double gainDb)
    {
        var (service, _) = Create();

        service.WriteGain(GuidA, gainDb);

        Assert.Equal(gainDb, service.ReadGain(GuidA));
    }

    // ---- Acceptance criterion 2: only marker-region changes ----

    [Fact]
    public void WriteGain_OnFreshConfig_AppendsOnlyMarkerRegion()
    {
        var (service, fs) = Create();

        service.WriteGain(GuidA, -12);

        Assert.Equal(FreshConfig + Region("\r\n", GuidA) + "\r\n", fs.Files[ConfigTxt]);
        Assert.Equal("Preamp: -12 dB\r\n", fs.Files[IncludePath(GuidA)]);
    }

    [Fact]
    public void WriteGain_WithExistingUserConfig_PreservesUserContentByteExactly()
    {
        const string userConfig =
            "# my notes\r\n" +
            "Device: High Definition Audio Device Speakers; Benchmark\r\n" +
            "Preamp: -3 dB\r\n" +
            "Filter 1: ON PK Fc 50 Hz Gain -3.0 dB Q 10.00\r\n" +
            "Include: example.txt\r\n";
        var (service, fs) = Create(userConfig);

        service.WriteGain(GuidA, -12);

        Assert.StartsWith(userConfig, fs.Files[ConfigTxt], StringComparison.Ordinal);
        Assert.Equal(userConfig + Region("\r\n", GuidA) + "\r\n", fs.Files[ConfigTxt]);
    }

    [Fact]
    public void WriteGain_ConfigWithoutTrailingNewline_AppendsRegionOnNewLine()
    {
        const string userConfig = "Preamp: -6 dB\r\nInclude: example.txt"; // no trailing EOL
        var (service, fs) = Create(userConfig);

        service.WriteGain(GuidA, -12);

        Assert.Equal(userConfig + "\r\n" + Region("\r\n", GuidA) + "\r\n", fs.Files[ConfigTxt]);
    }

    [Fact]
    public void WriteGain_SecondDevice_RebuildsRegionInPlace_PreservingSurroundingContent()
    {
        var before = FreshConfig + Region("\r\n", GuidA) + "\r\n# user content below\r\nPreamp: -1 dB\r\n";
        var (service, fs) = Create(before);

        service.WriteGain(GuidB, 6);

        var expected = FreshConfig + Region("\r\n", GuidA, GuidB) + "\r\n# user content below\r\nPreamp: -1 dB\r\n";
        Assert.Equal(expected, fs.Files[ConfigTxt]);
    }

    [Fact]
    public void WriteGain_LfOnlyConfig_PreservesLfLineEndings()
    {
        const string lfConfig = "Preamp: -6 dB\nInclude: example.txt\n";
        var (service, fs) = Create(lfConfig);

        service.WriteGain(GuidA, -12);

        Assert.Equal(lfConfig + Region("\n", GuidA) + "\n", fs.Files[ConfigTxt]);
    }

    // ---- Acceptance criterion 4: idempotency (markers already present) ----

    [Fact]
    public void WriteGain_WhenDeviceAlreadyManaged_DoesNotRewriteConfigTxt()
    {
        var seeded = FreshConfig + Region("\r\n", GuidA) + "\r\n";
        var (service, fs) = Create(seeded);

        service.WriteGain(GuidA, -3);

        Assert.Equal(seeded, fs.Files[ConfigTxt]);
        Assert.DoesNotContain(ConfigTxt, fs.WrittenPaths); // include file only
        Assert.Equal("Preamp: -3 dB\r\n", fs.Files[IncludePath(GuidA)]);
    }

    [Fact]
    public void WriteGain_CalledRepeatedly_ProducesExactlyOneMarkerRegion()
    {
        var (service, fs) = Create();

        service.WriteGain(GuidA, -12);
        service.WriteGain(GuidA, -6);
        service.WriteGain(GuidB, -1);

        Assert.Equal(1, Regex.Matches(fs.Files[ConfigTxt], Regex.Escape("# BEGIN micgain")).Count);
        Assert.Equal(1, Regex.Matches(fs.Files[ConfigTxt], Regex.Escape("# END micgain")).Count);
    }

    [Fact]
    public void WriteGain_RegionAlwaysEndsWithDeviceAllScopeReset()
    {
        var (service, fs) = Create();

        service.WriteGain(GuidA, -12);
        service.WriteGain(GuidB, -6);

        // Scope-leak rule (config-ref §Device): Device: all must close the region.
        Assert.Contains("Device: all\r\n# END micgain", fs.Files[ConfigTxt], StringComparison.Ordinal);
    }

    // ---- Acceptance criterion 3: malformed input → fail safe, no write ----

    [Theory]
    [InlineData(FreshConfig + "# BEGIN micgain\r\nDevice: all\r\n")] // BEGIN without END
    [InlineData(FreshConfig + "# END micgain\r\n")] // END without BEGIN
    [InlineData("# END micgain\r\n# BEGIN micgain\r\n")] // END before BEGIN
    [InlineData("# BEGIN micgain\r\n# END micgain\r\n# BEGIN micgain\r\n# END micgain\r\n")] // duplicated region
    public void WriteGain_MalformedMarkers_ThrowsAndWritesNothing(string malformedConfig)
    {
        var (service, fs) = Create(malformedConfig);

        Assert.Throws<ApoConfigException>(() => service.WriteGain(GuidA, -12));

        Assert.Equal(malformedConfig, fs.Files[ConfigTxt]); // untouched
        Assert.Empty(fs.WrittenPaths); // not even the include file
        Assert.False(fs.Files.ContainsKey(IncludePath(GuidA)));
    }

    [Fact]
    public void WriteGain_MissingConfigTxt_ThrowsAndWritesNothing()
    {
        var fs = new FakeFileSystem();
        var service = new ApoConfigService(fs, ConfigDir);

        Assert.Throws<ApoConfigException>(() => service.WriteGain(GuidA, -12));

        Assert.Empty(fs.WrittenPaths);
    }

    // ---- Reader: lenient, like APO itself ----

    [Fact]
    public void ReadGain_UnmanagedDevice_ReturnsNull()
    {
        var (service, _) = Create();

        Assert.Null(service.ReadGain(GuidA));
    }

    [Fact]
    public void ReadGain_LenientReader_SkipsNonConformingLines()
    {
        var (service, fs) = Create();
        fs.Files[IncludePath(GuidA)] = "garbage line\r\nFilter: ON PK Fc 50 Hz\r\npreamp: -7.5 db\r\n";

        Assert.Equal(-7.5, service.ReadGain(GuidA));
    }

    [Fact]
    public void ReadGain_NoValidPreampLine_ReturnsNull()
    {
        var (service, fs) = Create();
        fs.Files[IncludePath(GuidA)] = "Filter: ON PK Fc 50 Hz Gain -3.0 dB Q 10.00\r\nnot a config line\r\n";

        Assert.Null(service.ReadGain(GuidA));
    }

    // ---- Writer strictness and input validation ----

    [Fact]
    public void WriteGain_NormalizesGuidToBracedLowercase()
    {
        var (service, fs) = Create();

        service.WriteGain("AAAAAAAA-1111-2222-3333-444444444444", -12); // unbraced, uppercase

        Assert.True(fs.Files.ContainsKey(IncludePath(GuidA)));
        Assert.Contains("Device: " + GuidA, fs.Files[ConfigTxt], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-guid")]
    [InlineData("{aaaa}")]
    public void WriteGain_InvalidGuid_ThrowsArgumentException(string invalidGuid)
    {
        var (service, fs) = Create();

        Assert.Throws<ArgumentException>(() => service.WriteGain(invalidGuid, -12));
        Assert.Empty(fs.WrittenPaths);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void WriteGain_NonFiniteGain_ThrowsAndWritesNothing(double gainDb)
    {
        var (service, fs) = Create();

        Assert.Throws<ArgumentOutOfRangeException>(() => service.WriteGain(GuidA, gainDb));
        Assert.Empty(fs.WrittenPaths);
    }

    [Fact]
    public void WriteGain_GeneratedContent_NeverContainsBackticks()
    {
        var (service, fs) = Create();

        service.WriteGain(GuidA, -12.25);

        // config-ref maintainer note 9: backtick is the inline-expression delimiter.
        Assert.All(fs.WrittenPaths, path => Assert.DoesNotContain('`', fs.Files[path]));
    }

    [Fact]
    public void WriteGain_FormatsValueWithInvariantCultureAndMaxTwoDecimals()
    {
        var (service, fs) = Create();

        service.WriteGain(GuidA, -12.25);

        Assert.Equal("Preamp: -12.25 dB\r\n", fs.Files[IncludePath(GuidA)]);
    }
}
