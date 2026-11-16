namespace slskd.Tests.Unit.Core.Data;

using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Moq;
using Xunit;

public class MigratorTests
{
    [Fact]
    public void Satisfied_Migrations_Are_Recorded_And_Not_Inspected_Again()
    {
        using var db = new Db();
        var first = new Mock<IMigration>();
        var second = new Mock<IMigration>();
        first.Setup(m => m.NeedsToBeApplied()).Returns(false);
        second.Setup(m => m.NeedsToBeApplied()).Returns(false);

        var migrations = new Dictionary<string, IMigration> { ["First"] = first.Object, ["Second"] = second.Object };

        new Migrator(db.Databases, migrations).Migrate();
        new Migrator(db.Databases, migrations).Migrate();

        first.Verify(m => m.NeedsToBeApplied(), Times.Once);
        second.Verify(m => m.NeedsToBeApplied(), Times.Once);
        Assert.Equal(["First", "Second"], db.History());
    }

    [Fact]
    public void Only_Unrecorded_Migrations_Are_Inspected()
    {
        using var db = new Db();
        var old = new Mock<IMigration>();
        old.Setup(m => m.NeedsToBeApplied()).Returns(false);

        new Migrator(db.Databases, new Dictionary<string, IMigration> { ["Old"] = old.Object }).Migrate();

        var added = new Mock<IMigration>();
        added.Setup(m => m.NeedsToBeApplied()).Returns(false);

        new Migrator(db.Databases, new Dictionary<string, IMigration> { ["Old"] = old.Object, ["Added"] = added.Object }).Migrate();

        old.Verify(m => m.NeedsToBeApplied(), Times.Once);
        added.Verify(m => m.NeedsToBeApplied(), Times.Once);
    }

    [Fact]
    public void Force_Ignores_History()
    {
        using var db = new Db();
        var migration = new Mock<IMigration>();
        migration.Setup(m => m.NeedsToBeApplied()).Returns(false);
        var migrations = new Dictionary<string, IMigration> { ["Only"] = migration.Object };

        new Migrator(db.Databases, migrations).Migrate();
        new Migrator(db.Databases, migrations).Migrate(force: true);

        migration.Verify(m => m.NeedsToBeApplied(), Times.Exactly(2));
    }

    private sealed class Db : IDisposable
    {
        private readonly SqliteConnection anchor;
        private readonly string connectionString = $"Data Source=migrator_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";

        public Db()
        {
            // keep the shared in-memory database alive for the life of the fixture
            anchor = new SqliteConnection(connectionString);
            anchor.Open();

            var strings = new Dictionary<Database, ConnectionString>();

            foreach (var database in Database.List)
            {
                strings[database] = connectionString;
            }

            Databases = new ConnectionStringDictionary(strings);
        }

        public ConnectionStringDictionary Databases { get; }

        public List<string> History()
        {
            using var command = new SqliteCommand($"SELECT Name FROM {Migrator.HistoryTable} ORDER BY Name", anchor);
            using var reader = command.ExecuteReader();

            var names = new List<string>();

            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }

            return names;
        }

        public void Dispose() => anchor.Dispose();
    }
}
