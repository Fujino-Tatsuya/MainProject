using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// 저작 도구 — 23호 fbx 의 **클립 분할 + 애니메이션 이벤트 + Loop Time** 을 통째로 소유한다 (2026-08-10, 2026-09-15 확장).
//
// 배경: 보스 코드는 재작성이 끝났는데 클립 이벤트는 레거시 BT 시대 이름이 그대로 남아 있었다.
// 실측 결과 `SK_23.fbx` 의 이벤트는 5개이고 **전부 구 이름**이다:
//   SetTargetEvent(jump) · FallEvent/OnLandedEvent(landingattack) · TryGrabEvent(grab) · ThrowEvent(grabdump)
// 새 릴레이(`MonsterAnimationEventRelay`)가 받는 이름은 `OnAttackHit`/`OnAttackEnd`/`OnAttackCommit` 뿐이라
// **교집합이 0** 이었다. 그리고 히트는 **타이머 폴백이 없다**(`MonsterBase.HandleAttack`) —
// 즉 이 도구를 돌리기 전까지 보스의 훅·어퍼·잡기·돌진·착지는 **데미지를 한 발도 내지 못한다.**
//
// 🔴 왜 손으로 .meta 를 고치지 않고 도구로 만들었나
//    `Assets/50.Art` 는 **SVN** 이다. 아티스트가 fbx 를 다시 올리면 임포터 설정이 초기화되면서
//    이벤트가 통째로 날아간다. 그때 이 메뉴를 다시 누르면 복구된다(멱등).
//    → **2026-09-15 에 실제로 일어났다.** r299 로 1.7배 신규 fbx 가 올라왔고 `.meta` 는 기본값이었다.
//
// 🔴 2026-09-15 확장 — 이 도구가 **클립 목록 자체를 소유**한다.
//    구판은 이벤트/Loop 만 손댔고, 클립 분할(`.groggy_enter/_ing/_end`, `jumping`)과 트림
//    (`jump 44~158`, `landingattack 8~123`, `dash 17~102`, `.groggy 0~90`)은 **손으로 저작돼 있었다.**
//    그래서 fbx 가 갈리는 순간 통째로 사라졌다. 손 저작으로 남은 상태가 있으면 이 도구는
//    "멱등"이 아니라 "절반만 멱등"이다 — 아래 `Desired` 표가 이제 전부를 적는다.
//
// 🔴 시간 단위 = **정규화 0~1** (클립 길이 대비 비율). 초가 아니다.
//    근거: 39프레임 클립의 ThrowEvent 가 0.932 인데, 초라면 클립 길이를 넘어선다.
//
// ⚠️ 히트 타이밍은 **추정값**이다. 훅·어퍼·돌진에는 원래 이벤트가 없어서 기준으로 삼을 것이 없었다.
//    아래 표의 `Hit` 은 눈으로 보고 맞춰야 한다 — Animation 창에서 실제 타격 프레임을 확인할 것.
//    (grab·landingattack 은 기존 이벤트의 시간을 **그대로 승계**했다. 그건 아티스트가 잡아 둔 값이다.)
public static class No23ClipEventAuthoring
{
    // 🔴 2026-09-15 — 아트가 SVN r299 로 **1.7배 신규 FBX** 를 올리면서 대상이 바뀌었다.
    //    구 모델(`SK/SK_23.fbx`)은 아직 저장소에 남아 있지만 프리팹·컨트롤러는 신규를 본다.
    //    구 경로로 돌리고 싶으면 아래 한 줄만 바꾼다.
    const string FbxPath = "Assets/50.Art/Char/Boss/23_action_01_RiderSlot_x1_7.fbx";
    const string LegacyFbxPath = "Assets/50.Art/Char/Boss/SK/SK_23.fbx";

    // 🔴 신규 fbx(r299)는 **머티리얼이 익스포트돼 있지 않다.** 구 fbx 는 `Material.001` 슬롯이 있어
    //    `.meta` 의 externalObjects 로 리맵했는데, 신규는 슬롯 자체가 없어 리맵할 대상이 없다
    //    (임포트 산출물의 Material 서브애셋 0개 — 「덤프」로 확인 가능). 그대로 두면 **회색 무지**로 나온다.
    //    → 프리팹의 SkinnedMeshRenderer 에 직접 물린다. 아래 메뉴가 그 일을 한다(멱등).
    // 🔴 **툰을 쓴다**(2026-09-15 팀장 확정). 아트가 준 `texture/Boss_23_base.mat` 은 URP/**Lit** 이라
    //    팔에 PBR 빛 연산이 그대로 보였다. 팔라딘(플레이어)이 이미 쓰는 것과 같은 `ToonLit` 계열로 맞춘다.
    //
    // 🔴 **툰 머티리얼은 git(`3.Materials/Toon/`)에 두고 SVN 텍스처를 참조한다.** 반대로 하지 말 것 —
    //    `50.Art` 는 SVN 이라 아트가 덮어쓰면 셰이딩 설정이 통째로 날아가고, 우리가 고치면 SVN 커밋이 된다.
    //    (`No23_Toon.mat` 의 `_BaseMap` 은 구판 `lagacy/Boss_23_basecolor.png` 를 가리키고 있어
    //     현재판 `Boss23_BaseColor_4K.png` 로 돌려놨다. 안 고치고 갈아끼우면 텍스처가 퇴행한다.)
    const string MaterialPath = "Assets/3.Materials/Toon/No23_Toon.mat";
    const string LegacyLitMaterialPath = "Assets/50.Art/Char/Boss/texture/Boss_23_base.mat";
    static readonly string[] BossPrefabs =
    {
        "Assets/2.Prefabs/Monster/Boss/TwentyThree.prefab",
        "Assets/2.Prefabs/Monster/Boss/TwentyThree_Solo.prefab",
    };

    const string HitEvent = "OnAttackHit";
    const string EndEvent = "OnAttackEnd";

    struct ClipSpec
    {
        public string Take;      // null 이면 클립 이름과 같은 테이크
        // 🔴 nullable 이다. `const int TakeDefault = -1` 로 뒀더니 struct 필드 기본값이 -1 이 아니라
        //    **0** 이라, 트림을 안 적은 클립이 전부 `0~0`(길이 0) 이 됐다. 「검증만」이 잡아냈다.
        //    0 은 `.groggy_enter` 처럼 **실제로 쓰는 값**이라 센티널로 쓸 수 없다.
        public int? First;       // null = 테이크 기본값
        public int? Last;
        public bool Loop;
        public float Hit;        // 음수면 이벤트를 두지 않는다
        public float End;
        public string Why;
    }

    // 🔴 이 표가 **fbx 클립의 전부**다. 여기 없는 테이크는 클립으로 만들어지지 않는다.
    //    프레임 구간은 2026-09-15 신규 fbx 의 실측 테이크 길이와 대조해 넣었다(전부 구판과 동일했다).
    static readonly (string Name, ClipSpec Spec)[] Desired =
    {
        // ── 로코모션 ─────────────────────────────────────────────
        ("Boss_23_idle",     new ClipSpec { Loop = true,  Hit = -1f, End = -1f, Why = "로코모션 정지 — 안 켜면 한 바퀴 뒤 마지막 프레임에서 굳는다" }),
        ("Boss_23_walk",     new ClipSpec { Loop = true,  Hit = -1f, End = -1f, Why = "로코모션 이동" }),

        // ── 근접 단타 3종. 원래 이벤트가 없어 Hit 은 추정값이다(눈으로 맞출 것). ──
        ("Boss_23_hookL",    new ClipSpec { Hit = 0.40f, End = 0.85f, Why = "좌훅 — 추정값" }),
        ("Boss_23_hookR",    new ClipSpec { Hit = 0.40f, End = 0.85f, Why = "우훅 — 추정값" }),
        ("Boss_23_uppercut", new ClipSpec { Hit = 0.40f, End = 0.85f, Why = "어퍼 — 추정값" }),

        // ── 기존 이벤트의 시간을 승계한다(아티스트가 잡아 둔 값). 이름만 바꾼다. ──
        ("Boss_23_grab",          new ClipSpec { Hit = 0.3544601f,  End = -1f,  Why = "TryGrabEvent 시간 승계. End 는 두지 않는다 — 잡기 체인이 자기 종료를 소유한다" }),
        ("Boss_23_landingattack", new ClipSpec { First = 8, Last = 123, Hit = 0.20552148f, End = 0.90f, Why = "OnLandedEvent 시간 승계. 앞 8프레임은 체공 꼬리라 잘라 둔 것" }),

        // ── 돌진. Hit = 돌진 시작 시점이라 앞쪽이어야 한다(추정값). ──
        // ⚠️ 이 클립은 DashAttack 과 Rage 상태가 **공유**한다. Rage 로 재생될 때도 이벤트가 뜬다.
        ("Boss_23_dash",     new ClipSpec { First = 17, Last = 102, Loop = true, Hit = 0.15f, End = -1f, Why = "돌진 시작 — 추정값. End 는 두지 않는다(체인이 소유). 테이크는 158까지 있으나 17~102 만 쓴다" }),

        // ── 이벤트를 두지 않는 클립. 구 이벤트를 지우기 위해 명시적으로 올린다. ──
        ("Boss_23_jump",     new ClipSpec { First = 44, Last = 158, Hit = -1f, End = -1f, Why = "체공은 타이머(ArriveJump)가 몬다 — SetTargetEvent 는 수신자가 없어 제거. 앞 44프레임은 도약 준비라 잘라 둔 것" }),
        ("Boss_23_grabdump", new ClipSpec { Hit = -1f, End = -1f, Why = "던지기는 타이머(ReleaseGrabThrow)가 몬다 — ThrowEvent 는 수신자가 없어 제거" }),

        ("Boss_23_charging", new ClipSpec { Loop = true, Hit = -1f, End = -1f, Why = "차징 최대 20초 — 안 켜면 굳는다" }),
        ("Boss_23_grabshock",new ClipSpec { Hit = -1f, End = -1f, Why = "잡기 피격 리액션 — Holding 상태" }),
        ("Boss_23_grabend",  new ClipSpec { Hit = -1f, End = -1f, Why = "잡기 종료. 현재 컨트롤러는 안 쓰지만 클립은 남긴다" }),
        ("Boss_23_.die",     new ClipSpec { Hit = -1f, End = -1f, Why = "사망" }),

        // ── 🔴 분할 클립. 하나의 `.groggy` 테이크(0~158)를 셋으로 자른다. ──
        //    컨트롤러가 GroggyStart / Groggy / GroggyEnd 세 상태로 쓴다.
        ("Boss_23_.groggy",       new ClipSpec { Take = "Boss_23_.groggy", First = 0,   Last = 90,  Hit = -1f, End = -1f, Why = "GroggyStart — 진입 구간" }),
        ("Boss_23_.groggy_enter", new ClipSpec { Take = "Boss_23_.groggy", First = 0,   Last = 89,  Hit = -1f, End = -1f, Why = "현재 컨트롤러는 안 쓴다(GroggyStart 가 위를 쓴다). 구판 저작 승계" }),
        ("Boss_23_.groggy_ing",   new ClipSpec { Take = "Boss_23_.groggy", First = 88,  Last = 105, Loop = true, Hit = -1f, End = -1f, Why = "Groggy 유지 — 루프. 안 켜면 그로기가 한 바퀴 뒤 굳는다" }),
        ("Boss_23_.groggy_end",   new ClipSpec { Take = "Boss_23_.groggy", First = 105, Last = 158, Hit = -1f, End = -1f, Why = "GroggyEnd — 기상" }),

        // 🔴 `jumping` 은 `landingattack` 테이크의 **첫 2프레임 정지 포즈**다(체공 유지용).
        ("Boss_23_jumping",  new ClipSpec { Take = "Boss_23_landingattack", First = 0, Last = 1, Hit = -1f, End = -1f, Why = "JumpHover — 체공 중 유지할 정지 포즈. 착지공격 테이크의 첫 프레임을 쓴다" }),

        // ── 2026-09-15 신규 fbx 에서 이름이 바뀐 것 / 새로 들어온 것 ──
        // 🔴 구 `getowned01/02` 가 `getowned_L/_R` 로 개명됐다. 컨트롤러의 `getowned` 상태는 _L 을 쓴다.
        ("Boss_23_getowned_L", new ClipSpec { Hit = -1f, End = -1f, Why = "피격 리액션 좌. 구 getowned01" }),
        ("Boss_23_getowned_R", new ClipSpec { Hit = -1f, End = -1f, Why = "피격 리액션 우. 구 getowned02. 컨트롤러 미사용 — 좌/우 분기는 구조 변경 후보" }),
        // ⚠️ 아래 둘은 r299 신규 테이크다. **용도가 확인되지 않았다** — FSM 에 대응 상태가 없다.
        //    클립으로는 뽑아 두되 컨트롤러에는 안 물린다. 아트 확인 후 결정할 것.
        ("Boss_23_magneticgrab", new ClipSpec { Hit = -1f, End = -1f, Why = "r299 신규 — 용도 미확인. 미사용" }),
        ("Boss_23_dash.001",     new ClipSpec { Hit = -1f, End = -1f, Why = "r299 신규 — dash 변형으로 보이나 미확인. 미사용" }),
    };

    // 🔴 저작 전에 **이걸 먼저 누른다.** 아트가 재익스포트하면 테이크 길이가 바뀌는데,
    //    분할 클립(`.groggy_*`, `jumping`)의 프레임 구간을 옛 값에서 베끼면 조용히 엉뚱한 구간이 된다.
    //    이 메뉴는 fbx 가 실제로 들고 있는 테이크와 구간을 그대로 찍는다.
    [MenuItem("Tools/Boss/23호 — fbx 테이크 덤프 (읽기만)")]
    public static void Dump()
    {
        var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"[23호 클립] {FbxPath} 를 ModelImporter 로 열 수 없다.");
            return;
        }

        var log = new StringBuilder();
        log.AppendLine($"[23호 클립] 테이크 덤프 — {FbxPath}");

        ModelImporterClipAnimation[] def = importer.defaultClipAnimations;
        log.AppendLine($"  ── defaultClipAnimations (fbx 가 들고 있는 테이크) : {def.Length}개");
        foreach (ModelImporterClipAnimation c in def)
            log.AppendLine($"     {c.name,-26} take={c.takeName,-26} {c.firstFrame,6:0} ~ {c.lastFrame,6:0}");

        // 🔴 머티리얼 슬롯. fbx 가 갈리면 externalObjects 리맵도 함께 날아간다 —
        //    2026-09-15 신규 fbx 는 리맵이 비어 있어 보스가 **회색 무지**로 나왔다.
        //    (`ModelImporter.sourceMaterials` 는 이 버전에 없다 — 임포트 산출물에서 직접 센다.)
        Object[] subs = AssetDatabase.LoadAllAssetsAtPath(FbxPath);
        var mats = subs.OfType<Material>().ToArray();
        log.AppendLine($"  ── 머티리얼 슬롯 : {mats.Length}개");
        foreach (Material m in mats) log.AppendLine($"     {m.name}");

        var remap = importer.GetExternalObjectMap();
        log.AppendLine($"  ── externalObjects 리맵 : {remap.Count}개");
        foreach (KeyValuePair<AssetImporter.SourceAssetIdentifier, Object> kv in remap)
            log.AppendLine($"     {kv.Key.type.Name}:{kv.Key.name} -> {(kv.Value == null ? "(null)" : AssetDatabase.GetAssetPath(kv.Value))}");

        ModelImporterClipAnimation[] cur = importer.clipAnimations;
        log.AppendLine($"  ── clipAnimations (현재 저작된 것) : {(cur == null ? 0 : cur.Length)}개");
        if (cur != null)
            foreach (ModelImporterClipAnimation c in cur)
            {
                string evts = (c.events == null || c.events.Length == 0)
                    ? "(이벤트 없음)"
                    : string.Join(", ", c.events.Select(e => $"{e.functionName}@{e.time:0.###}"));
                log.AppendLine($"     {c.name,-26} take={c.takeName,-26} {c.firstFrame,6:0} ~ {c.lastFrame,6:0}  loop={(c.loopTime ? 1 : 0)}  {evts}");
            }

        Debug.Log(log.ToString());
    }

    // fbx 가 갈리면 프리팹의 머티리얼 참조도 함께 끊긴다. 이 메뉴로 되돌린다.
    [MenuItem("Tools/Boss/23호 — 프리팹 머티리얼 재연결 (검증만)")]
    public static void ValidateMaterial() => RunMaterial(apply: false);

    [MenuItem("Tools/Boss/23호 — 프리팹 머티리얼 재연결 (적용)")]
    public static void ApplyMaterial() => RunMaterial(apply: true);

    static void RunMaterial(bool apply)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
        {
            Debug.LogError($"[23호 머티리얼] {MaterialPath} 를 찾을 수 없다 — SVN 업데이트가 안 됐는지 확인할 것.");
            return;
        }

        var log = new StringBuilder();
        log.AppendLine($"[23호 머티리얼] {(apply ? "적용" : "검증만")} — {MaterialPath}");

        foreach (string prefabPath in BossPrefabs)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                Debug.LogError($"[23호 머티리얼] {prefabPath} 를 열 수 없다.");
                continue;
            }

            try
            {
                // 🔴 **`GetComponentsInChildren` 를 그대로 쓰면 안 된다.** `TwentyThree.prefab` 안에는
                //    23호 위에 탄 **웰즈(`body_female.005`)** 가 같이 들어 있다. 2026-09-15 에 실제로
                //    웰즈 머티리얼 6개(welz_head/frontH/top/backH/protectionglass/face)를 통째로
                //    Boss_23_base 로 덮어썼다 — 교훈 #74 #75 「유닛 전체를 훑는 코드가 남의 것까지 먹는다」.
                //
                // 🔴 판별은 **이름이 아니라 메시의 출처 fbx** 로 한다. 노드 이름은 이미 한 번 바뀌었다
                //    (`tripo_part_0` → `Boss_23`). 이름으로 거르면 다음 재익스포트에 또 깨진다.
                var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(r => r.sharedMesh != null &&
                                AssetDatabase.GetAssetPath(r.sharedMesh) == FbxPath)
                    .ToArray();
                if (renderers.Length == 0)
                {
                    Debug.LogError($"[23호 머티리얼] {prefabPath} 에 {FbxPath} 메시를 쓰는 SkinnedMeshRenderer 가 없다 — 모델이 안 물렸다.");
                    continue;
                }

                bool dirty = false;
                foreach (SkinnedMeshRenderer r in renderers)
                {
                    Material[] mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] == mat) continue;
                        log.AppendLine($"  ▶ {prefabPath.Split('/').Last()} / {r.name} [{i}] : " +
                                       $"{(mats[i] == null ? "(없음)" : mats[i].name)} -> {mat.name}");
                        mats[i] = mat;
                        dirty = true;
                    }
                    if (dirty && apply) r.sharedMaterials = mats;
                }

                if (!dirty) { log.AppendLine($"  = {prefabPath.Split('/').Last()} 이미 물려 있다(멱등)."); continue; }
                if (apply) PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        if (!apply) log.AppendLine("  → 검증만이라 아무것도 쓰지 않았다.");
        Debug.Log(log.ToString());
    }

    [MenuItem("Tools/Boss/23호 — 클립 이벤트 저작 (검증만)")]
    public static void Validate() => Run(apply: false);

    // ⚠️ 모달 확인창을 두지 않는다 — 이 메뉴는 MCP(에이전트)로도 실행되는데, 모달이 뜨면
    //    누를 사람이 없어 에디터가 통째로 멈춘다. 안전장치는 위의 **「검증만」 메뉴**다:
    //    무엇이 바뀌는지 먼저 읽고 누를 것. 되돌리기는 `svn revert` 로 된다.
    [MenuItem("Tools/Boss/23호 — 클립 이벤트 저작 (적용)")]
    public static void Apply() => Run(apply: true);

    static void Run(bool apply)
    {
        var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"[23호 클립] {FbxPath} 를 ModelImporter 로 열 수 없다 — 경로를 확인할 것.");
            return;
        }

        // 테이크의 **실제** 구간. 표의 프레임을 여기에 대조한다 — 옛 값을 그대로 믿지 않는다.
        var takes = new Dictionary<string, ModelImporterClipAnimation>();
        foreach (ModelImporterClipAnimation c in importer.defaultClipAnimations)
            takes[c.takeName] = c;

        if (takes.Count == 0)
        {
            Debug.LogError("[23호 클립] fbx 에 테이크가 하나도 없다 — 애니메이션이 없는 fbx 를 가리키고 있다.");
            return;
        }

        var log = new StringBuilder();
        log.AppendLine($"[23호 클립] {(apply ? "적용" : "검증만")} — {FbxPath}");
        log.AppendLine($"  테이크 {takes.Count}개 → 목표 클립 {Desired.Length}개");

        // ── 표를 실제 클립 배열로 전개 ────────────────────────────
        var wanted = new List<ModelImporterClipAnimation>();
        bool fatal = false;

        foreach ((string name, ClipSpec spec) in Desired)
        {
            string takeName = spec.Take ?? name;
            if (!takes.TryGetValue(takeName, out ModelImporterClipAnimation take))
            {
                // 🔴 조용히 지나가면 안 된다 — 테이크 이름이 바뀐 것이다(2026-09-15 getowned 사례).
                Debug.LogError($"[23호 클립] 표의 \"{name}\" 이 요구하는 테이크 \"{takeName}\" 이 fbx 에 없다 — 이름이 바뀌었다. 표를 갱신할 것.");
                fatal = true;
                continue;
            }

            float first = spec.First ?? take.firstFrame;
            float last = spec.Last ?? take.lastFrame;

            // 재익스포트로 테이크가 짧아졌는데 옛 구간을 그대로 쓰면 조용히 엉뚱한 클립이 된다.
            if (first < take.firstFrame || last > take.lastFrame || first >= last)
            {
                Debug.LogError(
                    $"[23호 클립] \"{name}\" 의 구간 {first:0}~{last:0} 이 테이크 \"{takeName}\" " +
                    $"({take.firstFrame:0}~{take.lastFrame:0}) 을 벗어난다 — 재익스포트로 길이가 바뀌었다. 표를 갱신할 것.");
                fatal = true;
                continue;
            }

            var clip = new ModelImporterClipAnimation
            {
                name = name,
                takeName = takeName,
                firstFrame = first,
                lastFrame = last,
                loopTime = spec.Loop,
                wrapMode = WrapMode.Default,
                maskType = ClipAnimationMaskType.None,
                keepOriginalPositionY = true,
            };

            var events = new List<AnimationEvent>();
            if (spec.Hit >= 0f) events.Add(new AnimationEvent { time = spec.Hit, functionName = HitEvent });
            if (spec.End >= 0f) events.Add(new AnimationEvent { time = spec.End, functionName = EndEvent });
            clip.events = events.ToArray();

            wanted.Add(clip);
        }

        // fbx 에는 있는데 표에 없는 테이크 = 새로 들어온 것이다. 경고로 남긴다(치명적이진 않다).
        foreach (string t in takes.Keys.Where(t => !Desired.Any(d => (d.Spec.Take ?? d.Name) == t)))
            Debug.LogWarning($"[23호 클립] fbx 의 테이크 \"{t}\" 가 표에 없다 — 클립으로 만들어지지 않는다. 의도한 것인지 확인할 것.");

        if (fatal)
        {
            Debug.LogError("[23호 클립] 위 오류 때문에 아무것도 쓰지 않았다. 표를 먼저 고칠 것.");
            return;
        }

        // ── 현재 상태와 비교 ──────────────────────────────────────
        ModelImporterClipAnimation[] have = importer.clipAnimations ?? new ModelImporterClipAnimation[0];
        var haveByName = have.ToDictionary(c => c.name, c => c);

        int changed = 0;
        foreach (ModelImporterClipAnimation w in wanted)
        {
            if (!haveByName.TryGetValue(w.name, out ModelImporterClipAnimation h))
            {
                changed++;
                log.AppendLine($"  + {w.name,-26} 신규  take={w.takeName} {w.firstFrame:0}~{w.lastFrame:0} loop={(w.loopTime ? 1 : 0)} {Describe(w.events)}");
                continue;
            }

            var diffs = new List<string>();
            if (h.takeName != w.takeName) diffs.Add($"take {h.takeName}→{w.takeName}");
            if (!Mathf.Approximately(h.firstFrame, w.firstFrame) || !Mathf.Approximately(h.lastFrame, w.lastFrame))
                diffs.Add($"구간 {h.firstFrame:0}~{h.lastFrame:0}→{w.firstFrame:0}~{w.lastFrame:0}");
            if (h.loopTime != w.loopTime) diffs.Add($"loop {(h.loopTime ? 1 : 0)}→{(w.loopTime ? 1 : 0)}");
            if (!SameEvents(h.events, w.events)) diffs.Add($"이벤트 {Describe(h.events)}→{Describe(w.events)}");

            if (diffs.Count == 0) continue;
            changed++;
            log.AppendLine($"  ▶ {w.name,-26} {string.Join(" · ", diffs)}");
        }

        foreach (string gone in haveByName.Keys.Where(n => !wanted.Any(w => w.name == n)))
        {
            changed++;
            log.AppendLine($"  - {gone,-26} 제거 (표에 없다)");
        }

        if (changed == 0)
        {
            log.AppendLine("  → 바꿀 것이 없다(멱등).");
            Debug.Log(log.ToString());
            return;
        }

        if (!apply)
        {
            log.AppendLine($"  → 검증만이라 아무것도 쓰지 않았다. 바뀔 항목 {changed}개.");
            Debug.Log(log.ToString());
            return;
        }

        // 🔴 clipAnimations 는 **복사본**을 돌려준다. 반드시 다시 대입해야 한다.
        importer.clipAnimations = wanted.ToArray();
        importer.SaveAndReimport();
        log.AppendLine($"  → 재임포트 완료. 항목 {changed}개 변경. 🔴 SVN 커밋은 팀장이 직접 할 것({FbxPath}.meta).");
        Debug.Log(log.ToString());
    }

    static string Describe(AnimationEvent[] events)
        => (events == null || events.Length == 0)
            ? "(없음)"
            : string.Join(",", events.Select(e => $"{e.functionName}@{e.time:0.###}"));

    static bool SameEvents(AnimationEvent[] a, AnimationEvent[] b)
    {
        a ??= new AnimationEvent[0];
        b ??= new AnimationEvent[0];
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].functionName != b[i].functionName) return false;
            if (Mathf.Abs(a[i].time - b[i].time) > 0.0001f) return false;
        }
        return true;
    }
}
