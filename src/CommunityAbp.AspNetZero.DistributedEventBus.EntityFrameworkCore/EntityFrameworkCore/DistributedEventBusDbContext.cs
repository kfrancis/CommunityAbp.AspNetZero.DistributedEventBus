using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Abp.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CommunityAbp.AspNetZero.DistributedEventBus.EntityFrameworkCore.EntityFrameworkCore
{
    public class DistributedEventBusDbContext : AbpDbContext
    {
        public DistributedEventBusDbContext(DbContextOptions<DistributedEventBusDbContext> options) : base(options)
        {
        }
    }
}
