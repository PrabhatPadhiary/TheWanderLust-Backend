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
            // ------------------- Users Table -------------------
            modelBuilder.Entity<User>(entity =>
            {
                entity.ToTable("users"); // lowercase, plural, safe

                entity.HasKey(u => u.Id);

                entity.Property(u => u.Id)
                    .ValueGeneratedOnAdd();

                entity.Property(u => u.FirstName).HasColumnType("VARCHAR(255)");
                entity.Property(u => u.LastName).HasColumnType("VARCHAR(255)");
                entity.Property(u => u.Email).HasColumnType("VARCHAR(255)").IsRequired();
                entity.HasIndex(u => u.Email).IsUnique();

                entity.Property(u => u.Username).HasColumnType("VARCHAR(255)");
                entity.Property(u => u.Password).HasColumnType("VARCHAR(255)");
                entity.Property(u => u.Role).HasColumnType("VARCHAR(255)");
                entity.Property(u => u.Token).HasColumnType("TEXT");
                entity.Property(u => u.ProfilePictureUrl).HasColumnType("TEXT");
                entity.Property(u => u.RefreshToken).HasColumnType("TEXT");
                entity.Property(u => u.RefreshTokenExpiryTime)
                      .HasColumnType("timestamp with time zone");
            });

            // ------------------- Blogs Table -------------------
            modelBuilder.Entity<Blog>(entity =>
            {
                entity.ToTable("blogs");

                entity.Property(b => b.Heading).HasColumnType("TEXT");
                entity.Property(b => b.Tagline).HasColumnType("TEXT");
                entity.Property(b => b.Content).HasColumnType("TEXT");
                entity.Property(b => b.Location).HasColumnType("VARCHAR(255)");
                entity.Property(b => b.CreatedAt).HasColumnType("timestamp with time zone");

                entity.HasOne(b => b.User)
                      .WithMany(u => u.Blogs)
                      .HasForeignKey(b => b.UserEmail)
                      .HasPrincipalKey(u => u.Email);
            });

            // ------------------- BlogLikes Table -------------------
            modelBuilder.Entity<BlogLikes>(entity =>
            {
                entity.ToTable("blog_likes");

                entity.Property(bl => bl.UserEmail).HasColumnType("VARCHAR(255)").IsRequired();
                entity.Property(bl => bl.CreatedAt).HasColumnType("timestamp with time zone");

                entity.HasOne(bl => bl.Blog)
                      .WithMany(b => b.Likes)
                      .HasForeignKey(bl => bl.BlogId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(bl => new { bl.BlogId, bl.UserEmail }).IsUnique();
            });

            // ------------------- ImageMetadata Table -------------------
            modelBuilder.Entity<ImageMetadata>(entity =>
            {
                entity.ToTable("image_metadata");

                entity.Property(im => im.Url).IsRequired().HasColumnType("TEXT");
                entity.Property(im => im.Width).IsRequired();
                entity.Property(im => im.Height).IsRequired();

                entity.HasOne(im => im.Blog)
                      .WithMany(b => b.ImagesMetadata)
                      .HasForeignKey(im => im.BlogId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // ------------------- BlogComments Table -------------------
            modelBuilder.Entity<BlogComments>(entity =>
            {
                entity.ToTable("blog_comments");

                entity.Property(bc => bc.Content).IsRequired().HasColumnType("TEXT");
                entity.Property(bc => bc.Author).IsRequired().HasColumnType("VARCHAR(255)");
                entity.Property(bc => bc.CreatedAt).HasColumnType("timestamp with time zone");

                entity.HasOne(bc => bc.Blog)
                      .WithMany(b => b.Comments)
                      .HasForeignKey(bc => bc.BlogId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
