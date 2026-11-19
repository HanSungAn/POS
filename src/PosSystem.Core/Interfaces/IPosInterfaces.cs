using PosSystem.Core.Models;

namespace PosSystem.Core.Interfaces;

/// <summary>
/// VAN 결제 전문(바이너리 패킷)을 만들고 해석하는 규격 인터페이스입니다.
/// </summary>
public interface IVanProtocolCodec
{
    /// <summary>결제 요청 객체를 VAN 전송용 바이트 배열(STX~LRC)로 인코딩합니다.</summary>
    byte[] EncodeRequest(VanRequest request);

    /// <summary>테스트/시뮬레이션용 응답 패킷을 생성합니다.</summary>
    byte[] EncodeMockResponse(VanRequest request, bool simulateFailure = false);

    /// <summary>수신한 바이트 배열을 검증 및 파싱하여 응답 객체로 변환합니다.</summary>
    VanResponse DecodeResponse(byte[] packet, string rawRequestHex);

    /// <summary>패킷 끝의 LRC 체크섬이 올바른지 검증합니다.</summary>
    bool VerifyLrc(byte[] packet);

    /// <summary>데이터 구간의 XOR 체크섬(LRC)을 계산합니다.</summary>
    byte ComputeLrc(ReadOnlySpan<byte> data);
}

/// <summary>
/// 카드단말기·간편결제·현금영수증 등 외부 결제 게이트웨이 추상화 인터페이스입니다.
/// </summary>
public interface IPaymentGateway
{
    Task<VanResponse> ProcessPaymentAsync(VanRequest request, CancellationToken ct = default);
}

/// <summary>
/// 영수증 프린터(ESC/POS 프로토콜) 제어 인터페이스입니다.
/// </summary>
public interface IReceiptPrinter
{
    PeripheralStatus Status { get; }
    string PortName { get; }
    byte[] BuildEscPosBytes(Order order, PaymentTransaction? payment);
    Task<(string PreviewText, string EscPosHex)> PrintReceiptAsync(Order order, PaymentTransaction? payment, CancellationToken ct = default);
    Task<bool> CheckHealthAsync(CancellationToken ct = default);
}

/// <summary>
/// 돈통(캐시드로어) 개방 제어 인터페이스입니다.
/// </summary>
public interface ICashDrawer
{
    PeripheralStatus Status { get; }
    Task<bool> OpenDrawerAsync(CancellationToken ct = default);
}

/// <summary>
/// 바코드 스캐너(시리얼 COM 포트 및 시뮬레이션) 인터페이스입니다.
/// </summary>
public interface IBarcodeScanner
{
    PeripheralStatus Status { get; }
    string PortName { get; }
    event EventHandler<string>? BarcodeScanned;
    void SimulateScan(string barcode);
    void StartListening(string portName);
    void StopListening();
}

/// <summary>
/// 고객용 듀얼/VFD 디스플레이(2줄×20자) 제어 인터페이스입니다.
/// </summary>
public interface ICustomerDisplay
{
    PeripheralStatus Status { get; }
    string CurrentLine1 { get; }
    string CurrentLine2 { get; }
    Task DisplayAsync(string line1, string line2, CancellationToken ct = default);
}

/// <summary>
/// POS 로컬 데이터베이스(MSSQL) CRUD 및 Outbox/로그 저장소 인터페이스입니다.
/// </summary>
public interface IPosRepository
{
    Task EnsureDatabaseInitializedAsync(CancellationToken ct = default);
    Task<List<Product>> GetProductsAsync(bool activeOnly = true, CancellationToken ct = default);
    Task<Product?> GetProductByBarcodeAsync(string barcode, CancellationToken ct = default);
    Task UpdateProductStockAsync(int productId, int deltaQuantity, CancellationToken ct = default);
    Task UpsertProductsAsync(IEnumerable<Product> products, CancellationToken ct = default);
    Task<Order> SaveOrderAsync(Order order, CancellationToken ct = default);
    Task<List<Order>> GetOrdersAsync(DateTime? fromDate = null, DateTime? toDate = null, CancellationToken ct = default);
    Task<Order?> GetOrderByIdAsync(int orderId, CancellationToken ct = default);
    Task CancelOrderAsync(int orderId, PaymentTransaction cancelPayment, CancellationToken ct = default);
    Task<List<SyncOutboxItem>> GetPendingOutboxAsync(CancellationToken ct = default);
    Task MarkOutboxSyncedAsync(int outboxId, CancellationToken ct = default);
    Task MarkOutboxFailedAsync(int outboxId, string error, CancellationToken ct = default);
    Task AddDiagnosticLogAsync(DiagnosticLog log, CancellationToken ct = default);
    Task<List<DiagnosticLog>> GetRecentLogsAsync(int count = 100, CancellationToken ct = default);
}

/// <summary>
/// 일일 매출 집계 및 시재 마감 이력을 다루는 정산 저장소 인터페이스입니다.
/// </summary>
public interface ISettlementRepository
{
    Task<SettlementSummary> GetDailySummaryAsync(DateTime businessDate, CancellationToken ct = default);
    Task SaveSettlementAsync(SettlementRecord record, CancellationToken ct = default);
    Task<List<SettlementRecord>> GetSettlementHistoryAsync(int count = 30, CancellationToken ct = default);
}

/// <summary>
/// 본사 서버(HqApi)와의 데이터 송수신(동기화) 서비스 인터페이스입니다.
/// </summary>
public interface ISyncService
{
    Task<(int SyncedCount, int FailedCount, string Message)> SyncPendingOutboxAsync(CancellationToken ct = default);
    Task<int> PullProductsFromHqAsync(CancellationToken ct = default);
    Task<bool> CheckHqHealthAsync(CancellationToken ct = default);
}
