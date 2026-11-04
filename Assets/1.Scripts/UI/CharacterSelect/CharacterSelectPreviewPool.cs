using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>캐릭터 선택 미리보기 애니메이션 칸(할 일 199) — 프리팹 이름(예: Player_Paladin)마다 등장 1회 → 루프.</summary>
[Serializable]
public sealed class CharacterSelectPreviewAnimation
{
    [Tooltip("플레이어 프리팹 이름(CharacterRoster 의 Player Prefab 이름, 예: Player_Paladin).")]
    public string prefabName;
    [Tooltip("복제본이 나타날 때 한 번 재생할 Animator 상태 이름(0번 레이어). 비우면 바로 루프.")]
    public string entryState;
    [Tooltip("등장이 끝난 뒤 반복할 Animator 상태 이름(0번 레이어). 비우면 Animator 기본 상태.")]
    public string loopState;

    public CharacterSelectPreviewAnimation(string prefabName, string entryState, string loopState)
    {
        this.prefabName = prefabName;
        this.entryState = entryState;
        this.loopState = loopState;
    }
}

/// <summary>
/// 캐릭터 선택 미리보기용 표시 복제본 풀(할 일 199) — 클라마다 로컬, 네트워크 무관(스폰 아님).
/// 로스터 캐릭터마다 Variant 프리팹에서 <c>Armature</c>·<c>PlayerWaeponSlot</c> 만 떼어 Animator·렌더러·화이트리스트 스크립트만 남긴다.
/// <list type="bullet">
/// <item>만들기 = 꺼진 홀더 아래 Variant 전체 Instantiate(Awake 없음) → 두 자식 말고 자식 파괴·루트 컴포넌트는 Transform 만(같은 복제라 무기 ↔ 소켓 참조 유지) → 꺼진 채 컨테이너로.</item>
/// <item>남기는 스크립트 = 애니 이벤트 수신자(부모가 없어 아무 일도 안 함 — "no receiver" 오류 방지)·무기 따라가기·팔 비틀기·어쌔신 머리카락·디졸브 오버레이. 네트워크 컴포넌트·콜라이더·리지드바디는 지운다. 파티클·트레일은 끈다.</item>
/// <item>컨테이너는 넘겨받은 씬(로비)에 둔다 — 씬이 내려가면 같이 사라진다.</item>
/// </list>
/// </summary>
public sealed class CharacterSelectPreviewPool
{
    public const string ArmatureName = "Armature";
    public const string WeaponSlotName = "PlayerWaeponSlot";
    private const float LoopCrossFadeSeconds = 0.15f;

    // 남기는 MonoBehaviour — 같은 오브젝트·자식만 참조한다(진짜 플레이어 루트 의존 없음).
    private static readonly HashSet<Type> KeptBehaviours = new HashSet<Type>
    {
        typeof(PlayerAnimationEventRelay),
        typeof(EffectAnimEventRelay),
        typeof(WeaponTransformRelay),
        typeof(SDArmTwist),
        typeof(AssassinHairMotion),
        typeof(AssassinHairDriver),
        typeof(AssassinActionHairAnimation),
        typeof(AssassinDiveHairAnimation),
        typeof(DissolveOverlay),
    };

    private sealed class Preview
    {
        public GameObject Root;
        public Animator Animator;
        public string PrefabName;
        public int EntryHash;
        public int LoopHash;
        public bool InEntry;
    }

    private readonly List<Preview> _previews = new List<Preview>();
    private GameObject _container;
    private int _shown = -1;

    public bool IsBuilt => _container != null;
    public int Count => _previews.Count;
    public int ShownIndex => _shown;

    /// <summary>로스터 순서대로 복제본을 만들어 꺼 둔다. 이미 있으면 아무것도 안 한다. 빈 칸은 null 자리로 둔다(index = 캐릭터 id 유지).</summary>
    public void Build(CharacterRoster roster, Scene scene, IReadOnlyList<CharacterSelectPreviewAnimation> animations)
    {
        if (IsBuilt || roster == null)
            return;

        _container = new GameObject("CharacterSelectPreviewPool");
        if (scene.IsValid() && scene.isLoaded && _container.scene != scene)
            SceneManager.MoveGameObjectToScene(_container, scene);
        var holder = new GameObject("PreviewHolder"); // 꺼진 채 둔다 — 아래에 복제한 프리팹은 Awake 하지 않는다
        holder.SetActive(false);
        holder.transform.SetParent(_container.transform, false);

        for (int i = 0; i < roster.Count; i++)
        {
            CharacterRoster.Entry entry = roster.Entries[i];
            GameObject prefab = entry != null ? entry.PlayerPrefab : null;
            _previews.Add(prefab != null ? CreatePreview(prefab, holder.transform, _container.transform, animations) : null);
        }
    }

    /// <summary>
    /// 이 캐릭터 복제본을 그 자리·방향에 보이고 나머지는 끈다. 다른 캐릭터로 바뀌면 등장 동작부터 다시.
    /// 없는 id 면 전부 끄고 false.
    /// </summary>
    public bool Show(int index, Vector3 position, Quaternion rotation)
    {
        Preview target = index >= 0 && index < _previews.Count ? _previews[index] : null;
        if (target == null || target.Root == null)
        {
            HideAll();
            return false;
        }

        if (_shown != index)
        {
            HideAll();
            target.Root.transform.SetPositionAndRotation(position, rotation);
            target.Root.SetActive(true);
            PlayEntry(target);
            _shown = index;
        }
        else
        {
            target.Root.transform.SetPositionAndRotation(position, rotation);
        }

        return true;
    }

    public void HideAll()
    {
        for (int i = 0; i < _previews.Count; i++)
            if (_previews[i] != null && _previews[i].Root != null && _previews[i].Root.activeSelf)
                _previews[i].Root.SetActive(false);
        _shown = -1;
    }

    /// <summary>보이는 복제본의 등장 → 루프 넘김. 매 프레임.</summary>
    public void Tick()
    {
        Preview shown = _shown >= 0 && _shown < _previews.Count ? _previews[_shown] : null;
        if (shown == null || !shown.InEntry || shown.Animator == null)
            return;

        if (shown.Animator.IsInTransition(0))
            return;
        AnimatorStateInfo state = shown.Animator.GetCurrentAnimatorStateInfo(0);
        if (state.shortNameHash == shown.EntryHash && state.normalizedTime < 1f)
            return;

        // 등장이 끝났거나(또는 컨트롤러 자체 전이로 이미 다른 상태) — 루프로.
        shown.InEntry = false;
        if (shown.LoopHash != 0)
            shown.Animator.CrossFadeInFixedTime(shown.LoopHash, LoopCrossFadeSeconds, 0);
    }

    public void Dispose()
    {
        if (_container != null)
            UnityEngine.Object.Destroy(_container);
        _container = null;
        _previews.Clear();
        _shown = -1;
    }

    /// <summary>
    /// 복제본 하나 — 테스트도 이 경로를 쓴다. <paramref name="inactiveHolder"/>(비어 있는 꺼진 오브젝트) 아래에 Variant 를 복제해 어떤 스크립트도 Awake 하지 않게 하고,
    /// Armature·무기 슬롯만 빈 새 루트로 옮긴 뒤 복제 루트는 GameObject 째 지운다.
    /// 🔴 복제 루트의 컴포넌트를 하나씩 지우지 않는다 — PlayerMovement ⇄ PlayerInputReader 처럼 [RequireComponent] 가 서로 걸려 있어 지워지지 않고
    /// PlayerInput·Motor·Rigidbody·CapsuleCollider 가 살아 남는다(복제본이 입력을 쥐고 진짜 플레이어를 밀어냄 — 199 Play 버그).
    /// 새 루트 = 빈 홀더를 Instantiate(<paramref name="parent"/> 씬에 바로 생김·꺼진 채) — 에디터 테스트가 열린 씬을 더럽히지 않게 new GameObject 를 쓰지 않는다.
    /// </summary>
    public static GameObject CreatePreviewObject(GameObject variantPrefab, Transform inactiveHolder, Transform parent)
    {
        GameObject root = UnityEngine.Object.Instantiate(inactiveHolder.gameObject, parent, false);
        root.name = $"Preview_{variantPrefab.name}";

        GameObject clone = UnityEngine.Object.Instantiate(variantPrefab, inactiveHolder, false);
        MoveChild(clone.transform, ArmatureName, root.transform);
        MoveChild(clone.transform, WeaponSlotName, root.transform);
        DestroyNow(clone); // 남은 복제(NetworkObject·역할 스크립트·VFX)는 한 번도 켜지지 않은 채 사라진다

        Strip(root);
        return root;
    }

    private static void MoveChild(Transform from, string childName, Transform to)
    {
        Transform child = from.Find(childName);
        if (child != null)
            child.SetParent(to, false); // 원래 Variant 루트 기준 로컬 자세 그대로
    }

    /// <summary>남길 것만 남긴다 — 활성화 전에 부른다.</summary>
    public static void Strip(GameObject root)
    {
        // 1)·2) 스크립트(네트워크 컴포넌트 포함, 남기는 것 외 전부) + 물리(로비 트리거·피격에 끼지 않게).
        //      [RequireComponent] 로 묶인 것은 의존하는 쪽부터 지운다(반대 순서면 Unity 가 거부·오류).
        foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
            RemoveComponents(node.gameObject, IsStrippable);
        // 3) VFX — 끈다(컴포넌트 의존이 얽혀 지우지 않는다).
        foreach (ParticleSystem particles in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.enabled = false;
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (particles.TryGetComponent(out ParticleSystemRenderer particleRenderer))
                particleRenderer.enabled = false;
        }
        foreach (TrailRenderer trail in root.GetComponentsInChildren<TrailRenderer>(true))
        {
            trail.emitting = false;
            trail.enabled = false;
        }
        foreach (AudioSource audio in root.GetComponentsInChildren<AudioSource>(true))
        {
            audio.playOnAwake = false;
            audio.enabled = false;
        }
        // 4) 제자리 재생.
        foreach (Animator animator in root.GetComponentsInChildren<Animator>(true))
        {
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
    }

    public static bool IsKeptBehaviour(Type type) => type != null && KeptBehaviours.Contains(type);

    public static CharacterSelectPreviewAnimation FindAnimation(IReadOnlyList<CharacterSelectPreviewAnimation> animations, string prefabName)
    {
        if (animations == null || string.IsNullOrEmpty(prefabName))
            return null;
        for (int i = 0; i < animations.Count; i++)
            if (animations[i] != null && animations[i].prefabName == prefabName)
                return animations[i];
        return null;
    }

    private static Preview CreatePreview(GameObject prefab, Transform inactiveHolder, Transform parent, IReadOnlyList<CharacterSelectPreviewAnimation> animations)
    {
        GameObject root = CreatePreviewObject(prefab, inactiveHolder, parent);
        var preview = new Preview
        {
            Root = root,
            Animator = root.GetComponentInChildren<Animator>(true),
            PrefabName = prefab.name
        };

        // 한 번 켜서 남긴 스크립트·Animator 를 초기화하고 끈다(첫 열기 끊김 줄이기).
        root.SetActive(true);
        CharacterSelectPreviewAnimation animation = FindAnimation(animations, prefab.name);
        preview.EntryHash = ResolveState(preview.Animator, animation != null ? animation.entryState : null, prefab.name);
        preview.LoopHash = ResolveState(preview.Animator, animation != null ? animation.loopState : null, prefab.name);
        root.SetActive(false);
        return preview;
    }

    private static int ResolveState(Animator animator, string stateName, string owner)
    {
        if (animator == null || string.IsNullOrEmpty(stateName))
            return 0;
        int hash = Animator.StringToHash(stateName);
        if (animator.runtimeAnimatorController != null && animator.HasState(0, hash))
            return hash;
        Debug.LogWarning($"[CharacterSelectPreviewPool] {owner} Animator 0번 레이어에 '{stateName}' 상태가 없다 — 기본 상태로 둔다.");
        return 0;
    }

    private static void PlayEntry(Preview preview)
    {
        if (preview.Animator == null)
            return;
        if (preview.EntryHash != 0)
        {
            preview.Animator.Play(preview.EntryHash, 0, 0f);
            preview.InEntry = true;
        }
        else if (preview.LoopHash != 0)
        {
            preview.Animator.Play(preview.LoopHash, 0, 0f);
            preview.InEntry = false;
        }
        preview.Animator.Update(0f); // 켜진 첫 프레임에 이전 포즈가 비치지 않게
    }

    private static bool IsStrippable(Component component) =>
        component is Collider || component is Rigidbody ||
        (component is MonoBehaviour && !KeptBehaviours.Contains(component.GetType()));

    private static void RemoveComponents(GameObject target, Predicate<Component> remove)
    {
        var pending = new List<Component>();
        foreach (Component component in target.GetComponents<Component>())
            if (component != null && remove(component))
                pending.Add(component);

        // 남은 컴포넌트 중 아무도 요구하지 않는 것부터 — 진전이 없으면(남기는 것이 요구) 남긴다.
        bool progressed = true;
        while (pending.Count > 0 && progressed)
        {
            progressed = false;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (IsRequiredByOther(target, pending[i]))
                    continue;
                DestroyNow(pending[i]);
                pending.RemoveAt(i);
                progressed = true;
            }
        }
    }

    private static bool IsRequiredByOther(GameObject target, Component candidate)
    {
        Type candidateType = candidate.GetType();
        foreach (Component other in target.GetComponents<Component>())
        {
            if (other == null || other == candidate)
                continue;
            foreach (RequireComponent require in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
            {
                if (Requires(require.m_Type0, candidateType) || Requires(require.m_Type1, candidateType) || Requires(require.m_Type2, candidateType))
                    return true;
            }
        }
        return false;
    }

    // 요구 타입과 같거나 상속하면 막는다(같은 타입이 둘 있어도 보수적으로).
    private static bool Requires(Type required, Type candidate) =>
        required != null && required.IsAssignableFrom(candidate);

    private static void DestroyNow(UnityEngine.Object target)
    {
        if (target == null)
            return;
        UnityEngine.Object.DestroyImmediate(target);
    }
}
