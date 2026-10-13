using UnityEngine;

/// <summary>
/// 빌드 프레임 상한(팀장 10-05 결정: 144). 상한이 없으면 vSync 0 인 빌드가 무제한으로 돌아
/// 리슨 서버 호스트의 GPU·CPU 를 다 쓴다(PLAN-cleanup-optimization S1-1).
///
/// 부팅 적용은 <see cref="DisplayConfig"/> 가 한다(설정창 프레임 제한 60/120/144/무제한, PLAN-settings S6 — 10-12).
/// 🔴 여기서 따로 부팅 적용하지 말 것 — 같은 BeforeSceneLoad 끼리는 순서가 보장되지 않아 저장된 설정을 덮을 수 있다.
/// ⚠️ <c>QualitySettings.vSyncCount</c> 가 0 이 아니면 <c>targetFrameRate</c> 는 무시된다 — VSync 켜면 DisplayConfig 가 상한을 끈다.
/// </summary>
public static class FrameRateCap
{
    public const int DefaultTarget = DisplayConfig.DefaultFrameLimit;

    /// <summary>0 이하 = 제한 없음.</summary>
    public static void Apply(int target)
    {
        Application.targetFrameRate = target > 0 ? target : -1;
    }
}
