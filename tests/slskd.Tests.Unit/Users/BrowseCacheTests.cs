namespace slskd.Tests.Unit.Users;

using System;
using System.Linq;
using slskd.Users;
using Soulseek;
using Xunit;

public class BrowseCacheTests
{
    [Fact]
    public void AddOrUpdate_Evicts_Least_Recently_Used_When_Over_File_Limit()
    {
        var cache = new BrowseCache(maxFiles: 10);

        cache.AddOrUpdate("a", Response(4));
        cache.AddOrUpdate("b", Response(4));
        Assert.True(cache.TryGet("a", out _)); // a is now more recently used than b

        cache.AddOrUpdate("c", Response(4));

        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void AddOrUpdate_Evicts_As_Many_Entries_As_Needed()
    {
        var cache = new BrowseCache(maxFiles: 10);

        cache.AddOrUpdate("a", Response(3));
        cache.AddOrUpdate("b", Response(3));
        cache.AddOrUpdate("c", Response(3));
        cache.AddOrUpdate("d", Response(7));

        Assert.False(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
        Assert.True(cache.TryGet("d", out _));
    }

    [Fact]
    public void AddOrUpdate_Counts_Locked_Files_Toward_Limit()
    {
        var cache = new BrowseCache(maxFiles: 10);

        cache.AddOrUpdate("a", Response(2, locked: 4));
        cache.AddOrUpdate("b", Response(2, locked: 3));

        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("b", out _));
    }

    [Fact]
    public void AddOrUpdate_Keeps_Newest_Entry_Even_If_It_Alone_Exceeds_Limit()
    {
        var cache = new BrowseCache(maxFiles: 10);

        cache.AddOrUpdate("a", Response(2));
        cache.AddOrUpdate("b", Response(11));

        Assert.False(cache.TryGet("a", out _));
        Assert.True(cache.TryGet("b", out var entry));
        Assert.Equal(11, entry.TotalFileCount);
    }

    [Fact]
    public void AddOrUpdate_Replaces_Existing_Entry_Without_Double_Counting()
    {
        var cache = new BrowseCache(maxFiles: 10);

        var first = cache.AddOrUpdate("a", Response(6));
        var second = cache.AddOrUpdate("a", Response(6));
        cache.AddOrUpdate("b", Response(4));

        Assert.NotSame(first, second);
        Assert.True(cache.TryGet("a", out var entry));
        Assert.Same(second, entry);
        Assert.True(cache.TryGet("b", out _));
    }

    [Fact]
    public void TryGet_Returns_False_For_Unknown_User()
    {
        var cache = new BrowseCache();

        Assert.False(cache.TryGet("a", out var entry));
        Assert.Null(entry);
    }

    [Fact]
    public void TryGet_Is_Case_Sensitive()
    {
        var cache = new BrowseCache();

        cache.AddOrUpdate("Alice", Response(1));

        Assert.False(cache.TryGet("alice", out _));
    }

    [Fact]
    public void TryGet_Returns_False_Once_Expired()
    {
        var cache = new BrowseCache(slidingExpiration: TimeSpan.Zero);

        cache.AddOrUpdate("a", Response(1));

        Assert.False(cache.TryGet("a", out _));
    }

    [Fact]
    public void Entry_Counts_And_Orders_Unlocked_Before_Locked()
    {
        var response = new BrowseResponse(
            [Dir("z", 2), Dir("a", 1)],
            [Dir("m", 3)]);

        var entry = new BrowseCacheEntry(response);

        Assert.Equal(2, entry.DirectoryCount);
        Assert.Equal(3, entry.FileCount);
        Assert.Equal(1, entry.LockedDirectoryCount);
        Assert.Equal(3, entry.LockedFileCount);
        Assert.Equal(new[] { "z", "a", "m" }, entry.Directories.Select(d => d.Directory.Name));
        Assert.Equal(new[] { false, false, true }, entry.Directories.Select(d => d.Locked));
    }

    [Fact]
    public void Entry_Find_Exact_Ignores_Descendants_And_Siblings()
    {
        var entry = new BrowseCacheEntry(new BrowseResponse([Dir("A\\B", 1), Dir("A\\B\\C", 1), Dir("A\\BC", 1)]));

        var matches = entry.Find("A\\B", recursive: false);

        Assert.Equal(new[] { "A\\B" }, matches.Select(d => d.Directory.Name));
        Assert.Empty(entry.Find("A", recursive: false));
        Assert.Empty(entry.Find("a\\b", recursive: false));
    }

    [Fact]
    public void Entry_Find_Recursive_Matches_Both_Separators_But_Not_Sibling_Prefix()
    {
        var response = new BrowseResponse(
            [Dir("A\\B\\C", 1), Dir("A\\BC", 1), Dir("A\\B", 1), Dir("A\\B/D", 1), Dir("A\\B\\C\\E", 1), Dir("A\\B.", 1), Dir("A\\B0", 1)],
            [Dir("A\\B\\Locked", 1), Dir("A\\BC\\Locked", 1)]);

        var entry = new BrowseCacheEntry(response);

        var matches = entry.Find("A\\B", recursive: true);

        // the directory itself first, then descendants in browse order
        Assert.Equal(new[] { "A\\B", "A\\B\\C", "A\\B/D", "A\\B\\C\\E", "A\\B\\Locked" }, matches.Select(d => d.Directory.Name));
        Assert.Equal(new[] { false, false, false, false, true }, matches.Select(d => d.Locked));
    }

    [Fact]
    public void Entry_Find_Recursive_Finds_Descendants_Of_Implied_Directory()
    {
        var entry = new BrowseCacheEntry(new BrowseResponse([Dir("Music\\Artist\\Album", 1), Dir("Music2", 1)]));

        Assert.Equal(new[] { "Music\\Artist\\Album" }, entry.Find("Music", recursive: true).Select(d => d.Directory.Name));
        Assert.Empty(entry.Find("Music", recursive: false));
    }

    [Fact]
    public void Entry_Find_Returns_Duplicate_Names_In_Browse_Order()
    {
        var first = Dir("A", 1);
        var second = Dir("A", 2);
        var entry = new BrowseCacheEntry(new BrowseResponse([first, Dir("B", 1)], [second]));

        var matches = entry.Find("A", recursive: false);

        Assert.Equal(2, matches.Count);
        Assert.Same(first, matches[0].Directory);
        Assert.Same(second, matches[1].Directory);
        Assert.True(matches[1].Locked);
    }

    private static BrowseResponse Response(int files, int locked = 0)
        => new BrowseResponse([Dir("unlocked", files)], [Dir("locked", locked)]);

    private static Directory Dir(string name, int files)
        => new Directory(name, Enumerable.Range(0, files).Select(i => new File(1, $"{name}\\{i:D2}.flac", 1000 + i, "flac")).ToList());
}
