using System;
using System.IO;
using System.Linq;
using Abp.Reflection.Extensions;
using CommunityAbp.AspNetZero.DistributedEventBus.Core;
using CommunityAbp.AspNetZero.DistributedEventBus.Core.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EntityFrameworkCore;

/* This class is needed to run EF Core PMC commands. Not used anywhere else  */
public class DistributedEventBusDbContextFactory : IDesignTimeDbContextFactory<DistributedEventBusDbContext>
{
    public DistributedEventBusDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<DistributedEventBusDbContext>();
        var configuration = AppConfigurations.Get(WebContentDirectoryFinder.CalculateContentRootFolder());

        DbContextOptionsConfigurer.Configure(
            builder,
            configuration.GetConnectionString(AspNetZeroDistributedEventBusConsts.ConnectionStringName)
        );

        return new DistributedEventBusDbContext(builder.Options);
    }
}

/// <summary>
///     This class is used to find root path of the web project in;
///     unit tests (to find views) and entity framework core command line commands (to find conn string).
/// </summary>
public static class WebContentDirectoryFinder
{
    public static string CalculateContentRootFolder()
    {
        var coreAssemblyDirectoryPath = Path.GetDirectoryName(typeof(WebContentDirectoryFinder).GetAssembly().Location);
        if (coreAssemblyDirectoryPath == null)
        {
            throw new Exception("Could not find location of ModularTodoApp.Core assembly!");
        }

        var directoryInfo = new DirectoryInfo(coreAssemblyDirectoryPath);
        while (!DirectoryContains(directoryInfo.FullName, "CommunityAbp.AspNetZero.DistributedEventBus.sln"))
        {
            directoryInfo = directoryInfo.Parent ?? throw new Exception("Could not find content root folder!");
        }
        // Return solution root (adjust if a specific web project is added later)
        return directoryInfo.FullName;
    }

    private static bool DirectoryContains(string directory, string fileName)
    {
        return Directory.GetFiles(directory).Any(filePath => string.Equals(Path.GetFileName(filePath), fileName));
    }
}
