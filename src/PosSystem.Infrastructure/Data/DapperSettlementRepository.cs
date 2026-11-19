using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PosSystem.Core.Interfaces;
using PosSystem.Core.Models;

namespace PosSystem.Infrastructure.Data;

/// <summary>
/// Dapper + MS SQL Server 기반 일일 매출/정산 집계 리포지토리입니다.
/// 주문 저장/수정은 EF Core를 사용하고, 다건의 매출 데이터를 SUM/COUNT로 빠르게 집계해야 하는 정산 조회에는
/// 가볍고 SQL 제어가 직접적인 Dapper를 병행하여 사용합니다.
/// </summary>
public class DapperSettlementRepository : ISettlementRepository
{
    private readonly string _connectionString;
    private readonly IDbContextFactory<PosDbContext> _contextFactory;

    public DapperSettlementRepository(string connectionString, IDbContextFactory<PosDbContext> contextFactory)
    {
        _connectionString = connectionString;
        _contextFactory = contextFactory;
    }

    /// <summary>
    /// 특정 영업일(businessDate)의 정상/취소 건수, 순매출, 부가세, 결제수단별 합계를 SQL로 집계합니다.
    /// </summary>
    public async Task<SettlementSummary> GetDailySummaryAsync(DateTime businessDate, CancellationToken ct = default)
    {
        var start = businessDate.Date;
        var end = start.AddDays(1);

        // SQL Injection을 방지하기 위해 문자열 결합 대신 @파라미터 바인딩을 사용합니다.
        const string orderSql = """
            SELECT
                COUNT(CASE WHEN Status = @CompletedStatus THEN 1 END) AS TotalOrders,
                COUNT(CASE WHEN Status = @CancelledStatus THEN 1 END) AS CancelledOrders,
                ISNULL(SUM(CASE WHEN Status = @CompletedStatus THEN TotalAmount + DiscountAmount ELSE 0 END), 0) AS GrossSales,
                ISNULL(SUM(CASE WHEN Status = @CompletedStatus THEN DiscountAmount ELSE 0 END), 0) AS DiscountTotal,
                ISNULL(SUM(CASE WHEN Status = @CompletedStatus THEN TotalAmount ELSE 0 END), 0) AS NetSales,
                ISNULL(SUM(CASE WHEN Status = @CompletedStatus THEN VatAmount ELSE 0 END), 0) AS VatTotal
            FROM Orders
            WHERE CreatedAt >= @StartDate AND CreatedAt < @EndDate;
            """;

        const string paymentSql = """
            SELECT
                ISNULL(SUM(CASE WHEN p.Method = @CardMethod THEN p.Amount ELSE 0 END), 0) AS CardSales,
                ISNULL(SUM(CASE WHEN p.Method = @EasyPayMethod THEN p.Amount ELSE 0 END), 0) AS EasyPaySales,
                ISNULL(SUM(CASE WHEN p.Method = @CashMethod THEN p.Amount ELSE 0 END), 0) AS CashSales,
                ISNULL(SUM(CASE WHEN p.Method = @CashReceiptMethod THEN p.Amount ELSE 0 END), 0) AS CashReceiptSales
            FROM PaymentTransactions p
            INNER JOIN Orders o ON o.Id = p.OrderId
            WHERE p.TransactedAt >= @StartDate AND p.TransactedAt < @EndDate
              AND o.Status = @CompletedStatus;
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var orderRow = await conn.QuerySingleAsync<OrderAggRow>(new CommandDefinition(
            orderSql,
            new
            {
                StartDate = start,
                EndDate = end,
                CompletedStatus = (int)OrderStatus.Completed,
                CancelledStatus = (int)OrderStatus.Cancelled
            },
            cancellationToken: ct));

        var paymentRow = await conn.QuerySingleAsync<PaymentAggRow>(new CommandDefinition(
            paymentSql,
            new
            {
                StartDate = start,
                EndDate = end,
                CompletedStatus = (int)OrderStatus.Completed,
                CardMethod = (int)PaymentMethod.Card,
                EasyPayMethod = (int)PaymentMethod.EasyPay,
                CashMethod = (int)PaymentMethod.Cash,
                CashReceiptMethod = (int)PaymentMethod.CashReceipt
            },
            cancellationToken: ct));

        return new SettlementSummary
        {
            BusinessDate = start,
            TotalOrders = orderRow.TotalOrders,
            CancelledOrders = orderRow.CancelledOrders,
            GrossSales = orderRow.GrossSales,
            DiscountTotal = orderRow.DiscountTotal,
            NetSales = orderRow.NetSales,
            VatTotal = orderRow.VatTotal,
            CardSales = paymentRow.CardSales,
            EasyPaySales = paymentRow.EasyPaySales,
            CashSales = paymentRow.CashSales,
            CashReceiptSales = paymentRow.CashReceiptSales
        };
    }

    public async Task SaveSettlementAsync(SettlementRecord record, CancellationToken ct = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(ct);
        db.SettlementRecords.Add(record);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<SettlementRecord>> GetSettlementHistoryAsync(int count = 30, CancellationToken ct = default)
    {
        const string sql = """
            SELECT TOP (@Count)
                Id, BusinessDate, StoreCode, PosNo, TotalOrders, NetSales,
                CardSales, EasyPaySales, CashSales, ExpectedDrawerCash,
                ActualDrawerCash, ClosedBy, ClosedAt
            FROM SettlementRecords
            ORDER BY ClosedAt DESC;
            """;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<SettlementRecord>(new CommandDefinition(sql, new { Count = count }, cancellationToken: ct));
        return rows.ToList();
    }

    private sealed class OrderAggRow
    {
        public int TotalOrders { get; set; }
        public int CancelledOrders { get; set; }
        public decimal GrossSales { get; set; }
        public decimal DiscountTotal { get; set; }
        public decimal NetSales { get; set; }
        public decimal VatTotal { get; set; }
    }

    private sealed class PaymentAggRow
    {
        public decimal CardSales { get; set; }
        public decimal EasyPaySales { get; set; }
        public decimal CashSales { get; set; }
        public decimal CashReceiptSales { get; set; }
    }
}
