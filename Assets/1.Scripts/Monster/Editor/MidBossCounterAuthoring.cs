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
    const string WallPrefab = "Assets/2.Prefabs/Monster/WallBot.prefab";
    const string SpinnerData = "Assets/2.Prefabs/Monster/Data/SpinnerBotData.asset";
    const string GauntletData = "Assets/2.Prefabs/Monster/Data/GauntletBotData.asset";
    const string WallData = "Assets/2.Prefabs/Monster/Data/WallBotData.asset";
    const string WallScript = "Assets/1.Scripts/Monster/Boss/WallBot.cs";

    const float RetargetInterval = 8f;   // 23호와 같은 대역에서 시작한다 — MPPM 2인으로 보고 조정할 값

    [MenuItem("Tools/Boss/중간보스 — 인터럽트 카운터 배선 (멱등)")]
    public static void Wire()
    {
        UpgradeWallBotClass();        // 🔴 WallBot 만 전용 클래스가 없었다 — 먼저 교체해야 창이 돌아간다
        EnsureWindow(SpinnerPrefab);
        EnsureWindow(GauntletPrefab);
        EnsureWindow(WallPrefab);
        TuneData(SpinnerData);
        TuneData(GauntletData);
        TuneData(WallData);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Boss/중간보스 — 인터럽트 카운터 배선 검증 (읽기 전용)")]
    public static void Verify()
    {
        VerifyOne(SpinnerPrefab, SpinnerData);
        VerifyOne(GauntletPrefab, GauntletData);
        VerifyOne(WallPrefab, WallData);
    }

    /// <summary>
    /// WallBot 프리팹의 루트 스크립트를 <c>MonsterBase</c> → <c>WallBot</c> 로 바꾼다(멱등).
    ///
    /// WallBot 은 전용 클래스 없이 <c>MonsterBase</c> 를 그대로 쓰고 있었다. 3단 공격(모으기 → 돌진 →
    /// 충격파)을 얹으려면 파생이 있어야 한다.
    ///
    /// 🔴 <b>손으로 교체하지 말 것.</b> 인스펙터에서 스크립트를 바꾸면 참조가 끊길 수 있다.
    ///    여기서는 <c>m_Script</c> 만 갈아끼우므로 Unity 가 <b>필드 이름</b>으로 다시 붙인다 —
    ///    WallBot 이 MonsterBase 의 필드를 모두 물려받아 이름이 같기 때문에 값이 유지된다.
    ///    교체 뒤 참조 6개를 로그로 확인한다(끊겼으면 되돌리고 손으로 다시 붙일 판단을 해야 한다).
    /// </summary>
    static void UpgradeWallBotClass()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(WallPrefab);
        if (root == null)
        {
            Debug.LogError($"[MidBossCounter] 프리팹을 열 수 없다: {WallPrefab}");
            return;
        }

        try
        {
            var monster = root.GetComponent<MonsterBase>();
            if (monster == null)
            {
                Debug.LogError("[MidBossCounter] WallBot 루트에 MonsterBase 계열 컴포넌트가 없다.");
                return;
            }

            if (monster is WallBot)
            {
                Debug.Log("[MidBossCounter] WallBot 클래스 이미 적용됨. 변경 없음.");
                return;
            }

            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(WallScript);
            if (script == null)
            {
                Debug.LogError($"[MidBossCounter] 스크립트를 못 찾았다: {WallScript}");
                return;
            }

            var so = new SerializedObject(monster);
            so.FindProperty("m_Script").objectReferenceValue = script;
            so.ApplyModifiedProperties();

            PrefabUtility.SaveAsPrefabAsset(root, WallPrefab);
            Debug.Log("[MidBossCounter] WallBot 루트 스크립트 교체: MonsterBase → WallBot");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        ReportWallBotReferences();
    }

    // 교체가 참조를 깨뜨리지 않았는지 눈으로 확인한다 — 끊기면 조용히 동작만 사라진다.
    static void ReportWallBotReferences()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallPrefab);
        var monster = prefab != null ? prefab.GetComponent<MonsterBase>() : null;
        if (monster == null) { Debug.LogError("[MidBossCounter] 교체 후 컴포넌트를 못 읽었다."); return; }

        var so = new SerializedObject(monster);
        // 필수 = 비면 몹이 동작하지 않는다. 선택 = 비어도 런타임에 스스로 찾는다
        // (animator 는 MonsterBase 가 GetComponentInChildren 으로 메운다 — 프리팹에서 비어 있는 게 정상이다).
        string[] required = { "data", "agent", "meleeAttack" };
        string[] optional = { "animator", "status", "bodyCollider" };
        var sb = new System.Text.StringBuilder($"[MidBossCounter] 교체 후 참조 점검({monster.GetType().Name}): ");
        int missing = 0;

        foreach (string f in required)
        {
            SerializedProperty p = so.FindProperty(f);
            bool ok = p != null && p.objectReferenceValue != null;
            if (!ok) missing++;
            sb.Append($"{f}={(ok ? "O" : "❌")} ");
        }

        foreach (string f in optional)
        {
            SerializedProperty p = so.FindProperty(f);
            bool ok = p != null && p.objectReferenceValue != null;
            sb.Append($"{f}={(ok ? "O" : "-")} ");
        }

        SerializedProperty mask = so.FindProperty("playerMask");
        sb.Append($"playerMask={(mask != null ? mask.intValue.ToString() : "?")}");

        if (missing > 0) Debug.LogError(sb.ToString() + $" — 끊긴 **필수** 참조 {missing}개. 되돌릴 것.");
        else Debug.Log(sb.ToString());
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
