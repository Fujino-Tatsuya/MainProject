using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 저작 도구 — 23호 + 웰즈의 애니메이터 컨트롤러를 **전면 재작성**하고 No23.asset 을 채운다 (2026-08-07).
//
// 배경: 보스 코드는 재작성이 끝났는데 애니메이터는 레거시 BT 시대 것이 그대로 남아 있었다.
// 23호 컨트롤러는 상태 18 · 전이 45 · 파라미터 8 이었고, 그중 **새 코드가 쓰는 것은 0개**였다.
// 전이 45개는 전부 `State`/`Jump` Int 조건인데 그 파라미터를 세팅하던 것은 레거시 BT 액션뿐이다
// (`Assets/1.Scripts/BT/…`). 지금은 무해하지만 누군가 그 Int 를 건드리는 순간 36개가 깨어나
// CrossFade 제어와 싸운다. 그래서 기존 것을 고치지 않고 **새로 만들고 지운다**(팀장 확정).
//
// ─────────────────────────────────────────────────────────────────────────────
// 설계 — 무엇이 애니를 모는가
//
//   23호 : 공격·잡기·점프·카운터리액션 = **CrossFade**(상태명, SO 저작).      전이 0
//          로코모션                     = **Speed(Float) BlendTree**.        전이 0
//          그로기·사망                  = **파라미터 + AnyState 전이**.       ← 유일한 전이
//            → base 의 `SafeSetBool(groggyBool)`·`SafeSetTrigger(deathTrigger)` 경로가
//              그대로 살아난다. **코드·SO 스키마 수정 0줄.**
//   웰즈 : 전부 트리거(23호와 규약이 다르다 — BossWells.cs §10.2). Throw 만 hasExitTime 자동복귀.
//
// 왜 그로기만 전이인가: `MonsterState.Groggy` 는 base 가 소유한 상태라 보스가 CrossFade 로
// 가로챌 지점이 없다. 파라미터로 받으면 enter→ing(loop)→end 3단 체인을 애니메이터가 알아서 돈다.
//
// ─────────────────────────────────────────────────────────────────────────────
// 🔴 값은 전부 실물에서 뽑았다. 문서에서 베끼지 않았다(교훈 #63).
//    상태·전이·파라미터 : .controller 직접 파싱
//    클립 대응          : 상태 m_Motion fileID ↔ fbx.meta internalID 대조
//    클립 구간·루프     : fbx.meta 의 firstFrame/lastFrame/loopTime
//
// 그 대조에서 확정된 것:
//   · `Boss_23_jumping` 은 체공 애니가 아니라 **landingattack take 의 0..1 프레임(2프레임 정지 포즈)**.
//     GDD 가 "최상단에서 mesh 를 끈다"고 했으므로 체공 중엔 어차피 안 보인다 — 그래서 이게 맞다.
//   · `Boss_23_.groggy`(0..90) 와 `.groggy_enter`(0..89) 는 **같은 구간의 중복 슬라이스**다.
//     팀장 확인 결과 `.groggy` 가 "그로기 시작" → 이쪽을 쓰고 `_enter` 는 버린다.
//   · `.groggy_ing` 만 loop=1 이다(88..105). 유지 루프가 맞다.
//
// ⚠️ 아직 **미배치 클립 2개**: `Boss_23_getowned02` · `Boss_23_grabend`.
//    용도 확인 대기(팀장). 확인되면 이 파일의 표에 한 줄씩 추가하면 된다.
//
// 🔴 fbx 임포트 쪽에 남은 문제 2건 — 여기서 못 고친다(SVN, 후속):
//    `Boss_23_idle`(loop=0) · `Boss_23_charging`(loop=0). 로코모션과 차징은 오래 유지되는데
//    루프가 꺼져 있어 한 바퀴 돌고 마지막 프레임에서 멈춘다. 실행 시 경고로 다시 알린다.
public static class TwentyThreeBossAuthoring
{
    // 새로 만들 것
    const string No23ControllerPath  = "Assets/4.Animations/Wells&No.23/No.23/Controller/No23Controller.controller";
    const string WellsControllerPath = "Assets/4.Animations/Wells&No.23/Wells/Controller/WellsBossController.controller";

    // 지울 것 (레거시 BT 시대)
    const string LegacyNo23Path  = "Assets/4.Animations/Wells&No.23/No.23/Controller/TwentyThreeController.controller";
    const string LegacyWellsPath = "Assets/4.Animations/Wells&No.23/Wells/Controller/WellsController.controller";

    const string No23FbxPath   = "Assets/50.Art/Char/Boss/SK/SK_23.fbx";
    const string WellsFbxPath  = "Assets/50.Art/Char/Boss/SK/SK_welz.fbx";
    const string No23Prefab    = "Assets/2.Prefabs/Wells&No.23/TwentyThree.prefab";
    const string WellsPrefab   = "Assets/2.Prefabs/Wells&No.23/Wells.prefab";
    const string BossDataPath  = "Assets/2.Prefabs/Monster/Data/No23.asset";

    // 로코모션 BlendTree — `_animSpeed` 는 `agent.velocity.magnitude` **원값(m/s)** 이다
    // (MonsterBase.cs:189). 정규화 값이 아니므로 임계값도 m/s 로 잡는다. moveSpeed 2.5 기준.
    const string SpeedParam = "Speed";
    const float  WalkSpeedThreshold = 2.5f;

    // 그로기·사망만 파라미터로 몬다. 이름은 MonsterDataSO 기본값과 맞춰 둔다.
    const string GroggyParam = "Groggy";  // Bool  — base 가 Groggy 상태에서 true
    const string DeathParam  = "Death";   // Trigger

    const string LocomotionState = "Locomotion";

    // ── 23호 상태표 (상태명, 클립명) ──────────────────────────────────────
    // 공격 8종은 아래 AttackStates 와 짝이 맞아야 한다.
    static readonly (string State, string Clip)[] No23States =
    {
        ("LeftHook",    "Boss_23_hookL"        ),
        ("RightHook",   "Boss_23_hookR"        ),
        ("Uppercut",    "Boss_23_uppercut"     ),
        ("Grab",        "Boss_23_grab"         ),
        ("Leap",        "Boss_23_jump"         ), // 도약(44..158)
        ("DashAttack",  "Boss_23_dash"         ),
        ("Charging",    "Boss_23_charging"     ),
        ("Rage",        "Boss_23_dash"         ), // 레이지 돌진 — 돌진과 같은 클립
        ("Holding",     "Boss_23_grabshock"    ), // 잡기 유지(전기)
        ("Throw",       "Boss_23_grabdump"     ), // 던지기
        ("JumpHover",   "Boss_23_jumping"      ), // 체공(2프레임 정지 — 이때 mesh off)
        ("JumpLanding", "Boss_23_landingattack"), // 하강+착지
        ("getowned",    "Boss_23_getowned01"   ), // 카운터 성공 리액션
        ("GroggyStart", "Boss_23_.groggy"      ), // 시작 (⚠️ _enter 는 중복이라 안 쓴다)
        ("Groggy",      "Boss_23_.groggy_ing"  ), // 유지 루프
        ("GroggyEnd",   "Boss_23_.groggy_end"  ), // 종료
        ("Dead",        "Boss_23_.die"         ),
    };

    // 공격 행 → 상태명. ⚠️ enum 이름과 상태 이름이 다른 것이 4건.
    // 🔴 배열이다 — 없는 행을 **이 순서 그대로** 뒤에 붙인다(행 순서 = 쿨다운 슬롯 번호).
    static readonly (BossAttackId Id, string State)[] AttackStates =
    {
        (BossAttackId.LeftHook,       "LeftHook"  ),
        (BossAttackId.RightHook,      "RightHook" ),
        (BossAttackId.Upper,          "Uppercut"  ), // ⚠️ Upper ≠ Uppercut
        (BossAttackId.Grab,           "Grab"      ),
        (BossAttackId.Jump,           "Leap"      ), // ⚠️ Jump ≠ Leap
        (BossAttackId.Dash,           "DashAttack"), // ⚠️ Dash ≠ DashAttack
        (BossAttackId.ChargeSequence, "Charging"  ),
        (BossAttackId.RageDash,       "Rage"      ),
    };

    // ── 웰즈 ──────────────────────────────────────────────────────────────
    // BossWells 는 전부 트리거로 몬다. 상태 4개(Jump 는 BossWellsState 에서 빠졌다).
    static readonly (string State, string Clip)[] WellsStates =
    {
        ("Idle",   "Boss_welz_idle"    ),
        ("Throw",  "Boss_welz_throwing"),
        ("Groggy", "Boss_welz_groggy"  ),
        ("Die",    "Boss_welz_die"     ),
    };

    // BossWells.cs 의 [SerializeField] 기본값과 일치해야 한다.
    const string WellsThrowTrigger  = "IsThrow";
    const string WellsGroggyTrigger = "IsGroggy";
    const string WellsDeadTrigger   = "IsDead";
    const string WellsInitTrigger   = "IsInit";

    // 루프가 켜져 있어야 하는데 꺼져 있는 클립 — 실행 시 경고한다(고치려면 fbx 임포트 = SVN).
    static readonly string[] MustLoopClips = { "Boss_23_idle", "Boss_23_charging" };

    [MenuItem("Tools/Boss/23호 — 컨트롤러 전면 재작성 + 데이터 저작")]
    public static void Rebuild()
    {
        bool go = EditorUtility.DisplayDialog(
            "23호 보스 — 전면 재작성",
            "새 컨트롤러 2개를 만들고, 프리팹 Animator 를 갈아끼우고, 레거시 컨트롤러 2개를 삭제한다.\n\n" +
            "🔴 손으로 고친 내용이 있으면 사라진다(매번 처음부터 다시 만든다).\n\n" +
            "진행할까?",
            "재작성", "취소");
        if (!go) return;

        var log = new StringBuilder("[23호] 전면 재작성\n");
        bool ok = true;

        AnimatorController no23  = BuildNo23Controller(log, ref ok);
        AnimatorController wells = BuildWellsController(log, ref ok);

        if (ok)
        {
            // 🔴 순서 중요 — 웰즈가 **먼저**다. Wells.prefab 은 TwentyThree.prefab 안에 중첩돼 있어서,
            //    원본을 먼저 고쳐야 중첩 인스턴스가 새 컨트롤러를 상속받는다.
            RepointPrefab(log, WellsPrefab, wells, ref ok);
            RepointPrefab(log, No23Prefab,  no23,  ref ok);
            ok &= AuthorBossData(log);
            DeleteLegacy(log);
        }
        else
        {
            log.AppendLine("🔴 컨트롤러 생성에 실패해서 프리팹 교체·레거시 삭제를 건너뛴다(참조가 끊기면 더 나쁘다).");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        WarnAboutClipLoops(log);

        log.AppendLine(ok ? "\n✅ 완료. 다음은 프리팹 조립(CONTEXT 3단계)." : "\n🔴 실패 항목 있음 — 위 로그 확인.");
        if (ok) Debug.Log(log.ToString()); else Debug.LogError(log.ToString());
    }

    // ── 23호 컨트롤러 ─────────────────────────────────────────────────────
    static AnimatorController BuildNo23Controller(StringBuilder log, ref bool ok)
    {
        log.AppendLine("\n── 23호 컨트롤러 ──");

        Dictionary<string, AnimationClip> clips = LoadClips(No23FbxPath, log, ref ok);
        if (clips == null) return null;

        AnimatorController c = Recreate(No23ControllerPath, log);
        c.AddParameter(SpeedParam,  AnimatorControllerParameterType.Float);
        c.AddParameter(GroggyParam, AnimatorControllerParameterType.Bool);
        c.AddParameter(DeathParam,  AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine sm = c.layers[0].stateMachine;

        // 로코모션을 **가장 먼저** 만든다 — 첫 상태가 기본 상태가 되고, 보스는 여기서 시작한다.
        AnimatorState locomotion = c.CreateBlendTreeInController(LocomotionState, out BlendTree tree, 0);
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = SpeedParam;
        tree.useAutomaticThresholds = false; // 🔴 AddChild 전에 꺼야 임계값이 안 덮인다
        if (TryClip(clips, "Boss_23_idle", log, ref ok, out AnimationClip idle)) tree.AddChild(idle, 0f);
        if (TryClip(clips, "Boss_23_walk", log, ref ok, out AnimationClip walk)) tree.AddChild(walk, WalkSpeedThreshold);
        sm.defaultState = locomotion;
        log.AppendLine($"  + {LocomotionState} (BlendTree: idle@0 / walk@{WalkSpeedThreshold})");

        var made = new Dictionary<string, AnimatorState>();
        float y = 80f;
        foreach ((string stateName, string clipName) in No23States)
        {
            if (!TryClip(clips, clipName, log, ref ok, out AnimationClip clip)) continue;
            AnimatorState st = sm.AddState(stateName, new Vector3(320f, y, 0f));
            st.motion = clip;
            made[stateName] = st;
            y += 55f;
            log.AppendLine($"  + {stateName} ← {clipName}");
        }

        // 🔴 전이는 그로기·사망 5개뿐이다. 나머지는 전부 CrossFade 가 몬다.
        //    AnyState 전이에 canTransitionToSelf=false 를 반드시 준다 — 안 주면 조건이 참인 동안
        //    매 프레임 자기 자신으로 재진입해 애니가 첫 프레임에 갇힌다.
        if (made.TryGetValue("Dead", out AnimatorState dead))
        {
            AnimatorStateTransition t = sm.AddAnyStateTransition(dead);
            t.hasExitTime = false; t.duration = 0.05f; t.canTransitionToSelf = false;
            t.AddCondition(AnimatorConditionMode.If, 0f, DeathParam);
            log.AppendLine("  → AnyState ⇒ Dead (Death 트리거)");
        }

        if (made.TryGetValue("GroggyStart", out AnimatorState gStart) &&
            made.TryGetValue("Groggy",      out AnimatorState gLoop)  &&
            made.TryGetValue("GroggyEnd",   out AnimatorState gEnd))
        {
            AnimatorStateTransition t1 = sm.AddAnyStateTransition(gStart);
            t1.hasExitTime = false; t1.duration = 0.1f; t1.canTransitionToSelf = false;
            t1.AddCondition(AnimatorConditionMode.If, 0f, GroggyParam);

            // 시작 클립이 끝나면 유지 루프로. 조건 없이 exitTime 으로만 넘어간다.
            AnimatorStateTransition t2 = gStart.AddTransition(gLoop);
            t2.hasExitTime = true; t2.exitTime = 0.95f; t2.duration = 0.1f;

            // base 가 Groggy 를 풀면(bool false) 종료 클립으로.
            AnimatorStateTransition t3 = gLoop.AddTransition(gEnd);
            t3.hasExitTime = false; t3.duration = 0.1f;
            t3.AddCondition(AnimatorConditionMode.IfNot, 0f, GroggyParam);

            AnimatorStateTransition t4 = gEnd.AddTransition(locomotion);
            t4.hasExitTime = true; t4.exitTime = 0.9f; t4.duration = 0.15f;

            log.AppendLine("  → AnyState ⇒ GroggyStart ⇒ Groggy ⇒(!Groggy) GroggyEnd ⇒ Locomotion");
        }

        EditorUtility.SetDirty(c);
        return c;
    }

    // ── 웰즈 컨트롤러 ─────────────────────────────────────────────────────
    static AnimatorController BuildWellsController(StringBuilder log, ref bool ok)
    {
        log.AppendLine("\n── 웰즈 컨트롤러 ──");

        Dictionary<string, AnimationClip> clips = LoadClips(WellsFbxPath, log, ref ok);
        if (clips == null) return null;

        AnimatorController c = Recreate(WellsControllerPath, log);
        foreach (string p in new[] { WellsThrowTrigger, WellsGroggyTrigger, WellsDeadTrigger, WellsInitTrigger })
            c.AddParameter(p, AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine sm = c.layers[0].stateMachine;
        var made = new Dictionary<string, AnimatorState>();
        float y = 0f;
        foreach ((string stateName, string clipName) in WellsStates)
        {
            if (!TryClip(clips, clipName, log, ref ok, out AnimationClip clip)) continue;
            AnimatorState st = sm.AddState(stateName, new Vector3(300f, y, 0f));
            st.motion = clip;
            made[stateName] = st;
            y += 60f;
            log.AppendLine($"  + {stateName} ← {clipName}");
        }

        if (!made.TryGetValue("Idle", out AnimatorState idle)) return c;
        sm.defaultState = idle;

        if (made.TryGetValue("Throw", out AnimatorState throwSt))
        {
            AnimatorStateTransition t = idle.AddTransition(throwSt);
            t.hasExitTime = false; t.duration = 0.1f;
            t.AddCondition(AnimatorConditionMode.If, 0f, WellsThrowTrigger);

            // 🔴 23호와 반대로 Throw 는 **스스로 Idle 로 돌아온다**(BossWellsState 주석).
            AnimatorStateTransition back = throwSt.AddTransition(idle);
            back.hasExitTime = true; back.exitTime = 0.9f; back.duration = 0.1f;
            log.AppendLine("  → Idle ⇒(IsThrow) Throw ⇒(exitTime) Idle");
        }

        if (made.TryGetValue("Groggy", out AnimatorState groggy))
        {
            AnimatorStateTransition t = sm.AddAnyStateTransition(groggy);
            t.hasExitTime = false; t.duration = 0.1f; t.canTransitionToSelf = false;
            t.AddCondition(AnimatorConditionMode.If, 0f, WellsGroggyTrigger);

            AnimatorStateTransition back = groggy.AddTransition(idle);
            back.hasExitTime = false; back.duration = 0.1f;
            back.AddCondition(AnimatorConditionMode.If, 0f, WellsInitTrigger);
            log.AppendLine("  → AnyState ⇒(IsGroggy) Groggy ⇒(IsInit) Idle");
        }

        if (made.TryGetValue("Die", out AnimatorState die))
        {
            AnimatorStateTransition t = sm.AddAnyStateTransition(die);
            t.hasExitTime = false; t.duration = 0.05f; t.canTransitionToSelf = false;
            t.AddCondition(AnimatorConditionMode.If, 0f, WellsDeadTrigger);
            log.AppendLine("  → AnyState ⇒ Die (IsDead)");
        }

        EditorUtility.SetDirty(c);
        return c;
    }

    // ── 프리팹 Animator 교체 ──────────────────────────────────────────────
    static void RepointPrefab(StringBuilder log, string prefabPath, AnimatorController controller, ref bool ok)
    {
        if (controller == null) { ok = false; return; }

        // 🔴 프리팹 애셋은 `LoadAssetAtPath` 로 얻어 직접 고치면 안 된다 — 저장이 보장되지 않는다.
        //    LoadPrefabContents 로 사본을 열고 SaveAsPrefabAsset 으로 되쓰는 것이 정식 경로다.
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null)
        {
            log.AppendLine($"🔴 프리팹을 열 수 없다: {prefabPath}");
            ok = false;
            return;
        }

        try
        {
            Animator[] animators = root.GetComponentsInChildren<Animator>(true);
            if (animators.Length == 0)
            {
                log.AppendLine($"🔴 {prefabPath} 에 Animator 가 없다");
                ok = false;
                return;
            }

            // 🔴 **중첩 프리팹 함정.** TwentyThree.prefab 안에 Wells.prefab 이 들어 있어서
            //    GetComponentsInChildren 는 웰즈 Animator 까지 집어 온다. 전부 갈아끼우면 웰즈가
            //    23호 컨트롤러를 쓰게 되고, 중첩 인스턴스에 불필요한 오버라이드까지 남는다.
            //
            // ⚠️ 이 판별로 **두 번 실패했다**. 안 되는 것부터 적어 둔다:
            //    · `IsPartOfPrefabInstance`        → 프리팹 애셋 내부의 **모든** 오브젝트가 true
            //    · `GetNearestPrefabInstanceRoot`  → 자기 소유 Animator 에도 루트와 다른 값이 나왔다
            //    두 번 다 전부 걸러져서 프리팹이 손도 안 닿은 채 레거시만 삭제됐다(참조 끊김).
            //
            // 이번 근거는 API 추측이 아니라 **직렬화 필드 실측**이다. 프리팹 YAML 을 열어 보니
            // 자기 소유 Animator 는 `m_CorrespondingSourceObject: {fileID: 0}` 이고 중첩된 것만
            // 소스를 갖는다. 그 필드를 그대로 읽는 API 가 GetCorrespondingObjectFromSource 다.
            // 그래도 못 믿으므로 아래에 진단 출력과 폴백을 함께 둔다.
            int changed = 0, skipped = 0;
            var candidates = new List<Animator>();

            log.AppendLine($"\n  [진단] {System.IO.Path.GetFileName(prefabPath)} — Animator {animators.Length}개");
            foreach (Animator a in animators)
            {
                Object src = PrefabUtility.GetCorrespondingObjectFromSource(a);
                bool nested = src != null;
                int depth = Depth(a.transform);
                log.AppendLine($"    · {Path(a.transform)}  depth={depth}  source={(nested ? AssetDatabase.GetAssetPath(src) : "(없음=자기소유)")}");
                if (!nested) candidates.Add(a);
            }

            // 폴백 — 판별이 아무것도 못 고르면 **루트에 가장 가까운** Animator 를 쓴다.
            // 23호 것은 모델 루트에 있고 중첩 웰즈 것은 그 아래 깊이 있다.
            if (candidates.Count == 0)
            {
                Animator shallowest = animators.OrderBy(a => Depth(a.transform)).First();
                candidates.Add(shallowest);
                log.AppendLine($"    ⚠️ 판별 실패 → 폴백: 최상위 Animator({Path(shallowest.transform)}) 사용");
            }

            foreach (Animator a in animators)
            {
                if (!candidates.Contains(a)) { skipped++; continue; }
                a.runtimeAnimatorController = controller;
                changed++;
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            log.AppendLine($"\n  ↻ {System.IO.Path.GetFileName(prefabPath)} — Animator {changed}개 → {controller.name}" +
                           (skipped > 0 ? $" (중첩 {skipped}개 건너뜀)" : ""));
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        // 🔴 호출이 성공했다는 것과 파일이 바뀌었다는 것은 별개다(교훈 #61).
        //    디스크에서 되읽어 실제 참조를 확인한다.
        var saved = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Animator[] check = saved != null ? saved.GetComponentsInChildren<Animator>(true) : System.Array.Empty<Animator>();
        if (!check.Any(a => a.runtimeAnimatorController == controller))
        {
            log.AppendLine($"🔴 되읽기 검증 실패 — {System.IO.Path.GetFileName(prefabPath)} 가 {controller.name} 를 가리키지 않는다");
            ok = false;
        }
    }

    // ── No23.asset ────────────────────────────────────────────────────────
    static bool AuthorBossData(StringBuilder log)
    {
        log.AppendLine("\n── No23.asset ──");

        var so = AssetDatabase.LoadAssetAtPath<BossDataSO>(BossDataPath);
        if (so == null)
        {
            log.AppendLine($"🔴 애셋을 못 찾았다: {BossDataPath}");
            return false;
        }

        // 🔴 코드의 초기화자는 **새로 만드는 애셋**에만 적용된다(교훈 #22/#55).
        //    이미 저장된 이 애셋은 여기서 명시적으로 덮어써야 바뀐다.
        Set(log, "archetype", so.archetype, MonsterArchetype.Boss, v => so.archetype = v);

        // 애니 계약 — 새 컨트롤러에 맞춘다.
        Set(log, "locomotionState", so.locomotionState, LocomotionState, v => so.locomotionState = v);
        Set(log, "animSpeedParam",  so.animSpeedParam,  SpeedParam,      v => so.animSpeedParam  = v);
        Set(log, "groggyBool",      so.groggyBool,      GroggyParam,     v => so.groggyBool      = v);
        Set(log, "deathTrigger",    so.deathTrigger,    DeathParam,      v => so.deathTrigger    = v);
        // 이 둘은 비운다 — 보스의 공격/피격 애니는 전부 CrossFade 경로다(파라미터가 아예 없다).
        Set(log, "attackTrigger",   so.attackTrigger,   "",              v => so.attackTrigger   = v);
        Set(log, "hitTrigger",      so.hitTrigger,      "",              v => so.hitTrigger      = v);

        // 확정 스펙(PLAN §5). attackDuration 은 **건드리지 않는다** —
        // 보스가 `_stateTimer = 체인길이 + attackDuration` 으로 이미 더하고 있고
        // (TwentyThreeBoss.cs:456), 단순 공격에서는 그 자체가 공격 길이다(MonsterBase.cs:560).
        Set(log, "maxGroggyCount", so.maxGroggyCount, 5,  v => so.maxGroggyCount = v);
        Set(log, "groggyDuration", so.groggyDuration, 2f, v => so.groggyDuration = v);

        Set(log, "hitReactionState", so.hitReactionState, "getowned",    v => so.hitReactionState = v);
        Set(log, "grabHoldState",    so.grabHoldState,    "Holding",     v => so.grabHoldState    = v);
        Set(log, "grabThrowState",   so.grabThrowState,   "Throw",       v => so.grabThrowState   = v);
        Set(log, "jumpHoverState",   so.jumpHoverState,   "JumpHover",   v => so.jumpHoverState   = v);
        Set(log, "jumpLandingState", so.jumpLandingState, "JumpLanding", v => so.jumpLandingState = v);

        bool ok = AuthorAttackTable(log, so);
        EditorUtility.SetDirty(so);
        return ok;
    }

    // 공격 테이블 — ⚠️ **행 순서 = 쿨다운 슬롯 번호**다. 재정렬·중간삽입 금지, 끝에만 추가한다.
    // 그래서 인덱스가 아니라 attackId 로 찾아 상태명만 채우고, 없는 행은 뒤에 붙인다.
    // 튜닝값(쿨다운·거리창·가중치)은 이미 저작돼 있으므로 건드리지 않는다.
    static bool AuthorAttackTable(StringBuilder log, BossDataSO so)
    {
        log.AppendLine("\n── 공격 테이블 ──");

        List<BossAttackEntry> rows = (so.attacks ?? System.Array.Empty<BossAttackEntry>()).ToList();
        bool ok = true;

        foreach ((BossAttackId id, string stateName) in AttackStates)
        {
            BossAttackEntry row = rows.FirstOrDefault(r => r != null && r.attackId == id);
            if (row == null)
            {
                // 페이즈 시퀀스 전용 2행. weight 0 이라 룰렛이 절대 뽑지 않고 페이즈가 직접 트리거한다.
                log.AppendLine($"  + [{rows.Count}] {id} 행 추가 (weight 0) → {stateName}");
                rows.Add(new BossAttackEntry
                {
                    attackId             = id,
                    animatorStateName    = stateName,
                    cooldown             = 0f,
                    ignoreDistanceWindow = true,
                    weight               = 0f,
                    superArmor           = true,
                });
                continue;
            }

            int slot = rows.IndexOf(row);
            if (row.animatorStateName == stateName)
            {
                log.AppendLine($"  = [{slot}] {id} — 이미 {stateName}");
                continue;
            }

            log.AppendLine($"  ~ [{slot}] {id} — {Show(row.animatorStateName)} → \"{stateName}\"");
            row.animatorStateName = stateName;
        }

        so.attacks = rows.ToArray();

        // 저작 후에도 빈 상태명이 남으면 스폰 시 LogError 가 난다 — 여기서 미리 잡는다.
        foreach (BossAttackEntry r in rows.Where(r => r != null && string.IsNullOrEmpty(r.animatorStateName)))
        {
            log.AppendLine($"🔴 {r.attackId} 행의 animatorStateName 이 비어 있다 — 매핑 표에 없는 값이다.");
            ok = false;
        }

        return ok;
    }

    // ── 레거시 삭제 ───────────────────────────────────────────────────────
    // 프리팹 교체가 끝난 뒤에만 부른다. 순서가 뒤집히면 프리팹이 missing 참조를 갖게 된다.
    static void DeleteLegacy(StringBuilder log)
    {
        log.AppendLine("\n── 레거시 삭제 ──");
        foreach (string p in new[] { LegacyNo23Path, LegacyWellsPath })
        {
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(p) == null)
            {
                log.AppendLine($"  = {System.IO.Path.GetFileName(p)} — 이미 없음");
                continue;
            }
            log.AppendLine(AssetDatabase.DeleteAsset(p)
                ? $"  - {System.IO.Path.GetFileName(p)} 삭제"
                : $"🔴 삭제 실패: {p}");
        }
        log.AppendLine("  ⚠️ Stone_Golem 테스트 애셋 2개의 참조가 끊긴다 — 무의미하다고 확정(팀장).");
    }

    // ── 헬퍼 ──────────────────────────────────────────────────────────────

    // 전면 재작성이므로 매번 지우고 새로 만든다. 손으로 고친 것은 남지 않는다(다이얼로그에서 경고함).
    // ⚠️ 재생성이라 **매 실행마다 GUID 가 바뀐다** → 프리팹 2개도 함께 diff 에 뜬다.
    //    깨지지는 않는다(같은 실행 안에서 프리팹을 새 GUID 로 다시 가리키므로). 깔끔한 애셋을
    //    택하고 git 노이즈를 감수한 것 — 어차피 이 컨트롤러들은 아직 커밋된 적 없는 신규 파일이다.
    static AnimatorController Recreate(string path, StringBuilder log)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(path) != null)
        {
            AssetDatabase.DeleteAsset(path);
            log.AppendLine($"  (기존 {System.IO.Path.GetFileName(path)} 삭제 후 재생성)");
        }
        return AnimatorController.CreateAnimatorControllerAtPath(path);
    }

    // 클립은 전부 fbx 서브에셋이다. 별도 .anim 파일은 존재하지 않는다.
    static Dictionary<string, AnimationClip> LoadClips(string fbxPath, StringBuilder log, ref bool ok)
    {
        Dictionary<string, AnimationClip> clips = AssetDatabase
            .LoadAllAssetsAtPath(fbxPath)
            .OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__"))
            .GroupBy(c => c.name)
            .ToDictionary(g => g.Key, g => g.First());

        if (clips.Count != 0) return clips;

        log.AppendLine($"🔴 fbx 에서 클립을 못 읽었다: {fbxPath}");
        ok = false;
        return null;
    }

    static bool TryClip(Dictionary<string, AnimationClip> clips, string name,
                        StringBuilder log, ref bool ok, out AnimationClip clip)
    {
        if (clips.TryGetValue(name, out clip)) return true;
        log.AppendLine($"🔴 클립 없음: {name} — fbx 임포트 설정을 볼 것");
        ok = false;
        return false;
    }

    // 루프가 꺼져 있으면 오래 유지되는 상태에서 마지막 프레임에 굳는다. 여기서는 못 고친다(fbx=SVN).
    static void WarnAboutClipLoops(StringBuilder log)
    {
        Dictionary<string, AnimationClip> clips = AssetDatabase
            .LoadAllAssetsAtPath(No23FbxPath).OfType<AnimationClip>()
            .GroupBy(c => c.name).ToDictionary(g => g.Key, g => g.First());

        foreach (string name in MustLoopClips)
            if (clips.TryGetValue(name, out AnimationClip c) && !c.isLooping)
                log.AppendLine($"⚠️ {name} 의 Loop Time 이 꺼져 있다 — 한 바퀴 뒤 마지막 프레임에서 멈춘다. " +
                               "fbx 임포트에서 켜야 하고 이는 SVN 커밋 대상이다.");
    }

    static int Depth(Transform t)
    {
        int d = 0;
        for (Transform p = t.parent; p != null; p = p.parent) d++;
        return d;
    }

    static string Path(Transform t)
    {
        string s = t.name;
        for (Transform p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
        return s;
    }

    // 값이 실제로 달라질 때만 로그를 남긴다 — 안 바뀐 항목까지 찍으면 진짜 변경이 묻힌다(교훈 #8).
    static void Set<T>(StringBuilder log, string field, T current, T target, System.Action<T> apply)
    {
        if (EqualityComparer<T>.Default.Equals(current, target))
        {
            log.AppendLine($"  = {field} — 이미 {Show(target)}");
            return;
        }
        log.AppendLine($"  ~ {field}: {Show(current)} → {Show(target)}");
        apply(target);
    }

    static string Show<T>(T v) => v is string s ? (string.IsNullOrEmpty(s) ? "(비움)" : $"\"{s}\"") : v.ToString();
}
