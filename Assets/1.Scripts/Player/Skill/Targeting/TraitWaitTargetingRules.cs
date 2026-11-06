using System;

/// <summary>
/// 증강(특성)을 고른 뒤 파티원을 기다리는 동안 타겟팅 스킬 금지(할 일 236) — 순수 규칙.
/// 원본: Docs/tech/tutorial-traits.md §8.1 · Docs/tech/skill-reject-feedback.md §0.
///
/// · 기간 = 내가 그 복도 특성을 고른 순간(디렉터 개인 체크 TraitCorridorN 켜짐 — 수동·자동·0장 바로 체크 모두)부터
///   그 복도 출구 게이트(G2/G4/G7/G10)가 열릴 때까지. 맵씬 진행이 켜져 있고 끝나기(보스방 도착) 전만.
///   개인 뒤 배리어가 서 있는 구간이라 벽 너머로 타겟팅 스킬을 쓸 수 있던 문제를 막는다(벽 검사가 없다).
/// · 대상 = <see cref="SkillTargetingMode"/> 가 None 이 아닌 스킬 전부(가렌 R·거너 R·어쌔신 변신 E — 새 타겟팅 스킬도 자동).
///   평타·대시·방향형 Q/E/우클릭은 그대로.
/// · 판정 입력 = 서버 복제 상태(디렉터 게이트 마스크·플레이어 체크 마스크)라 오너 1차(조준 진입)·서버 2차(승인)가 같은 값을 본다.
/// </summary>
public static class TraitWaitTargetingRules
{
    /// <summary>한 복도 기준 — 그 복도 특성을 골랐고(체크 켜짐) 출구가 아직 닫혀 있으면 막는다.</summary>
    public static bool IsBlocked(bool gateOpen, bool pickedThisGate) => pickedThisGate && !gateOpen;

    /// <summary>이 조준 방식이 금지 대상인가 — 타겟팅 모드 스킬(SingleTarget·GroundPoint)만.</summary>
    public static bool AppliesTo(SkillTargetingMode mode) => mode != SkillTargetingMode.None;

    /// <summary>
    /// 플레이어가 지금 '특성 고른 뒤 대기' 중인가 — 복도 1~4 중 하나라도 <see cref="IsBlocked"/> 면 참.
    /// <paramref name="mapSceneRunning"/> = 맵씬 진행 활성이고 끝나지 않음(보스방 도착 뒤엔 만료 강제 이동으로 G10 이 닫혀 있어도 풀림).
    /// </summary>
    public static bool IsWaitingForParty(bool mapSceneRunning, MapScenePlayerProgress progress, Func<MapSceneGate, bool> isGateOpen)
    {
        if (!mapSceneRunning || isGateOpen == null) return false;
        for (int c = 1; c <= MapSceneTraitCorridorRules.CorridorCount; c++)
        {
            if (IsBlocked(isGateOpen(MapSceneTraitCorridorRules.ExitGate(c)),
                          progress.Has(MapSceneTraitCorridorRules.CheckOf(c))))
                return true;
        }
        return false;
    }

    /// <summary>이 스킬 시전(조준 진입·승인)을 막는가 — 타겟팅 모드 스킬이고 대기 중일 때만.</summary>
    public static bool BlocksSkill(SkillTargetingMode mode, bool waitingForParty) => waitingForParty && AppliesTo(mode);
}

/// <summary>
/// <see cref="TraitWaitTargetingRules"/> 를 맵씬 디렉터 복제 상태로 읽는 얇은 어댑터(모든 피어 — 오너·서버 같은 값).
/// 디렉터가 없거나(로비·보스 단독 씬·오프라인) 맵씬 진행이 꺼져 있으면 항상 false.
/// </summary>
public static class TraitWaitTargeting
{
    public static bool IsWaitingForParty(ulong clientId)
    {
        MapSceneDirector d = MapSceneDirector.Instance;
        if (d == null || !d.IsTutorialActive) return false;
        if (!d.TryGetPlayerProgress(clientId, out MapScenePlayerProgress p)) return false;
        return TraitWaitTargetingRules.IsWaitingForParty(!d.IsFinished, p, d.IsGateOpen);
    }

    /// <summary>이 클라이언트가 이 스킬 데이터로 조준·시전하는 것을 지금 막는가.</summary>
    public static bool Blocks(PlayerSkillData data, ulong clientId) =>
        data != null && TraitWaitTargetingRules.AppliesTo(data.TargetingMode) && IsWaitingForParty(clientId);
}
