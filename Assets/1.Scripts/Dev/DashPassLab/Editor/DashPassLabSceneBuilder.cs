using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 할 일 209 대시 관통 확인 씬 저작 메뉴 — <c>DebugInterruptLab.unity</c> 를 복사해 허수아비를 빼고 레인 5개를 깐다.
/// 부트 구성(Dev Boot → 호스트 → NGO 로 씬 로드 → 플레이어 스폰)·카메라·대시 검증 매니저·배속·쿨 0·F1 캐릭터 순환은 원본 그대로 쓴다.
/// 몬스터는 씬에 두지 않고 <see cref="DebugDashPassLab"/> 가 서버에서 스폰 + FSM 정지한다.
/// 다시 실행하면 씬을 지우고 새로 만든다(수동 수정은 사라진다). 빌드 목록에는 넣지 않는다.
/// </summary>
public static class DashPassLabSceneBuilder
{
    public const string SourceScenePath = "Assets/0.Scenes/Debug/DebugInterruptLab.unity";
    public const string ScenePath = "Assets/0.Scenes/Debug/DebugDashPassLab.unity";

    private const string NormalMonsterPath = "Assets/2.Prefabs/Monster/HumanoidBot.prefab";
    private const string MidBossPath = "Assets/2.Prefabs/Monster/GauntletBot.prefab";
    private const string BossPath = "Assets/2.Prefabs/Monster/Boss/TwentyThree.prefab";

    // 대시 = 20m/s × 0.25s = 5m. 레인마다 START(z=2)에서 +Z 로 대시하면 z≈7 에서 끝난다.
    private const float StartZ = 2f;
    private const float LaneA = -12f, LaneB = -6f, LaneC = 0f, LaneD = 6f, LaneE = 14f;
    private const float Facing = 180f;

    // 원본 랩 오브젝트에서 남길 디버그 컴포넌트(배속·쿨 0·F1 캐릭터 순환). 나머지(간파 튜닝 패널 등)는 뺀다.
    private static readonly System.Type[] KeepOnLab = { typeof(DebugTimeScale), typeof(DebugZeroCooldown), typeof(DebugCharacterCycle) };

    [MenuItem("Tools/Debug/209 대시 관통 테스트 씬 만들기")]
    public static void BuildFromMenu()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("209 대시 관통 테스트 씬", "Play 모드를 끝낸 뒤 실행하세요.", "확인");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        Build();
        EditorUtility.DisplayDialog("209 대시 관통 테스트 씬",
            $"{ScenePath} 를 만들었습니다.\n툴바 Dev Boot 씬 드롭다운에서 DebugDashPassLab 을 고르고 ▶.", "확인");
    }

    /// <summary>batchmode 진입점(-executeMethod DashPassLabSceneBuilder.Build). 저장 확인 창 없이 만든다.</summary>
    public static void Build()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            AssetDatabase.DeleteAsset(ScenePath);
        if (!AssetDatabase.CopyAsset(SourceScenePath, ScenePath))
        {
            Debug.LogError($"[DashPassLab] {SourceScenePath} 복사 실패");
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // 허수아비(간파 표적) 제거.
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.GetComponentInChildren<TrainingDummy>(true) != null)
                Object.DestroyImmediate(root);
        }

        // 랩 오브젝트 — 배속·쿨 0·캐릭터 순환만 남기고 배치기를 붙인다.
        GameObject lab = scene.GetRootGameObjects().FirstOrDefault(r => r.GetComponent<DebugTimeScale>() != null);
        if (lab == null)
        {
            lab = new GameObject();
            SceneManager.MoveGameObjectToScene(lab, scene);
            lab.AddComponent<DebugTimeScale>();
            lab.AddComponent<DebugZeroCooldown>();
        }

        lab.name = "DashPassLab";
        foreach (MonoBehaviour behaviour in lab.GetComponents<MonoBehaviour>())
        {
            if (behaviour != null && !KeepOnLab.Contains(behaviour.GetType()))
                Object.DestroyImmediate(behaviour);
        }

        DebugDashPassLab spawner = lab.AddComponent<DebugDashPassLab>();
        spawner.SetEntries(BuildEntries());

        BuildLanes(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"[DashPassLab] {ScenePath} 생성 — 레인 A~E, 몬스터 {spawner.Entries.Count(e => e.enabled)}줄");
    }

    private static IEnumerable<DebugDashPassLab.Entry> BuildEntries()
    {
        GameObject normal = Load(NormalMonsterPath);
        GameObject midBoss = Load(MidBossPath);
        GameObject boss = Load(BossPath);

        // A: 일반 3마리 일렬 — 몸 지름 1m, 틈 0.3m. 2마리 관통 후 3번째 직전 정지(2번째와 겹친 채).
        yield return Entry("A", normal, LaneA, 4.0f);
        yield return Entry("A", normal, LaneA, 5.3f);
        yield return Entry("A", normal, LaneA, 6.6f);
        // B: 중간보스 — 처음부터 막힘.
        yield return Entry("B", midBoss, LaneB, 5.5f);
        // C: 일반 1 + 뒤 벽(z 6.5 앞면) — 몬스터를 지나 벽 앞 정지, 대시 상태는 시간 끝까지.
        yield return Entry("C", normal, LaneC, 4.0f);
        // D: 일반 2 바짝 붙임 — 대시 끝(z≈7)이 2번째 몸 안이라 겹친 채 끝난다 → 밀어내기.
        yield return Entry("D", normal, LaneD, 6.0f);
        yield return Entry("D", normal, LaneD, 7.0f);
        // E: 23호(보스) — 처음부터 막힘. 단독 스폰이 무거우면 인스펙터에서 끈다.
        yield return Entry("E", boss, LaneE, 8.5f);
    }

    private static DebugDashPassLab.Entry Entry(string lane, GameObject prefab, float x, float z) =>
        new DebugDashPassLab.Entry
        {
            lane = lane,
            prefab = prefab,
            position = new Vector3(x, 0f, z),
            yaw = Facing,
            enabled = prefab != null,
        };

    private static GameObject Load(string path)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
            Debug.LogError($"[DashPassLab] 프리팹 없음: {path}");
        return prefab;
    }

    private static void BuildLanes(Scene scene)
    {
        var root = new GameObject("Lanes (START 칸에서 +Z 대시)");
        SceneManager.MoveGameObjectToScene(root, scene);

        Lane(root.transform, "A", LaneA, "A: 3 normal -> pass 2, stop at 3rd");
        Lane(root.transform, "B", LaneB, "B: mid-boss -> blocked");
        Lane(root.transform, "C", LaneC, "C: 1 normal + wall -> stop at wall");
        Lane(root.transform, "D", LaneD, "D: 2 normal touching -> end inside");
        Lane(root.transform, "E", LaneE, "E: boss 23 -> blocked");

        // 레인 C 의 벽 — Wall 레이어(플레이어 장애물 마스크), 앞면 z = 6.5.
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "C_Wall";
        wall.transform.SetParent(root.transform, false);
        wall.transform.position = new Vector3(LaneC, 1.5f, 6.75f);
        wall.transform.localScale = new Vector3(3f, 3f, 0.5f);
        int wallLayer = LayerMask.NameToLayer("Wall");
        if (wallLayer >= 0)
            wall.layer = wallLayer;
        else
            Debug.LogError("[DashPassLab] Wall 레이어가 없다 — 벽이 Default 레이어로 남는다.");
    }

    private static void Lane(Transform parent, string id, float x, string text)
    {
        var lane = new GameObject($"Lane {id}");
        lane.transform.SetParent(parent, false);
        lane.transform.position = new Vector3(x, 0f, 0f);

        // START 칸 — 콜라이더 없는 얇은 판(발 밑 표시).
        GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
        pad.name = $"{id}_START";
        Object.DestroyImmediate(pad.GetComponent<Collider>());
        pad.transform.SetParent(lane.transform, false);
        pad.transform.position = new Vector3(x, 0.01f, StartZ);
        pad.transform.localScale = new Vector3(1.6f, 0.02f, 0.6f);

        Label(lane.transform, $"{id}_Label", new Vector3(x, 0.03f, StartZ - 1.2f), text, 0.18f);
        Label(lane.transform, $"{id}_StartText", new Vector3(x, 0.03f, StartZ), $"START {id}  (dash +Z)", 0.12f);
    }

    private static void Label(Transform parent, string name, Vector3 position, string text, float size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // 바닥에 눕힘(탑다운 카메라에서 읽힘)

        var mesh = go.AddComponent<TextMesh>();
        mesh.text = text;
        mesh.characterSize = size;
        mesh.fontSize = 48;
        mesh.anchor = TextAnchor.MiddleCenter;
        mesh.alignment = TextAlignment.Center;
        mesh.color = Color.yellow;
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
        {
            mesh.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }
    }
}
