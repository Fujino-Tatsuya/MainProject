using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 프롭 프리팹을 <b>파괴 가능한 상자 더미</b>로 만드는 일괄 저작 툴.
///
/// 손으로 하면 프리팹 6종 × (루트 컴포넌트 2개 + 자식 레이어 14개)라 반복적이고,
/// <b>하나만 빠뜨려도 그 더미만 반응하지 않는</b> 재현 어려운 버그가 된다.
///
/// <b>피격 판정은 전부 루트에 모은다</b> — 몬스터 프리팹의 Hurtbox 세팅과 같은 형태다
/// (레이어 <c>EnemyHurtBox</c> + <b>트리거</b> 콜라이더 + <c>Hurtbox</c>).
///
/// <list type="bullet">
/// <item><b>루트</b>: <see cref="BreakableCrate"/> + <c>Hurtbox</c> + 더미 전체를 덮는
///       트리거 BoxCollider + 레이어 <c>EnemyHurtBox</c>.
///       <c>Hurtbox.attackReceiverSource</c>에 <see cref="BreakableCrate"/>를 <b>명시 배선</b>한다.</item>
/// <item><b>자식 상자</b>: 손대지 않는다. 기존 MeshCollider는 플레이어를 막는
///       <b>물리 차단</b> 역할이라 레이어를 그대로 둬야 한다.</item>
/// </list>
///
/// 판정 콜라이더와 차단 콜라이더를 분리하는 것이 요점이다. 하나로 겸하면
/// 레이어를 <c>EnemyHurtBox</c>로 옮기는 순간 물리 차단이 깨진다.
///
/// 더미 전체가 트리거 하나라 <b>어느 상자를 때려도 한 번만</b> 판정된다 —
/// 상자마다 Hurtbox를 두면 한 번 휘두를 때 hp가 여러 번 깎인다.
/// </summary>
public static class CrateAuthoringTool
{
    const string HurtboxLayerName = "EnemyHurtBox";

    // 크레이트로 인정할 메시 이름 조각. FBX가 늘면 여기에 추가한다.
    static readonly string[] CrateMeshHints = { "Crate", "Box" };

    /// <summary>
    /// 열려 있는 씬의 <see cref="BreakableCrate"/>에 <b>저작 ID</b>를 부여한다.
    ///
    /// 생성 맵의 상자는 <c>MapContentSpawner</c>가 스폰 순번으로 ID를 주지만,
    /// <b>씬에 직접 배치한 상자</b>(BossScene 같은 테스트 씬)는 그 경로를 안 탄다.
    /// 그런 상자는 여기서 미리 구운 ID로 스스로 등록한다.
    ///
    /// 두 ID 공간이 충돌하지 않도록 <b>저작 ID는 음수</b>를 쓴다(생성 ID는 양수).
    /// 계층 경로로 정렬해 부여하므로 다시 돌려도 같은 값이 나온다 — 씬 diff가 튀지 않는다.
    /// </summary>
    [MenuItem("Tools/Crates/씬의 상자에 ID 부여")]
    static void AssignAuthoredIds()
    {
        BreakableCrate[] crates = Object.FindObjectsByType<BreakableCrate>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (crates.Length == 0)
        {
            EditorUtility.DisplayDialog("상자 ID", "열려 있는 씬에 BreakableCrate가 없습니다.", "확인");
            return;
        }

        // FindObjectsByType의 순서는 보장되지 않는다. 정렬하지 않으면 돌릴 때마다 ID가
        // 재배치돼 씬 diff가 통째로 튀고 머지 충돌이 난다.
        System.Array.Sort(crates, (a, b) =>
            string.CompareOrdinal(HierarchyPath(a.transform), HierarchyPath(b.transform)));

        for (int i = 0; i < crates.Length; i++)
        {
            var so = new SerializedObject(crates[i]);
            so.FindProperty("authoredId").intValue = -(i + 1);   // 음수 = 저작 ID
            so.ApplyModifiedProperties();
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
        Debug.Log($"[CrateAuthoring] 씬 상자 {crates.Length}개에 ID(-1 ~ -{crates.Length})를 부여했습니다. 씬을 저장하세요.");
    }

    static string HierarchyPath(Transform t)
    {
        var sb = new System.Text.StringBuilder(t.name);
        for (Transform p = t.parent; p != null; p = p.parent)
            sb.Insert(0, p.name + "/");

        return $"{t.gameObject.scene.name}/{sb}";
    }

    [MenuItem("Tools/Crates/선택한 프리팹을 파괴 가능하게 만들기")]
    static void MakeSelectedBreakable()
    {
        GameObject[] targets = Selection.GetFiltered<GameObject>(SelectionMode.Assets);
        if (targets.Length == 0)
        {
            EditorUtility.DisplayDialog("상자 저작", "프로젝트 창에서 프롭 프리팹을 선택하세요.", "확인");
            return;
        }

        int layer = LayerMask.NameToLayer(HurtboxLayerName);
        if (layer < 0)
        {
            Debug.LogError($"[CrateAuthoring] '{HurtboxLayerName}' 레이어가 없습니다. TagManager를 확인하세요.");
            return;
        }

        int converted = 0;

        foreach (GameObject asset in targets)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab")) continue;

            GameObject root = PrefabUtility.LoadPrefabContents(path);

            if (Convert(root, layer))
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
                converted++;
                Debug.Log($"[CrateAuthoring] '{asset.name}' 변환 완료.", asset);
            }
            else
            {
                Debug.LogWarning($"[CrateAuthoring] '{asset.name}'에서 크레이트 메시를 찾지 못해 건너뜁니다.", asset);
            }

            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[CrateAuthoring] 프리팹 {converted}개 변환.");
    }

    static bool Convert(GameObject root, int layer)
    {
        List<Transform> crates = FindCratePieces(root);
        if (crates.Count == 0) return false;

        // ── 루트: 파괴 단위 + 피격 판정 ──
        root.layer = layer;

        BreakableCrate crate = root.GetComponent<BreakableCrate>();
        if (crate == null) crate = root.AddComponent<BreakableCrate>();

        // 판정용 트리거. 자식 MeshCollider(물리 차단)와 역할이 다르므로 별개로 둔다.
        // 트리거가 아니면 더미를 두 겹으로 막게 된다.
        BoxCollider box = root.GetComponent<BoxCollider>();
        if (box == null) box = root.AddComponent<BoxCollider>();
        box.isTrigger = true;
        FitToChildren(box, root, crates);

        Hurtbox hurtbox = root.GetComponent<Hurtbox>();
        if (hurtbox == null) hurtbox = root.AddComponent<Hurtbox>();

        // pieces = 버스트를 터뜨릴 자리, attackReceiverSource = 공격 수신자.
        // 자동 탐색에 맡기지 않고 명시 배선한다 — 자동은 중첩 계층에서 엉뚱한 것을 집는다.
        var crateSo = new SerializedObject(crate);
        SerializedProperty list = crateSo.FindProperty("pieces");
        list.arraySize = crates.Count;
        for (int i = 0; i < crates.Count; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = crates[i];
        crateSo.ApplyModifiedPropertiesWithoutUndo();

        var hurtboxSo = new SerializedObject(hurtbox);
        hurtboxSo.FindProperty("attackReceiverSource").objectReferenceValue = crate;
        hurtboxSo.ApplyModifiedPropertiesWithoutUndo();

        return true;
    }

    /// <summary>판정 트리거를 자식 상자 전체를 덮는 크기로 맞춘다(루트 로컬 기준).</summary>
    static void FitToChildren(BoxCollider box, GameObject root, List<Transform> crates)
    {
        Renderer first = null;
        Bounds bounds = default;

        for (int i = 0; i < crates.Count; i++)
        {
            var r = crates[i].GetComponent<Renderer>();
            if (r == null) continue;

            if (first == null) { first = r; bounds = r.bounds; }
            else bounds.Encapsulate(r.bounds);
        }

        if (first == null) return;

        box.center = root.transform.InverseTransformPoint(bounds.center);
        box.size = bounds.size;   // 프롭 루트는 스케일 1이라 월드 크기를 그대로 쓴다
    }

    /// <summary>메시 이름으로 크레이트 자식을 찾는다. 루트 자신은 제외한다.</summary>
    static List<Transform> FindCratePieces(GameObject root)
    {
        var found = new List<Transform>();

        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < filters.Length; i++)
        {
            if (filters[i].sharedMesh == null) continue;
            if (filters[i].transform == root.transform) continue;

            string meshName = filters[i].sharedMesh.name;
            for (int h = 0; h < CrateMeshHints.Length; h++)
            {
                if (meshName.IndexOf(CrateMeshHints[h], System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                found.Add(filters[i].transform);
                break;
            }
        }

        return found;
    }
}
