using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace slskd.Tests.Unit.Common;

public class BlacklistTests
{
    private List<(string, bool)> IPs = new List<(string, bool)>()
    {
        ("1.2.4.0", true),
        ("1.2.4.128", true),
        ("1.2.4.255", true),
        ("1.2.8.0", true),
        ("1.2.8.128", true),
        ("1.2.8.255", true),
        ("1.9.96.104", false),
        ("1.9.96.105", true),
        ("1.9.102.250", false),
        ("1.9.102.251", true),
        ("1.9.106.186", true),
        ("1.9.106.187", false),
        ("192.168.1.1", false),
        ("4.4.4.4", false),
        ("123.234.111.123", false),
    };

    [Theory]
    [InlineData("Data/Blacklist/cidr.txt", BlacklistFormat.CIDR)]
    [InlineData("Data/Blacklist/dat.txt", BlacklistFormat.DAT)]
    [InlineData("Data/Blacklist/p2p.txt", BlacklistFormat.P2P)]
    public async Task DetectFormat_Detects(string filename, BlacklistFormat expected)
    {
        var result = await Blacklist.DetectFormat(filename);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Data/Blacklist/junk.txt", typeof(FormatException))]
    [InlineData("Data/Blacklist/empty.txt", typeof(FormatException))]
    [InlineData("Data/does.not.exist.txt", typeof(IOException))]
    public async Task DetectFormat_Throws(string filename, Type exType)
    {
        var ex = await Record.ExceptionAsync(() => Blacklist.DetectFormat(filename));

        Assert.NotNull(ex);
        Assert.IsAssignableFrom(exType, ex);
    }

    [Theory]
    [InlineData("Data/Blacklist/junk.txt", BlacklistFormat.CIDR, typeof(FormatException))]
    [InlineData("Data/does.not.exist.txt", BlacklistFormat.DAT, typeof(IOException))]
    public async Task Load_throws(string filename, BlacklistFormat format, Type exType)
    {
        var bl = new Blacklist();
        var ex = await Record.ExceptionAsync(() => bl.Load(filename, format));

        Assert.NotNull(ex);
        Assert.IsAssignableFrom(exType, ex);
    }

    [Theory]
    [InlineData("Data/Blacklist/cidr.txt", BlacklistFormat.CIDR)]
    [InlineData("Data/Blacklist/dat.txt", BlacklistFormat.DAT)]
    [InlineData("Data/Blacklist/p2p.txt", BlacklistFormat.P2P)]
    public async Task Load_Loads(string filename, BlacklistFormat format)
    {
        var bl = new Blacklist();
        var ex = await Record.ExceptionAsync(() => bl.Load(filename, format));

        Assert.Null(ex);

        // the test files are assumed to all contain 5 entries
        Assert.Equal(5, bl.Count);
    }

    [Theory]
    [InlineData("Data/Blacklist/cidr.txt", BlacklistFormat.CIDR)]
    public async Task Load_Then_Clear(string filename, BlacklistFormat format)
    {
        var bl = new Blacklist();
        var ex = await Record.ExceptionAsync(() => bl.Load(filename, format));

        Assert.Null(ex);

        // the test files are assumed to all contain 5 entries
        Assert.Equal(5, bl.Count);

        bl.Clear();

        Assert.Equal(0, bl.Count);
    }

    [Theory]
    [InlineData("Data/Blacklist/cidr.txt")]
    [InlineData("Data/Blacklist/dat.txt")]
    [InlineData("Data/Blacklist/p2p.txt")]
    public async Task Load_AutoDetects_And_Loads(string filename)
    {
        var bl = new Blacklist();
        var ex = await Record.ExceptionAsync(() => bl.Load(filename));

        Assert.Null(ex);

        // the test files are assumed to all contain 5 entries
        Assert.Equal(5, bl.Count);
    }

    [Theory]
    [InlineData("Data/Blacklist/cidr.txt")]
    [InlineData("Data/Blacklist/dat.txt")]
    [InlineData("Data/Blacklist/p2p.txt")]
    public async Task CIDR_Contains(string filename)
    {
        var bl = new Blacklist();
        await bl.Load(filename);

        foreach (var ip in IPs)
        {
            if (bl.Contains(IPAddress.Parse(ip.Item1)) != ip.Item2)
            {
                throw new Exception($"Expected contains for {ip.Item1} to be {ip.Item2} but it wasn't");
            }
        }
    }

    [Theory]
    [InlineData("10.200.0.1", true)] // only in the /8; the /16 nested inside it starts closer but ends before it
    [InlineData("10.1.2.3", true)]
    [InlineData("12.0.0.200", true)]
    [InlineData("12.0.1.200", true)] // adjacent ranges merge
    [InlineData("12.0.2.0", false)]
    [InlineData("9.255.255.255", false)]
    [InlineData("11.0.0.1", false)]
    public async Task Contains_Handles_Nested_Overlapping_And_Adjacent_Ranges(string ip, bool expected)
    {
        var filename = Path.GetTempFileName();

        try
        {
            await File.WriteAllLinesAsync(filename, ["10.0.0.0/8", "10.1.0.0/16", "12.0.0.0/24", "12.0.0.128/25", "12.0.1.0/24"]);

            var bl = new Blacklist();
            await bl.Load(filename, BlacklistFormat.CIDR);

            Assert.Equal(expected, bl.Contains(IPAddress.Parse(ip)));
        }
        finally
        {
            File.Delete(filename);
        }
    }
}