/// <summary>
/// FMOD 파라미터 "이름"을 모아두는 상수 모음.
/// 파라미터 이름은 컴파일 타임 상수이며 인스펙터 연결이 필요 없으므로
/// MonoBehaviour 싱글톤이 아니라 static class로 둔다. (매직 스트링 제거 목적)
///
/// 사용: soundManager.GetParameterId(eventRef, FmodParams.Local.RPM)
/// </summary>
public static class FmodParams
{
    /// <summary>특정 EventInstance에만 적용되는 로컬 파라미터.</summary>
    public static class Local
    {
        // public const string RPM = "RPM";
        // public const string BossPhase = "Phase";
    }

    /// <summary>시스템 전체(모든 이벤트)에 영향을 주는 글로벌 파라미터.</summary>
    public static class Global
    {
        // public const string Environment = "Environment";
        // public const string CombatIntensity = "CombatIntensity";
    }
}
