using UnityEditor;
using UnityEngine;

// 저작 도구 — 잡기 연출이 붙지 않는 문제의 **한시적 우회** (PLAN 「열린 결정 (가)」, 2026-08-10).
//
// ── 증상 ─────────────────────────────────────────────────────────────────────
// 보스가 플레이어를 잡기는 하는데 **손에 붙어 따라오지 않는다.**
//
// ── 원인 (실측) ──────────────────────────────────────────────────────────────
// `PlayerStateController.cs:760` 이 시전자에서 **레거시 구체 타입** `GrabController` 를 찾아
// `GrabSocket` 을 가져온다(`RestraintMode.Carry` 주석에도 그 종속이 명시돼 있다).
// 그런데 `GrabController` 는 신형 보스에서 **제거된 레거시**라 부착 0곳 → `followTarget = null` →
// 따라가기 자체가 성립하지 않는다.
//
// ── 우회 방식과 그 대가 ──────────────────────────────────────────────────────
// 팀장 확정: 은희 님의 인터페이스 작업(요청 문서 기준 4줄)을 기다리지 않고 **지금 살린다.**
// 즉 `GrabController` 를 보스 프리팹에 다시 붙이고 `grabSocket` 만 채운다.
//
// 🔴 **컴포넌트를 `enabled = false` 로 둔다.** 이유가 있다 —
//   · `Start()` 는 `bt == null` 이면 `[No.23] BehaviorTree is null.` **LogError** 를 낸다.
//     그건 내가 교훈 #70 으로 기록한 "부착 0곳 컴포넌트의 거짓 에러 로그"를 되살리는 짓이다.
//   · `Update()` 는 `Start` 가 조기 반환해 **초기화되지 않은 블랙보드 변수** `CurrentState.Value` 를
//     매 프레임 읽는다 → 서버에서 NRE 가 쏟아진다.
//   · 비활성 컴포넌트는 `Start`·`Update` 가 돌지 않지만, **`GetComponentInChildren<T>()` 는 그대로
//     찾아낸다**(컴포넌트의 enabled 는 조회에 영향이 없다). 그래서 소켓 게터만 살아남는다.
//
// ⚠️ 이것은 **한시적 조치**다. 은희 님 인터페이스가 들어오면 이 컴포넌트를 걷어내고
//    `Enemy/Boss/` 레거시 폴더 삭제를 마무리한다. 그때까지 `GrabController.cs` 는 지울 수 없다.
//
// ── 소켓 위치 ────────────────────────────────────────────────────────────────
// 플레이어는 소켓에 **부모로 붙지 않는다** — `followTarget.position/.rotation` 을 매 틱 따라간다
// (`PlayerStateController.cs:811`). 그래서 리그 100배 스케일이 위치에 영향을 주지 않는다.
// 다만 나중에 오프셋을 미터로 생각할 수 있도록 `localScale = 0.01`(lossyScale = 1)로 맞춰 둔다.
public static class BossGrabSocketAuthoring
{
    static readonly string[] TargetPrefabs =
    {
        "Assets/2.Prefabs/Monster/Boss/TwentyThree.prefab",
        "Assets/2.Prefabs/Monster/Boss/TwentyThree_Solo.prefab",
    };

    // 잡기 앵커가 `Hand_R`(SO 의 hitboxAnchorName)이므로 소켓도 오른손에 둔다.
    const string HandBoneName = "hand.r";
    const string SocketName = "GrabSocket";

    [MenuItem("Tools/Boss/잡기 소켓 — 레거시 GrabController 한시 부착 (비활성)")]
    public static void Author()
    {
        foreach (string path in TargetPrefabs) AuthorOne(path);
        AssetDatabase.SaveAssets();
        Verify();
    }

    static void AuthorOne(string path)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null) { Debug.LogWarning($"[잡기소켓] {path} 없음 — 건너뛴다."); return; }

        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        try
        {
            // 🔴 잘못 만들어진 소켓을 먼저 걷어낸다 — 초판이 Wells 의 손에 붙였다(아래 FindBossHand 주석).
            foreach (Transform stray in contents.GetComponentsInChildren<Transform>(true))
            {
                if (stray == null || stray.name != SocketName) continue;
                if (!IsUnderWells(stray, contents.transform)) continue;
                Debug.LogWarning($"[잡기소켓] {asset.name}: Wells 손에 붙어 있던 잘못된 '{SocketName}' 을 제거한다.");
                Object.DestroyImmediate(stray.gameObject);
            }

            Transform hand = FindBossHand(contents.transform);
            if (hand == null)
            {
                Debug.LogError($"[잡기소켓] {asset.name}: 보스 자신의 '{HandBoneName}' 을 못 찾았다 — 중단.");
                return;
            }

            Transform socket = hand.Find(SocketName);
            if (socket == null)
            {
                var go = new GameObject(SocketName);
                socket = go.transform;
                socket.SetParent(hand, false);
                Debug.Log($"[잡기소켓] {asset.name}: '{HandBoneName}/{SocketName}' 신규.");
            }
            socket.localPosition = Vector3.zero;
            socket.localRotation = Quaternion.identity;
            socket.localScale = Vector3.one * 0.01f; // lossyScale = 1 (리그 100배 상쇄)

            // 🔴 플레이어는 소켓의 **회전까지** 복사한다(`followTarget.rotation`). 소켓이 손 본의 자식이라
            //    그대로 두면 잡힌 플레이어가 **누운 채로** 매달린다(Play 에서 관찰).
            //    손 본은 애니메이션으로 매 프레임 돌기 때문에 정적 로컬 회전으로는 못 고친다 →
            //    LateUpdate 로 자세를 세우는 컴포넌트를 붙인다.
            if (socket.GetComponent<BossGrabSocketUpright>() == null)
            {
                socket.gameObject.AddComponent<BossGrabSocketUpright>();
                Debug.Log($"[잡기소켓] {asset.name}: BossGrabSocketUpright 부착 — 잡힌 플레이어가 서 있게 된다.");
            }

            var grab = contents.GetComponentInChildren<GrabController>(true);
            if (grab == null)
            {
                grab = contents.AddComponent<GrabController>();
                Debug.Log($"[잡기소켓] {asset.name}: GrabController 부착(한시적 우회).");
            }

            var so = new SerializedObject(grab);
            so.FindProperty("grabSocket").objectReferenceValue = socket;
            so.ApplyModifiedPropertiesWithoutUndo();

            // 🔴 레거시 로직이 돌지 않게 반드시 끈다. 이유는 파일 상단 주석.
            if (grab.enabled)
            {
                grab.enabled = false;
                Debug.Log($"[잡기소켓] {asset.name}: GrabController.enabled = false " +
                          "(Start 의 LogError · Update 의 NRE 차단. 소켓 게터만 남는다).");
            }

            PrefabUtility.SaveAsPrefabAsset(contents, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    [MenuItem("Tools/Boss/잡기 소켓 — 검증 (읽기 전용)")]
    public static void Verify()
    {
        var sb = new System.Text.StringBuilder("[잡기소켓] 검증\n");
        foreach (string path in TargetPrefabs)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) { sb.AppendLine($"  – {path} 없음"); continue; }

            var grab = asset.GetComponentInChildren<GrabController>(true);
            Transform socket = grab != null
                ? new SerializedObject(grab).FindProperty("grabSocket").objectReferenceValue as Transform
                : null;

            sb.AppendLine($"  {asset.name}");
            sb.AppendLine($"    {M(grab != null)} GrabController 부착 · {M(grab != null && !grab.enabled)} enabled=false");
            sb.AppendLine($"    {M(socket != null)} grabSocket = {(socket != null ? GetPath(socket, asset.transform) : "(비어 있음)")}");
            if (socket != null)
            {
                sb.AppendLine($"    부모 = {socket.parent?.name} · localScale = {socket.localScale.x:0.###}");
                sb.AppendLine($"    {M(!IsUnderWells(socket, asset.transform))} Wells 하위가 아님(보스 자신의 손이어야 한다)");
                sb.AppendLine($"    {M(socket.GetComponent<BossGrabSocketUpright>() != null)} BossGrabSocketUpright (잡힌 플레이어 자세 세우기)");
            }

            // 🔴 같은 함정이 히트박스 앵커에도 있는지 함께 본다 — 앵커가 Wells 손에 붙어 있으면
            //    훅·어퍼·잡기 판정이 전부 엉뚱한 위치에서 난다.
            foreach (ColliderInfo ci in asset.GetComponentsInChildren<ColliderInfo>(true))
            {
                bool bad = IsUnderWells(ci.transform, asset.transform);
                sb.AppendLine($"    {M(!bad)} 앵커 '{ci.name}' — {GetPath(ci.transform, asset.transform)}");
            }
        }
        Debug.Log(sb.ToString());
    }

    // 🔴 이름만으로 깊이우선 탐색하면 **Wells 의 손**을 먼저 만난다.
    //    Wells 는 23호 리그의 `c_root_master.x` 밑에 중첩돼 있고, 자기 리그에 같은 이름의 `hand.r` 을
    //    갖고 있다. 초판이 그래서 플레이어를 Wells 손에 붙게 만들었다(검증 출력이 잡아냈다).
    //    → 후보를 전부 모은 뒤 **조상에 BossWells 가 없는 것**만 고른다.
    static Transform FindBossHand(Transform root)
    {
        Transform fallback = null;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == null || t.name != HandBoneName) continue;
            if (IsUnderWells(t, root)) { fallback = fallback ?? t; continue; }
            return t;
        }

        if (fallback != null)
            Debug.LogError("[잡기소켓] 보스 자신의 손을 못 찾고 Wells 의 손만 있다 — 리그 구조를 확인할 것.");
        return null;
    }

    // t 의 조상 사슬(root 까지)에 BossWells 가 있으면 true.
    static bool IsUnderWells(Transform t, Transform root)
    {
        for (Transform p = t; p != null && p != root.parent; p = p.parent)
            if (p.GetComponent<BossWells>() != null) return true;
        return false;
    }

    static string GetPath(Transform t, Transform root)
    {
        string p = t.name;
        while (t.parent != null && t.parent != root) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }

    static string M(bool ok) => ok ? "✓" : "✗";
}
