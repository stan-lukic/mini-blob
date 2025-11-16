using Microsoft.EntityFrameworkCore;
using MiniBlob.Api.Data.Models;

namespace MiniBlob.Api.Data;

public class BlobDbContext : DbContext
{
    public BlobDbContext(DbContextOptions<BlobDbContext> options) : base(options) { }
    public DbSet<BlobMetadata> Blobs => Set<BlobMetadata>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BlobMetadata>()
            .HasIndex(b => new { b.Container, b.BlobPath })
            .IsUnique();
    }
}
