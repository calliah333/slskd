namespace slskd.Tests.Unit.Shares
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Moq;
    using slskd.Files;
    using slskd.Shares;
    using Soulseek;
    using Xunit;

    public class ShareServiceTests
    {
        public class SearchAsync
        {
            [Fact]
            public async Task Applies_Limit_Across_All_Hosts()
            {
                var a = Random();
                var localFiles = new[] { $@"@@local\{a}\1.dat", $@"@@local\{a}\2.dat", $@"@@local\{a}\3.dat" };
                var hostFiles = new[] { $@"@@host\{a}\1.dat", $@"@@host\{a}\2.dat", $@"@@host\{a}\3.dat" };

                using var local = SqliteShareRepositoryTests.GetRepository(localFiles);
                using var host = SqliteShareRepositoryTests.GetRepository(hostFiles);

                var service = CreateService(local, ("host", host));

                var results = await service.SearchAsync(new SearchQuery(terms: [a], exclusions: []), limit: 4);

                Assert.Equal(localFiles.Append(hostFiles[0]), results.Select(f => f.Filename));
            }

            [Fact]
            public async Task Does_Not_Query_Remaining_Hosts_Once_Limit_Is_Reached()
            {
                var a = Random();

                using var local = SqliteShareRepositoryTests.GetRepository([$@"@@local\{a}\1.dat", $@"@@local\{a}\2.dat"]);
                var host = new Mock<IShareRepository>();

                var service = CreateService(local, ("host", host.Object));

                var results = await service.SearchAsync(new SearchQuery(terms: [a], exclusions: []), limit: 2);

                Assert.Equal(2, results.Count);
                host.Verify(r => r.Search(It.IsAny<ShareSearchQuery>(), It.IsAny<int?>()), Times.Never);
            }

            [Fact]
            public async Task Returns_Matches_From_All_Hosts_Without_Limit()
            {
                var a = Random();

                using var local = SqliteShareRepositoryTests.GetRepository([$@"@@local\{a}\1.dat"]);
                using var host = SqliteShareRepositoryTests.GetRepository([$@"@@host\{a}\1.dat", $@"@@host\{a}\2.dat"]);

                var service = CreateService(local, ("host", host));

                var results = await service.SearchAsync(new SearchQuery(terms: [a], exclusions: []));

                Assert.Equal(3, results.Count);
            }
        }

        private static string Random() => Guid.NewGuid().ToString("N");

        private static ShareService CreateService(IShareRepository local, params (string Name, IShareRepository Repository)[] hosts)
        {
            var optionsMonitor = new TestOptionsMonitor<Options>(new Options());

            var factory = new Mock<IShareRepositoryFactory>();
            factory.Setup(f => f.CreateFromHost(Program.LocalHostName)).Returns(local);

            foreach (var (name, repository) in hosts)
            {
                factory.Setup(f => f.CreateFromHost(name)).Returns(repository);
            }

            var scanner = new Mock<IShareScanner>();
            scanner.Setup(s => s.StateMonitor).Returns(new Mock<IStateMonitor<SharedFileCacheState>>().Object);

            var service = new ShareService(new FileService(optionsMonitor), factory.Object, optionsMonitor, scanner.Object);

            foreach (var (name, _) in hosts)
            {
                service.AddOrUpdateHost(new Host(name));
            }

            return service;
        }
    }
}
