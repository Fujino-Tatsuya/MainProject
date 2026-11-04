using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 캐릭터 선택 미리보기 복제본(할 일 199) — 실제 로스터의 Variant 프리팹마다 <see cref="CharacterSelectPreviewPool.CreatePreviewObject"/> 로 만든 복제본이
/// Armature·무기 슬롯만 남기고(네트워크·콜라이더·리지드바디 0, 화이트리스트 밖 스크립트 0), Animator·렌더러를 갖는지 본다.
/// 패널 기본 애니메이션 칸의 상태 이름이 그 캐릭터 Animator 0번 레이어에 있는지도 본다(오타면 Play 에서 기본 상태로 떨어진다).
/// </summary>
public sealed class CharacterSelectPreviewPoolTests
{
    const string RosterPath = "Assets/9.ScriptableObject/Player/CharacterRoster.asset";

    Scene _scene;
    GameObject _holder;
    GameObject _parent;

    // DontSave + 프리뷰 씬 — 사용자 열린 씬을 더럽히지 않는다.
    [SetUp]
    public void SetUp()
    {
        _scene = EditorSceneManager.NewPreviewScene();
        _holder = EditorUtility.CreateGameObjectWithHideFlags("PreviewPoolTestHolder", HideFlags.HideAndDontSave);
        _holder.SetActive(false);
        SceneManager.MoveGameObjectToScene(_holder, _scene);
        _parent = EditorUtility.CreateGameObjectWithHideFlags("PreviewPoolTest", HideFlags.HideAndDontSave);
        SceneManager.MoveGameObjectToScene(_parent, _scene);
    }

    [TearDown]
    public void TearDown()
    {
        if (_parent != null)
            Object.DestroyImmediate(_parent);
        if (_holder != null)
            Object.DestroyImmediate(_holder);
        EditorSceneManager.ClosePreviewScene(_scene);
    }

    static CharacterRoster Roster()
    {
        var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(RosterPath);
        Assert.IsNotNull(roster, RosterPath);
        return roster;
    }

    static IEnumerable<GameObject> RosterPrefabs()
    {
        foreach (CharacterRoster.Entry entry in Roster().Entries)
            if (entry != null && entry.PlayerPrefab != null)
                yield return entry.PlayerPrefab;
    }

    [Test]
    public void EveryRosterCharacter_PreviewKeepsOnlyArmatureAndWeaponSlot_Stripped()
    {
        int built = 0;
        foreach (GameObject prefab in RosterPrefabs())
        {
            GameObject root = CharacterSelectPreviewPool.CreatePreviewObject(prefab, _holder.transform, _parent.transform);
            built++;

            Assert.IsFalse(root.activeSelf, $"{prefab.name}: 만든 직후엔 꺼져 있다");
            CollectionAssert.AreEqual(new[] { typeof(Transform) }, System.Array.ConvertAll(root.GetComponents<Component>(), c => c.GetType()),
                $"{prefab.name}: 루트는 Transform 만");
            Assert.AreEqual(_parent.transform, root.transform.parent);
            Assert.IsNotNull(root.transform.Find(CharacterSelectPreviewPool.ArmatureName), $"{prefab.name}: Armature");
            foreach (Transform child in root.transform)
                Assert.That(child.name, Is.EqualTo(CharacterSelectPreviewPool.ArmatureName).Or.EqualTo(CharacterSelectPreviewPool.WeaponSlotName),
                    $"{prefab.name}: Armature·무기 슬롯 말고는 남지 않는다");

            Assert.IsEmpty(root.GetComponentsInChildren<NetworkObject>(true), $"{prefab.name}: NetworkObject 0");
            Assert.IsEmpty(root.GetComponentsInChildren<NetworkBehaviour>(true), $"{prefab.name}: NetworkBehaviour 0");
            Assert.IsEmpty(root.GetComponentsInChildren<Collider>(true), $"{prefab.name}: Collider 0");
            Assert.IsEmpty(root.GetComponentsInChildren<Rigidbody>(true), $"{prefab.name}: Rigidbody 0");
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                Assert.IsTrue(behaviour == null || CharacterSelectPreviewPool.IsKeptBehaviour(behaviour.GetType()),
                    $"{prefab.name}: 화이트리스트 밖 스크립트 {behaviour.GetType().Name} ({behaviour.name})");

            Animator animator = root.GetComponentInChildren<Animator>(true);
            Assert.IsNotNull(animator, $"{prefab.name}: Animator");
            Assert.IsFalse(animator.applyRootMotion, $"{prefab.name}: 제자리 재생");
            Assert.IsNotEmpty(root.GetComponentsInChildren<SkinnedMeshRenderer>(true), $"{prefab.name}: 몸 렌더러");
            foreach (ParticleSystemRenderer particles in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
                Assert.IsFalse(particles.enabled, $"{prefab.name}: 파티클 끔 ({particles.name})");
            foreach (TrailRenderer trail in root.GetComponentsInChildren<TrailRenderer>(true))
                Assert.IsFalse(trail.enabled, $"{prefab.name}: 트레일 끔 ({trail.name})");
        }

        Assert.Greater(built, 0, "로스터에 플레이어 프리팹이 있어야 한다");
        Assert.AreEqual(built, _parent.transform.childCount, "복제본 루트만 옮겨 온다");
        Assert.AreEqual(0, _holder.transform.childCount, "홀더에 남는 복제 없음");
    }

    [Test]
    public void PanelDefaultAnimations_StatesExistInEachCharactersAnimator()
    {
        var panelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterSelectPanelAuthoring.PanelPath);
        Assert.IsNotNull(panelPrefab, CharacterSelectPanelAuthoring.PanelPath);
        var panel = panelPrefab.GetComponent<CharacterSelectPanel>();
        var animations = (List<CharacterSelectPreviewAnimation>)typeof(CharacterSelectPanel)
            .GetField("previewAnimations", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);

        int checkedCount = 0;
        foreach (GameObject prefab in RosterPrefabs())
        {
            CharacterSelectPreviewAnimation animation = CharacterSelectPreviewPool.FindAnimation(animations, prefab.name);
            Assert.IsNotNull(animation, $"{prefab.name}: 미리보기 애니메이션 칸이 있어야 한다");
            Assert.Greater(animation.loopBlendSeconds, 0f, $"{prefab.name}: 등장 → 루프 블렌드가 0 이면 뚝 끊긴다");

            Animator animator = prefab.GetComponentInChildren<Animator>(true);
            Assert.IsNotNull(animator, $"{prefab.name}: Animator");
            RuntimeAnimatorController runtime = animator.runtimeAnimatorController;
            if (runtime is AnimatorOverrideController overrideController)
                runtime = overrideController.runtimeAnimatorController;
            var controller = runtime as AnimatorController;
            Assert.IsNotNull(controller, $"{prefab.name}: AnimatorController");

            var states = new HashSet<string>();
            CollectStates(controller.layers[0].stateMachine, states);
            var names = new List<string>(animation.entryStates) { animation.loopState };
            foreach (string state in names)
                if (!string.IsNullOrEmpty(state))
                    Assert.IsTrue(states.Contains(state), $"{prefab.name}: 0번 레이어에 '{state}' 상태가 없다");

            if (animation.beamNormalizedTime >= 0f)
                AssertPreviewHasBeamSocket(prefab);
            if (animation.transformAura)
                Assert.IsNotNull(prefab.GetComponentInChildren<AssassinSkillView>(true), $"{prefab.name}: 변신 오라 칸이 있는데 AssassinSkillView 가 없다");
            checkedCount++;
        }

        Assert.Greater(checkedCount, 0);
    }

    [Test]
    public void FindAnimation_ByPrefabName_NullSafe()
    {
        var list = new List<CharacterSelectPreviewAnimation> { new CharacterSelectPreviewAnimation("Player_A", new[] { "Enter", "Enter2" }, "Loop") };
        CollectionAssert.AreEqual(new[] { "Enter", "Enter2" }, CharacterSelectPreviewPool.FindAnimation(list, "Player_A").entryStates);
        Assert.IsNull(CharacterSelectPreviewPool.FindAnimation(list, "Player_B"));
        Assert.IsNull(CharacterSelectPreviewPool.FindAnimation(null, "Player_A"));
        Assert.IsNull(CharacterSelectPreviewPool.FindAnimation(list, null));
    }

    // 빔 미리보기 = 프리팹 GunnerBeamView 의 Q 빔 소켓이 복제본 안 같은 경로에 남아 있어야 한다(Armature 아래 총구).
    void AssertPreviewHasBeamSocket(GameObject prefab)
    {
        GunnerBeamView view = prefab.GetComponentInChildren<GunnerBeamView>(true);
        Assert.IsNotNull(view, $"{prefab.name}: 빔 칸이 있는데 GunnerBeamView 가 없다");
        Assert.IsNotNull(view.ChargeBeamPlayer, $"{prefab.name}: Q 빔 소켓 미배선");

        string path = null;
        for (Transform t = view.ChargeBeamPlayer.transform; t != prefab.transform; t = t.parent)
        {
            Assert.IsNotNull(t, $"{prefab.name}: Q 빔 소켓이 프리팹 밖");
            path = path == null ? t.name : $"{t.name}/{path}";
        }

        GameObject root = CharacterSelectPreviewPool.CreatePreviewObject(prefab, _holder.transform, _parent.transform);
        Transform found = root.transform.Find(path);
        Assert.IsNotNull(found, $"{prefab.name}: 복제본에 빔 소켓 {path} 이 없다");
        Assert.IsNotNull(found.GetComponent<EffectSocketPlayer>(), $"{prefab.name}: 빔 EffectSocketPlayer 가 지워졌다");
    }

    // Animator.HasState 의 shortNameHash 는 하위 상태 기계 안 상태도 찾는다 — 같은 범위로 모은다.
    static void CollectStates(AnimatorStateMachine machine, HashSet<string> states)
    {
        foreach (ChildAnimatorState child in machine.states)
            states.Add(child.state.name);
        foreach (ChildAnimatorStateMachine sub in machine.stateMachines)
            CollectStates(sub.stateMachine, states);
    }
}
