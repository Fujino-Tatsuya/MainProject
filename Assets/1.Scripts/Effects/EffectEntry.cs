using UnityEngine;

/// <summary>
/// 이펙트 하나의 데이터. 개별 SO 에셋으로 만든다(라이브러리 단일 파일 아님 — 머지 충돌 회피).
///
/// ⚠️ 이 시스템의 중심축: <b>수명은 데이터로 명시하고 매니저의 타이머로 회수한다.</b>
/// 프리팹에서 추론하지 않는다 — 신뢰할 수 있는 종료 감지가 있는 기술은 8종 중 1종(Shuriken 원샷)뿐이라
/// 진실의 원천을 데이터로 옮겨야 기술 8종을 하나의 회수 경로로 통합할 수 있다.
/// duration은 감으로 적지 말고 <see cref="EffectDurationProbe"/>로 실측해 넣는다.
/// </summary>
[CreateAssetMenu(fileName = "FX_", menuName = "Effects/Effect Entry")]
public class EffectEntry : ScriptableObject
{
    [Header("수명 — 프리팹에서 추론하지 않는다")]
    [Tooltip("원샷: 재생 시작 → 풀 반납까지의 시간(초). 실측값을 넣는다")]
    [Min(0f)] public float duration = 1f;

    [Tooltip("루프: Release() 호출 → 풀 반납까지의 시간(초). outroParts가 다 죽는 시간")]
    [Min(0f)] public float outroDuration = 1f;

    [Header("풀링")]
    [Tooltip("씬 로드 시 파트 프리팹마다 미리 만들어 둘 개수")]
    [Min(0)] public int prewarmCount;

    [Tooltip("이 엔트리의 동시 활성 개수가 이 값을 넘으면 경고한다. 재생은 계속된다 — 상한의 목적은 성능이 아니라 반납 누락(누수) 발견이다")]
    [Min(1)] public int maxActiveWarn = 32;

    [Header("컴포지트")]
    [Tooltip("재생 시작 시 발화하는 파트들. 각 파트의 delay로 3막을 만든다")]
    public EffectPart[] parts;

    [Tooltip("Release() 시 발화하는 파트들. 루프 이펙트의 3막 중 L_Outro")]
    public EffectPart[] outroParts;

    /// <summary>parts 중 가장 늦게 발화하는 파트의 delay. duration 타당성 검사에 쓴다.</summary>
    public float LongestPartDelay => LongestDelay(parts);

    /// <summary>outroParts 중 가장 늦게 발화하는 파트의 delay.</summary>
    public float LongestOutroDelay => LongestDelay(outroParts);

    private static float LongestDelay(EffectPart[] source)
    {
        float longest = 0f;
        if (source == null) return longest;

        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] != null && source[i].delay > longest)
                longest = source[i].delay;
        }
        return longest;
    }
}
