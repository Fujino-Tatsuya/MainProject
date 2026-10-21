using System;
using System.Text;

/// <summary>
/// NetworkConfig.ConnectionData 에 담는 playerId — PLAN-48 Q4. 형식은 길이 제한을 둔 UTF8 문자열 하나뿐이다.
/// 클라(와 호스트 자신)가 StartClient/StartHost 전에 <see cref="Encode"/> 로 넣고, 서버 승인 콜백이 <see cref="TryDecode"/> 로 읽는다.
/// 순수 함수다(EditMode 테스트 대상).
/// </summary>
public static class PlayerIdConnectionData
{
    /// <summary>UTF8 바이트 상한. 한글 21자 남짓.</summary>
    public const int MaxBytes = 64;

    // 보낼 땐 짝 없는 서로게이트를 U+FFFD 로 바꿔 던지지 않고, 받을 땐 깨진 바이트를 거절한다.
    static readonly UTF8Encoding WriteUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
    static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// 앞뒤 공백을 지우고, 상한을 넘으면 글자 단위로 잘라 담는다(자른 값도 매번 같으므로 재접속 키로 쓸 수 있다).
    /// 비었으면 빈 배열 — 서버는 InvalidPlayerId 로 본다.
    /// </summary>
    public static byte[] Encode(string playerId)
    {
        var normalized = Normalize(playerId);
        return normalized.Length == 0 ? Array.Empty<byte>() : WriteUtf8.GetBytes(normalized);
    }

    /// <summary><see cref="Encode"/> 가 실제로 담을 문자열(공백 제거·상한 자르기).</summary>
    public static string Normalize(string playerId)
    {
        if (string.IsNullOrWhiteSpace(playerId))
            return string.Empty;

        var text = playerId.Trim();
        while (text.Length > 0 && WriteUtf8.GetByteCount(text) > MaxBytes)
        {
            // 서로게이트 쌍을 반으로 가르지 않는다.
            var cut = text.Length - 1;
            if (cut > 0 && char.IsLowSurrogate(text[cut]) && char.IsHighSurrogate(text[cut - 1]))
                cut--;
            text = text.Substring(0, cut).TrimEnd();
        }

        return text;
    }

    /// <summary>비었거나 상한을 넘거나 UTF8 이 깨졌거나 제어 문자가 섞였으면 false.</summary>
    public static bool TryDecode(byte[] payload, out string playerId)
    {
        playerId = string.Empty;
        if (payload == null || payload.Length == 0 || payload.Length > MaxBytes)
            return false;

        string text;
        try
        {
            text = StrictUtf8.GetString(payload);
        }
        catch (ArgumentException)
        {
            return false;
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsControl(text[i]))
                return false;
        }

        text = text.Trim();
        if (text.Length == 0)
            return false;

        playerId = text;
        return true;
    }
}
