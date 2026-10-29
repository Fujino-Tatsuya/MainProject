#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
// 개발용(F11 — DevBossFinalBlow, 경석 10-27): 등장 연출을 서버에서 바로 스킵한다. DEV_HOTKEYS 없는 릴리스 빌드엔 이 파일이 없다.
public sealed partial class BossEncounterDirector
{
    /// <summary>[서버] 보스가 살아 있고 전투 중인가.</summary>
    public bool DevIsBossInCombat =>
        IsServer && IsSpawned && phase.Value == BossEncounterPhase.Combat && _bossUnit != null && _bossUnit.CurrentHealth > 0;

    /// <summary>[서버] 등장 연출 중인가(워크인·강하·착지·지시).</summary>
    public bool DevIsIntroRunning => IsServer && IsSpawned && IsCinematicPhase(phase.Value);

    /// <summary>
    /// [서버] 등장 연출을 전원 동의 스킵과 같은 경로(<see cref="SkipIntroServer"/>)로 넘긴다 — 보스를 착지점에 세우고 카메라 복귀 뒤 전투.
    /// 이미 카메라 복귀 중이면 false(할 일 없음). 연출이 아닌 단계면 false.
    /// </summary>
    public bool DevSkipIntroServer()
    {
        if (!IsServer || !IsSpawned || !_introRunning || !IsCinematicPhase(phase.Value)) return false;
        double elapsed = NetworkManager.ServerTime.Time - phaseStartServerTime.Value;
        if (phase.Value == BossEncounterPhase.Dialogue && elapsed >= cameraReturnAt.Value) return false;
        SkipIntroServer();
        return true;
    }
}
#endif
