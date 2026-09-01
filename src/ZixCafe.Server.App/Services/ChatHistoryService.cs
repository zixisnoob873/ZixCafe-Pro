using Microsoft.EntityFrameworkCore;
using ZixCafe.Domain.Entities;
using ZixCafe.Infrastructure;
using ZixCafe.Shared.Contracts;

namespace ZixCafe.Server.App.Services;

public class ChatHistoryService
{
    private readonly IDbContextFactory<ZixCafeDbContext> _dbFactory;

    public ChatHistoryService(IDbContextFactory<ZixCafeDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task SaveChatAsync(Guid terminalId, Guid? sessionId, string fromName, string message, bool isFromCustomer)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var entry = new ChatEntry
        {
            TerminalId = terminalId,
            SessionId = sessionId,
            FromName = fromName.Trim(),
            Message = message.Trim(),
            IsFromCustomer = isFromCustomer,
            SentAtUtc = DateTime.UtcNow
        };

        db.ChatEntries.Add(entry);
        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<ChatHistoryItemDto>> GetChatHistoryAsync(Guid terminalId, Guid? sessionId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var query = db.ChatEntries.Where(c => c.TerminalId == terminalId).AsQueryable();

        if (sessionId.HasValue && sessionId.Value != Guid.Empty)
        {
            query = query.Where(c => c.SessionId == sessionId.Value);
        }

        var list = await query
            .OrderByDescending(c => c.SentAtUtc)
            .Take(50)
            .ToListAsync();

        return list.OrderBy(c => c.SentAtUtc).Select(c => new ChatHistoryItemDto(
            c.Id,
            c.SessionId,
            c.TerminalId,
            c.FromName,
            c.Message,
            c.IsFromCustomer,
            c.SentAtUtc
        )).ToList();
    }
}
