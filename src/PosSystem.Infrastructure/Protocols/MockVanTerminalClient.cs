using PosSystem.Core.Interfaces;
using PosSystem.Core.Models;

namespace PosSystem.Infrastructure.Protocols;

/// <summary>
/// VAN/PG 카드단말기 시리얼/TCP 연동 클라이언트 (시뮬레이터 포함).
/// 99,999원 결제 시 의도적으로 승인 거절(E001)을 반환하여 장애/예외 처리 흐름을 테스트할 수 있습니다.
/// </summary>
public class MockVanTerminalClient : IPaymentGateway
{
    private readonly IVanProtocolCodec _codec;
    private readonly IPosRepository _repository;

    // UI(주변기기/VAN 전문 모니터 탭)에서 실시간 송수신 전문을 구독할 수 있도록 이벤트를 제공합니다.
    public event EventHandler<VanResponse>? PacketTransacted;

    public MockVanTerminalClient(IVanProtocolCodec codec, IPosRepository repository)
    {
        _codec = codec;
        _repository = repository;
    }

    public async Task<VanResponse> ProcessPaymentAsync(VanRequest request, CancellationToken ct = default)
    {
        // 1. 요청 객체를 VAN 바이너리 패킷으로 인코딩
        byte[] reqPacket = _codec.EncodeRequest(request);
        string reqHex = VanPacketCodec.ToHexString(reqPacket);

        // 2. 실제 단말기 통신 지연 시뮬레이션
        await Task.Delay(250, ct);

        // 3. 테스트용 거절 조건(99,999원) 확인 후 응답 패킷 생성 및 디코딩
        bool simulateFailure = request.Amount == 99999m;
        byte[] respPacket = _codec.EncodeMockResponse(request, simulateFailure);
        VanResponse response = _codec.DecodeResponse(respPacket, reqHex);

        // 4. 현장 장애 분석을 위해 전문 송수신 원문(Hex)을 DB 진단 로그에 기록
        await _repository.AddDiagnosticLogAsync(new DiagnosticLog
        {
            Category = "VAN",
            Level = DiagnosticLevel.Protocol,
            Message = $"VAN 전문 송수신 ({request.TransactionType}, {request.Amount:N0}원) -> {response.ResponseCode}",
            Detail = $"[REQ] {response.RawRequestHex}\n[RES] {response.RawResponseHex}"
        }, ct);

        // 5. 모니터 UI에 실시간 알림 전파
        PacketTransacted?.Invoke(this, response);
        return response;
    }
}
