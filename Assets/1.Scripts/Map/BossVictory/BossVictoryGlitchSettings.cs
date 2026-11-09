using UnityEngine;

/// <summary>
/// 보스 처치 연출 — ZZZ 글리치(PLAN-boss-victory-glitch) 수치. 정본 에셋: <c>Resources/BossVictory/BossVictoryGlitchSettings.asset</c>
/// (없으면 코드 기본값). 단계가 늘 때마다 칸을 더한다 — 2단계 = 대상별 시간 배율·처치 연출 스킬.
/// </summary>
[CreateAssetMenu(menuName = "MainProject/Boss Victory Glitch Settings", fileName = "BossVictoryGlitchSettings")]
public sealed class BossVictoryGlitchSettings : ScriptableObject
{
    public const string ResourcePath = "BossVictory/BossVictoryGlitchSettings";

    [Header("대상별 시간 배율(전역 timeScale 아님 — 각 피어 로컬 애니·이펙트)")]
    [Tooltip("처치자 — 처치 연출 스킬이 End 에 닿은 뒤 배율(그 전엔 1).")]
    [Range(0f, 1f)] public float killerAfterFinisherScale = 0.1f;
    [Tooltip("처치자 외 플레이어 — 처치 판정 즉시 배율.")]
    [Range(0f, 1f)] public float otherPlayersScale = 0.05f;
    [Tooltip("보스 — 처치 연출 스킬이 End 에 닿은 뒤 배율(그 전엔 0 = 마지막 피격 자세로 정지).")]
    [Range(0f, 1f)] public float bossAfterFinisherScale = 0.1f;

    [Header("처치 연출 스킬(Q9)")]
    [Tooltip("캐릭터 Variant 에 PlayerVictoryFinisher 가 없을 때 쓰는 스킬 칸. 그 스킬의 애니·이펙트를 표시로만 재생하고 클립 End 이벤트에서 처치자·보스가 느려진다.")]
    public PlayerSkillSlot defaultFinisherSlot = PlayerSkillSlot.Ultimate;

    static BossVictoryGlitchSettings _cached;

    /// <summary>Resources 에셋, 없으면 코드 기본값 인스턴스.</summary>
    public static BossVictoryGlitchSettings Load()
    {
        if (_cached != null) return _cached;
        _cached = Resources.Load<BossVictoryGlitchSettings>(ResourcePath);
        if (_cached == null)
        {
            Debug.LogWarning($"[BossVictory] Resources/{ResourcePath} 가 없어 코드 기본값으로 돈다.");
            _cached = CreateInstance<BossVictoryGlitchSettings>();
        }
        return _cached;
    }
}
