using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

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
                log.AppendLine($"  ▶ {t.Name} — 컨트롤러 {(exists ? "재생성" : "생성")} ({ctrlPath}) + " +
                               $"프리팹 배선 + 데이터 정리 (clips: {idle.name}, {shoot.name})");
                done++;
                continue;
            }

            AnimatorController controller = BuildController(ctrlPath, idle, shoot);
            string dataFix = FixData(prefabAsset, controller);

            log.AppendLine($"  ✓ {t.Name} — {ctrlPath}\n      데이터: {dataFix}");
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
    static AnimatorController BuildController(string path, AnimationClip idleClip, AnimationClip shootClip)
    {
        if (!AssetDatabase.IsValidFolder(OutFolder))
            AssetDatabase.CreateFolder("Assets/2.Prefabs/Monster", "Controllers");

        var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (c == null)
        {
            c = AnimatorController.CreateAnimatorControllerAtPath(path);
        }
        else
        {
            // 제자리 재구성 — guid 를 유지한다. 파라미터·상태를 비우고 아래에서 다시 만든다.
            for (int i = c.parameters.Length - 1; i >= 0; i--) c.RemoveParameter(i);

            AnimatorStateMachine old = c.layers[0].stateMachine;
            foreach (ChildAnimatorState cs in old.states) old.RemoveState(cs.state);
            foreach (ChildAnimatorStateMachine csm in old.stateMachines) old.RemoveStateMachine(csm.stateMachine);
        }

        c.AddParameter("Shoot", AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine sm = c.layers[0].stateMachine;

        // 위치를 주지 않으면 노드가 원점에 겹쳐 쌓여 그래프를 손으로 열었을 때 못 읽는다.
        AnimatorState idle = sm.AddState("Idle", new Vector3(300f, 0f, 0f));
        idle.motion = idleClip;
        sm.defaultState = idle;

        AnimatorState shoot = sm.AddState("Shoot", new Vector3(600f, 120f, 0f));
        shoot.motion = shootClip;

        // Idle → Shoot : 트리거 즉시. exitTime 을 쓰면 Idle 이 한 바퀴 돌 때까지 사격이 밀린다.
        AnimatorStateTransition toShoot = idle.AddTransition(shoot);
        toShoot.hasExitTime = false;
        toShoot.duration = 0.05f;
        toShoot.AddCondition(AnimatorConditionMode.If, 0f, "Shoot");

        // Shoot → Idle : 클립이 끝나면 돌아온다(조건 없음 — 조건을 걸면 그 트리거가 없을 때 갇힌다.
        //                TeslaBot 이 Charge 에 갇힌 원인이 정확히 그것이었다).
        AnimatorStateTransition toIdle = shoot.AddTransition(idle);
        toIdle.hasExitTime = true;
        toIdle.exitTime = 0.9f;
        toIdle.duration = 0.1f;

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
    ///    → <c>MonsterDataSO.animatorControllerOverride</c> 에 넣고 <c>MonsterBase.Awake</c> 가 덮는다.
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
