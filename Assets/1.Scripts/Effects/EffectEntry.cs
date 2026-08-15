using UnityEngine;

/// <summary>
/// 이펙트 하나의 데이터. 개별 SO 에셋으로 만든다(라이브러리 단일 파일 아님 — 머지 충돌 회피).
///
/// ⚠️ 이 시스템의 중심축: <b>수명은 데이터가 진실이고, 매니저의 타이머로 회수한다.</b>
/// 런타임은 프리팹을 들여다보지 않는다 — 신뢰할 수 있는 종료 감지가 있는 기술은 8종 중
/// 1종(Shuriken 원샷)뿐이라, 런타임이 프리팹에 의존하면 기술마다 회수 경로가 갈린다.
///
/// 대신 <b>저작 시점</b>에는 프리팹에서 자동으로 계산한다 (<see cref="EffectLifetime"/>):
/// <code>
/// duration == 0  →  프리팹에서 계산한 값을 쓴다 (프리팹을 튜닝하면 따라온다)
/// duration  &gt; 0  →  적어 넣은 값을 쓴다 (의도적 오버라이드)
/// </code>
/// 계산값을 <c>duration</c>에 직접 쓰지 않는 이유: 그러면 0이 아니게 되어 "자동"과 "손으로 적음"을
/// 구분할 수 없고, 나중에 프리팹을 고쳐도 값이 굳은 채 남아 조용히 잘린다.
/// </summary>
[CreateAssetMenu(fileName = "FX_", menuName = "Effects/Effect Entry")]
public class EffectEntry : ScriptableObject
{
    [Header("수명 — 0이면 프리팹에서 자동 계산")]
    [Tooltip("원샷: 재생 시작 → 풀 반납까지의 시간(초).\n0으로 두면 파티클 설정에서 계산한 값을 따라간다. " +
             "값을 적으면 그 값이 우선한다 (꼬리를 일찍 자르거나, 사운드에 맞춰 늘릴 때)")]
    [Min(0f)] public float duration;

    [Tooltip("루프: Release() 호출 → 풀 반납까지의 시간(초). outroParts가 다 죽는 시간.\n" +
             "0으로 두면 outroParts에서 계산한 값을 따라간다")]
    [Min(0f)] public float outroDuration;

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

    // 프리팹에서 계산한 수명. OnValidate가 갱신하고 에셋에 저장된다 —
    // OnValidate는 빌드에서 돌지 않으므로 런타임이 쓸 값은 직렬화돼 있어야 한다.
    [SerializeField, HideInInspector] private float computedDuration;
    [SerializeField, HideInInspector] private float computedOutroDuration;

    /// <summary>런타임이 실제로 쓰는 원샷 수명. 이것 하나만 보면 된다.</summary>
    public float ResolvedDuration => duration > 0f ? duration : computedDuration;

    /// <summary>런타임이 실제로 쓰는 outro 수명.</summary>
    public float ResolvedOutroDuration => outroDuration > 0f ? outroDuration : computedOutroDuration;

    /// <summary>자동 계산된 값(참고용). 인스펙터 표시와 불일치 경고에 쓴다.</summary>
    public float ComputedDuration => computedDuration;

    /// <summary>자동 계산된 outro 값(참고용).</summary>
    public float ComputedOutroDuration => computedOutroDuration;

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

#if UNITY_EDITOR
    private void OnValidate() => RecomputeLifetimes();

    /// <summary>
    /// 프리팹에서 수명을 다시 계산해 저장한다. 값이 바뀌었으면 true.
    /// 인스펙터 수정(OnValidate)과 프리팹 재임포트(<c>EffectEntryPostprocessor</c>) 양쪽에서 불린다.
    /// </summary>
    public bool RecomputeLifetimes()
    {
        bool changed = false;
        changed |= Apply(EffectLifetime.Estimate(parts), ref computedDuration);
        changed |= Apply(EffectLifetime.Estimate(outroParts), ref computedOutroDuration);
        return changed;
    }

    private static bool Apply(float estimate, ref float stored)
    {
        // 루프가 섞였거나(Unknown) 아직 프리팹이 로드되지 않아 0이 나온 경우에는 덮어쓰지 않는다.
        // 임포트 도중의 "잠깐 비어 있는 상태"를 저장해버리면 그게 굳어 조용히 틀린다.
        if (estimate <= 0f) return false;
        if (Mathf.Approximately(stored, estimate)) return false;

        stored = estimate;
        return true;
    }
#endif
}
