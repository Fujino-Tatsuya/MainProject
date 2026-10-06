using UnityEngine;

/// <summary>
/// 빌드 프레임 상한(팀장 10-05 결정: 144). 상한이 없으면 vSync 0 인 빌드가 무제한으로 돌아
/// 리슨 서버 호스트의 GPU·CPU 를 다 쓴다(PLAN-cleanup-optimization S1-1).
///
/// 씬 배치 없이 첫 씬 로드 전에 한 번 건다. 나중에 옵션 메뉴로 뺄 때는 <see cref="Apply"/> 만 부르면 된다.
/// ⚠️ <c>QualitySettings.vSyncCount</c> 가 0 이 아니면 <c>targetFrameRate</c> 는 무시된다 — 지금 PC 품질은 0.
/// </summary>
public static class FrameRateCap
{
    public const int DefaultTarget = 144;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Init() => Apply(DefaultTarget);

    /// <summary>0 이하 = 제한 없음.</summary>
    public static void Apply(int target)
    {
        Application.targetFrameRate = target > 0 ? target : -1;
    }
}
