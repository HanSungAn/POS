using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PosSystem.Core.Interfaces;
using PosSystem.Core.Models;

namespace PosSystem.Infrastructure.Data;

/// <summary>
/// Entity Framework Core를 사용하여 MS SQL Server에 POS 데이터를 저장/조회하는 리포지토리입니다.
/// WPF 같은 데스크톱 앱에서는 여러 비동기 작업이 동시에 DB에 접근할 수 있으므로,
/// DbContext를 직접 주입받지 않고 매 작업마다 새 DbContext를 생성하는 IDbContextFactory를 사용합니다.
/// </summary>
public class PosRepository : IPosRepository
{
    private readonly IDbContextFactory<PosDbContext> _contextFactory;

    public PosRepository(IDbContextFactory<PosDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <summary>
    /// 프로그램 최초 실행 시 DB와 테이블이 없으면 자동 생성하고, 기본 상품 12종을 시드(Seed)합니다.
    /// </summary>
    public async Task EnsureDatabaseInitializedAsync(CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        await db.Database.EnsureCreatedAsync(ct);

        // 상품 테이블이 비어 있을 때만 초기 데이터를 넣습니다.
        if (!await db.Products.AnyAsync(ct))
        {
            var seedProducts = new List<Product>
            {
                new() { Barcode = "880100000001", Name = "아메리카노 (ICE)", Category = "커피/음료", Price = 4500, StockQuantity = 120, SafetyStock = 20 },
                new() { Barcode = "880100000002", Name = "카페라떼 (HOT)", Category = "커피/음료", Price = 5000, StockQuantity = 85, SafetyStock = 15 },
                new() { Barcode = "880100000003", Name = "바닐라 라떼", Category = "커피/음료", Price = 5500, StockQuantity = 64, SafetyStock = 15 },
                new() { Barcode = "880100000004", Name = "자몽 에이드", Category = "커피/음료", Price = 5800, StockQuantity = 40, SafetyStock = 10 },
                new() { Barcode = "880100000005", Name = "플레인 크루아상", Category = "베이커리", Price = 3800, StockQuantity = 25, SafetyStock = 8 },
                new() { Barcode = "880100000006", Name = "소금빵", Category = "베이커리", Price = 3500, StockQuantity = 32, SafetyStock = 10 },
                new() { Barcode = "880100000007", Name = "바스크 치즈케이크", Category = "베이커리", Price = 6500, StockQuantity = 14, SafetyStock = 5 },
                new() { Barcode = "880100000008", Name = "생크림 스콘", Category = "베이커리", Price = 4200, StockQuantity = 18, SafetyStock = 5 },
                new() { Barcode = "880100000009", Name = "생수 500ml", Category = "편의/MD", Price = 1200, StockQuantity = 200, SafetyStock = 30 },
                new() { Barcode = "880100000010", Name = "프리미엄 텀블러", Category = "편의/MD", Price = 18000, StockQuantity = 12, SafetyStock = 3 },
                new() { Barcode = "880100000011", Name = "원두 200g (에티오피아)", Category = "편의/MD", Price = 14500, StockQuantity = 19, SafetyStock = 5 },
                new() { Barcode = "880100000012", Name = "친환경 쇼핑백", Category = "편의/MD", Price = 500, StockQuantity = 500, SafetyStock = 50 }
            };
            db.Products.AddRange(seedProducts);

            db.DiagnosticLogs.Add(new DiagnosticLog
            {
                Category = "DB",
                Level = DiagnosticLevel.Info,
                Message = "MSSQL POS 데이터베이스 초기화 및 기본 상품 12종 시드 완료",
                Timestamp = DateTime.Now
            });

            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<List<Product>> GetProductsAsync(bool activeOnly = true, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        // 단순 조회는 변경 추적이 필요 없으므로 AsNoTracking()으로 성능을 높입니다.
        var query = db.Products.AsNoTracking();
        if (activeOnly)
            query = query.Where(p => p.IsActive);

        return await query.OrderBy(p => p.Category).ThenBy(p => p.Name).ToListAsync(ct);
    }

    public async Task<Product?> GetProductByBarcodeAsync(string barcode, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Barcode == barcode && p.IsActive, ct);
    }

    public async Task UpdateProductStockAsync(int productId, int deltaQuantity, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var product = await db.Products.FindAsync(new object[] { productId }, ct);
        if (product is null) return;

        product.StockQuantity = Math.Max(0, product.StockQuantity + deltaQuantity);
        product.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// 본사 서버에서 내려받은 상품 마스터를 로컬 DB에 반영합니다. (있으면 수정, 없으면 신규 등록 = Upsert)
    /// </summary>
    public async Task UpsertProductsAsync(IEnumerable<Product> products, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        foreach (var incoming in products)
        {
            var existing = await db.Products.FirstOrDefaultAsync(p => p.Barcode == incoming.Barcode, ct);
            if (existing is null)
            {
                incoming.Id = 0; // 로컬 DB의 자동 증가(Identity) PK를 새로 발급받기 위해 0으로 초기화
                incoming.UpdatedAt = DateTime.Now;
                db.Products.Add(incoming);
            }
            else
            {
                existing.Name = incoming.Name;
                existing.Category = incoming.Category;
                existing.Price = incoming.Price;
                existing.IsActive = incoming.IsActive;
                existing.UpdatedAt = DateTime.Now;
            }
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// 주문을 저장하고, 재고를 차감하며, 본사 동기화 대기열(Outbox)에 이벤트를 적재합니다.
    /// 세 작업 중 하나라도 실패하면 모두 원상복구(Rollback)되도록 DB 트랜잭션으로 묶습니다.
    /// </summary>
    public async Task<Order> SaveOrderAsync(Order order, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        try
        {
            // 1. 주문 + 주문상세 + 결제내역 저장
            db.Orders.Add(order);

            // 2. 판매된 수량만큼 상품 재고 차감
            foreach (var item in order.Items)
            {
                var product = await db.Products.FindAsync(new object[] { item.ProductId }, ct);
                if (product is not null)
                {
                    product.StockQuantity = Math.Max(0, product.StockQuantity - item.Quantity);
                    product.UpdatedAt = DateTime.Now;
                }
            }

            await db.SaveChangesAsync(ct);

            // 3. Transactional Outbox 패턴:
            //    인터넷이 끊겨도 주문 데이터 유실 없이 나중에 본사 서버로 보낼 수 있도록 대기열 테이블에 저장합니다.
            var outbox = new SyncOutboxItem
            {
                EventType = "ORDER_COMPLETED",
                AggregateId = order.OrderNumber,
                PayloadJson = JsonSerializer.Serialize(order),
                Status = SyncStatus.Pending,
                CreatedAt = DateTime.Now
            };
            db.SyncOutboxItems.Add(outbox);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return order;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<List<Order>> GetOrdersAsync(DateTime? fromDate = null, DateTime? toDate = null, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var query = db.Orders
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .AsNoTracking();

        if (fromDate.HasValue)
            query = query.Where(o => o.CreatedAt >= fromDate.Value);
        if (toDate.HasValue)
            query = query.Where(o => o.CreatedAt <= toDate.Value);

        return await query.OrderByDescending(o => o.CreatedAt).Take(200).ToListAsync(ct);
    }

    public async Task<Order?> GetOrderByIdAsync(int orderId, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.Orders
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);
    }

    /// <summary>
    /// 주문 상태를 Cancelled로 바꾸고, 취소 결제 이력 추가 + 재고 복원 + Outbox 이벤트 적재를 하나의 트랜잭션으로 처리합니다.
    /// </summary>
    public async Task CancelOrderAsync(int orderId, PaymentTransaction cancelPayment, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        try
        {
            var order = await db.Orders
                .Include(o => o.Items)
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.Id == orderId, ct)
                ?? throw new InvalidOperationException($"주문을 찾을 수 없습니다. (ID: {orderId})");

            order.Status = OrderStatus.Cancelled;
            db.PaymentTransactions.Add(cancelPayment);

            // 취소된 수량만큼 재고를 다시 늘려줍니다.
            foreach (var item in order.Items)
            {
                var product = await db.Products.FindAsync(new object[] { item.ProductId }, ct);
                if (product is not null)
                {
                    product.StockQuantity += item.Quantity;
                    product.UpdatedAt = DateTime.Now;
                }
            }

            db.SyncOutboxItems.Add(new SyncOutboxItem
            {
                EventType = "ORDER_CANCELLED",
                AggregateId = order.OrderNumber,
                PayloadJson = JsonSerializer.Serialize(new { order.OrderNumber, CancelledAt = DateTime.Now, cancelPayment.ApprovalNumber }),
                Status = SyncStatus.Pending,
                CreatedAt = DateTime.Now
            });

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task<List<SyncOutboxItem>> GetPendingOutboxAsync(CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.SyncOutboxItems
            .AsNoTracking()
            .Where(x => x.Status != SyncStatus.Synced)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task MarkOutboxSyncedAsync(int outboxId, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var item = await db.SyncOutboxItems.FindAsync(new object[] { outboxId }, ct);
        if (item is null) return;

        item.Status = SyncStatus.Synced;
        item.SyncedAt = DateTime.Now;
        item.LastError = null;
        await db.SaveChangesAsync(ct);
    }

    public async Task MarkOutboxFailedAsync(int outboxId, string error, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        var item = await db.SyncOutboxItems.FindAsync(new object[] { outboxId }, ct);
        if (item is null) return;

        item.Status = SyncStatus.Failed;
        item.RetryCount += 1;
        item.LastError = error.Length > 500 ? error[..500] : error;
        await db.SaveChangesAsync(ct);
    }

    public async Task AddDiagnosticLogAsync(DiagnosticLog log, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        db.DiagnosticLogs.Add(log);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<DiagnosticLog>> GetRecentLogsAsync(int count = 100, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        return await db.DiagnosticLogs
            .AsNoTracking()
            .OrderByDescending(l => l.Timestamp)
            .Take(count)
            .ToListAsync(ct);
    }
}
