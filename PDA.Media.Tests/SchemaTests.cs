using System.IO;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace PDA.Media.Tests;

/// <summary>
/// Checks that the entity maps describe exactly the database in PDA.Media.Data/Schema/Media.Master.sql (the
/// database is the source of truth). When a test fails, the message lists each line that differs: "db:" is the
/// script, "ef:" is the maps.
/// </summary>
[TestClass]
public sealed partial class SchemaTests
{
    private static readonly string Script = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Schema", "Media.Master.sql"));

    private static IModel LoadModel()
    {
        using var context = new SqlCaptureContext().Context;
        // The design-time model keeps every annotation (clustered, filters, constraint names).
        return context.GetService<IDesignTimeModel>().Model;
    }

    [TestMethod]
    public void TestColumnsMatchTheDatabase()
    {
        var db = new List<string>();
        foreach (Match table in CreateTableRegex().Matches(Script))
        {
            foreach (Match column in ColumnRegex().Matches(table.Groups["body"].Value))
            {
                string identity = column.Groups["seed"].Success ? $" IDENTITY({column.Groups["seed"].Value})" : "";
                db.Add($"{table.Groups["table"].Value}.{column.Groups["column"].Value} {column.Groups["type"].Value.ToLowerInvariant()}{identity} {column.Groups["null"].Value}");
            }
        }

        var ef = new List<string>();
        foreach (var entity in LoadModel().GetEntityTypes())
        {
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!);
            foreach (var property in entity.GetProperties())
            {
                string identity = property.GetValueGenerationStrategy() == Microsoft.EntityFrameworkCore.Metadata.SqlServerValueGenerationStrategy.IdentityColumn
                    ? $" IDENTITY({property.GetIdentitySeed() ?? 1})" : "";
                ef.Add($"{entity.GetTableName()}.{property.GetColumnName(table)} {property.GetColumnType().ToLowerInvariant()}{identity} {(property.IsColumnNullable() ? "NULL" : "NOT NULL")}");
            }
        }

        AssertSame(db, ef);
    }

    [TestMethod]
    public void TestKeysAndIndexesMatchTheDatabase()
    {
        var db = new List<string>();
        foreach (Match table in CreateTableRegex().Matches(Script))
        {
            var pk = PrimaryKeyRegex().Match(table.Groups["body"].Value);
            db.Add($"{table.Groups["table"].Value} {pk.Groups["name"].Value} PRIMARY KEY {pk.Groups["clustered"].Value} ({pk.Groups["column"].Value})");
        }
        foreach (Match index in IndexRegex().Matches(Script))
        {
            string columns = string.Join(", ", ColumnNameRegex().Matches(index.Groups["columns"].Value).Select(m => m.Groups[1].Value));
            string where = index.Groups["filter"].Success ? $" WHERE {index.Groups["filter"].Value}" : "";
            db.Add($"{index.Groups["table"].Value} {index.Groups["name"].Value} {index.Groups["unique"].Value}{index.Groups["clustered"].Value} ({columns}){where}");
        }

        var ef = new List<string>();
        foreach (var entity in LoadModel().GetEntityTypes())
        {
            string table = entity.GetTableName()!;
            var pk = entity.FindPrimaryKey()!;
            ef.Add($"{table} {pk.GetName()} PRIMARY KEY {(pk.IsClustered() == false ? "NONCLUSTERED" : "CLUSTERED")} ({string.Join(", ", pk.Properties.Select(p => p.GetColumnName()))})");
            foreach (var index in entity.GetIndexes())
            {
                string where = index.GetFilter() is { } filter ? $" WHERE {filter}" : "";
                ef.Add($"{table} {index.GetDatabaseName()} {(index.IsUnique ? "UNIQUE " : "")}{(index.IsClustered() == true ? "CLUSTERED" : "NONCLUSTERED")} ({string.Join(", ", index.Properties.Select(p => p.GetColumnName()))}){where}");
            }
        }

        AssertSame(db, ef);
    }

    [TestMethod]
    public void TestForeignKeysMatchTheDatabase()
    {
        var db = ForeignKeyRegex().Matches(Script).Select(fk =>
        {
            // EF has no "set default"; ClientNoAction leaves it to the database.
            string onDelete = fk.Groups["actions"].Value switch
            {
                var a when a.Contains("ON DELETE SET DEFAULT") => "SET DEFAULT",
                var a when a.Contains("ON DELETE CASCADE") => "CASCADE",
                _ => "NO ACTION",
            };
            return $"{fk.Groups["table"].Value} {fk.Groups["name"].Value} ({fk.Groups["column"].Value}) -> {fk.Groups["principal"].Value} ON DELETE {onDelete}";
        }).ToList();

        var ef = LoadModel().GetEntityTypes().SelectMany(entity => entity.GetForeignKeys().Select(fk =>
        {
            string onDelete = fk.DeleteBehavior switch
            {
                DeleteBehavior.ClientNoAction => "SET DEFAULT",
                DeleteBehavior.Cascade => "CASCADE",
                _ => "NO ACTION",
            };
            return $"{entity.GetTableName()} {fk.GetConstraintName()} ({string.Join(", ", fk.Properties.Select(p => p.GetColumnName()))}) -> {fk.PrincipalEntityType.GetTableName()} ON DELETE {onDelete}";
        })).ToList();

        AssertSame(db, ef);
    }

    [TestMethod]
    public void TestDefaultConstraintsExistInTheDatabase()
    {
        // The maps only need the defaults EF has to know about; database defaults of 0 / false match the C#
        // default and are left out. So this checks one way: every default in the maps is in the database.
        var db = new HashSet<string>();
        foreach (Match table in CreateTableRegex().Matches(Script))
        {
            foreach (Match column in ColumnRegex().Matches(table.Groups["body"].Value))
            {
                if (column.Groups["default"].Success)
                    db.Add($"{table.Groups["table"].Value}.{column.Groups["column"].Value} {column.Groups["default"].Value}");
            }
        }

        var missing = new List<string>();
        foreach (var entity in LoadModel().GetEntityTypes())
        {
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!);
            foreach (var property in entity.GetProperties())
            {
                // The name given in HasDefaultValue(value, name) / HasDefaultValueSql(sql, name).
                if (property.FindAnnotation(RelationalAnnotationNames.DefaultConstraintName)?.Value is string name)
                {
                    string line = $"{entity.GetTableName()}.{property.GetColumnName(table)} {name}";
                    if (!db.Contains(line)) missing.Add("ef: " + line);
                }
            }
        }

        if (missing.Count > 0)
            Assert.Fail("Default constraints in the maps but not in Media.Master.sql:\n" + string.Join("\n", missing));
    }

    private static void AssertSame(List<string> db, List<string> ef)
    {
        Assert.IsNotEmpty(db, "Nothing was read from Media.Master.sql");
        var differences = db.Except(ef).Select(d => "db: " + d).Concat(ef.Except(db).Select(e => "ef: " + e)).Order().ToList();
        if (differences.Count > 0)
            Assert.Fail("The entity maps don't match Media.Master.sql:\n" + string.Join("\n", differences));
    }

    // Media.Master.sql is written one column / index / foreign key per line, which these patterns rely on.

    [GeneratedRegex(@"CREATE TABLE \[dbo\]\.\[(?<table>\w+)\] \((?<body>.*?)\n\);", RegexOptions.Singleline)]
    private static partial Regex CreateTableRegex();

    [GeneratedRegex(@"^\s+\[(?<column>\w+)\]\s+(?<type>\w+(\((\d+|max)\))?)\s+(IDENTITY\((?<seed>\d+),1\)\s+)?(?<null>NOT NULL|NULL)(\s+CONSTRAINT \[(?<default>\w+)\] DEFAULT)?", RegexOptions.Multiline)]
    private static partial Regex ColumnRegex();

    [GeneratedRegex(@"CONSTRAINT \[(?<name>\w+)\] PRIMARY KEY (?<clustered>CLUSTERED|NONCLUSTERED) \(\[(?<column>\w+)\]\)")]
    private static partial Regex PrimaryKeyRegex();

    [GeneratedRegex(@"^CREATE (?<unique>UNIQUE )?(?<clustered>CLUSTERED|NONCLUSTERED) INDEX \[(?<name>\w+)\] ON \[dbo\]\.\[(?<table>\w+)\] \((?<columns>[^)]*)\)( WHERE (?<filter>.*?))?;\r?$", RegexOptions.Multiline)]
    private static partial Regex IndexRegex();

    [GeneratedRegex(@"^ALTER TABLE \[dbo\]\.\[(?<table>\w+)\] ADD CONSTRAINT \[(?<name>\w+)\] FOREIGN KEY \(\[(?<column>\w+)\]\) REFERENCES \[dbo\]\.\[(?<principal>\w+)\] \(\[Id\]\)(?<actions>[^;]*);", RegexOptions.Multiline)]
    private static partial Regex ForeignKeyRegex();

    [GeneratedRegex(@"\[(\w+)\]")]
    private static partial Regex ColumnNameRegex();
}
