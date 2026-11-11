using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 보스 처치 연출 캐릭터별 프로필(할 일 267 — A안: 캐릭터별 값을 에셋 통째로). 타임라인 전체(카메라 컷·처치자 평타·마커·끝 자르기·느려짐·보스 노드·화면 효과 세기 클립) +
/// 백대쉬 + 대상별 시간 배율 + 처치 연출 스킬 칸. 화면 효과 모양(글리치 색·노이즈·찢김·CRT·셰이더·벽 숨김 기준)은 공용 <see cref="BossVictoryGlitchSettings"/>.
/// 고르는 곳 = 공용 설정의 '캐릭터 → 프로필' 표(<see cref="BossVictoryGlitchSettings.characterProfiles"/>), 없거나 비면 기본 프로필(<see cref="BossVictoryGlitchSettings.defaultProfile"/>).
/// 정본 에셋 = <c>Resources/BossVictory/Profiles/</c>(저작 메뉴 <c>Tools/Boss Victory/캐릭터별 프로필 만들기 (267)</c>).
/// </summary>
[CreateAssetMenu(menuName = "MainProject/Boss Victory Profile", fileName = "BossVictoryProfile")]
public sealed class BossVictoryProfile : ScriptableObject, ISerializationCallbackReceiver
{
    [Header("대상별 시간 배율(전역 timeScale 아님 — 각 피어 로컬 애니·이펙트)")]
    [Tooltip("처치자 — 처치 순간 하던 동작(평타·스킬 등 루프 아닌 클립)을 이 배율로(타임라인 처치자 평타 클립 끝 또는 그 클립 끝까지). 그 뒤 백대쉬·처치 연출 스킬은 1.")]
    [Range(0.05f, 1f)] public float killerCurrentActionScale = 0.5f;
    [Tooltip("처치자 — 느려짐 시작 마커(꺼짐이면 처치 연출 스킬 End) 뒤 배율(그 전엔 1).")]
    [Range(0f, 1f)] public float killerAfterFinisherScale = 0.1f;
    [Tooltip("처치자 외 플레이어 — 처치 판정 즉시 배율.")]
    [Range(0f, 1f)] public float otherPlayersScale = 0.05f;
    [Tooltip("보스 — 보스 슬로우 마커(꺼짐이면 느려짐 시작 → 처치 연출 스킬 End) 뒤 배율. 그 전 = 피격 애니 ×1 / 보스 정지 0 / 처치 순간 0(마지막 피격 자세).")]
    [Range(0f, 1f)] public float bossAfterFinisherScale = 0.1f;

    [Header("처치 연출 스킬(Q9)")]
    [Tooltip("이 프로필 캐릭터의 처치 연출 스킬 칸. 그 스킬의 애니·이펙트를 표시로만 재생하고 클립 End 이벤트(또는 '느려짐 시작' 마커)에서 처치자·보스가 느려진다.")]
    [FormerlySerializedAs("defaultFinisherSlot")] public PlayerSkillSlot finisherSlot = PlayerSkillSlot.Ultimate;

    [Header("백대쉬(처치 연출 전용 — 기본값 = 보스 등장 밀림 BossEncounterDirector impactSkid 값 복사, 255 후속 3)")]
    [Tooltip("백대쉬 거리(m). 보스 등장 밀림과 따로 — 등장 연출은 영향 없음.")]
    [Min(0f)] public float backDashDistance = 1.5f;
    [Tooltip("백대쉬 시간(초).")]
    [Min(0.05f)] public float backDashDuration = 0.35f;
    [Tooltip("백대쉬 이동 곡선(기본 ease out = 보스 등장 밀림 SkidEase 와 같은 식).")]
    public BossVictoryCameraCuts.Ease backDashEase = BossVictoryCameraCuts.Ease.EaseOut;
    [Tooltip("방향 보정(도) — 0 = 보스 반대쪽, + = 위에서 봐 시계 방향으로 돌림.")]
    [Range(-180f, 180f)] public float backDashAngle;
    [Tooltip("백대쉬 후 대기(초) — 백대쉬 끝 ~ Idle 복귀. 기본 0.35 = 보스 등장 밀림 ReactionIdleDelay(등장 연출은 상수 그대로).")]
    [Min(0f)] public float backDashIdleDelay = 0.35f;

    [Header("타임라인(트랙별 클립·마커 — 창 Tools/Boss Victory/처치 연출 타임라인 에서 캐릭터를 골라 편집)")]
    [Tooltip("t = 처치 판정 순간(복제 처치 시각) 뒤 초. 화면 효과 세기·HUD 숨김·TV 꺼짐·카메라 컷 = 클립, 카메라 처치자 포커스·처치자 외 숨김·로비 전환 = 마커. 처치 연출 스킬은 이 속에서 재생.")]
    public BossVictoryTimeline timeline = BossVictoryTimeline.CreateDefault();

    void ISerializationCallbackReceiver.OnBeforeSerialize() { }

    // 옛 형식 타임라인(255 후속 4·6 전)을 읽으면 새 형식으로 옮긴다 — 설정 SO 에 있던 이관을 프로필로(267).
    void ISerializationCallbackReceiver.OnAfterDeserialize()
    {
        if (timeline == null) return;
        timeline.MigrateLegacyKillerActionEnd(backDashDuration, backDashIdleDelay);
        timeline.MigrateKeys();
    }

    /// <summary>다른 프로필 값을 통째로 덮어쓴다(키 목록·컷 값까지 새 목록 — 원본과 공유하지 않음).</summary>
    public void CopyFrom(BossVictoryProfile source)
    {
        if (source == null || source == this) return;
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(source), this);
    }

    /// <summary>코드 기본값 인스턴스(에셋 없음 — 저장 안 함).</summary>
    public static BossVictoryProfile CreateDefault()
    {
        var p = CreateInstance<BossVictoryProfile>();
        p.hideFlags = HideFlags.DontSave;
        p.name = "BossVictoryProfile(코드 기본값)";
        return p;
    }
}
