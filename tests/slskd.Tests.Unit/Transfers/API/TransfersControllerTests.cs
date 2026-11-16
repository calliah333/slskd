namespace slskd.Tests.Unit.Transfers.API;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using slskd.Transfers;
using slskd.Transfers.API;
using slskd.Transfers.Downloads;
using slskd.Transfers.Uploads;
using slskd.Users;
using Soulseek;
using Xunit;

using Transfer = slskd.Transfers.Transfer;

public class TransfersControllerTests
{
    private const TransferStates Succeeded = TransferStates.Completed | TransferStates.Succeeded;
    private const TransferStates Errored = TransferStates.Completed | TransferStates.Errored;
    private const TransferStates Cancelled = TransferStates.Completed | TransferStates.Cancelled;
    private const TransferStates QueuedLocally = TransferStates.Queued | TransferStates.Locally;

    [Theory]
    [InlineData("bogus", 0, 500)]
    [InlineData("all", -1, 500)]
    [InlineData("all", 0, 0)]
    [InlineData("all", 0, -5)]
    public void GetUploadsPage_Returns_BadRequest_On_Invalid_Input(string filter, int offset, int limit)
    {
        var (controller, mocks) = GetFixture();

        var result = controller.GetUploadsPage(filter, offset: offset, limit: limit);

        Assert.IsType<BadRequestObjectResult>(result);
        mocks.Uploads.Verify(m => m.Page(It.IsAny<TransferFilter>(), It.IsAny<TransferQuery>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void GetUploadsPage_Clamps_Limit_And_Passes_Parsed_Filter()
    {
        var (controller, mocks) = GetFixture();
        mocks.Uploads.Setup(m => m.Page(It.IsAny<TransferFilter>(), It.IsAny<TransferQuery>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(new TransferPage { Total = 0, Counts = new Dictionary<TransferStates, int>(), Transfers = [] });

        var result = controller.GetUploadsPage("SUCCEEDED", offset: 10, limit: 100_000);

        var response = Assert.IsType<TransferPageResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("succeeded", response.Filter);
        Assert.Equal(10, response.Offset);
        Assert.Equal(TransferPaging.MaxLimit, response.Limit);
        mocks.Uploads.Verify(m => m.Page(TransferFilter.Succeeded, new TransferQuery(), 10, TransferPaging.MaxLimit), Times.Once);
    }

    [Fact]
    public void GetDownloadsPage_Defaults_To_All_And_Queries_Downloads()
    {
        var (controller, mocks) = GetFixture();
        mocks.Downloads.Setup(m => m.Page(It.IsAny<TransferFilter>(), It.IsAny<TransferQuery>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(new TransferPage { Total = 0, Counts = new Dictionary<TransferStates, int>(), Transfers = [] });

        var result = controller.GetDownloadsPage();

        var response = Assert.IsType<TransferPageResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("all", response.Filter);
        Assert.Equal(TransferPaging.DefaultLimit, response.Limit);
        mocks.Downloads.Verify(m => m.Page(TransferFilter.All, new TransferQuery(), 0, TransferPaging.DefaultLimit), Times.Once);
        mocks.Uploads.Verify(m => m.Page(It.IsAny<TransferFilter>(), It.IsAny<TransferQuery>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void GetUploadsPage_Passes_Trimmed_Username_And_Search_And_Echoes_Them()
    {
        var (controller, mocks) = GetFixture();
        mocks.Uploads.Setup(m => m.Page(It.IsAny<TransferFilter>(), It.IsAny<TransferQuery>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(new TransferPage { Total = 0, Counts = new Dictionary<TransferStates, int>(), Transfers = [] });

        var result = controller.GetUploadsPage(username: " bob ", search: "  ");

        var response = Assert.IsType<TransferPageResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("bob", response.Username);
        Assert.Null(response.Search);
        mocks.Uploads.Verify(m => m.Page(TransferFilter.All, new TransferQuery { Username = "bob" }, 0, TransferPaging.DefaultLimit), Times.Once);
    }

    [Theory]
    [InlineData("bogus", null, null, 10)]
    [InlineData(null, "bogus", null, 10)]
    [InlineData(null, null, "bogus", 10)]
    [InlineData("1", null, null, 10)]
    [InlineData(null, null, null, 0)]
    public void GetUploadsSummary_Returns_BadRequest_On_Invalid_Input(string by, string sort, string filter, int limit)
    {
        var (controller, mocks) = GetFixture();

        var result = controller.GetUploadsSummary(by, sort, filter, limit: limit);

        Assert.IsType<BadRequestObjectResult>(result);
        mocks.Uploads.Verify(
            m => m.Summarize(It.IsAny<TransferFilter>(), It.IsAny<TransferQuery>(), It.IsAny<TransferSummaryGrouping>(), It.IsAny<TransferSummarySort>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public void GetDownloadsSummary_Defaults_To_Directories_By_Users_And_Clamps_Limit()
    {
        var (controller, mocks) = GetFixture();
        mocks.Downloads.Setup(m => m.Summarize(It.IsAny<TransferFilter>(), It.IsAny<TransferQuery>(), It.IsAny<TransferSummaryGrouping>(), It.IsAny<TransferSummarySort>(), It.IsAny<int>()))
            .Returns(new TransferSummaryResult { Groups = 3, Transfers = 7, Rows = [] });

        var result = controller.GetDownloadsSummary(search: "flac", limit: 100_000);

        var response = Assert.IsType<TransferSummaryResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("directory", response.By);
        Assert.Equal("users", response.Sort);
        Assert.Equal(3, response.Groups);
        Assert.Equal(7, response.Transfers);
        Assert.Equal(TransferPaging.MaxSummaryLimit, response.Limit);
        mocks.Downloads.Verify(
            m => m.Summarize(TransferFilter.All, new TransferQuery { Search = "flac" }, TransferSummaryGrouping.Directory, TransferSummarySort.Users, TransferPaging.MaxSummaryLimit),
            Times.Once);
    }

    [Fact]
    public void GetUploadsPage_Groups_By_User_Then_Directory_In_Page_Order()
    {
        var (controller, mocks) = GetFixture();

        var transfers = new List<Transfer>
        {
            NewTransfer("bob", "@@a\\one\\1.flac"),
            NewTransfer("alice", "@@a\\two\\1.flac"),
            NewTransfer("bob", "@@a\\two\\2.flac"),
            NewTransfer("bob", "@@a\\one\\3.flac"),
        };

        mocks.Uploads.Setup(m => m.Page(It.IsAny<TransferFilter>(), It.IsAny<TransferQuery>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(new TransferPage { Total = 42, Counts = new Dictionary<TransferStates, int>(), Transfers = transfers });

        var result = controller.GetUploadsPage();

        var response = Assert.IsType<TransferPageResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(42, response.Total);

        var users = response.Users.ToList();
        Assert.Equal(new[] { "bob", "alice" }, users.Select(u => u.Username));

        var bob = users[0].Directories.ToList();
        Assert.Equal(new[] { "@@a\\one", "@@a\\two" }, bob.Select(d => d.Directory));
        Assert.Equal(2, bob[0].FileCount);
        Assert.Equal(new[] { transfers[0].Id, transfers[3].Id }, bob[0].Files.Select(f => f.Id));
        Assert.Equal(new[] { transfers[2].Id }, bob[1].Files.Select(f => f.Id));

        var alice = Assert.Single(users[1].Directories);
        Assert.Equal(new[] { transfers[1].Id }, alice.Files.Select(f => f.Id));
    }

    [Fact]
    public void GetUploadsPage_Counts_Are_Keyed_Like_Serialized_Transfer_State()
    {
        var (controller, mocks) = GetFixture();

        var counts = new Dictionary<TransferStates, int>
        {
            [Succeeded] = 503458,
            [QueuedLocally] = 56,
            [TransferStates.InProgress] = 2,
            [Cancelled] = 3,
        };

        mocks.Uploads.Setup(m => m.Page(It.IsAny<TransferFilter>(), It.IsAny<TransferQuery>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(new TransferPage { Total = 0, Counts = counts, Transfers = [] });

        var result = controller.GetUploadsPage();

        var response = Assert.IsType<TransferPageResponse>(Assert.IsType<OkObjectResult>(result).Value);

        // same converter configuration as the API (see Program.cs)
        var json = new JsonSerializerOptions();
        json.Converters.Add(new JsonStringEnumConverter());

        Assert.Equal(counts.Count, response.Counts.Count);

        foreach (var (state, count) in counts)
        {
            var key = JsonSerializer.Deserialize<string>(JsonSerializer.Serialize(state, json));
            Assert.Equal(count, response.Counts[key]);
        }

        Assert.Equal(503458, response.Counts["Completed, Succeeded"]);
        Assert.Equal(56, response.Counts["Queued, Locally"]);
    }

    [Theory]
    [InlineData("all")]
    [InlineData("active")]
    [InlineData("bogus")]
    public void ClearCompletedUploads_Returns_BadRequest_On_Invalid_Filter(string filter)
    {
        var (controller, mocks) = GetFixture();

        var result = controller.ClearCompletedUploads(filter);

        Assert.IsType<BadRequestObjectResult>(result);
        mocks.Uploads.Verify(m => m.Remove(It.IsAny<Expression<Func<Transfer, bool>>>()), Times.Never);
    }

    [Theory]
    [InlineData(null, new[] { Succeeded, Errored, Cancelled, TransferStates.Completed | TransferStates.Rejected })]
    [InlineData("completed", new[] { Succeeded, Errored, Cancelled, TransferStates.Completed | TransferStates.Rejected })]
    [InlineData("succeeded", new[] { Succeeded })]
    [InlineData("errored", new[] { Errored, TransferStates.Completed | TransferStates.Rejected })]
    [InlineData("cancelled", new[] { Cancelled })]
    public void ClearCompletedUploads_Removes_Only_Filtered_States(string filter, TransferStates[] expected)
    {
        var (controller, mocks) = GetFixture();

        Expression<Func<Transfer, bool>> captured = null;
        mocks.Uploads.Setup(m => m.Remove(It.IsAny<Expression<Func<Transfer, bool>>>()))
            .Callback<Expression<Func<Transfer, bool>>>(e => captured = e)
            .Returns(0);

        var result = controller.ClearCompletedUploads(filter);

        Assert.IsType<NoContentResult>(result);

        var predicate = captured.Compile();
        var candidates = new[] { Succeeded, Errored, Cancelled, TransferStates.Completed | TransferStates.Rejected, QueuedLocally, TransferStates.InProgress };

        Assert.Equal(expected.OrderBy(s => s), candidates.Where(s => predicate(new Transfer { State = s })).OrderBy(s => s));
        Assert.False(predicate(new Transfer { State = Succeeded, Removed = true }));
    }

    [Fact]
    public void ClearCompletedDownloads_Uses_Download_Service()
    {
        var (controller, mocks) = GetFixture();

        var result = controller.ClearCompletedDownloads("errored");

        Assert.IsType<NoContentResult>(result);
        mocks.Downloads.Verify(m => m.Remove(It.IsAny<Expression<Func<Transfer, bool>>>()), Times.Once);
        mocks.Uploads.Verify(m => m.Remove(It.IsAny<Expression<Func<Transfer, bool>>>()), Times.Never);
    }

    [Fact]
    public void CancelUploads_Returns_BadRequest_On_Invalid_Filter()
    {
        var (controller, mocks) = GetFixture();

        var result = controller.CancelUploads("completed");

        Assert.IsType<BadRequestObjectResult>(result);
        mocks.Uploads.Verify(m => m.ListActive(It.IsAny<TransferCancelFilter>()), Times.Never);
    }

    [Theory]
    [InlineData(null, TransferCancelFilter.All)]
    [InlineData("queued", TransferCancelFilter.Queued)]
    [InlineData("InProgress", TransferCancelFilter.InProgress)]
    public void CancelUploads_Cancels_Each_Match_And_Reports_Counts(string filter, TransferCancelFilter expected)
    {
        var (controller, mocks) = GetFixture();

        var matches = new List<Transfer> { NewTransfer("a", "@@x\\1"), NewTransfer("b", "@@x\\2"), NewTransfer("c", "@@x\\3") };

        mocks.Uploads.Setup(m => m.ListActive(expected)).Returns(matches);
        mocks.Uploads.Setup(m => m.TryCancel(It.IsAny<Guid>())).Returns(true);
        mocks.Uploads.Setup(m => m.TryCancel(matches[1].Id)).Returns(false);

        var result = controller.CancelUploads(filter);

        var response = Assert.IsType<CancelTransfersResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(3, response.Matched);
        Assert.Equal(2, response.Cancelled);

        foreach (var match in matches)
        {
            mocks.Uploads.Verify(m => m.TryCancel(match.Id), Times.Once);
        }
    }

    [Fact]
    public void CancelDownloads_Uses_Download_Service()
    {
        var (controller, mocks) = GetFixture();

        var matches = new List<Transfer> { NewTransfer("a", "@@x\\1") };
        mocks.Downloads.Setup(m => m.ListActive(TransferCancelFilter.Queued)).Returns(matches);
        mocks.Downloads.Setup(m => m.TryCancel(matches[0].Id)).Returns(true);

        var result = controller.CancelDownloads("queued");

        var response = Assert.IsType<CancelTransfersResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(1, response.Matched);
        Assert.Equal(1, response.Cancelled);
        mocks.Uploads.Verify(m => m.ListActive(It.IsAny<TransferCancelFilter>()), Times.Never);
    }

    private static Transfer NewTransfer(string username, string filename) => new()
    {
        Id = Guid.NewGuid(),
        Username = username,
        Filename = filename,
        State = Succeeded,
    };

    private static (TransfersController Controller, Mocks Mocks) GetFixture()
    {
        var mocks = new Mocks();

        var controller = new TransfersController(
            new TransferService(contextFactory: null, uploadService: mocks.Uploads.Object, downloadService: mocks.Downloads.Object),
            mocks.Users.Object,
            mocks.OptionsSnapshot.Object);

        return (controller, mocks);
    }

    private class Mocks
    {
        public Mock<IUploadService> Uploads { get; } = new Mock<IUploadService>();
        public Mock<IDownloadService> Downloads { get; } = new Mock<IDownloadService>();
        public Mock<IUserService> Users { get; } = new Mock<IUserService>();
        public Mock<IOptionsSnapshot<slskd.Options>> OptionsSnapshot { get; } = new Mock<IOptionsSnapshot<slskd.Options>>();
    }
}
