using UnityEditor;
using UnityEngine;

// 저작 도구 — 중간보스 2종에 인터럽트 카운터를 **배선**한다 (2026-09-08). 멱등이다.
//
// 손으로 인스펙터를 만지지 않고 도구로 둔 이유는 두 가지다:
//  ① 프리팹 2개 + 데이터 2개에 걸친 배선이라 하나만 빠져도 "창이 안 열리는" 조용한 실패가 된다.
//  ② 값의 **근거**를 코드에 남길 수 있다. 인스펙터 값은 왜 그 값인지가 남지 않는다.
//
// 하는 일:
//  - `MonsterCounterWindow` 컴포넌트 부착(없을 때만) — 창 1.5초 · 그로기 0.5초는 컴포넌트 기본값
//  - 데이터의 `maxGroggyCount` → **0** : 창 없이 인터럽트를 N번 맞으면 눕던 **누적식을 끈다**
//    (`MonsterBase.TakeDamage` 가 `maxGroggyCount > 0` 로 게이트돼 있어 코드 변경 없이 꺼진다)
//  - 데이터의 `retargetInterval` → **8초** : 주기 어그로 재선정. 0 이면 기존 락온 유지
//
// ⚠️ `groggyDuration`(3초)은 건드리지 않는다. 카운터 성공 그로기는 컴포넌트의 0.5초를 쓰고,
//    이 값은 누적식 전용이라 지금은 잠들어 있다 — 나중에 누적식을 되살릴 때의 값이다.
public static class MidBossCounterAuthoring
{
    const string SpinnerPrefab = "Assets/2.Prefabs/Monster/SpinnerBot.prefab";
    const string GauntletPrefab = "Assets/2.Prefabs/Monster/GauntletBot.prefab";
    const string SpinnerData = "Assets/2.Prefabs/Monster/Data/SpinnerBotData.asset";
    const string GauntletData = "Assets/2.Prefabs/Monster/Data/GauntletBotData.asset";

    const float RetargetInterval = 8f;   // 23호와 같은 대역에서 시작한다 — MPPM 2인으로 보고 조정할 값

    [MenuItem("Tools/Boss/중간보스 — 인터럽트 카운터 배선 (멱등)")]
    public static void Wire()
    {
        EnsureWindow(SpinnerPrefab);
        EnsureWindow(GauntletPrefab);
        TuneData(SpinnerData);
        TuneData(GauntletData);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Boss/중간보스 — 인터럽트 카운터 배선 검증 (읽기 전용)")]
    public static void Verify()
    {
        VerifyOne(SpinnerPrefab, SpinnerData);
        VerifyOne(GauntletPrefab, GauntletData);
    }

    static void EnsureWindow(string prefabPath)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null)
        {
            Debug.LogError($"[MidBossCounter] 프리팹을 열 수 없다: {prefabPath}");
            return;
        }

        try
        {
            if (root.GetComponent<MonsterCounterWindow>() != null)
            {
                Debug.Log($"[MidBossCounter] 이미 있다 — {root.name}. 변경 없음.");
                return;
            }

            root.AddComponent<MonsterCounterWindow>();
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Debug.Log($"[MidBossCounter] 부착: {root.name} ← MonsterCounterWindow (창 1.5초 · 그로기 0.5초)");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void TuneData(string dataPath)
    {
        var data = AssetDatabase.LoadAssetAtPath<MonsterDataSO>(dataPath);
        if (data == null)
        {
            Debug.LogError($"[MidBossCounter] 데이터를 열 수 없다: {dataPath}");
            return;
        }

        bool changed = false;

        if (data.maxGroggyCount != 0)
        {
            Debug.Log($"[MidBossCounter] {data.name}: maxGroggyCount {data.maxGroggyCount} → 0 (누적식 끔)");
            data.maxGroggyCount = 0;
            changed = true;
        }

        if (!Mathf.Approximately(data.retargetInterval, RetargetInterval))
        {
            Debug.Log($"[MidBossCounter] {data.name}: retargetInterval {data.retargetInterval} → {RetargetInterval}");
            data.retargetInterval = RetargetInterval;
            changed = true;
        }

        if (changed) EditorUtility.SetDirty(data);
        else Debug.Log($"[MidBossCounter] {data.name}: 이미 맞다. 변경 없음.");
    }

    static void VerifyOne(string prefabPath, string dataPath)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        var data = AssetDatabase.LoadAssetAtPath<MonsterDataSO>(dataPath);
        if (prefab == null || data == null)
        {
            Debug.LogError($"[MidBossCounter] 검증 실패 — 자산 없음: {prefabPath} / {dataPath}");
            return;
        }

        MonsterCounterWindow win = prefab.GetComponent<MonsterCounterWindow>();
        Debug.Log($"[MidBossCounter] {prefab.name}: 카운터 창 {(win != null ? $"창 {win.WindowDuration}초 · 그로기 {win.GroggyDuration}초" : "없음 ❌")} · " +
                  $"maxGroggyCount {data.maxGroggyCount}{(data.maxGroggyCount == 0 ? " (누적식 꺼짐)" : " ❌ 누적식이 살아 있다")} · " +
                  $"retargetInterval {data.retargetInterval}초{(data.retargetInterval > 0f ? "" : " ❌ 재선정 꺼짐")}");
    }
}
