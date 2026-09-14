using ForgeVault.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ForgeVault.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Email).IsRequired().HasMaxLength(320);
        builder.HasIndex(u => u.Email).IsUnique();

        builder.Property(u => u.PasswordHash).IsRequired().HasMaxLength(512);
        builder.Property(u => u.MfaEnabled).IsRequired().HasDefaultValue(false);
        builder.Property(u => u.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(u => u.MfaSecretAlgorithm).HasMaxLength(50);

        builder.Property(u => u.Username).HasMaxLength(100);
        // Filtered unique index: only constrains rows that actually have a Username set —
        // legacy/test-seeded users with a null Username never collide with each other or
        // with a real, deliberately-chosen one (application code in UserEndpoints still
        // checks uniqueness before ever writing a non-null value).
        builder.HasIndex(u => u.Username).IsUnique().HasFilter("username IS NOT NULL");
        builder.Property(u => u.FirstName).HasMaxLength(255);
        builder.Property(u => u.LastName).HasMaxLength(255);
        builder.Property(u => u.IsAdmin).IsRequired().HasDefaultValue(false);

        builder.Property(u => u.CreatedAt).IsRequired();
        builder.Property(u => u.UpdatedAt).IsRequired();

        builder.HasMany(u => u.RefreshTokens)
            .WithOne(t => t.User)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
