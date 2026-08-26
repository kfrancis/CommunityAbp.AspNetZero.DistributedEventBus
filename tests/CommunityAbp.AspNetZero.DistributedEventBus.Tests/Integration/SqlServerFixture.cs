using CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace CommunityAbp.AspNetZero.DistributedEventBus.Tests.Integration;

public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .WithDatabase("DistributedEventBusTests")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
    }

    public Task DisposeAsync()
    {
        return _container.DisposeAsync().AsTask();
    }

    public DistributedEventBusDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<DistributedEventBusDbContext>()
            .UseSqlServer(_container.GetConnectionString())
            .Options;

        return new DistributedEventBusDbContext(options);
    }

    public async Task ResetDatabaseAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureDeletedAsync();
        await dbContext.Database.EnsureCreatedAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SQL Server";
}
