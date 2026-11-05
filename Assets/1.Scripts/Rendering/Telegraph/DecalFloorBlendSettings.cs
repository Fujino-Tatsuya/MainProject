using UnityEngine;

/// <summary>
/// 스킬 범위 데칼·몬스터 공격 예고 바닥 블렌딩(할 일 221) 튜닝값 — 한 에셋으로 스킬·예고를 각각 조절한다.
/// 에셋: <c>Assets/Resources/Rendering/DecalFloorBlendSettings.asset</c>(<see cref="DecalFloorBlendDriver"/> 가 Resources 로 읽는다 — 씬 배치 없음).
/// 코드 기본값 = 1·0·1 = 221 이전 화면. 렌더 룩 값이라 데이터 테이블 시트를 달지 않는다. 문서: Docs/tech/decal.md §221.
/// 🔴 예고는 회피 신호 — 채움을 너무 낮추지 말 것(외곽선·안쪽 선은 이 값과 무관하게 그대로 남는다).
/// </summary>
[CreateAssetMenu(fileName = "DecalFloorBlendSettings", menuName = "Rendering/Decal Floor Blend Settings")]
public sealed class DecalFloorBlendSettings : ScriptableObject
{
    public const string ResourcePath = "Rendering/DecalFloorBlendSettings";

    [Header("스킬 인디케이터 (플레이어)")]
    [Tooltip("안쪽 채움 알파 배율. 1 = 그대로, 낮출수록 바닥 무늬·명암이 비친다. 외곽선·안쪽 선은 그대로.")]
    [Range(0f, 1f)] public float skillFillAlphaScale = 1f;

    [Tooltip("가장자리 소프트 — 바깥 외곽선 폭 중 안·밖 가장자리에서 각각 이 비율만큼을 부드럽게 흐린다. 0 = 그대로(또렷), 0.5 = 외곽선이 가운데만 진한 그라데이션.")]
    [Range(0f, 1f)] public float skillEdgeSoftness;

    [Tooltip("외곽선(바깥 외곽선·안쪽 선) 알파 배율. 1 = 그대로.")]
    [Range(0f, 1f)] public float skillOutlineAlphaScale = 1f;

    [Tooltip("외곽선 물들임 — 0 = 지금(일반 섞기, 스티커처럼 덮음) ~ 1 = 완전 물들임(바닥 무늬·명암이 절반 남고 색만 외곽선 쪽으로).")]
    [Range(0f, 1f)] public float skillOutlineStain;

    [Tooltip("물들임 근사 방식. 밝기 보존 = 색조 유지·밝기를 바닥 쪽으로(흰 선도 읽힘) · 곱하기 = 바닥 × 색(흰 선은 거의 사라짐).")]
    public DecalFloorStainMode skillStainMode = DecalFloorStainMode.LuminanceKeep;

    [Tooltip("판(투명 쿼드 — 거너 Q 직선·대시 화살표·조준 표시) 전용 채움 추가 배율. 투영 데칼과 진하기를 맞출 때. 1 = 채움 배율만.")]
    [Range(0f, 1f)] public float skillPlateFillAlphaScale = 1f;

    [Header("공격 예고 (몬스터·보스)")]
    [Tooltip("안쪽 채움(채움·띠 그라데이션·쐐기) 알파 배율. 1 = 그대로. 외곽선·안쪽 선은 그대로라 범위 경계는 남는다.")]
    [Range(0f, 1f)] public float telegraphFillAlphaScale = 1f;

    [Tooltip("가장자리 소프트 — 바깥 외곽선 폭 중 안·밖 가장자리에서 각각 이 비율만큼을 부드럽게 흐린다. 0 = 그대로.")]
    [Range(0f, 1f)] public float telegraphEdgeSoftness;

    [Tooltip("외곽선(바깥 외곽선·안쪽 선) 알파 배율. 1 = 그대로. 🔴 회피 신호 — 너무 낮추지 말 것.")]
    [Range(0f, 1f)] public float telegraphOutlineAlphaScale = 1f;

    [Tooltip("외곽선 물들임 — 0 = 지금(일반 섞기) ~ 1 = 완전 물들임.")]
    [Range(0f, 1f)] public float telegraphOutlineStain;

    [Tooltip("물들임 근사 방식(스킬 칸과 같은 뜻).")]
    public DecalFloorStainMode telegraphStainMode = DecalFloorStainMode.LuminanceKeep;

    [Tooltip("판(C3 바닥 판 — 드론·박격포·전기 장판·점프 착지, TelegraphQuad) 전용 채움 추가 배율. 1 = 채움 배율만.")]
    [Range(0f, 1f)] public float telegraphPlateFillAlphaScale = 1f;

    public Vector4 SkillGlobal() => DecalFloorBlend.ToGlobal(skillFillAlphaScale, skillEdgeSoftness, skillPlateFillAlphaScale);
    public Vector4 TelegraphGlobal() => DecalFloorBlend.ToGlobal(telegraphFillAlphaScale, telegraphEdgeSoftness, telegraphPlateFillAlphaScale);
    public Vector4 SkillEdgeGlobal() => DecalFloorBlend.ToEdgeGlobal(skillOutlineAlphaScale, skillOutlineStain, skillStainMode);
    public Vector4 TelegraphEdgeGlobal() => DecalFloorBlend.ToEdgeGlobal(telegraphOutlineAlphaScale, telegraphOutlineStain, telegraphStainMode);
}
