using Microsoft.EntityFrameworkCore;
using TheWanderLustWebAPI.Models;

namespace TheWanderLustWebAPI.Context
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.FirebaseId).IsUnique();
                entity.HasIndex(e => e.Email).IsUnique();
            });
        }
    }
}
