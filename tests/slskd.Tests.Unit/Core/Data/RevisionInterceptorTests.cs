namespace slskd.Tests.Unit.Core.Data;

using System;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using slskd.Transfers;
using Soulseek;
using Xunit;

using Transfer = slskd.Transfers.Transfer;

public class RevisionInterceptorTests
{
    [Theory]
    [InlineData("SELECT 1", false)]
    [InlineData("  select * from x", false)]
    [InlineData("PRAGMA synchronous=1;", false)]
    [InlineData("INSERT INTO x VALUES (1)", true)]
    [InlineData("UPDATE x SET a = 1", true)]
    [InlineData("DELETE FROM x", true)]
    public void IsWrite_Classifies_Statements(string sql, bool expected)
    {
        Assert.Equal(expected, RevisionInterceptor<TransfersDbContext>.IsWrite(sql));
    }

    [Fact]
    public void Revision_Changes_On_Writes_And_Not_On_Reads()
    {
        var connectionString = $"Data Source=revision_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        using var anchor = new SqliteConnection(connectionString);
        anchor.Open();

        var options = new DbContextOptionsBuilder<TransfersDbContext>()
            .UseSqlite(connectionString)
            .AddInterceptors(new RevisionInterceptor<TransfersDbContext>())
            .Options;

        using (var context = new TransfersDbContext(options))
        {
            context.Database.EnsureCreated();
        }

        var id = Guid.NewGuid();
        var before = RevisionInterceptor<TransfersDbContext>.Current;

        using (var context = new TransfersDbContext(options))
        {
            context.Transfers.Add(new Transfer { Id = id, Username = "u", Filename = "f", Direction = TransferDirection.Upload, RequestedAt = DateTime.UtcNow });
            context.SaveChanges();
        }

        var afterInsert = RevisionInterceptor<TransfersDbContext>.Current;
        Assert.NotEqual(before, afterInsert);

        using (var context = new TransfersDbContext(options))
        {
            _ = context.Transfers.AsNoTracking().ToList();
        }

        Assert.Equal(afterInsert, RevisionInterceptor<TransfersDbContext>.Current);

        using (var context = new TransfersDbContext(options))
        {
            context.Transfers.Where(t => t.Id == id).ExecuteUpdate(s => s.SetProperty(t => t.BytesTransferred, 42));
        }

        Assert.NotEqual(afterInsert, RevisionInterceptor<TransfersDbContext>.Current);
    }
}
