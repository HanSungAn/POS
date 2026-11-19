using System.Text;
using PosSystem.Core.Interfaces;
using PosSystem.Core.Models;

namespace PosSystem.Infrastructure.Protocols;

/// <summary>
/// VAN 단말기 통신용 바이너리/ASCII 전문 인코더·디코더입니다.
/// 프레임 구조: [STX(0x02)] + [LEN(4byte)] + [BODY (FS=0x1C 구분)] + [ETX(0x03)] + [LRC(1byte XOR)]
/// </summary>
public class VanPacketCodec : IVanProtocolCodec
{
    public const byte Stx = 0x02; // Start of Text: 전문 시작 제어 문자
    public const byte Etx = 0x03; // End of Text: 전문 종료 제어 문자
    public const byte Fs = 0x1C;  // File Separator: 본문 내 필드 구분자

    /// <summary>
    /// 결제 요청 정보를 VAN 단말기로 보낼 바이트 패킷으로 직렬화합니다.
    /// </summary>
    public byte[] EncodeRequest(VanRequest request)
    {
        // 거래 구분 코드 매핑 (실제 국내 VAN사 전문 체계 예시)
        string txCode = request.TransactionType switch
        {
            VanTransactionType.Approval => "D1",            // 신용승인 요청
            VanTransactionType.Cancellation => "D4",        // 신용취소 요청
            VanTransactionType.EasyPayApproval => "E1",     // 간편결제 요청
            VanTransactionType.CashReceiptApproval => "C1", // 현금영수증 발급 요청
            VanTransactionType.CashReceiptCancel => "C4",   // 현금영수증 취소 요청
            _ => "D1"
        };

        string extra = request.TransactionType switch
        {
            VanTransactionType.EasyPayApproval => request.PayProvider ?? "KAKAOPAY",
            VanTransactionType.CashReceiptApproval => request.CashReceiptIdentity ?? "01000001234",
            _ => string.Empty
        };

        // 각 필드를 FS(0x1C) 문자로 이어 붙여 본문 문자열을 구성합니다.
        string body = string.Join((char)Fs,
            txCode,
            request.PosNo,
            ((long)request.Amount).ToString(),
            ((long)request.TaxAmount).ToString(),
            request.InstallmentMonths.ToString("D2"),
            request.OriginalApprovalNumber ?? string.Empty,
            request.OriginalTransDate ?? string.Empty,
            extra);

        return FramePacket(body);
    }

    /// <summary>
    /// 실제 단말기가 없는 개발/테스트 환경에서 응답 패킷을 시뮬레이션 생성합니다.
    /// </summary>
    public byte[] EncodeMockResponse(VanRequest request, bool simulateFailure = false)
    {
        var now = DateTime.Now;
        string txCode = request.TransactionType switch
        {
            VanTransactionType.Approval => "D2",
            VanTransactionType.Cancellation => "D5",
            VanTransactionType.EasyPayApproval => "E2",
            VanTransactionType.CashReceiptApproval => "C2",
            VanTransactionType.CashReceiptCancel => "C5",
            _ => "D2"
        };

        if (simulateFailure)
        {
            string failBody = string.Join((char)Fs,
                txCode,
                "E001",
                string.Empty,
                "5409-****-****-1234",
                "신한카드",
                "KFTC-VAN",
                now.ToString("yyyyMMddHHmmss"),
                "한도초과 또는 단말기 거절(테스트)");
            return FramePacket(failBody);
        }

        string approvalNo = Random.Shared.Next(10000000, 99999999).ToString();
        (string maskedCard, string issuer) = request.TransactionType switch
        {
            VanTransactionType.EasyPayApproval => ("QR-TOKEN-****", request.PayProvider ?? "카카오페이"),
            VanTransactionType.CashReceiptApproval or VanTransactionType.CashReceiptCancel => (request.CashReceiptIdentity ?? "010-****-1234", "국세청 현금영수증"),
            _ => ("5409-26**-****-4821", "KB국민카드")
        };

        string msg = request.TransactionType is VanTransactionType.Cancellation or VanTransactionType.CashReceiptCancel
            ? "정상 취소 완료"
            : "정상 승인 완료";

        string okBody = string.Join((char)Fs,
            txCode,
            "0000",
            approvalNo,
            maskedCard,
            issuer,
            "KFTC-VAN",
            now.ToString("yyyyMMddHHmmss"),
            msg);

        return FramePacket(okBody);
    }

    /// <summary>
    /// 수신한 VAN 응답 패킷의 STX/LRC/길이를 검증한 뒤 VanResponse 객체로 파싱합니다.
    /// </summary>
    public VanResponse DecodeResponse(byte[] packet, string rawRequestHex)
    {
        if (packet.Length < 8 || packet[0] != Stx)
            throw new FormatException("유효하지 않은 VAN 전문 프레임입니다 (STX 누락).");

        // 통신 도중 노이즈로 데이터가 깨졌는지 LRC 체크섬으로 확인합니다.
        if (!VerifyLrc(packet))
            throw new FormatException("VAN 전문 LRC 체크섬 검증에 실패했습니다.");

        // 1~4번 바이트(4자리 ASCII 숫자)에서 본문 길이를 읽습니다.
        string lenStr = Encoding.ASCII.GetString(packet, 1, 4);
        if (!int.TryParse(lenStr, out int bodyLen) || packet.Length < 1 + 4 + bodyLen + 2)
            throw new FormatException("VAN 전문 길이 필드가 손상되었습니다.");

        // 본문 문자열을 추출해 FS(0x1C) 기준으로 필드를 분리합니다.
        string body = Encoding.UTF8.GetString(packet, 5, bodyLen);
        string[] fields = body.Split((char)Fs);

        string respCode = fields.ElementAtOrDefault(1) ?? "9999";
        string approvalNo = fields.ElementAtOrDefault(2) ?? string.Empty;
        string maskedCard = fields.ElementAtOrDefault(3) ?? string.Empty;
        string issuer = fields.ElementAtOrDefault(4) ?? string.Empty;
        string vanCode = fields.ElementAtOrDefault(5) ?? "KFTC-VAN";
        string dtStr = fields.ElementAtOrDefault(6) ?? string.Empty;
        string message = fields.ElementAtOrDefault(7) ?? string.Empty;

        DateTime transactedAt = DateTime.TryParseExact(dtStr, "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.None, out var parsed)
            ? parsed
            : DateTime.Now;

        string rawRespHex = ToHexString(packet);

        return new VanResponse
        {
            IsSuccess = respCode == "0000",
            ResponseCode = respCode,
            ResponseMessage = message,
            ApprovalNumber = approvalNo,
            CardMaskedNumber = maskedCard,
            CardIssuer = issuer,
            VanCode = vanCode,
            TransactedAt = transactedAt,
            RawRequestHex = rawRequestHex,
            RawResponseHex = rawRespHex,
            ParsedSummary = $"[{fields.ElementAtOrDefault(0)}] 응답코드={respCode}, 승인번호={approvalNo}, 발급사={issuer}, 메시지={message}"
        };
    }

    /// <summary>
    /// 패킷의 마지막 바이트(LRC)가 LEN~ETX 구간을 XOR 연산한 값과 일치하는지 확인합니다.
    /// </summary>
    public bool VerifyLrc(byte[] packet)
    {
        if (packet.Length < 4 || packet[0] != Stx)
            return false;

        int etxIndex = packet.Length - 2;
        if (packet[etxIndex] != Etx)
            return false;

        byte expected = ComputeLrc(packet.AsSpan(1, etxIndex));
        return packet[^1] == expected;
    }

    /// <summary>
    /// 모든 바이트를 순차적으로 XOR(^=)하여 1바이트 오류 검출 코드(LRC)를 계산합니다.
    /// </summary>
    public byte ComputeLrc(ReadOnlySpan<byte> data)
    {
        byte lrc = 0;
        foreach (byte b in data)
            lrc ^= b;
        return lrc;
    }

    /// <summary>로그 화면에서 읽기 쉽도록 바이트 배열을 "02 30 30 ..." 형태의 16진수 문자열로 변환합니다.</summary>
    public static string ToHexString(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", " ");

    private byte[] FramePacket(string body)
    {
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        byte[] lenBytes = Encoding.ASCII.GetBytes(bodyBytes.Length.ToString("D4"));

        // 전체 프레임 크기 = STX(1) + LEN(4) + BODY(n) + ETX(1) + LRC(1)
        byte[] packet = new byte[1 + 4 + bodyBytes.Length + 1 + 1];
        packet[0] = Stx;
        Buffer.BlockCopy(lenBytes, 0, packet, 1, 4);
        Buffer.BlockCopy(bodyBytes, 0, packet, 5, bodyBytes.Length);
        int etxIdx = 5 + bodyBytes.Length;
        packet[etxIdx] = Etx;
        packet[etxIdx + 1] = ComputeLrc(packet.AsSpan(1, etxIdx));
        return packet;
    }
}
