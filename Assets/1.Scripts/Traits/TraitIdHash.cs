using System.Collections.Generic;
using System.Text;

/// <summary>
/// 특성 영구 ID(명세 문자열 ID, 예: <c>PAL_1_Q_DAMAGE</c>)의 고정 해시 — 네트워크 복제·재합류 보관은 이 int 로 한다(할 일 125).
/// FNV-1a 32비트를 UTF-8 바이트에 돌린다 — 빌드·플랫폼·실행마다 같다(<c>string.GetHashCode</c> 는 실행마다 달라 쓰면 안 된다).
/// 0 은 "없음"으로 비워 둔다 — 해시가 0 인 ID 는 카탈로그 검증 오류다(<see cref="TraitCatalogIndex"/>).
/// </summary>
public static class TraitIdHash
{
    public const int None = 0;

    private const uint OffsetBasis = 2166136261u;
    private const uint Prime = 16777619u;

    /// <summary>ID 의 고정 해시. null·빈 문자열은 <see cref="None"/>.</summary>
    public static int Of(string id)
    {
        if (string.IsNullOrEmpty(id))
            return None;

        uint hash = OffsetBasis;
        byte[] bytes = Encoding.UTF8.GetBytes(id);
        for (int i = 0; i < bytes.Length; i++)
            hash = (hash ^ bytes[i]) * Prime;

        return unchecked((int)hash);
    }

    /// <summary>
    /// 특성 목록 버전 — 해시 목록을 순서대로 FNV-1a 로 접는다(리틀 엔디언 4바이트씩). 순서·개수·값 중 하나라도 다르면 다른 값.
    /// 서버·클라가 같은 풀(같은 정의·같은 순서)을 들고 있는지 접속 때 비교한다(<see cref="TraitDirector"/>).
    /// </summary>
    public static int Version(IReadOnlyList<int> hashes)
    {
        uint hash = OffsetBasis;
        int count = hashes != null ? hashes.Count : 0;
        for (int i = 0; i < count; i++)
        {
            uint value = unchecked((uint)hashes[i]);
            for (int b = 0; b < 4; b++)
            {
                hash = (hash ^ (value & 0xFFu)) * Prime;
                value >>= 8;
            }
        }

        // 개수도 섞는다 — 빈 목록과 다른 목록이 우연히 같은 값이 되지 않게.
        hash = (hash ^ unchecked((uint)count)) * Prime;
        return unchecked((int)hash);
    }
}
