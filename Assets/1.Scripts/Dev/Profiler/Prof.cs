// Prof.cs — 게임 코드용 커스텀 ProfilerMarker 모음.
//
// Unity Behavior(com.unity.behavior)는 자체 ProfilerMarker가 없어서 BT 비용이
// "Scripts > Update"에 뭉쳐 잡힌다. BT(또는 특정 AI 코드)를 따로 보고 싶으면
// 해당 구동 코드를 이 마커로 감싼다:
//
//     using (Prof.BT.Auto())
//     {
//         // BT/AI 관련 per-frame 코드 (블랙보드 갱신, 의사결정 등)
//     }
//
// 그러면 Unity Profiler 창(검색 "BT")과 ProfilerHUD 의 "게임플레이 마커 > BT" 에 따로 표시된다.
// ProfilerMarker는 릴리스 빌드에서도 비용이 거의 없으므로(프로파일러 비활성 시) 그대로 둬도 된다.

using Unity.Profiling;

public static class Prof
{
    // 카테고리는 Scripts 로 묶어 Profiler CPU 차트에서 스크립트 영역에 함께 보이게 함.
    public static readonly ProfilerMarker BT = new ProfilerMarker(ProfilerCategory.Scripts, "BT");

    // ── FragmentExploder ──────────────────────────────────────────────────
    // 파편 폭발은 "한 프레임에 몰아서 터지는" 작업이라 평균값으로는 안 보이고,
    // 수동 트리거라 9만 프레임 녹화에서 해당 프레임을 눈으로 찾을 수가 없다.
    // 그래서 구간을 쪼개 이름을 붙여둔다 — Profiler 창에서 "Fragment"로 검색하면
    // 그 프레임만 걸러지고, Timeline에서 어느 구간이 두꺼운지가 곧 원인이다.
    //
    // Activate / Forces는 루프 안에서 파편 수만큼 반복 측정된다. Timeline에는 잘게
    // 나뉘어 보이지만 Hierarchy 뷰가 프레임 단위로 합산해 주므로 그쪽을 보면 된다.

    /// <summary>폭발 전체. 아래 세 구간의 합 + 나머지.</summary>
    public static readonly ProfilerMarker FragExplode = new ProfilerMarker(ProfilerCategory.Scripts, "Fragment.Explode");

    /// <summary>파편 켜기(위치 리셋 + SetActive). 렌더러 등록·PhysX actor 생성이 여기 잡힌다.</summary>
    public static readonly ProfilerMarker FragActivate = new ProfilerMarker(ProfilerCategory.Scripts, "Fragment.Activate");

    /// <summary>리지드바디 깨우기 + 폭발력·회전 부여.</summary>
    public static readonly ProfilerMarker FragForces = new ProfilerMarker(ProfilerCategory.Scripts, "Fragment.Forces");

    /// <summary>onExploded UnityEvent — 지금은 이펙트 재생(EffectSocketPlayer.PlayOnce)이 걸려 있다.</summary>
    public static readonly ProfilerMarker FragOnExploded = new ProfilerMarker(ProfilerCategory.Scripts, "Fragment.OnExploded");

    /// <summary>파편 GameObject 생성. prewarm이 켜져 있으면 Start, 꺼져 있으면 첫 폭발 프레임에 잡힌다.</summary>
    public static readonly ProfilerMarker FragEnsurePool = new ProfilerMarker(ProfilerCategory.Scripts, "Fragment.EnsurePool");

    /// <summary>파편 회수. Explode와 대칭으로 SetActive를 파편 수만큼 부르므로 여기도 튈 수 있다.</summary>
    public static readonly ProfilerMarker FragRecall = new ProfilerMarker(ProfilerCategory.Scripts, "Fragment.Recall");

    // 필요하면 여기에 더 추가:
    // public static readonly ProfilerMarker Ability = new ProfilerMarker(ProfilerCategory.Scripts, "Ability");
    // public static readonly ProfilerMarker StatusEffect = new ProfilerMarker(ProfilerCategory.Scripts, "StatusEffect");
}
