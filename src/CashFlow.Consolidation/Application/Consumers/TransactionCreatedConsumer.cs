using CashFlow.Consolidation.Infrastructure.Data;
using CashFlow.Shared.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Consolidation.Application.Consumers;

public class TransactionCreatedConsumer(
    ConsolidationDbContext db,
    ILogger<TransactionCreatedConsumer> logger) : IConsumer<TransactionCreatedEvent>
{
    public async Task Consume(ConsumeContext<TransactionCreatedEvent> context)
    {
        var @event = context.Message;
        var date = DateOnly.FromDateTime(@event.OccurredAt.ToUniversalTime());

        logger.LogInformation("Processing transaction {TransactionId} ({Type} {Amount}) for date {Date}",
            @event.TransactionId, @event.Type, @event.Amount, date);

        if (@event.Type is not ("credit" or "debit"))
        {
            logger.LogError(
                "Unknown transaction type '{Type}' for transaction {TransactionId}. Message will be dead-lettered.",
                @event.Type, @event.TransactionId);
            throw new ArgumentException($"Unknown transaction type: '{@event.Type}'.");
        }

        var consolidation = await db.DailyConsolidations
            .FirstOrDefaultAsync(d => d.Date == date, context.CancellationToken);

        if (consolidation is null)
        {
            consolidation = Domain.Entities.DailyConsolidation.Create(date);
            db.DailyConsolidations.Add(consolidation);
            logger.LogInformation("Created new consolidation record for {Date}", date);
        }

        if (@event.Type == "credit")
            consolidation.ApplyCredit(@event.Amount);
        else
            consolidation.ApplyDebit(@event.Amount);

        try
        {
            await db.SaveChangesAsync(context.CancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Conflito de concorrência (xmin) ou insert duplicado do mesmo dia: descarta o estado
            // rastreado e relança para o retry do MassTransit reprocessar com dados atualizados.
            logger.LogWarning(ex, "Concurrency conflict for {Date} on transaction {TransactionId}, will retry",
                date, @event.TransactionId);
            db.ChangeTracker.Clear();
            throw;
        }

        logger.LogInformation(
            "Consolidation for {Date} updated — Balance: {Balance}, Transactions: {Count}",
            date, consolidation.Balance, consolidation.TransactionCount);
    }
}
