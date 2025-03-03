using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EntityFrameworkCore;

public static class DbContextOptionsConfigurer
{
    public static void Configure(DbContextOptionsBuilder<DistributedEventBusDbContext> builder, string connectionString)
    {
        builder.UseSqlServer(connectionString);
    }

    public static void Configure(DbContextOptionsBuilder<DistributedEventBusDbContext> builder, DbConnection connection)
    {
        builder.UseSqlServer(connection);
    }
}
