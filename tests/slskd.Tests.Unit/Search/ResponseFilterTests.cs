namespace slskd.Tests.Unit.Search;

using System.Collections.Generic;
using System.Linq;
using slskd.Search;
using Xunit;

using File = slskd.Search.File;

public class ResponseFilterTests
{
    [Fact]
    public void Parse_Returns_None_For_Null_Or_Empty()
    {
        Assert.Same(ResponseFilter.None, ResponseFilter.Parse(null));
        Assert.Same(ResponseFilter.None, ResponseFilter.Parse(string.Empty));
    }

    [Theory]
    [InlineData("foo minbr:42 bar", 42)]
    [InlineData("foo minbitrate:123 bar", 123)]
    [InlineData("foo MINBR:7", 7)]
    [InlineData("foo minbr:abc", 0)]
    public void Parse_Reads_MinBitRate(string text, long expected)
    {
        Assert.Equal(expected, ResponseFilter.Parse(text).MinBitRate);
    }

    [Theory]
    [InlineData("foo minbd:24 bar", 24)]
    [InlineData("foo minbitdepth:16 bar", 16)]
    public void Parse_Reads_MinBitDepth(string text, long expected)
    {
        Assert.Equal(expected, ResponseFilter.Parse(text).MinBitDepth);
    }

    [Theory]
    [InlineData("foo minfs:42 bar", 42)]
    [InlineData("foo minfilesize:123 bar", 123)]
    public void Parse_Reads_MinFileSize(string text, long expected)
    {
        Assert.Equal(expected, ResponseFilter.Parse(text).MinFileSize);
    }

    [Theory]
    [InlineData("foo minlen:42 bar", 42)]
    [InlineData("foo minlength:123 bar", 123)]
    public void Parse_Reads_MinLength(string text, long expected)
    {
        Assert.Equal(expected, ResponseFilter.Parse(text).MinLength);
    }

    [Theory]
    [InlineData("foo minfif:42 bar", 42)]
    [InlineData("foo minfilesinfolder:123 bar", 123)]
    public void Parse_Reads_MinFilesInFolder(string text, long expected)
    {
        Assert.Equal(expected, ResponseFilter.Parse(text).MinFilesInFolder);
    }

    [Fact]
    public void Parse_Treats_Overflowing_Numbers_As_Maximum()
    {
        Assert.Equal(long.MaxValue, ResponseFilter.Parse("minfs:99999999999999999999999").MinFileSize);
    }

    [Theory]
    [InlineData("foo minbr:42 bar", new[] { "foo", "bar" }, new string[0])]
    [InlineData("foo iscbr isvbr bar", new[] { "foo", "bar" }, new string[0])]
    [InlineData("foo some:thing bar", new[] { "foo", "bar" }, new string[0])]
    [InlineData("foo -bar", new[] { "foo" }, new[] { "bar" })]
    [InlineData("-foo -bar -baz qux", new[] { "qux" }, new[] { "foo", "bar", "baz" })]
    [InlineData("foo bar baz -qux", new[] { "foo", "bar", "baz" }, new[] { "qux" })]
    [InlineData("FOO  Bar", new[] { "foo", "bar" }, new string[0])]
    public void Parse_Splits_Terms(string text, string[] include, string[] exclude)
    {
        var filter = ResponseFilter.Parse(text);

        Assert.Equal(include, filter.Include);
        Assert.Equal(exclude, filter.Exclude);
    }

    [Fact]
    public void Parse_Reads_Flags()
    {
        var filter = ResponseFilter.Parse("big -mix of:everything isvbr iscbr minbr:42");

        Assert.True(filter.IsVBR);
        Assert.True(filter.IsCBR);
        Assert.False(filter.IsLossless);
        Assert.False(filter.IsLossy);
        Assert.Equal(42, filter.MinBitRate);
        Assert.Equal(new[] { "big" }, filter.Include);
        Assert.Equal(new[] { "mix" }, filter.Exclude);
    }

    [Fact]
    public void Parse_Recognizes_Flags_Anywhere_Like_The_Web_UI_Did()
    {
        // the flag check is a substring match over the whole text; '-isvbr' is also an exclusion term
        var filter = ResponseFilter.Parse("IsLossless -isvbr");

        Assert.True(filter.IsLossless);
        Assert.True(filter.IsVBR);
        Assert.Equal(new[] { "isvbr" }, filter.Exclude);
        Assert.Empty(filter.Include);
    }

    [Fact]
    public void CBR_Excludes_Variable_And_Unknown_Bit_Rates()
    {
        var filter = new ResponseFilter { IsCBR = true };

        Assert.True(filter.Matches(new File { IsVariableBitRate = false }));
        Assert.False(filter.Matches(new File { IsVariableBitRate = true }));
        Assert.False(filter.Matches(new File()));
    }

    [Fact]
    public void VBR_Excludes_Constant_And_Unknown_Bit_Rates()
    {
        var filter = new ResponseFilter { IsVBR = true };

        Assert.True(filter.Matches(new File { IsVariableBitRate = true }));
        Assert.False(filter.Matches(new File { IsVariableBitRate = false }));
        Assert.False(filter.Matches(new File()));
    }

    [Fact]
    public void CBR_And_VBR_Together_Exclude_Everything()
    {
        var filter = new ResponseFilter { IsCBR = true, IsVBR = true };

        Assert.False(filter.Matches(new File { IsVariableBitRate = true }));
        Assert.False(filter.Matches(new File { IsVariableBitRate = false }));
    }

    [Fact]
    public void Lossless_Requires_Sample_Rate_And_Bit_Depth()
    {
        var filter = new ResponseFilter { IsLossless = true };

        Assert.True(filter.Matches(new File { BitDepth = 16, SampleRate = 44_100 }));
        Assert.False(filter.Matches(new File { BitRate = 320 }));
        Assert.False(filter.Matches(new File { SampleRate = 44_100 }));
        Assert.False(filter.Matches(new File { BitDepth = 0, SampleRate = 44_100 }));
    }

    [Fact]
    public void Lossy_Excludes_Files_With_Sample_Rate_Or_Bit_Depth()
    {
        var filter = new ResponseFilter { IsLossy = true };

        Assert.True(filter.Matches(new File { BitRate = 320 }));
        Assert.True(filter.Matches(new File { BitDepth = 0, SampleRate = 0 }));
        Assert.False(filter.Matches(new File { BitDepth = 16, SampleRate = 44_100 }));
        Assert.False(filter.Matches(new File { SampleRate = 44_100 }));
    }

    [Fact]
    public void Minimums_Exclude_Smaller_Values_But_Not_Missing_Ones()
    {
        Assert.True(new ResponseFilter { MinBitRate = 100 }.Matches(new File { BitRate = 100 }));
        Assert.False(new ResponseFilter { MinBitRate = 100 }.Matches(new File { BitRate = 99 }));
        Assert.True(new ResponseFilter { MinBitRate = 100 }.Matches(new File()));

        Assert.True(new ResponseFilter { MinBitDepth = 24 }.Matches(new File { BitDepth = 24 }));
        Assert.False(new ResponseFilter { MinBitDepth = 24 }.Matches(new File { BitDepth = 16 }));
        Assert.True(new ResponseFilter { MinBitDepth = 24 }.Matches(new File()));

        Assert.True(new ResponseFilter { MinFileSize = 100 }.Matches(new File { Size = 100 }));
        Assert.False(new ResponseFilter { MinFileSize = 100 }.Matches(new File { Size = 99 }));

        Assert.True(new ResponseFilter { MinLength = 100 }.Matches(new File { Length = 100 }));
        Assert.False(new ResponseFilter { MinLength = 100 }.Matches(new File { Length = 99 }));
        Assert.True(new ResponseFilter { MinLength = 100 }.Matches(new File()));
    }

    [Fact]
    public void No_Minimum_Does_Not_Exclude_Negative_Values()
    {
        var file = new File { BitRate = -1, BitDepth = -1, Size = -1, Length = -1 };

        Assert.True(ResponseFilter.None.Matches(file));
        Assert.True(ResponseFilter.Parse("minbr:0 minfif:2").Matches(file));
        Assert.False(ResponseFilter.Parse("minbr:1").Matches(file));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("minfif:8", false)]
    [InlineData("minbr:0 unknown:1", false)]
    [InlineData("foo", true)]
    [InlineData("-foo", true)]
    [InlineData("-", true)]
    [InlineData("iscbr", true)]
    [InlineData("isvbr", true)]
    [InlineData("islossless", true)]
    [InlineData("islossy", true)]
    [InlineData("minbr:1", true)]
    [InlineData("minbd:1", true)]
    [InlineData("minfs:1", true)]
    [InlineData("minlen:1", true)]
    public void ConstrainsFiles_Tells_Whether_Any_File_Can_Be_Rejected(string text, bool expected)
    {
        var filter = ResponseFilter.Parse(text);

        Assert.Equal(expected, filter.ConstrainsFiles);

        if (!expected)
        {
            Assert.True(filter.Matches(new File { Filename = "x", BitRate = -5, BitDepth = 0, Size = long.MinValue, Length = -1, IsVariableBitRate = true }));
        }
    }

    [Fact]
    public void Terms_Match_Filenames_Ignoring_Case()
    {
        var files = new[] { "/path/to/foo.mp3", "/path/to/bar.mp3", "/path/to/baz.mp3", "/path/to/qux.mp3", "/path/to/info.nfo", "/path/to/Folder.JPG" }
            .Select(f => new File { Filename = f })
            .ToList();

        string[] Matching(string text) => files.Where(ResponseFilter.Parse(text).Matches).Select(f => f.Filename).ToArray();

        Assert.Equal(new[] { "/path/to/info.nfo" }, Matching("path to .nfo"));
        Assert.Equal(new[] { "/path/to/foo.mp3", "/path/to/baz.mp3", "/path/to/info.nfo" }, Matching("-bar -jpg -qux"));
        Assert.Equal(new[] { "/path/to/baz.mp3", "/path/to/qux.mp3" }, Matching("PATH .mp3 -foo -bar"));
        Assert.Equal(new[] { "/path/to/Folder.JPG" }, Matching("folder.jpg"));
    }

    [Fact]
    public void A_Lone_Dash_Excludes_Everything_Like_The_Web_UI_Did()
    {
        Assert.False(ResponseFilter.Parse("foo -").Matches(new File { Filename = "foo" }));
    }

    [Fact]
    public void Apply_Filters_Files_And_Adjusts_Counts()
    {
        var response = Response(
            files: [new File { Filename = "a.flac" }, new File { Filename = "b.mp3" }],
            lockedFiles: [new File { Filename = "c.flac" }, new File { Filename = "d.mp3" }]);

        var filtered = ResponseFilter.Parse("flac").Apply(response, hideLocked: false);

        Assert.Equal(new[] { "a.flac" }, filtered.Files.Select(f => f.Filename));
        Assert.Equal(1, filtered.FileCount);
        Assert.Equal(new[] { "c.flac" }, filtered.LockedFiles.Select(f => f.Filename));
        Assert.Equal(1, filtered.LockedFileCount);
        Assert.Equal(response.Username, filtered.Username);

        // the original is left alone
        Assert.Equal(2, response.Files.Count);
        Assert.Equal(2, response.FileCount);
    }

    [Fact]
    public void Apply_Removes_Locked_Files_When_Hiding_Locked()
    {
        var response = Response(files: [new File { Filename = "a" }], lockedFiles: [new File { Filename = "b" }]);

        var filtered = ResponseFilter.None.Apply(response, hideLocked: true);

        Assert.Single(filtered.Files);
        Assert.Empty(filtered.LockedFiles);
        Assert.Equal(0, filtered.LockedFileCount);
    }

    [Fact]
    public void Apply_Returns_Null_When_No_Files_Match()
    {
        var response = Response(files: [new File { Filename = "a.mp3" }], lockedFiles: [new File { Filename = "b.flac" }]);

        Assert.Null(ResponseFilter.Parse("flac").Apply(response, hideLocked: true));
        Assert.False(ResponseFilter.Parse("flac").Matches(response, hideLocked: true));
        Assert.True(ResponseFilter.Parse("flac").Matches(response, hideLocked: false));
    }

    [Fact]
    public void Apply_Returns_The_Original_When_Nothing_Is_Removed()
    {
        var response = Response(files: [new File { Filename = "a" }], lockedFiles: []);

        Assert.Same(response, ResponseFilter.None.Apply(response, hideLocked: true));
    }

    [Fact]
    public void MinFilesInFolder_Excludes_Responses_With_Too_Few_Files_Counting_Locked_Only_When_Shown()
    {
        var response = Response(
            files: [new File { Filename = "a" }, new File { Filename = "b" }],
            lockedFiles: [new File { Filename = "c" }]);

        var filter = ResponseFilter.Parse("minfif:3");

        Assert.True(filter.Matches(response, hideLocked: false));
        Assert.NotNull(filter.Apply(response, hideLocked: false));

        Assert.False(filter.Matches(response, hideLocked: true));
        Assert.Null(filter.Apply(response, hideLocked: true));
    }

    [Fact]
    public void MinFilesInFolder_Counts_Files_Before_Other_Filters()
    {
        var response = Response(files: [new File { Filename = "a.mp3" }, new File { Filename = "b.jpg" }], lockedFiles: []);

        var filtered = ResponseFilter.Parse("minfif:2 mp3").Apply(response, hideLocked: true);

        Assert.Equal(new[] { "a.mp3" }, filtered.Files.Select(f => f.Filename));
    }

    private static Response Response(List<File> files, List<File> lockedFiles, string username = "user") => new()
    {
        Username = username,
        Files = files,
        FileCount = files.Count,
        LockedFiles = lockedFiles,
        LockedFileCount = lockedFiles.Count,
    };
}
