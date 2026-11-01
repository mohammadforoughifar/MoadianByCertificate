using Microsoft.EntityFrameworkCore;
namespace Inventory.Api.Data;
public static class DocEvolutionModel
{
    public static void Configure(ModelBuilder mb)
    {
        mb.Entity<DocTemporaryGrant>().ToTable("DocTemporaryGrants").HasIndex(x => new { x.DocumentId, x.UserId }).IsUnique();
        mb.Entity<DocTemporaryGrant>().HasIndex(x => new { x.UserId, x.ExpiresAtUtc });
        mb.Entity<DocRenewalPolicy>().ToTable("DocRenewalPolicies");
        mb.Entity<DocRenewalPolicy>().Property(x => x.DocumentId).ValueGeneratedNever();
        mb.Entity<DocIndexJob>().Property(x => x.AttachmentId).ValueGeneratedNever();
        mb.Entity<DocRenewalRun>().ToTable("DocRenewalRuns").HasIndex(x => new { x.DocumentId, x.ExpiryDate }).IsUnique();
        mb.Entity<DocIndexJob>().ToTable("DocIndexJobs").HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
    }
}
