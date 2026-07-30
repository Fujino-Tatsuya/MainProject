#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// v11 회전 저작 창: (슬롯 × 프리팹) 조합을 찍어서 스폰 → 씬에서 90° 회전으로 통로에 맞춤 → 저장.
// 조합 집합은 실제 생성 로직(MapGenerator.ComputePlacements)을 여러 시드로 돌려 정확히 산출한다(손계산 없음).
// 위치는 슬롯 baseline 공유이므로 이 창은 회전(YawSteps)만 저작한다.
public class ZoneRotationAuthoringWindow : EditorWindow
{
    const int SimSeeds = 2000;                 // 조합 커버용 시뮬 시드 수
    const string AuthRootName = "ZoneRotAuthoring";

    struct Combo { public int SlotID; public ZoneSize Size; public GameObject Prefab; }

    readonly List<Combo> _combos = new List<Combo>();
    readonly Dictionary<int, ZoneSlot> _slots = new Dictionary<int, ZoneSlot>();
    Vector2 _scroll;
    GameObject _activeInstance;
    int _activeSlot = -1;
    GameObject _activePrefab;

    [MenuItem("Tools/MapGen/Zone Rotation Authoring")]
    static void Open() => GetWindow<ZoneRotationAuthoringWindow>("Zone Rotation");

    void OnEnable() => Refresh();
    void OnDisable() => ClearSpawn();

    void Refresh()
    {
        _combos.Clear();
        _slots.Clear();

        var mg = Object.FindFirstObjectByType<MapGenerator>();
        if (mg == null) { Debug.LogError("[RotAuthoring] MapGenerator 없음 — Wire 먼저"); return; }

        foreach (var s in Object.FindObjectsByType<ZoneSlot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            _slots[s.SlotID] = s;
        if (_slots.Count == 0) { Debug.LogError("[RotAuthoring] ZoneSlot 없음 — Wire 먼저"); return; }

        // 실제 생성 로직을 여러 시드로 돌려 가능한 (슬롯, 프리팹) 조합을 정확히 수집.
        var seen = new HashSet<(int, GameObject)>();
        for (int seed = 0; seed < SimSeeds; seed++)
            foreach (var p in mg.ComputePlacements(seed, 0))
                if (p.Slot != null && p.LayoutPrefab != null)
                    seen.Add((p.Slot.SlotID, p.LayoutPrefab));

        foreach (var t in seen.OrderBy(x => x.Item1).ThenBy(x => x.Item2.name))
            if (_slots.TryGetValue(t.Item1, out var s))
                _combos.Add(new Combo { SlotID = t.Item1, Size = s.Size, Prefab = t.Item2 });
    }

    void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("조합 새로고침 (시뮬)")) Refresh();
            int done = _combos.Count(c => _slots.TryGetValue(c.SlotID, out var s) && s.TryGetYaw(c.Prefab, out _));
            GUILayout.FlexibleSpace();
            GUILayout.Label($"저작 {done}/{_combos.Count}", EditorStyles.boldLabel);
        }

        EditorGUILayout.HelpBox("① [스폰] → ② 씬에서 90° 회전 + (필요시) 위치 미세 이동으로 통로에 맞춤 → ③ [저장]. 회전과 위치 둘 다 조합별로 저장됨.", MessageType.Info);

        if (_activeInstance != null)
        {
            using (new EditorGUILayout.VerticalScope("box"))
            {
                EditorGUILayout.LabelField($"작업 중: Slot {_activeSlot} × {(_activePrefab != null ? _activePrefab.name : "?")}   현재 {CurrentSteps() * 90}°", EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("저장")) SaveActive();
                    if (GUILayout.Button("취소")) ClearSpawn();
                }
            }
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        int curSlot = int.MinValue;
        foreach (var c in _combos)
        {
            if (c.SlotID != curSlot)
            {
                curSlot = c.SlotID;
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"── Slot {c.SlotID} ({c.Size}) ──", EditorStyles.boldLabel);
            }

            bool has = _slots.TryGetValue(c.SlotID, out var slot) && slot.TryGetYaw(c.Prefab, out _);
            int savedSteps = 0;
            if (has) slot.TryGetYaw(c.Prefab, out savedSteps);

            using (new EditorGUILayout.HorizontalScope())
            {
                var style = new GUIStyle(EditorStyles.label)
                { normal = { textColor = has ? new Color(0.45f, 0.85f, 0.45f) : new Color(0.9f, 0.55f, 0.55f) } };
                GUILayout.Label(has ? $"✔ {c.Prefab.name}   {savedSteps * 90}°" : $"✗ {c.Prefab.name}   미저작", style, GUILayout.Width(280));

                bool isActive = _activeInstance != null && _activeSlot == c.SlotID && _activePrefab == c.Prefab;
                using (new EditorGUI.DisabledScope(isActive))
                    if (GUILayout.Button("스폰", GUILayout.Width(60))) Spawn(c);
            }
        }
        EditorGUILayout.EndScrollView();
    }

    void Spawn(Combo c)
    {
        ClearSpawn();
        if (!_slots.TryGetValue(c.SlotID, out var slot) || c.Prefab == null) return;

        var root = GameObject.Find(AuthRootName);
        if (root == null) root = new GameObject(AuthRootName);
        root.hideFlags = HideFlags.DontSaveInEditor;

        int steps = 0;
        slot.TryGetYaw(c.Prefab, out steps);
        Vector3 pos = slot.TryGetPosition(c.Prefab, out var savedPos) ? savedPos : slot.transform.position;
        var inst = (GameObject)Object.Instantiate(c.Prefab, pos,
            Quaternion.Euler(0f, steps * 90f, 0f), root.transform);
        inst.name = $"AUTH_{c.SlotID}_{c.Prefab.name}";
        inst.hideFlags = HideFlags.DontSaveInEditor;

        _activeInstance = inst; _activeSlot = c.SlotID; _activePrefab = c.Prefab;
        Selection.activeGameObject = inst;
        if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
        Repaint();
    }

    void SaveActive()
    {
        if (_activeInstance == null || !_slots.TryGetValue(_activeSlot, out var slot)) return;
        int steps = CurrentSteps();
        Vector3 pos = _activeInstance.transform.position;
        Undo.RecordObject(slot, "Save Zone Placement");
        slot.SetPlacement(_activePrefab, steps, pos);
        EditorUtility.SetDirty(slot);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(slot.gameObject.scene);
        Debug.Log($"[RotAuthoring] 저장: Slot {_activeSlot} × {_activePrefab.name} = {steps * 90}° @({pos.x:F1},{pos.y:F1},{pos.z:F1}) (미저장 — 씬 저장 필요)");
        ClearSpawn();
        Repaint();
    }

    int CurrentSteps()
    {
        if (_activeInstance == null) return 0;
        int s = Mathf.RoundToInt(_activeInstance.transform.eulerAngles.y / 90f) % 4;
        return s < 0 ? s + 4 : s;
    }

    void ClearSpawn()
    {
        if (_activeInstance != null) Object.DestroyImmediate(_activeInstance);
        var root = GameObject.Find(AuthRootName);
        if (root != null && root.transform.childCount == 0) Object.DestroyImmediate(root);
        _activeInstance = null; _activeSlot = -1; _activePrefab = null;
    }
}
#endif
