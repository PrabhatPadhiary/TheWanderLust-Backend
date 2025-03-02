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
        public DbSet<Blog> Blogs { get; set; }
        public DbSet<BlogLikes> BlogLikes { get; set; }
        public DbSet<ImageMetadata> ImageMetadata { get; set; }
        public DbSet<BlogComments> BlogComments { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>().ToTable("User");

            // Explicitly defining column types for MySQL compatibility
            modelBuilder.Entity<User>()
                .Property(u => u.FirstName)
                .HasColumnType("VARCHAR(255)");

            modelBuilder.Entity<User>()
                .Property(u => u.LastName)
                .HasColumnType("VARCHAR(255)");

            modelBuilder.Entity<User>()
                .Property(u => u.Email)
                .HasColumnType("VARCHAR(255)");

            modelBuilder.Entity<User>()
                .Property(u => u.Username)
                .HasColumnType("VARCHAR(255)");

            modelBuilder.Entity<User>()
                .Property(u => u.Password)
                .HasColumnType("VARCHAR(255)");

            modelBuilder.Entity<User>()
                .Property(u => u.Role)
                .HasColumnType("VARCHAR(255)");

            modelBuilder.Entity<User>()
                .Property(u => u.Token)
                .HasColumnType("TEXT");

            modelBuilder.Entity<User>()
                .Property(u => u.RefreshToken)
                .HasColumnType("TEXT");

            modelBuilder.Entity<User>()
                .Property(u => u.RefreshTokenExpiryTime)
                .HasColumnType("DATETIME");

            modelBuilder.Entity<User>()
                .Property(u => u.Id)
                .ValueGeneratedOnAdd();

            //Blog Table Mapping

            modelBuilder.Entity<Blog>()
                .Property(b => b.Heading)
                .HasColumnType("TEXT");

            modelBuilder.Entity<Blog>()
                .Property(b => b.Tagline)
                .HasColumnType("TEXT");

            modelBuilder.Entity<Blog>()
                .Property(b => b.Content)
                .HasColumnType("TEXT");

            modelBuilder.Entity<Blog>()
                .Property(b => b.Location)
                .HasColumnType("VARCHAR(255)");

            modelBuilder.Entity<Blog>()
                .Property(b => b.CreatedAt)
                .HasColumnType("DATETIME");

            modelBuilder.Entity<Blog>()
                .HasOne(b => b.User)
                .WithMany(u => u.Blogs)
                .HasForeignKey(b => b.UserEmail)
                .HasPrincipalKey(u => u.Email);

            //BlogLike Table Mapping

            modelBuilder.Entity<BlogLikes>(entity =>
            {
                entity.ToTable("BlogLike");

                entity.Property(bl => bl.UserEmail)
                    .HasColumnType("VARCHAR(255)")
                    .IsRequired();

                entity.Property(bl => bl.CreatedAt)
                    .HasColumnType("DATETIME");

                entity.HasOne(bl => bl.Blog)
                    .WithMany(b => b.Likes)
                    .HasForeignKey(bl => bl.BlogId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(bl => new { bl.BlogId, bl.UserEmail })
                    .IsUnique();
            });

            //ImageMetaData Table Mapping
            modelBuilder.Entity<ImageMetadata>(entity =>
            {
                entity.ToTable("ImageMetadata");

                entity.Property(im => im.Url)
                    .IsRequired()
                    .HasColumnType("TEXT");

                entity.Property(im => im.Width)
                    .IsRequired();

                entity.Property(im => im.Height)
                    .IsRequired();

                // Setup the relationship with Blog.
                entity.HasOne(im => im.Blog)
                    .WithMany(b => b.ImagesMetadata)
                    .HasForeignKey(im => im.BlogId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // BlogComments Table Mapping
            modelBuilder.Entity<BlogComments>(entity =>
            {
                entity.ToTable("BlogComments");

                entity.Property(bc => bc.Content)
                    .IsRequired()
                    .HasColumnType("TEXT");

                entity.Property(bc => bc.Author)
                    .IsRequired()
                    .HasColumnType("VARCHAR(255)");

                entity.Property(bc => bc.CreatedAt)
                    .HasColumnType("DATETIME");

                entity.HasOne(bc => bc.Blog)
                    .WithMany(b => b.Comments)
                    .HasForeignKey(bc => bc.BlogId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
