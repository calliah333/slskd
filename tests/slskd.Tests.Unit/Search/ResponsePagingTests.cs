namespace slskd.Tests.Unit.Search;

using System;
using System.Collections.Generic;
using System.Linq;
using slskd.Search;
using Xunit;

using File = slskd.Search.File;

public class ResponsePagingTests
{
    [Fact]
    public void Sorts_By_Upload_Speed_Descending_Keeping_Arrival_Order_For_Ties()
    {
        var responses = new[]
        {
            Make("a", uploadSpeed: 10),
            Make("b", uploadSpeed: 30),
            Make("c", uploadSpeed: 10),
            Make("d", uploadSpeed: 20),
            Make("e", uploadSpeed: 30),
        };

        var page = ResponsePaging.Page(responses, new ResponseQuery { SortBy = ResponseSortOrder.UploadSpeed });

        Assert.Equal(new[] { "b", "e", "d", "a", "c" }, page.Responses.Select(r => r.Username));
    }

    [Fact]
    public void Sorts_By_Queue_Length_Ascending_Keeping_Arrival_Order_For_Ties()
    {
        var responses = new[]
        {
            Make("a", queueLength: 5),
            Make("b", queueLength: 0),
            Make("c", queueLength: 5),
            Make("d", queueLength: 1),
        };

        var page = ResponsePaging.Page(responses, new ResponseQuery { SortBy = ResponseSortOrder.QueueLength });

        Assert.Equal(new[] { "b", "d", "a", "c" }, page.Responses.Select(r => r.Username));
    }

    [Fact]
    public void Successive_Pages_Cover_Every_Match_Exactly_Once()
    {
        var responses = Enumerable.Range(0, 23)
            .Select(i => Make($"user{i}", uploadSpeed: i % 4))
            .ToList();

        var all = ResponsePaging.Page(responses, new ResponseQuery { Limit = 1000 }).Responses.Select(r => r.Username).ToList();

        var paged = new List<string>();

        for (var offset = 0; offset < responses.Count; offset += 5)
        {
            var page = ResponsePaging.Page(responses, new ResponseQuery { Offset = offset, Limit = 5 });

            Assert.Equal(23, page.MatchedCount);
            Assert.Equal(offset, page.Offset);
            Assert.Equal(5, page.Limit);
            paged.AddRange(page.Responses.Select(r => r.Username));
        }

        Assert.Equal(all, paged);
        Assert.Equal(23, paged.Distinct().Count());
    }

    [Fact]
    public void Offset_Past_The_End_Returns_An_Empty_Page()
    {
        var page = ResponsePaging.Page(new[] { Make("a") }, new ResponseQuery { Offset = 5, Limit = 5 });

        Assert.Empty(page.Responses);
        Assert.Equal(1, page.MatchedCount);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public void Omits_Hidden_Users_And_Users_Without_Free_Slots()
    {
        var responses = new[]
        {
            Make("a"),
            Make("b", hasFreeUploadSlot: false),
            Make("c"),
            Make("d"),
        };

        var page = ResponsePaging.Page(responses, new ResponseQuery
        {
            HideNoFreeSlots = true,
            HiddenUsernames = ["c"],
        });

        Assert.Equal(new[] { "a", "d" }, page.Responses.Select(r => r.Username));
        Assert.Equal(4, page.TotalCount);
        Assert.Equal(2, page.MatchedCount);
    }

    [Fact]
    public void Omits_Responses_Without_Matching_Files_And_Filters_The_Files_Of_The_Rest()
    {
        var responses = new[]
        {
            Make("a", files: ["x.mp3", "y.flac"]),
            Make("b", files: ["z.mp3"]),
            Make("c", files: ["w.flac"]),
        };

        var page = ResponsePaging.Page(responses, new ResponseQuery { Filter = "flac" });

        Assert.Equal(new[] { "a", "c" }, page.Responses.Select(r => r.Username));
        Assert.Equal(new[] { "y.flac" }, page.Responses[0].Files.Select(f => f.Filename));
        Assert.Equal(1, page.Responses[0].FileCount);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.MatchedCount);
    }

    [Fact]
    public void Hiding_Locked_Files_Omits_Responses_With_Only_Locked_Files()
    {
        var responses = new[]
        {
            Make("a", files: ["x"], lockedFiles: ["y"]),
            Make("b", files: [], lockedFiles: ["z"]),
        };

        var shown = ResponsePaging.Page(responses, new ResponseQuery { HideLocked = false });
        var hidden = ResponsePaging.Page(responses, new ResponseQuery { HideLocked = true });

        Assert.Equal(new[] { "a", "b" }, shown.Responses.Select(r => r.Username));
        Assert.Equal(new[] { "a" }, hidden.Responses.Select(r => r.Username));
        Assert.Empty(hidden.Responses[0].LockedFiles);
        Assert.Equal(0, hidden.Responses[0].LockedFileCount);
    }

    [Fact]
    public void Unfiltered_Responses_Are_Returned_As_Is()
    {
        var response = Make("a", files: ["x"]);

        var page = ResponsePaging.Page(new[] { response }, new ResponseQuery());

        Assert.Same(response, page.Responses.Single());
    }

    [Fact]
    public void Rejects_Negative_Offset_And_Limit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ResponsePaging.Page([], new ResponseQuery { Offset = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ResponsePaging.Page([], new ResponseQuery { Limit = -1 }));
    }

    private static Response Make(
        string username,
        int uploadSpeed = 0,
        long queueLength = 0,
        bool hasFreeUploadSlot = true,
        string[] files = null,
        string[] lockedFiles = null)
    {
        var fileList = (files ?? ["file"]).Select(f => new File { Filename = f }).ToList();
        var lockedFileList = (lockedFiles ?? []).Select(f => new File { Filename = f }).ToList();

        return new Response
        {
            Username = username,
            UploadSpeed = uploadSpeed,
            QueueLength = queueLength,
            HasFreeUploadSlot = hasFreeUploadSlot,
            Files = fileList,
            FileCount = fileList.Count,
            LockedFiles = lockedFileList,
            LockedFileCount = lockedFileList.Count,
        };
    }
}
