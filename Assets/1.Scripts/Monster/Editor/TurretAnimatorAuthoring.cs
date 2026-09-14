using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 고정 터렛(PeekABot·TeslaBot)의 애니메이터를 <b>`Idle` + `Shoot` 둘만</b> 있는 컨트롤러로 교체한다.
/// 팀장 확정(2026-09-10).
///
/// 🔴 <b>왜 갈아야 하는가</b> — 아트 팩의 컨트롤러에 <b>우리 코드가 빠져나올 수 없는 상태</b>가 남아 있었다.
///    그 상태로 들어가면 3단 신축 컬럼이 중간에 걸려 <b>머리와 베이스가 떨어져 보인다</b>("될려다 마는" 포즈).
///
///    <c>Controller_PeekABot</c> : <c>Idle → Hide</c>(<c>Hide</c> 트리거) 와 <c>Hide → Raise</c>(<c>Raise</c> 트리거).
///        <c>Hide</c> 는 <c>Reveal</c> 을 <b>speed -1 로 역재생</b>한다 = 컬럼을 접는다.
///    <c>Controller_TeslaBot</c> : <c>Idle → Charge</c>(<c>Charge</c> 트리거) 뒤 <c>Charge → Shoot</c> 가
///        <b><c>Attack</c> 트리거를 요구</b>하는데 우리 코드는 <c>Attack</c> 을 <b>절대 세팅하지 않는다</b>
///        (데이터의 <c>attackTrigger</c> 가 <c>Charge</c> 뿐이었다) → <c>Charge</c> 에 <b>갇힌다</b>.
///
///    즉 두 종의 증상 모양은 같고 원인 상태만 다르다. <b>안 쓰는 상태를 남겨 두면 조용히 진입한다</b>
///    (#90 의 거울상 — 그쪽은 안 지나가는 경로, 이쪽은 안 쓰는 경로).
///
/// 만드는 것 (두 종 동일):
///   파라미터  <c>Shoot</c> (Trigger) <b>하나</b>
///   상태      <c>Idle</c>(A_Idle.fbx 의 <c>Idle</c>, 루프) · <c>Shoot</c>(A_Shoot.fbx 의 <c>Shoot</c>)
///   전이      <c>Idle → Shoot</c>(exitTime 없음, <c>Shoot</c> 조건) · <c>Shoot → Idle</c>(exitTime)
///
/// 데이터도 같이 맞춘다 — 컨트롤러에 없는 이름을 남기면 <c>MonsterBase</c> 가 <b>조용히 no-op</b> 하고
/// 배선 감사가 영구히 ❌ 를 낸다:
///   <c>attackTrigger</c> → <c>"Shoot"</c> (TeslaBot 은 <c>"Charge"</c> 였다)
///   <c>hitTrigger</c> → <b>비움</b> (피격 피드백은 색 변경으로 처리한다 — 팀장 확정)
///   <c>animSpeedParam</c> → <b>비움</b> (고정 터렛은 이동하지 않는다)
///   <c>locomotionState</c> 는 이미 <c>"Idle"</c> 이라 그대로 둔다.
///   <c>deathTrigger</c> 는 이미 비어 있다 — 사망 연출은 <c>DissolveDeath</c> 가 맡는다.
///
/// ⚠️ <b>1회용 마이그레이션 도구다.</b> 두 컨트롤러가 커밋되고 Play 검증이 끝나면 이 파일을 지워도 된다
///    (멱등하므로 남겨 둬도 무해하지만, 다 쓴 저작 도구는 지우는 것이 이 레포의 규칙이다).
/// </summary>
public static class TurretAnimatorAuthoring
{
    const string OutFolder = "Assets/2.Prefabs/Monster/Controllers";
    const string PackRoot = "Assets/50.Art/Char/Monster/Robot Sentries - 3D Character Mega Pack";

    sealed class Turret
    {
        public string Name;          // 몬스터 프리팹 이름
        public string ClipFolder;    // 팩 안의 애니메이션 폴더
    }

    static readonly Turret[] Targets =
    {
        new Turret { Name = "PeekABot", ClipFolder = $"{PackRoot}/Robot Sentries Pack 01/Animations/PeekABot" },
        new Turret { Name = "TeslaBot", ClipFolder = $"{PackRoot}/Robot Sentries Pack 02/Animations/TeslaBot" },
    };

    [MenuItem("Tools/Boss/터렛 애니메이터 — Idle+Shoot 컨트롤러 생성·배선 (검증)")]
    public static void Validate() => Run(dryRun: true);

    [MenuItem("Tools/Boss/터렛 애니메이터 — Idle+Shoot 컨트롤러 생성·배선 (적용)")]
    public static void Apply() => Run(dryRun: false);

    static void Run(bool dryRun)
    {
        var log = new StringBuilder($"[TurretAnim/{(dryRun ? "검증" : "적용")}] Idle+Shoot 컨트롤러\n");
        int done = 0, failed = 0;

        foreach (Turret t in Targets)
        {
            string prefabPath = $"Assets/2.Prefabs/Monster/{t.Name}.prefab";
            string ctrlPath = $"{OutFolder}/Controller_{t.Name}_Turret.controller";

            AnimationClip idle = FindClip($"{t.ClipFolder}/A_Idle.fbx", "Idle");
            AnimationClip shoot = FindClip($"{t.ClipFolder}/A_Shoot.fbx", "Shoot");
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (idle == null || shoot == null || prefabAsset == null)
            {
                log.AppendLine($"  ✗ {t.Name} — Idle {(idle == null ? "없음" : "OK")} / " +
                               $"Shoot {(shoot == null ? "없음" : "OK")} / 프리팹 {(prefabAsset == null ? "없음" : "OK")}");
                failed++;
                continue;
            }

            if (dryRun)
            {
                bool exists = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath) != null;
                bool hasAgent = prefabAsset.GetComponent<NavMeshAgent>() != null;
                log.AppendLine($"  ▶ {t.Name} — 컨트롤러 {(exists ? "재생성" : "생성")} ({ctrlPath}) + " +
                               $"머리 마스크 + 데이터 정리 + NavMeshAgent {(hasAgent ? "제거" : "이미 없음")} " +
                               $"(clips: {idle.name}, {shoot.name})");
                done++;
                continue;
            }

            string maskPath = $"{OutFolder}/Mask_{t.Name}_HeadOnly.mask";
            AvatarMask mask = BuildHeadMask(maskPath, prefabAsset, out string maskFix);
            if (mask == null)
            {
                log.AppendLine($"  ✗ {t.Name} — 마스크 생성 실패: {maskFix}");
                failed++;
                continue;
            }

            AnimatorController controller = BuildController(ctrlPath, idle, shoot, mask);
            string dataFix = FixData(prefabAsset, controller);
            string agentFix = StripNavMeshAgent(prefabPath);
            string muzzleFix = WireMuzzle(prefabPath);
            string aimFix = EnsureHeadAim(prefabPath);

            log.AppendLine($"  ✓ {t.Name} — {ctrlPath}\n      마스크: {maskFix}\n      데이터: {dataFix}" +
                           $"\n      NavMeshAgent: {agentFix}\n      muzzle: {muzzleFix}\n      머리 조준: {aimFix}");
            done++;
        }

        if (!dryRun)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        log.Append($"\n  처리 {done} / 실패 {failed}");
        if (failed > 0) Debug.LogError(log.ToString());
        else Debug.Log(log.ToString());
    }

    const string HeadBone = "HeadRotator";

    /// <summary>
    /// 이미 「Shoot 파라미터 1개 + Base(Idle 단독) + Shoot 레이어(마스크 적용)」인가.
    ///
    /// 🔴 <b>2레이어인 이유</b>(2026-09-14 Play 실측): <c>A_Shoot</c> 클립은 머리뿐 아니라
    ///    <b>기둥 본(Column02·Column03)까지 크게 회전시킨다.</b> 증상 개체(Shoot 재생 중)는
    ///    Column02 (335.80, 12.59, 331.15) · Column03 (5.84, 53.62, 285.40) 이었고,
    ///    정상 개체(Idle)는 둘 다 (0,0,0) 이었다 — 컴포넌트 구성은 완전히 동일했다.
    ///    이것이 「몸체가 분리돼 보인다」의 정체다(물리·랙돌·넉백 전부 기각).
    ///    Unity 의 <see cref="AvatarMask"/> 는 <b>상태가 아니라 레이어 단위</b>이므로,
    ///    Shoot 을 별도 레이어로 올려 머리 본에만 적용한다. <b>레이어 0 에는 마스크가 안 먹는다.</b>
    /// </summary>
    static bool AlreadyCorrect(AnimatorController c, AnimationClip idleClip, AnimationClip shootClip, AvatarMask mask)
    {
        if (c.parameters.Length != 1) return false;
        if (c.parameters[0].name != "Shoot" ||
            c.parameters[0].type != AnimatorControllerParameterType.Trigger) return false;
        if (c.layers == null || c.layers.Length != 2) return false;

        // 레이어 0 = Idle 단독
        AnimatorStateMachine baseSm = c.layers[0].stateMachine;
        if (baseSm.states.Length != 1 || baseSm.stateMachines.Length != 0) return false;
        AnimatorState idle = baseSm.states[0].state;
        if (idle == null || idle.name != "Idle" || idle.motion != idleClip) return false;
        if (baseSm.defaultState != idle) return false;

        // 레이어 1 = Shoot (마스크 적용, Override, weight 1)
        AnimatorControllerLayer shootLayer = c.layers[1];
        if (shootLayer.avatarMask != mask) return false;
        if (shootLayer.blendingMode != AnimatorLayerBlendingMode.Override) return false;
        if (!Mathf.Approximately(shootLayer.defaultWeight, 1f)) return false;

        AnimatorStateMachine shootSm = shootLayer.stateMachine;
        if (shootSm.states.Length != 2 || shootSm.stateMachines.Length != 0) return false;

        AnimatorState headIdle = null, shoot = null;
        foreach (ChildAnimatorState cs in shootSm.states)
        {
            if (cs.state == null) return false;
            if (cs.state.name == "HeadIdle") headIdle = cs.state;
            else if (cs.state.name == "Shoot") shoot = cs.state;
        }

        // 🔴 HeadIdle 의 모션이 Idle 클립이어야 한다. 비어 있으면(예전 "Empty") Override 레이어가
        //    머리 본 소유권만 가져가고 아무 값도 안 써서 머리가 폭주한다(2026-09-14 실측).
        return headIdle != null && shoot != null
               && headIdle.motion == idleClip && shoot.motion == shootClip
               && shootSm.defaultState == headIdle;
    }

    /// <summary>
    /// 머리(<c>HeadRotator</c> 이하)만 켜진 <see cref="AvatarMask"/> 를 만든다.
    ///
    /// 경로를 <b>하드코딩하지 않는다</b> — 게임 프리팹의 <see cref="Animator"/> 를 찾아 그 아래
    /// 트랜스폼을 실제로 걸어서 경로를 뽑는다. PeekABot 과 TeslaBot 의 아트 프리팹이 서로 다른
    /// FBX 를 참조하고(<c>P_TeslaBot</c> 은 <c>R_PeekABot</c>·<c>R_TeslaBot</c> 을 둘 다 참조한다)
    /// 메시 노드 이름도 다르므로(<c>PeekaBot</c> / <c>G_BossMob_PeekABot</c>), 고정 경로는 조용히 빗나간다.
    /// </summary>
    static AvatarMask BuildHeadMask(string maskPath, GameObject gamePrefab, out string report)
    {
        var animator = gamePrefab.GetComponentInChildren<Animator>(true);
        if (animator == null) { report = "Animator 를 못 찾음"; return null; }

        Transform root = animator.transform;
        Transform[] all = root.GetComponentsInChildren<Transform>(true);

        Transform head = null;
        foreach (Transform t in all) if (t.name == HeadBone) { head = t; break; }
        if (head == null) { report = $"{HeadBone} 본이 없음 (본 {all.Length}개)"; return null; }

        var paths = new List<string>();
        var active = new List<bool>();
        foreach (Transform t in all)
        {
            if (t == root) continue;                    // 루트 자신은 경로가 빈 문자열이다
            paths.Add(AnimationUtility.CalculateTransformPath(t, root));
            active.Add(IsSelfOrDescendantOf(t, head));
        }

        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(maskPath);
        bool created = mask == null;

        if (!created && MaskMatches(mask, paths, active))
        {
            // 🔴 같은 내용이면 건드리지 않는다 — 매번 다시 쓰면 재실행마다 헛 diff 가 남는다.
            report = $"이미맞음 (켬 {CountTrue(active)} / 전체 {paths.Count})";
            return mask;
        }

        if (created) mask = new AvatarMask();
        mask.transformCount = paths.Count;
        for (int i = 0; i < paths.Count; i++)
        {
            mask.SetTransformPath(i, paths[i]);
            mask.SetTransformActive(i, active[i]);
        }

        if (created) AssetDatabase.CreateAsset(mask, maskPath);
        else EditorUtility.SetDirty(mask);

        report = $"{(created ? "생성" : "갱신")} — 켬 {CountTrue(active)} / 전체 {paths.Count} ({maskPath})";
        return mask;
    }

    static bool IsSelfOrDescendantOf(Transform t, Transform ancestor)
    {
        for (Transform p = t; p != null; p = p.parent) if (p == ancestor) return true;
        return false;
    }

    static int CountTrue(List<bool> v)
    {
        int n = 0;
        foreach (bool b in v) if (b) n++;
        return n;
    }

    static bool MaskMatches(AvatarMask mask, List<string> paths, List<bool> active)
    {
        if (mask.transformCount != paths.Count) return false;
        for (int i = 0; i < paths.Count; i++)
        {
            if (mask.GetTransformPath(i) != paths[i]) return false;
            if (mask.GetTransformActive(i) != active[i]) return false;
        }
        return true;
    }

    /// <summary>
    /// 고정 터렛에서 <see cref="NavMeshAgent"/> 를 제거한다(팀장 확정 2026-09-10).
    ///
    /// 🔴 <b>왜</b> — 고정형은 이동하지 않으므로 에이전트가 필요 없는데, 스폰 지점이 NavMesh 밖이면
    ///    에이전트가 배치에 실패한다. <c>MapContentSpawner.TryResolveSpawnPoint</c> 는 바닥
    ///    레이캐스트(<c>Default</c>∪<c>Ground</c>)만 하고 <b>NavMesh 를 샘플링하지 않는다</b> —
    ///    바닥 위이지만 NavMesh 밖인 지점이 그대로 통과한다. 없는 컴포넌트는 실패할 수 없다.
    ///
    /// <b>안전한 근거</b>(2026-09-10 전수 확인):
    ///   · <c>RequireComponent(typeof(NavMeshAgent))</c> 선언 <b>0건</b>
    ///   · <c>MonsterBase</c> 는 모든 접근을 가드한다 — 초기 설정은 <c>if (agent != null)</c> 블록(`:195~204`),
    ///     <c>DriveCombatMove</c>(`:643`)·<c>MoveAgentTo</c>(`:1402`)·<c>StopAgent</c>(`:1412`) 는 조기 반환
    ///   · 복귀 판정에 <b>거리 폴백</b>이 있다(`:954`) → 에이전트가 없어도 상태가 안 멈춘다.
    ///     고정형은 스폰 지점을 벗어나지 않으니 항상 도착 판정이다
    ///   · <c>LinearKnockback</c> 은 4곳 전부 <c>if (_navMeshAgent)</c> 가드. RangedTurret 은 애초에
    ///     넉백 무효다(<c>MonsterBase.cs:1050</c>)
    ///   · 터렛의 나머지 컴포넌트(ColliderInfo·DissolveDeath·EffectSocketPlayer·EffectAnimEvents·
    ///     Hurtbox·MonsterMeleeAttack·MonsterStatusEffect)는 에이전트를 쓰지 않는다
    ///
    /// ⚠️ <b>이동형에는 하면 안 된다.</b> MortarBot(RangedMobile)·근접 3종은 에이전트로 움직인다.
    ///    그쪽의 NavMesh 밖 스폰은 별 문제로 남아 있다(팀장 판단: 지금은 스폰되지 않아 보류).
    /// </summary>
    static string StripNavMeshAgent(string prefabPath)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var agent = root.GetComponent<NavMeshAgent>();
            if (agent == null) return "이미 없음";

            Object.DestroyImmediate(agent, true);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        // 되읽어 확인한다 — 프리팹 저장이 조용히 안 먹은 전례가 있다(m_Controller 오버라이드).
        var saved = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        bool gone = saved != null && saved.GetComponent<NavMeshAgent>() == null;
        return gone ? "제거 (되읽기: 없음 ✓)" : "🔴 제거했는데 되읽으니 아직 있다";
    }

    /// <summary>
    /// <c>MonsterRangedAttack.muzzle</c>(발사 원점)을 머리 쪽 본에 배선한다.
    ///
    /// 🔴 <b>왜</b>(2026-09-14 실측): TeslaBot 은 <c>muzzle: {fileID: 0}</c> 로 <b>비어 있었다</b>.
    /// 비면 <c>MonsterRangedAttack</c> 이 <c>transform.position</c> 으로 폴백해 <b>탄이 발밑에서</b> 나간다.
    /// PeekABot 은 <c>Muzzle_Socket</c> 이 배선돼 있었다. TeslaBot 리그에는 그 본이 없으므로
    /// <c>Head</c> 를 쓴다(팀장 확정). 둘 다 머리 마스크에 포함돼 있어 조준을 따라간다.
    /// </summary>
    static string WireMuzzle(string prefabPath)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        string boneName;
        try
        {
            var ranged = root.GetComponent<MonsterRangedAttack>();
            if (ranged == null) return "MonsterRangedAttack 없음 — 건너뜀";

            // Muzzle_Socket 이 있으면 그것, 없으면 Head.
            Transform bone = FindChildByName(root.transform, "Muzzle_Socket")
                             ?? FindChildByName(root.transform, "Head");
            if (bone == null) return "🔴 Muzzle_Socket·Head 둘 다 없음";
            boneName = bone.name;

            var so = new SerializedObject(ranged);
            SerializedProperty prop = so.FindProperty("muzzle");
            if (prop == null) return "🔴 muzzle 필드를 못 찾음(이름이 바뀌었나)";
            if (prop.objectReferenceValue == bone) return $"이미맞음 ({boneName})";

            prop.objectReferenceValue = bone;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        // 되읽어 확인한다 — 중첩 프리팹에서 저장이 조용히 안 먹은 전례가 있다(m_Controller 오버라이드).
        var saved = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        var savedRanged = saved != null ? saved.GetComponent<MonsterRangedAttack>() : null;
        if (savedRanged == null) return "🔴 되읽기 실패";

        var check = new SerializedObject(savedRanged);
        Object got = check.FindProperty("muzzle").objectReferenceValue;
        return got != null
            ? $"배선 → {got.name} (되읽기 ✓)"
            : "🔴 배선했는데 되읽으니 비어 있다";
    }

    /// <summary>
    /// 머리 조준 컴포넌트(<see cref="TurretHeadAim"/>)를 프리팹 루트에 보장한다.
    /// 몸통 회전은 <c>MonsterBase.BodyRotationLocked</c> 가 막으므로, 이게 없으면
    /// 터렛이 <b>아무 쪽도 안 보고</b> 쏘게 된다 — 빠지면 조용히 퇴보하는 자리다.
    /// </summary>
    static string EnsureHeadAim(string prefabPath)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            if (root.GetComponent<TurretHeadAim>() != null) return "이미있음";
            root.AddComponent<TurretHeadAim>();
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        var saved = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        bool ok = saved != null && saved.GetComponent<TurretHeadAim>() != null;
        return ok ? "추가 (되읽기 ✓)" : "🔴 추가했는데 되읽으니 없다";
    }

    static Transform FindChildByName(Transform root, string childName)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == childName) return t;
        return null;
    }

    /// <summary>FBX 안의 서브 애셋에서 이름으로 클립을 찾는다. 팩은 한 FBX 에 여러 클립을 담는다.</summary>
    static AnimationClip FindClip(string fbxPath, string clipName)
    {
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            if (o is AnimationClip c && c.name == clipName) return c;
        return null;
    }

    /// <summary>
    /// 멱등 — 애셋이 있으면 <b>지우지 않고 내용만 비워 다시 채운다.</b>
    ///
    /// 🔴 <b>왜 지우면 안 되는가</b>(2026-09-10 실측): `DeleteAsset` + 재생성은 **guid 가 바뀐다.**
    ///    프리팹이 이미 그 컨트롤러를 참조하고 있으면 재실행 한 번에 참조가 끊기고,
    ///    배선 감사는 그 몹을 "컨트롤러 없음 → 감사 불가"로 **건너뛰어** 죽은 이름 0개라는
    ///    **거짓 초록**을 낸다. 실제로 그렇게 한 번 깼다.
    /// </summary>
    static AnimatorController BuildController(string path, AnimationClip idleClip, AnimationClip shootClip, AvatarMask mask)
    {
        if (!AssetDatabase.IsValidFolder(OutFolder))
            AssetDatabase.CreateFolder("Assets/2.Prefabs/Monster", "Controllers");

        var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (c == null)
        {
            c = AnimatorController.CreateAnimatorControllerAtPath(path);
        }
        else if (AlreadyCorrect(c, idleClip, shootClip, mask))
        {
            // 🔴 이미 원하는 모양이면 손대지 않는다. 제자리 재구성도 상태 fileID 를 새로 만들어
            //    매 실행 컨트롤러가 전량 churn(63+/63-) 한다 — 재실행할 때마다 헛 diff 가 남는다.
            return c;
        }
        else
        {
            // 제자리 재구성 — guid 를 유지한다. 파라미터·상태·추가 레이어를 비우고 아래에서 다시 만든다.
            for (int i = c.parameters.Length - 1; i >= 0; i--) c.RemoveParameter(i);
            for (int i = c.layers.Length - 1; i >= 1; i--) c.RemoveLayer(i);

            AnimatorStateMachine old = c.layers[0].stateMachine;
            foreach (ChildAnimatorState cs in old.states) old.RemoveState(cs.state);
            foreach (ChildAnimatorStateMachine csm in old.stateMachines) old.RemoveStateMachine(csm.stateMachine);
        }

        c.AddParameter("Shoot", AnimatorControllerParameterType.Trigger);

        // ── 레이어 0 (Base) : Idle 단독 ──────────────────────────────────────────
        // 기둥 본을 포함한 전신을 여기서 쥔다. Shoot 은 여기에 두지 않는다 —
        // 레이어 0 에는 AvatarMask 가 적용되지 않아 기둥까지 휘둘러 버린다.
        AnimatorStateMachine baseSm = c.layers[0].stateMachine;

        // 위치를 주지 않으면 노드가 원점에 겹쳐 쌓여 그래프를 손으로 열었을 때 못 읽는다.
        AnimatorState idle = baseSm.AddState("Idle", new Vector3(300f, 0f, 0f));
        idle.motion = idleClip;
        baseSm.defaultState = idle;

        // ── 레이어 1 (Shoot) : 머리 본만 ────────────────────────────────────────
        c.AddLayer("Shoot");
        AnimatorControllerLayer[] layers = c.layers;
        layers[1].avatarMask = mask;
        layers[1].blendingMode = AnimatorLayerBlendingMode.Override;
        layers[1].defaultWeight = 1f;
        c.layers = layers;                       // 🔴 배열을 되돌려 넣어야 반영된다(복사본이다)

        AnimatorStateMachine shootSm = c.layers[1].stateMachine;

        // 🔴 평상시 상태에 <b>Idle 클립을 물린다</b>. 모션을 비우면 안 된다 —
        //    Override 레이어는 마스크에 포함된 본의 <b>소유권을 가져가는데</b>, 모션이 없으면
        //    아무 값도 쓰지 않는다. 그러면 레이어 0 의 Idle 머리 커브가 덮이면서
        //    <b>매 프레임 아무도 머리를 안 쓰는 구간</b>이 생기고, 그 위에 코드가 각도를 얹으면
        //    프레임마다 쌓여 머리가 폭주한다(2026-09-14 팀장 Play 에서 실제로 터졌다).
        //    Idle 을 물려 두면 평상시 머리 포즈가 매 프레임 확정된다.
        AnimatorState headIdle = shootSm.AddState("HeadIdle", new Vector3(300f, 0f, 0f));
        headIdle.motion = idleClip;
        shootSm.defaultState = headIdle;

        AnimatorState shoot = shootSm.AddState("Shoot", new Vector3(600f, 120f, 0f));
        shoot.motion = shootClip;

        // Empty → Shoot : 트리거 즉시. exitTime 을 쓰면 사격이 한 바퀴 밀린다.
        AnimatorStateTransition toShoot = headIdle.AddTransition(shoot);
        toShoot.hasExitTime = false;
        toShoot.duration = 0.05f;
        toShoot.AddCondition(AnimatorConditionMode.If, 0f, "Shoot");

        // Shoot → Empty : 클립이 끝나면 돌아온다(조건 없음 — 조건을 걸면 그 트리거가 없을 때 갇힌다.
        //                 TeslaBot 이 Charge 에 갇힌 원인이 정확히 그것이었다).
        AnimatorStateTransition toHeadIdle = shoot.AddTransition(headIdle);
        toHeadIdle.hasExitTime = true;
        toHeadIdle.exitTime = 0.9f;
        toHeadIdle.duration = 0.1f;

        EditorUtility.SetDirty(c);
        return c;
    }

    /// <summary>
    /// 데이터의 이름을 새 컨트롤러에 맞추고, <b>컨트롤러 자체도 데이터에 넣는다.</b>
    ///
    /// 🔴 <b>왜 프리팹에 배선하지 않는가</b>(2026-09-10 실측 — 두 방식 모두 실패했다):
    ///    Animator 는 2단 중첩(우리 프리팹 → 아트 프리팹 → FBX) 안에 있다. 외부 프리팹에서
    ///    <c>m_Controller</c> 를 오버라이드하면 — 직접 대입이든 <c>SerializedObject</c> 든 —
    ///    <b>저장은 성공하고 YAML 에 엔트리도 남는데 로드하면 null</b> 이다(오버라이드 타깃이
    ///    해석되지 않는다). 인스턴스를 만들어 물어봐야만 드러나는 <b>조용한 실패</b>다.
    ///    아트 프리팹을 고치는 길도 있지만 SVN 이고 팩 업데이트에 덮인다.
    ///    → <c>MonsterDataSO.animatorControllerOverride</c> 에 넣고 <c>MonsterBase.OnNetworkSpawn</c> 이
    ///      덮는다(<c>IsServer</c> 게이트 없음 = 전 피어. 클라에서도 같은 컨트롤러여야 보이는 것이 같다).
    /// </summary>
    static string FixData(GameObject prefabAsset, AnimatorController controller)
    {
        var monster = prefabAsset.GetComponent<MonsterBase>();
        if (monster == null) return "🔴 MonsterBase 없음";

        var so = new SerializedObject(monster);
        SerializedProperty dataProp = so.FindProperty("data");
        var data = dataProp != null ? dataProp.objectReferenceValue as MonsterDataSO : null;
        if (data == null) return "🔴 데이터 없음";

        var changes = new List<string>();
        if (data.animatorControllerOverride != controller)
        {
            string before = data.animatorControllerOverride != null ? data.animatorControllerOverride.name : "(없음)";
            changes.Add($"animatorControllerOverride {before}→{controller.name}");
            data.animatorControllerOverride = controller;
        }
        if (data.attackTrigger != "Shoot") { changes.Add($"attackTrigger \"{data.attackTrigger}\"→\"Shoot\""); data.attackTrigger = "Shoot"; }
        // 🔴 단발 사격이므로 2단계 트리거를 비운다(`MonsterDataSO`: "비우면 단발").
        //    TeslaBot 은 구 컨트롤러의 `Attack` 을 들고 있었다 — 새 컨트롤러에 없는 이름이다.
        //    원래도 없는 파라미터라 이미 no-op 였으므로 동작 변화는 없고, 죽은 이름만 사라진다.
        //    ⚠️ 이 필드는 확장 전 배선 감사에 **아예 빠져 있었다** — 그래서 이 도구 1차판도 놓쳤다.
        if (!string.IsNullOrEmpty(data.attackFinishTrigger)) { changes.Add($"attackFinishTrigger \"{data.attackFinishTrigger}\"→비움"); data.attackFinishTrigger = ""; }
        if (!string.IsNullOrEmpty(data.hitTrigger)) { changes.Add($"hitTrigger \"{data.hitTrigger}\"→비움"); data.hitTrigger = ""; }
        if (!string.IsNullOrEmpty(data.animSpeedParam)) { changes.Add($"animSpeedParam \"{data.animSpeedParam}\"→비움"); data.animSpeedParam = ""; }
        if (data.locomotionState != "Idle") { changes.Add($"locomotionState \"{data.locomotionState}\"→\"Idle\""); data.locomotionState = "Idle"; }

        if (changes.Count == 0) return "변경 없음";

        EditorUtility.SetDirty(data);
        return string.Join(" · ", changes);
    }
}
