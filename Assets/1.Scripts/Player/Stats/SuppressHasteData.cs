using UnityEngine;

/// <summary>
/// 제압 가속 버프 수치(할 일 91, PLAN-91 §3.6) — xlsx <c>SuppressHaste</c> 시트.
/// 에셋 1개 = <c>Assets/Resources/Player/SuppressHasteData.asset</c>(<see cref="SuppressHaste"/> 가 Resources 로 읽는다 — 보스·로비 훈련 등 어느 쪽이 불러도 같은 값).
/// </summary>
[CreateAssetMenu(fileName = "SuppressHasteData", menuName = "BeaverLobby/Player/Suppress Haste Data", order = 10)]
[DataTableSheet("SuppressHaste")]
public sealed class SuppressHasteData : ScriptableObject
{
    public const string ResourcesPath = "Player/SuppressHasteData";

    [Header("배율")]
    [Tooltip("이동속도 최종 배율(2 = ×2). % 단계 뒤에 곱한다(player-stats.md §2).")]
    [SerializeField, Min(0.1f)] private float moveSpeedMultiplier = 2f;
    [Tooltip("공격속도 최종 배율(2 = 평타·스킬 애니 ×2). 고정 타이밍(대시·거너 충전·채널링 틱)은 제외.")]
    [SerializeField, Min(0.1f)] private float attackSpeedMultiplier = 2f;
    [Tooltip("쿨타임이 줄어드는 속도 배율(2 = 두 배 빨리 참). 스킬·궁극기·대시 충전·패시브 내부 쿨.")]
    [SerializeField, Min(0.1f)] private float cooldownRateMultiplier = 2f;

    [Header("지속")]
    [Tooltip("0 = 이번 제압 길이를 따른다(breakDuration 또는 북진 압박 보상 길이). 0 보다 크면 이 초로 고정.")]
    [SerializeField, Min(0f)] private float durationOverride;

    [Header("화면 연출(비네팅)")]
    [Tooltip("버프가 걸릴 때 화면 가장자리 비네팅이 나타나는 시간(초).")]
    [SerializeField, Min(0f)] private float vignetteFadeIn = 0.2f;
    [Tooltip("버프가 끝날 때 비네팅이 사라지는 시간(초).")]
    [SerializeField, Min(0f)] private float vignetteFadeOut = 0.3f;

    public float VignetteFadeIn => vignetteFadeIn;
    public float VignetteFadeOut => vignetteFadeOut;

    public SuppressHasteValues Values =>
        new SuppressHasteValues(moveSpeedMultiplier, attackSpeedMultiplier, cooldownRateMultiplier, durationOverride);
}
