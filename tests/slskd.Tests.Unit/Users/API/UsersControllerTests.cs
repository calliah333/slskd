namespace slskd.Tests.Unit.Users.API;

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using slskd.Users;
using slskd.Users.API;
using Soulseek;
using Xunit;

public class UsersControllerTests
{
    private const string Username = "alice";

    [Fact]
    public async Task BrowseDirectories_Returns_Counts_And_Unlocked_Then_Locked_Directories_Without_Files()
    {
        var (controller, mocks) = GetFixture(new BrowseResponse(
            [Dir("Music\\Z", 2), Dir("Music\\A", 3)],
            [Dir("Private\\Stuff", 4)]));

        var result = await controller.BrowseDirectories(Username);

        var response = Assert.IsType<BrowseDirectoriesResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(2, response.DirectoryCount);
        Assert.Equal(5, response.FileCount);
        Assert.Equal(1, response.LockedDirectoryCount);
        Assert.Equal(4, response.LockedFileCount);

        var directories = response.Directories.ToList();
        Assert.Equal(new[] { "Music\\Z", "Music\\A", "Private\\Stuff" }, directories.Select(d => d.Name));
        Assert.Equal(new[] { 2, 3, 4 }, directories.Select(d => d.FileCount));
        Assert.Equal(new[] { false, false, true }, directories.Select(d => d.Locked));

        var json = JsonSerializer.Serialize(response, JsonOptions());
        Assert.DoesNotContain("\"files\"", json);
        Assert.Contains("{\"name\":\"Private\\\\Stuff\",\"fileCount\":4,\"locked\":true}", json);

        Assert.True(mocks.Cache.TryGet(Username, out _));
    }

    [Fact]
    public async Task BrowseDirectories_Returns_NotFound_With_Message_When_User_Offline()
    {
        var (controller, mocks) = GetFixture(response: null);
        mocks.Client.Setup(m => m.BrowseAsync(Username, It.IsAny<BrowseOptions>(), It.IsAny<CancellationToken?>()))
            .ThrowsAsync(new UserOfflineException("offline"));

        var result = await controller.BrowseDirectories(Username);

        Assert.Equal("offline", Assert.IsType<NotFoundObjectResult>(result).Value);
        Assert.False(mocks.Cache.TryGet(Username, out _));
    }

    [Fact]
    public async Task Browse_Returns_Response_Unchanged_And_Populates_Cache()
    {
        var browse = new BrowseResponse([Dir("Music\\A", 1)]);
        var (controller, mocks) = GetFixture(browse);

        var result = await controller.Browse(Username);

        Assert.Same(browse, Assert.IsType<OkObjectResult>(result).Value);
        Assert.True(mocks.Cache.TryGet(Username, out var entry));
        Assert.Equal(1, entry.FileCount);
    }

    [Fact]
    public async Task BrowseFiles_Returns_Files_Of_Exact_Directory_When_Not_Recursive()
    {
        var album = Dir("Music\\A", 3);
        var (controller, _) = GetFixture(new BrowseResponse([album, Dir("Music\\A\\CD1", 2), Dir("Music\\AB", 2)]));
        await controller.Browse(Username);

        var result = controller.BrowseFiles(Username, new BrowseFilesRequest { Directory = "Music\\A" });

        var response = Assert.IsType<BrowseFilesResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(3, response.Total);
        Assert.False(response.Truncated);

        var directory = Assert.Single(response.Directories);
        Assert.Equal("Music\\A", directory.Name);
        Assert.Equal(3, directory.FileCount);
        Assert.False(directory.Locked);
        Assert.Same(album.Files, directory.Files);
    }

    [Fact]
    public async Task BrowseFiles_Recursive_Matches_Both_Separators_And_Not_Sibling_Prefix()
    {
        var (controller, _) = GetFixture(new BrowseResponse(
            [Dir("A\\B", 1), Dir("A\\BC", 5), Dir("A\\B\\C", 2), Dir("A\\B/D", 3)],
            [Dir("A\\B\\Locked", 4), Dir("A\\BC\\Locked", 6)]));
        await controller.Browse(Username);

        var result = controller.BrowseFiles(Username, new BrowseFilesRequest { Directory = "A\\B", Recursive = true });

        var response = Assert.IsType<BrowseFilesResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(10, response.Total);
        Assert.False(response.Truncated);
        Assert.Equal(new[] { "A\\B", "A\\B\\C", "A\\B/D", "A\\B\\Locked" }, response.Directories.Select(d => d.Name));
        Assert.Equal(new[] { false, false, false, true }, response.Directories.Select(d => d.Locked));
        Assert.Equal(new[] { 1, 2, 3, 4 }, response.Directories.Select(d => d.Files.Count()));
    }

    [Fact]
    public async Task BrowseFiles_Fills_Files_Up_To_Limit_And_Reports_Truncation()
    {
        var (controller, _) = GetFixture(new BrowseResponse([Dir("A", 2), Dir("A\\1", 3), Dir("A\\2", 4), Dir("A\\3", 5)]));
        await controller.Browse(Username);

        var result = controller.BrowseFiles(Username, new BrowseFilesRequest { Directory = "A", Recursive = true, Limit = 4 });

        var response = Assert.IsType<BrowseFilesResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(14, response.Total);
        Assert.True(response.Truncated);

        var directories = response.Directories.ToList();
        Assert.Equal(new[] { "A", "A\\1" }, directories.Select(d => d.Name));
        Assert.Equal(new[] { 2, 3 }, directories.Select(d => d.FileCount));
        Assert.Equal(new[] { 2, 2 }, directories.Select(d => d.Files.Count()));
        Assert.Equal(new[] { "A\\1\\00.flac", "A\\1\\01.flac" }, directories[1].Files.Select(f => f.Filename));
    }

    [Fact]
    public async Task BrowseFiles_Stops_At_Directory_Boundary_When_Limit_Reached_Exactly()
    {
        var (controller, _) = GetFixture(new BrowseResponse([Dir("A", 2), Dir("A\\1", 3), Dir("A\\2", 4)]));
        await controller.Browse(Username);

        var result = controller.BrowseFiles(Username, new BrowseFilesRequest { Directory = "A", Recursive = true, Limit = 5 });

        var response = Assert.IsType<BrowseFilesResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(9, response.Total);
        Assert.True(response.Truncated);
        Assert.Equal(new[] { "A", "A\\1" }, response.Directories.Select(d => d.Name));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(BrowseFilesRequest.MaxLimit + 1, 6)]
    public async Task BrowseFiles_Clamps_Limit(int limit, int expectedFiles)
    {
        var (controller, _) = GetFixture(new BrowseResponse([Dir("A", 6)]));
        await controller.Browse(Username);

        var result = controller.BrowseFiles(Username, new BrowseFilesRequest { Directory = "A", Limit = limit });

        var response = Assert.IsType<BrowseFilesResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(expectedFiles, response.Directories.Single().Files.Count());
        Assert.Equal(expectedFiles < 6, response.Truncated);
    }

    [Fact]
    public void BrowseFiles_Defaults_Limit_And_Recursive()
    {
        var request = JsonSerializer.Deserialize<BrowseFilesRequest>("{\"directory\":\"A\"}", JsonOptions());

        Assert.Equal("A", request.Directory);
        Assert.False(request.Recursive);
        Assert.Equal(BrowseFilesRequest.DefaultLimit, request.Limit);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task BrowseFiles_Returns_BadRequest_When_Directory_Empty(string directory)
    {
        var (controller, _) = GetFixture(new BrowseResponse([Dir("A", 1)]));
        await controller.Browse(Username);

        Assert.IsType<BadRequestResult>(controller.BrowseFiles(Username, new BrowseFilesRequest { Directory = directory }));
        Assert.IsType<BadRequestResult>(controller.BrowseFiles(Username, null));
    }

    [Fact]
    public void BrowseFiles_Returns_NotFound_When_Not_Cached()
    {
        var (controller, mocks) = GetFixture(new BrowseResponse([Dir("A", 1)]));

        var result = controller.BrowseFiles(Username, new BrowseFilesRequest { Directory = "A" });

        Assert.Equal($"No cached browse results for {Username}; browse again", Assert.IsType<NotFoundObjectResult>(result).Value);
        mocks.Client.Verify(m => m.BrowseAsync(It.IsAny<string>(), It.IsAny<BrowseOptions>(), It.IsAny<CancellationToken?>()), Times.Never);
    }

    [Theory]
    [InlineData("B", false)]
    [InlineData("B", true)]
    [InlineData("A\\", true)]
    [InlineData("a", false)]
    public async Task BrowseFiles_Returns_NotFound_When_Directory_Not_Cached(string directory, bool recursive)
    {
        var (controller, _) = GetFixture(new BrowseResponse([Dir("A", 1), Dir("AB", 1)]));
        await controller.Browse(Username);

        var result = controller.BrowseFiles(Username, new BrowseFilesRequest { Directory = directory, Recursive = recursive });

        Assert.Equal("Directory not found", Assert.IsType<NotFoundObjectResult>(result).Value);
    }

    [Fact]
    public async Task BrowseFiles_Serializes_Files_Exactly_As_Legacy_Browse()
    {
        var album = new Directory("Music\\A", [
            new File(1, "Music\\A\\01.flac", 31_000_000, "flac", [new FileAttribute(FileAttributeType.Length, 245), new FileAttribute(FileAttributeType.BitDepth, 16), new FileAttribute(FileAttributeType.SampleRate, 44100)]),
            new File(1, "Music\\A\\02.mp3", 9_000_000, "mp3", [new FileAttribute(FileAttributeType.BitRate, 320), new FileAttribute(FileAttributeType.VariableBitRate, 0)]),
            new File(1, "Music\\A\\cover.jpg", 120_000, "jpg"),
        ]);

        var browse = new BrowseResponse([album]);
        var (controller, _) = GetFixture(browse);
        await controller.Browse(Username);

        var response = Assert.IsType<OkObjectResult>(controller.BrowseFiles(Username, new BrowseFilesRequest { Directory = "Music\\A" })).Value;

        using var legacy = JsonDocument.Parse(JsonSerializer.Serialize(browse, JsonOptions()));
        using var current = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonOptions()));

        var legacyFiles = legacy.RootElement.GetProperty("directories")[0].GetProperty("files").GetRawText();
        var currentFiles = current.RootElement.GetProperty("directories")[0].GetProperty("files").GetRawText();

        Assert.Equal(legacyFiles, currentFiles);
        Assert.Contains("\"bitDepth\":16", currentFiles);
    }

    private static Directory Dir(string name, int files)
        => new Directory(name, Enumerable.Range(0, files).Select(i => new File(1, $"{name}\\{i:D2}.flac", 1000 + i, "flac")).ToList());

    // same configuration as the API (see Program.cs); MVC starts from the web defaults
    private static JsonSerializerOptions JsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        return options;
    }

    private static (UsersController Controller, Mocks Mocks) GetFixture(BrowseResponse response)
    {
        var mocks = new Mocks();

        mocks.Client.Setup(m => m.BrowseAsync(Username, It.IsAny<BrowseOptions>(), It.IsAny<CancellationToken?>()))
            .ReturnsAsync(response);

        var controller = new UsersController(
            mocks.Client.Object,
            mocks.Tracker.Object,
            mocks.Cache,
            mocks.Users.Object,
            mocks.OptionsSnapshot.Object);

        return (controller, mocks);
    }

    private class Mocks
    {
        public Mock<ISoulseekClient> Client { get; } = new Mock<ISoulseekClient>();
        public Mock<IBrowseTracker> Tracker { get; } = new Mock<IBrowseTracker>();
        public BrowseCache Cache { get; } = new BrowseCache();
        public Mock<IUserService> Users { get; } = new Mock<IUserService>();
        public Mock<IOptionsSnapshot<slskd.Options>> OptionsSnapshot { get; } = new Mock<IOptionsSnapshot<slskd.Options>>();
    }
}
