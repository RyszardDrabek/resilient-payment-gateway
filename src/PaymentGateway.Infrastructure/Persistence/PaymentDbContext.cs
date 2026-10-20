using Microsoft.EntityFrameworkCore;

namespace PaymentGateway.Infrastructure.Persistence;

/// <summary>PAY/outbox DbContext. Tables arrive with features; scaffold keeps an empty model + migration.</summary>
public sealed class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<PaymentGateway.Domain.Entities.Payment> Payments => Set<PaymentGateway.Domain.Entities.Payment>();
    public DbSet<PaymentGateway.Domain.Entities.IdempotencyRecord> IdempotencyRecords => Set<PaymentGateway.Domain.Entities.IdempotencyRecord>();
    public DbSet<PaymentGateway.Domain.Entities.AdyenNotification> AdyenNotifications => Set<PaymentGateway.Domain.Entities.AdyenNotification>();
    public DbSet<PaymentGateway.Domain.Entities.MerchantWebhookSubscription> MerchantWebhookSubscriptions => Set<PaymentGateway.Domain.Entities.MerchantWebhookSubscription>();
    public DbSet<PaymentGateway.Domain.Entities.MerchantApiKey> MerchantApiKeys => Set<PaymentGateway.Domain.Entities.MerchantApiKey>();
    public DbSet<PaymentGateway.Domain.Entities.MerchantWebhookDeadLetter> MerchantWebhookDeadLetters => Set<PaymentGateway.Domain.Entities.MerchantWebhookDeadLetter>();
    public DbSet<PaymentGateway.Domain.Entities.PaymentMethodToken> PaymentMethods => Set<PaymentGateway.Domain.Entities.PaymentMethodToken>();
    public DbSet<PaymentGateway.Domain.Entities.Subscription> Subscriptions => Set<PaymentGateway.Domain.Entities.Subscription>();
    public DbSet<PaymentGateway.Domain.Entities.ReconciliationRecord> ReconciliationRecords => Set<PaymentGateway.Domain.Entities.ReconciliationRecord>();
    public DbSet<PaymentGateway.Domain.Entities.FeeSchedule> FeeSchedules => Set<PaymentGateway.Domain.Entities.FeeSchedule>();
    public DbSet<PaymentGateway.Domain.Entities.MerchantPayoutBatch> PayoutBatches => Set<PaymentGateway.Domain.Entities.MerchantPayoutBatch>();
    public DbSet<PaymentGateway.Domain.Entities.PayoutItem> PayoutItems => Set<PaymentGateway.Domain.Entities.PayoutItem>();
    public DbSet<PaymentGateway.Domain.Entities.CurrencyQuote> CurrencyQuotes => Set<PaymentGateway.Domain.Entities.CurrencyQuote>();
    public DbSet<OutboxMessageRecord> OutboxMessages => Set<OutboxMessageRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("pay");

        modelBuilder.Entity<PaymentGateway.Domain.Entities.Payment>(b =>
        {
            b.ToTable("Payments");
            b.HasKey(p => p.Id);
            b.Property(p => p.PartyId).IsRequired().HasMaxLength(128);
            b.Property(p => p.Currency).IsRequired().HasMaxLength(12);
            b.Property(p => p.SettlementChannel).IsRequired().HasMaxLength(64);
            b.Property(p => p.ChannelReference).HasMaxLength(128);
            b.Property(p => p.DeclineReason).HasMaxLength(256);
            b.Property(p => p.State).HasConversion<string>();
            b.Property(p => p.TransactionOrigin).HasConversion<string>().HasMaxLength(16).HasDefaultValue(PaymentGateway.Domain.Entities.TransactionOrigin.Cit);
            b.Property(p => p.PaymentMethodId).HasMaxLength(64);
            b.Property(p => p.SubscriptionId).HasMaxLength(64);
            b.Property(p => p.Version).IsRequired().HasDefaultValue(1).IsConcurrencyToken();
            b.Property(p => p.CapturedAmount).IsRequired().HasDefaultValue(0L);
            b.Property(p => p.RefundedAmount).IsRequired().HasDefaultValue(0L);
            b.Property(p => p.QuoteId).HasMaxLength(64);
            b.Property(p => p.OriginalCurrency).HasMaxLength(12);
            b.Property(p => p.ExchangeRate).HasPrecision(18, 6);
            b.OwnsOne(p => p.ThreeDsAction, a =>
            {
                a.Property(x => x.ActionType).HasMaxLength(64);
                a.Property(x => x.TargetUrl).HasMaxLength(1024);
                a.Property(x => x.Token).HasMaxLength(4096);
                a.HasIndex(x => x.ExpiresAt)
                    .HasDatabaseName("IX_Payments_RequiresAction_ExpiresAt")
                    .HasFilter("\"State\" = 'RequiresAction' AND \"ThreeDsAction_ExpiresAt\" IS NOT NULL");
            });
            b.Ignore(p => p.RemainingAuthorizedAmount);
            b.Ignore(p => p.RemainingRefundableAmount);
            b.Ignore(p => p.DomainEvents);

            b.HasIndex(p => new { p.CreatedAt, p.SettlementChannel })
                .HasDatabaseName("IX_Payments_Unresolved")
                .HasFilter("\"State\" IN ('Pending', 'Unknown')");
        });

        modelBuilder.Entity<OutboxMessageRecord>(b =>
        {
            b.ToTable("OutboxMessages", "pay");
            b.HasKey(o => o.Id);
            b.Property(o => o.EventType).IsRequired().HasMaxLength(256);
            b.Property(o => o.Payload).IsRequired();
            b.Property(o => o.CreatedAt).IsRequired();
            b.Property(o => o.ProcessedAt);
            b.Property(o => o.DeliveryAttempts).IsRequired().HasDefaultValue(0);
            b.Property(o => o.Error).HasMaxLength(1024);
            b.HasIndex(o => o.ProcessedAt);
        });

        modelBuilder.Entity<PaymentGateway.Domain.Entities.IdempotencyRecord>(b =>
        {
            b.ToTable("IdempotencyRecords");
            b.HasKey(r => r.Id);
            b.Property(r => r.Key).IsRequired().HasMaxLength(128);
            b.Property(r => r.CommandType).IsRequired().HasMaxLength(64);
            b.Property(r => r.PaymentId).HasMaxLength(128);
            b.Property(r => r.PayloadHash).IsRequired().HasMaxLength(128);
            b.Property(r => r.Status).HasConversion<string>();
            b.Property(r => r.ResponseStatusCode);
            b.Property(r => r.ResponsePayload);
            b.Property(r => r.CreatedAt).IsRequired();
            b.Property(r => r.ExpiresAt).IsRequired();
            b.Property(r => r.LockedUntil);

            b.HasIndex(r => new { r.CommandType, r.Key })
                .HasFilter("\"CommandType\" = 'Authorize'")
                .IsUnique();

            b.HasIndex(r => new { r.PaymentId, r.CommandType, r.Key })
                .HasFilter("\"CommandType\" != 'Authorize'")
                .IsUnique();
        });

        modelBuilder.Entity<PaymentGateway.Domain.Entities.AdyenNotification>(b =>
        {
            b.ToTable("AdyenNotifications");
            b.HasKey(n => n.Id);
            b.Property(n => n.PspReference).IsRequired().HasMaxLength(128);
            b.Property(n => n.OriginalReference).HasMaxLength(128);
            b.Property(n => n.MerchantAccountCode).IsRequired().HasMaxLength(128);
            b.Property(n => n.MerchantReference).IsRequired().HasMaxLength(128);
            b.Property(n => n.EventCode).IsRequired().HasMaxLength(64);
            b.Property(n => n.AmountCurrency).IsRequired().HasMaxLength(3);
            b.Property(n => n.Reason).HasMaxLength(512);
            b.Property(n => n.CorrelatedPaymentId).HasMaxLength(128);
            b.Property(n => n.Status).IsRequired().HasMaxLength(64);
            b.Property(n => n.CreatedAt).IsRequired();

            b.HasIndex(n => new { n.PspReference, n.EventCode }).IsUnique();
            b.HasIndex(n => n.MerchantReference);
            b.HasIndex(n => n.CorrelatedPaymentId);
        });

        modelBuilder.Entity<PaymentGateway.Domain.Entities.MerchantWebhookSubscription>(b =>
        {
            b.ToTable("MerchantWebhookSubscriptions");
            b.HasKey(s => s.Id);
            b.Property(s => s.PartyId).IsRequired().HasMaxLength(128);
            b.Property(s => s.EndpointUrl).IsRequired().HasMaxLength(512);
            b.Property(s => s.SecretKey).IsRequired().HasMaxLength(256);
            b.Property(s => s.IsActive).IsRequired().HasDefaultValue(true);

            b.HasIndex(s => s.PartyId)
                .IsUnique()
                .HasFilter("\"IsActive\" = true");
        });

        modelBuilder.Entity<PaymentGateway.Domain.Entities.MerchantApiKey>(b =>
        {
            b.ToTable("MerchantApiKeys");
            b.HasKey(k => k.Id);
            b.Property(k => k.MerchantId).IsRequired().HasMaxLength(128);
            b.Property(k => k.Name).IsRequired().HasMaxLength(128);
            b.Property(k => k.Prefix).IsRequired().HasMaxLength(64);
            b.Property(k => k.KeyHash).IsRequired().HasMaxLength(256);
            b.Property(k => k.Salt).IsRequired().HasMaxLength(128);
            b.Property(k => k.CreatedAt).IsRequired();
            b.Property(k => k.IsRevoked).IsRequired().HasDefaultValue(false);
            b.Property(k => k.RevokedAt);
            b.Property(k => k.RevokedBy).HasMaxLength(128);

            b.Property(k => k.Scopes)
                .HasConversion(
                    v => string.Join(',', v),
                    v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList())
                .HasMaxLength(256);

            b.HasIndex(k => k.Prefix);
            b.HasIndex(k => new { k.MerchantId, k.IsRevoked });
        });

        modelBuilder.Entity<PaymentGateway.Domain.Entities.MerchantWebhookDeadLetter>(b =>
        {
            b.ToTable("MerchantWebhookDeadLetters");
            b.HasKey(d => d.Id);
            b.Property(d => d.EventId).IsRequired().HasMaxLength(128);
            b.Property(d => d.PaymentId).IsRequired().HasMaxLength(128);
            b.Property(d => d.MerchantId).IsRequired().HasMaxLength(128);
            b.Property(d => d.DestinationUrl).IsRequired().HasMaxLength(512);
            b.Property(d => d.PayloadJson).IsRequired();
            b.Property(d => d.DeliveryAttempts).IsRequired();
            b.Property(d => d.LastErrorMessage).IsRequired().HasMaxLength(1024);
            b.Property(d => d.LastStatusCode);
            b.Property(d => d.Status).IsRequired().HasMaxLength(64);
            b.Property(d => d.FailedAt).IsRequired();
            b.Property(d => d.ReplayedAt);
            b.Property(d => d.ReplayAttemptCount).IsRequired().HasDefaultValue(0);
            b.Property(d => d.LastReplayStatusCode);

            b.HasIndex(d => new { d.MerchantId, d.Status });
            b.HasIndex(d => d.FailedAt);
        });

        modelBuilder.Entity<PaymentGateway.Domain.Entities.PaymentMethodToken>(b =>
        {
            b.ToTable("PaymentMethods");
            b.HasKey(m => m.Id);
            b.Property(m => m.Id).HasMaxLength(64);
            b.Property(m => m.PartyId).IsRequired().HasMaxLength(128);
            b.Property(m => m.SettlementChannel).IsRequired().HasMaxLength(64);
            b.Property(m => m.CardBrand).IsRequired().HasMaxLength(32);
            b.Property(m => m.MaskedPan).IsRequired().HasMaxLength(32);
            b.Property(m => m.Token).IsRequired().HasMaxLength(256);
            b.Property(m => m.Status).HasConversion<string>().HasMaxLength(32);
            b.Property(m => m.CreatedAt).IsRequired();

            b.HasIndex(m => new { m.PartyId, m.Status });
        });

        modelBuilder.Entity<PaymentGateway.Domain.Entities.Subscription>(b =>
        {
            b.ToTable("Subscriptions");
            b.HasKey(s => s.Id);
            b.Property(s => s.Id).HasMaxLength(64);
            b.Property(s => s.PartyId).IsRequired().HasMaxLength(128);
            b.Property(s => s.PaymentMethodId).IsRequired().HasMaxLength(64);
            b.Property(s => s.Currency).IsRequired().HasMaxLength(12);
            b.Property(s => s.Interval).HasConversion<string>().HasMaxLength(32);
            b.Property(s => s.State).HasConversion<string>().HasMaxLength(32);
            b.Property(s => s.Version).IsRequired().HasDefaultValue(1).IsConcurrencyToken();

            b.HasIndex(s => new { s.State, s.NextBillingAt });
            b.HasIndex(s => s.PartyId);
        });

        modelBuilder.Entity<PaymentGateway.Domain.Entities.ReconciliationRecord>(b =>
        {
            b.ToTable("ReconciliationRecords");
            b.HasKey(r => r.Id);
            b.Property(r => r.Id).HasMaxLength(64);
            b.Property(r => r.PaymentId).HasMaxLength(64);
            b.Property(r => r.Channel).IsRequired().HasMaxLength(64);
            b.Property(r => r.ChannelReference).IsRequired().HasMaxLength(128);
            b.Property(r => r.DiscrepancyType).HasConversion<string>().HasMaxLength(32);
            b.Property(r => r.Status).HasConversion<string>().HasMaxLength(32);
            b.Property(r => r.Notes).HasMaxLength(1024);

            b.HasIndex(r => r.Status);
            b.HasIndex(r => r.PaymentId);
            b.HasIndex(r => r.ReconciledAt);
        });

        modelBuilder.Entity<PaymentGateway.Domain.Entities.FeeSchedule>(b =>
        {
            b.ToTable("FeeSchedules");
            b.HasKey(f => f.Id);
            b.Property(f => f.Id).HasMaxLength(64);
            b.Property(f => f.MerchantId).IsRequired().HasMaxLength(128);
            b.Property(f => f.Currency).IsRequired().HasMaxLength(12);

            b.HasIndex(f => new { f.MerchantId, f.Currency, f.EffectiveFrom });
        });

        modelBuilder.Entity<PaymentGateway.Domain.Entities.MerchantPayoutBatch>(b =>
        {
            b.ToTable("PayoutBatches");
            b.HasKey(p => p.Id);
            b.Property(p => p.Id).HasMaxLength(64);
            b.Property(p => p.MerchantId).IsRequired().HasMaxLength(128);
            b.Property(p => p.Currency).IsRequired().HasMaxLength(12);
            b.Property(p => p.Status).HasConversion<string>().HasMaxLength(32);
            b.Property(p => p.FailureReason).HasMaxLength(512);

            b.HasMany(p => p.Items)
                .WithOne()
                .HasForeignKey(i => i.PayoutBatchId)
                .OnDelete(DeleteBehavior.Cascade);

            b.Navigation(p => p.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

            b.HasIndex(p => new { p.MerchantId, p.PeriodEnd });
            b.HasIndex(p => p.Status);
        });

        modelBuilder.Entity<PaymentGateway.Domain.Entities.PayoutItem>(b =>
        {
            b.ToTable("PayoutItems");
            b.HasKey(i => i.Id);
            b.Property(i => i.Id).HasMaxLength(64);
            b.Property(i => i.PayoutBatchId).IsRequired().HasMaxLength(64);
            b.Property(i => i.PaymentId).IsRequired().HasMaxLength(64);
            b.Property(i => i.Currency).IsRequired().HasMaxLength(12);

            b.HasIndex(i => i.PayoutBatchId);
            b.HasIndex(i => i.PaymentId).IsUnique();
        });

        modelBuilder.Entity<PaymentGateway.Domain.Entities.CurrencyQuote>(b =>
        {
            b.ToTable("CurrencyQuotes");
            b.HasKey(q => q.Id);
            b.Property(q => q.Id).HasMaxLength(64);
            b.Property(q => q.PartyId).HasMaxLength(128);
            b.Property(q => q.BaseCurrency).IsRequired().HasMaxLength(12);
            b.Property(q => q.TargetCurrency).IsRequired().HasMaxLength(12);
            b.Property(q => q.SpotRate).HasPrecision(18, 6);
            b.Property(q => q.ExchangeRate).HasPrecision(18, 6);
            b.Property(q => q.Status).HasConversion<string>().HasMaxLength(16);
            b.Property(q => q.ConsumedByPaymentId).HasMaxLength(64);

            b.HasIndex(q => q.ExpiresAt);
            b.HasIndex(q => q.Status);
        });
    }

    /// <summary>
    /// Scans all tracked entities implementing <see cref="PaymentGateway.Domain.Events.IHasDomainEvents"/>,
    /// serializes domain events to transactional outbox records (<see cref="OutboxMessageRecord"/>), and clears
    /// domain events on the entities. Idempotent: safe to run multiple times per change-tracking cycle.
    /// </summary>
    public void ConvertDomainEventsToOutboxMessages()
    {
        var entries = ChangeTracker.Entries<PaymentGateway.Domain.Events.IHasDomainEvents>()
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Count > 0)
            .ToList();

        if (entries.Count == 0)
        {
            return;
        }

        foreach (var entity in entries)
        {
            foreach (var domainEvent in entity.DomainEvents)
            {
                if (domainEvent is PaymentGateway.Domain.Events.PaymentTransitionDomainEvent transition)
                {
                    var lifecycleEvent = new PaymentGateway.Application.Events.PaymentLifecycleEvent(
                        EventId: $"evt_{Guid.NewGuid():N}",
                        PaymentId: transition.PaymentId,
                        PartyId: transition.PartyId,
                        Outcome: transition.Outcome.ToString().ToLowerInvariant(),
                        Amount: transition.Amount,
                        Currency: transition.Currency,
                        Version: transition.Version,
                        OccurredAt: transition.OccurredAt,
                        TraceParent: System.Diagnostics.Activity.Current?.Id);

                    OutboxMessages.Add(new OutboxMessageRecord
                    {
                        Id = Guid.NewGuid(),
                        EventType = PaymentGateway.Application.Events.PaymentLifecycleEvent.EventType,
                        Payload = System.Text.Json.JsonSerializer.Serialize(lifecycleEvent),
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                }
            }

            entity.ClearDomainEvents();
        }
    }

    public override int SaveChanges()
    {
        ConvertDomainEventsToOutboxMessages();
        return base.SaveChanges();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ConvertDomainEventsToOutboxMessages();
        return await base.SaveChangesAsync(cancellationToken);
    }
}
