#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// 와이어링(에디터 전용): 씬 ZoneVolume → ZoneSlot 스켈레톤 생성 +
// ZoneLayoutCatalog 등록 + MapGenerator 참조 연결 + 임시 Zone_* 숨김. + 셔플 generate 메뉴.
// 배치 소스오브트루스 = ZoneVolume(씬). 볼륨을 옮기고 Wire 재실행하면 맵이 따라온다.
// 2026-07 리팩토링: 통로는 Stage1/Level_wall_hallway 손배치로 고정 — 절차생성/연결그래프/벽컷/개방변 매칭은 폐기.
public static class ZoneWiring
{
    const string PrefabDir = "Assets/50.Art/MapGen/MapObj/Zoneprefab"; // 2026-07-03 아트가 prefab→Zoneprefab로 폴더명 변경(GUID 유지)
    const string CatalogPath = "Assets/50.Art/MapGen/MapObj/ZoneLayout/ZoneLayoutCatalog.asset";

    // 배치 소스오브트루스 = 씬(Stage1)의 ZoneVolume 10개 (2026-07 리팩토링).
    //  - ZoneVolume.transform.position → 슬롯 중심 (Y는 0으로 클램프)
    //  - ZoneVolume.Size 가로세로비 → 크기/회전 (양축≥30m=대형 / 한축만≥30m=중형, X가 길면 90° / 그 외=소형)
    //  - ZoneDefinitionSO 플래그 → 퀘스트/스폰/보스입구 후보
    //  - SlotID = ZoneID - 1 (1~10 필수, 결정적 순서)
    // 디자이너가 씬에서 볼륨을 옮기면 Wire 재실행만으로 배치가 따라온다.

    // 카탈로그 (prefab 폴더, 2026-07 정리 완료).
    // 대형: 3디자인 ↔ 3슬롯 시드 셔플(재사용 없음).
    // 중형: 퀘스트 슬롯(4후보 중 랜덤 1곳) = 전용 디자인 2종(Quest01/02) 중 랜덤 1개.
    // 소형: typeA=우상단 고정 전투, typeBossEnter=보스맵 입구, typeStart=스폰
    //       (좌상/좌하 후보 2곳에 스폰/보스입구가 매판 랜덤 배정).

    // 매번 다른 배치: 대형 3 순열 / 중형 3+퀘스트(4곳 중 1) / 스폰↔보스입구(좌상·좌하 랜덤)
    [MenuItem("Tools/MapGen/Test Generate (random seed)")]
    static void GenRandom() => RunGen(System.Environment.TickCount);

    // 고정 시드 — 재현/디버그용
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
