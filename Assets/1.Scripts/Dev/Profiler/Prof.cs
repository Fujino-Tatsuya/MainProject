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

    // 필요하면 여기에 더 추가:
    // public static readonly ProfilerMarker Ability = new ProfilerMarker(ProfilerCategory.Scripts, "Ability");
    // public static readonly ProfilerMarker StatusEffect = new ProfilerMarker(ProfilerCategory.Scripts, "StatusEffect");
}
