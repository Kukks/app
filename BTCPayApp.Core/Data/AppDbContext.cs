using Microsoft.EntityFrameworkCore;
using NArk.Storage.EfCore;

namespace BTCPayApp.Core.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Setting> Settings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigureArkEntities(o => o.StoreDateTimeOffsetAsTicks = true);
        base.OnModelCreating(modelBuilder);
    }
}
