#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// MapGen 테스트 생성 메뉴 (에디터 전용). 씬의 MapGenerator 를 찾아 시드로 Generate 한다.
//
// ⚠️ 파일명이 내용과 다르다 — 실질은 `MapGenTestMenu` 다. 원래 이 파일에는 와이어링 도구
//    (씬 ZoneVolume → ZoneSlot 스켈레톤 생성 + ZoneLayoutCatalog 등록 + MapGenerator 참조 연결)가
//    있었으나 2026-07 리팩토링에서 폐기됐다 — 통로는 Stage1/Level_wall_hallway 손배치로 고정됐고,
//    절차생성·연결그래프·벽컷·개방변 매칭이 함께 사라졌다. `ZoneVolume`·`ZoneDefinitionSO` 타입도
//    프로젝트에 없다(`ZoneSlot.cs` 헤더가 그 모델을 대체했다고 적고 있다).
//
// 2026-09-09 전수조사에서 그 폐기된 구현을 설명하던 주석과, 아무도 읽지 않는 상수 2개를 제거했다:
//    `PrefabDir = "Assets/50.Art/MapGen/MapObj/Zoneprefab"`  ← 존재하지 않는 폴더였다
//    `CatalogPath = ".../ZoneLayoutCatalog.asset"`
//    미사용 using 2개(`System.Collections.Generic`, `System.Linq`)
//
// 배치 저작의 현재 정본은 `SavePlacements`(Tools/MapGen/Save Placements) + `ZoneSlot` 이다.
// 역할 배정·프리팹 선택 규칙은 `MapGenerator.AssignSlotRoles` 와 `LayoutPlacer` 를 읽을 것 —
// 여기에 규칙을 주석으로 복제하면 또 낡는다(이 파일이 그렇게 낡았다).
public static class ZoneWiring
{
    // 매번 다른 배치 — 시드 랜덤.
    [MenuItem("Tools/MapGen/Test Generate (random seed)")]
    static void GenRandom() => RunGen(System.Environment.TickCount);

    // 고정 시드 — 재현/디버그용.
    [MenuItem("Tools/MapGen/Test Generate (seed 12345, 재현용)")]
    static void Gen12345() => RunGen(12345);

    static void RunGen(int seed)
    {
        var mg = Object.FindFirstObjectByType<MapGenerator>();
        if (mg == null) { Debug.LogError("[Gen] MapGenerator 없음"); return; }
        mg.Generate(seed, 0);
    }
}
#endif
