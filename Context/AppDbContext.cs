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
        public DbSet<Favourite> Favourites { get; set; }
        public DbSet<Trip> Trips { get; set; }
        public DbSet<TripDestination> TripDestinations { get; set; }
        public DbSet<TripPlace> TripPlaces { get; set; }
        public DbSet<TripMember> TripMembers { get; set; }
        public DbSet<TripExpense> TripExpenses { get; set; }
        public DbSet<Invitation> Invitations { get; set; }
        public DbSet<TripChecklistItem> TripChecklistItems { get; set; }
        public DbSet<Journal> Journals { get; set; }
        public DbSet<JournalPlace> JournalPlaces { get; set; }
        public DbSet<JournalPhoto> JournalPhotos { get; set; }
        public DbSet<JournalLike> JournalLikes { get; set; }
        public DbSet<JournalComment> JournalComments { get; set; }
        public DbSet<ItineraryItem> ItineraryItems { get; set; }
        public DbSet<JournalReport> JournalReports { get; set; }
        public DbSet<UserBlock> UserBlocks { get; set; }
        public DbSet<HiddenJournal> HiddenJournals { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.FirebaseId).IsUnique();
                entity.HasIndex(e => e.Email).IsUnique();
            });

            modelBuilder.Entity<Favourite>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.UserId, e.PlaceId }).IsUnique();
                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Trip>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.TotalBudget).HasColumnType("decimal(18,2)");
                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TripDestination>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasOne(e => e.Trip)
                    .WithMany(t => t.Destinations)
                    .HasForeignKey(e => e.TripId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TripPlace>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasOne(e => e.TripDestination)
                    .WithMany(d => d.Places)
                    .HasForeignKey(e => e.TripDestinationId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TripMember>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.TripId, e.UserId }).IsUnique();
                entity.HasOne(e => e.Trip)
                    .WithMany(t => t.Members)
                    .HasForeignKey(e => e.TripId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TripExpense>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Amount).HasColumnType("decimal(18,2)");
                entity.Property(e => e.Category).HasConversion<string>();
                entity.HasOne(e => e.Trip)
                    .WithMany()
                    .HasForeignKey(e => e.TripId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Invitation>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasOne(e => e.Trip)
                    .WithMany()
                    .HasForeignKey(e => e.TripId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.UsedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.UsedBy)
                    .OnDelete(DeleteBehavior.SetNull)
                    .IsRequired(false);
            });

            modelBuilder.Entity<TripChecklistItem>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasOne(e => e.Trip)
                    .WithMany()
                    .HasForeignKey(e => e.TripId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.AssignedToUser)
                    .WithMany()
                    .HasForeignKey(e => e.AssignedToUserId)
                    .OnDelete(DeleteBehavior.SetNull)
                    .IsRequired(false);
                entity.HasOne(e => e.CompletedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.CompletedByUserId)
                    .OnDelete(DeleteBehavior.SetNull)
                    .IsRequired(false);
                entity.HasOne(e => e.CreatedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.CreatedByUserId)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<Journal>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Budget).HasColumnType("decimal(18,2)");
                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Trip)
                    .WithMany()
                    .HasForeignKey(e => e.TripId)
                    .OnDelete(DeleteBehavior.SetNull)
                    .IsRequired(false);
            });

            modelBuilder.Entity<JournalPlace>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasOne(e => e.Journal)
                    .WithMany(j => j.Places)
                    .HasForeignKey(e => e.JournalId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<JournalPhoto>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasOne(e => e.Journal)
                    .WithMany(j => j.Photos)
                    .HasForeignKey(e => e.JournalId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Community safety: reports, blocks and "Not interested". All
            // cascade, so deleting a user or a story removes their rows too.
            modelBuilder.Entity<JournalReport>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.JournalId, e.ReporterId }).IsUnique();
                entity.HasIndex(e => e.Status);
                entity.Property(e => e.Reason).HasMaxLength(30);
                entity.Property(e => e.Details).HasMaxLength(500);
                entity.Property(e => e.Status).HasMaxLength(20);
                entity.HasOne(e => e.Journal).WithMany().HasForeignKey(e => e.JournalId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Reporter).WithMany().HasForeignKey(e => e.ReporterId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<UserBlock>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.BlockerId, e.BlockedId }).IsUnique();
                entity.HasOne(e => e.Blocker).WithMany().HasForeignKey(e => e.BlockerId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Blocked).WithMany().HasForeignKey(e => e.BlockedId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<HiddenJournal>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.UserId, e.JournalId }).IsUnique();
                entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Journal).WithMany().HasForeignKey(e => e.JournalId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<JournalLike>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.JournalId, e.UserId }).IsUnique();
                entity.HasOne(e => e.Journal)
                    .WithMany()
                    .HasForeignKey(e => e.JournalId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<JournalComment>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasOne(e => e.Journal)
                    .WithMany()
                    .HasForeignKey(e => e.JournalId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            modelBuilder.Entity<ItineraryItem>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasOne(e => e.Trip)
                    .WithMany()
                    .HasForeignKey(e => e.TripId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasOne(e => e.Destination)
                    .WithMany()
                    .HasForeignKey(e => e.DestinationId)
                    .OnDelete(DeleteBehavior.NoAction);
                entity.HasOne(e => e.TripPlace)
                    .WithMany()
                    .HasForeignKey(e => e.TripPlaceId)
                    .OnDelete(DeleteBehavior.SetNull)
                    .IsRequired(false);
            });
        }
    }
}
