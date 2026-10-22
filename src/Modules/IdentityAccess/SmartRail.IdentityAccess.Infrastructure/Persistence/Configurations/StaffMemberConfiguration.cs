using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartRail.IdentityAccess.Domain.Entities;

namespace SmartRail.IdentityAccess.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="StaffMember"/> to <c>identity.staff_members</c>.</summary>
internal sealed class StaffMemberConfiguration : IEntityTypeConfiguration<StaffMember>
{
    public void Configure(EntityTypeBuilder<StaffMember> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("staff_members");
        builder.HasKey(staffMember => staffMember.Id);

        builder.Property(staffMember => staffMember.Id).HasColumnName("id").ValueGeneratedNever();
        builder
            .Property(staffMember => staffMember.AuthentikSubject)
            .HasColumnName("authentik_subject")
            .HasMaxLength(150)
            .IsRequired();
        builder.Property(staffMember => staffMember.Username).HasColumnName("username").HasMaxLength(100).IsRequired();
        builder
            .Property(staffMember => staffMember.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(200)
            .IsRequired();
        builder.Property(staffMember => staffMember.CreatedAt).HasColumnName("created_at");

        // A subject and a username each identify one account, which is what makes seeding by
        // username and resolving a token by subject unambiguous.
        builder.HasIndex(staffMember => staffMember.AuthentikSubject).IsUnique();
        builder.HasIndex(staffMember => staffMember.Username).IsUnique();
    }
}
