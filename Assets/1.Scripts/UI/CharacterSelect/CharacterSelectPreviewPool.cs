using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 캐릭터 선택 미리보기 애니메이션 칸(할 일 199) — 프리팹 이름(예: Player_Paladin)마다 등장 상태들을 순서대로 1회씩 → 루프(또는 마지막 포즈 홀드).
/// 상태 넘김 시점 = 그 클립의 End 애니 이벤트(평타 <c>HandleDefaultAttackEvent(End)</c>·스킬 <c>HandleSkillEvent(End)</c>), 없으면 클립 끝 − 블렌드.
/// </summary>
[Serializable]
public sealed class CharacterSelectPreviewAnimation
{
    public const float DefaultLoopBlendSeconds = 0.25f;
    public const float DefaultChainBlendSeconds = 0.1f;

    [Tooltip("플레이어 프리팹 이름(CharacterRoster 의 Player Prefab 이름, 예: Player_Paladin).")]
    public string prefabName;
    [Tooltip("복제본이 나타날 때 순서대로 한 번씩 재생할 Animator 상태 이름(0번 레이어). 비우면 바로 루프.")]
    public List<string> entryStates = new List<string>();
    [Tooltip("등장이 끝난 뒤 반복할 Animator 상태 이름(0번 레이어). 비우면 Animator 기본 상태.")]
    public string loopState;
    [Tooltip("켜면 루프로 넘기지 않고 마지막 등장 상태를 클립 끝 프레임까지 재생하고 그 포즈로 멈춰 둔다(Animator 속도 0, 루프·블렌드 없음).")]
    public bool holdLastPose;
    [Tooltip("등장 → 루프 크로스페이드 시간(초). End 이벤트(없으면 클립 끝 − 이 시간)에서 시작 — 컨트롤러의 끝 전이(Exit Time 1)보다 먼저 잡는다.")]
    [Min(0f)] public float loopBlendSeconds = DefaultLoopBlendSeconds;
    [Tooltip("등장 상태끼리 넘어갈 때 크로스페이드 시간(초).")]
    [Min(0f)] public float chainBlendSeconds = DefaultChainBlendSeconds;
    [Tooltip("첫 등장 상태의 이 정규화 시각에 무기 빔(거너 Q 레이저)을 로컬로 튼다. 음수 = 안 쏨. 그 클립에 발사 순간 애니 이벤트가 없어서 칸으로 둔다.")]
    public float beamNormalizedTime = -1f;
    [Tooltip("빔 길이(m) — 복제본 앞 방향으로.")]
    [Min(0.1f)] public float beamLength = 8f;
    [Tooltip("켜면 미리보기가 보이는 동안 어쌔신 R 변신 오라(EffectCatalog.Assassin_R_BladeVortex)를 계속 켠다 — 오프셋·배율은 프리팹 AssassinSkillView 값.")]
    public bool transformAura;

    public CharacterSelectPreviewAnimation(string prefabName, string[] entryStates, string loopState, float loopBlendSeconds = DefaultLoopBlendSeconds)
    {
        this.prefabName = prefabName;
        this.entryStates = new List<string>(entryStates ?? Array.Empty<string>());
        this.loopState = loopState;
        this.loopBlendSeconds = loopBlendSeconds;
    }
}

/// <summary>
/// 캐릭터 선택 미리보기용 표시 복제본 풀(할 일 199) — 클라마다 로컬, 네트워크 무관(스폰 아님).
/// 로스터 캐릭터마다 Variant 프리팹에서 <c>Armature</c>·<c>PlayerWaeponSlot</c> 만 떼어 Animator·렌더러·화이트리스트 스크립트만 남긴다.
/// <list type="bullet">
/// <item>만들기 = 꺼진 홀더 아래 Variant 전체 Instantiate(Awake 없음) → 두 자식 말고 자식 파괴·루트 컴포넌트는 Transform 만(같은 복제라 무기 ↔ 소켓 참조 유지) → 꺼진 채 컨테이너로.</item>
/// <item>남기는 스크립트 = 애니 이벤트 수신자(부모가 없어 아무 일도 안 함 — "no receiver" 오류 방지)·무기 따라가기·팔 비틀기·어쌔신 머리카락·디졸브 오버레이·이펙트 소켓(거너 Q 빔 — 코드가 부를 때만). 네트워크 컴포넌트·콜라이더·리지드바디는 지운다. 파티클·트레일은 끈다.</item>
/// <item>컨테이너는 넘겨받은 씬(로비)에 둔다 — 씬이 내려가면 같이 사라진다.</item>
/// </list>
/// </summary>
public sealed class CharacterSelectPreviewPool
{
    public const string ArmatureName = "Armature";
    public const string WeaponSlotName = "PlayerWaeponSlot";
    private const float HoldNormalizedTime = 0.999f; // 홀드 정지 위치 — 1 이면 컨트롤러 끝 전이가 걸린다

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
        typeof(EffectSocketPlayer), // 거너 Q 빔 미리보기 — 이벤트 중계(EffectAnimEvents)는 지워져 코드가 부를 때만 켜진다
    };

    private sealed class Preview
    {
        public GameObject Root;
        public Animator Animator;
        public string PrefabName;
        public int[] EntryHashes = Array.Empty<int>();
        public int LoopHash;
        public float LoopBlend;
        public float ChainBlend;
        public bool HoldLastPose;
        public int Step = -1;           // 재생 중인 등장 칸, -1 = 등장 끝(루프·홀드)
        public float StepEnd = -1f;     // 그 칸에서 넘길 정규화 시각, -1 = 아직 안 읽음
        public EffectSocketPlayer Beam; // 거너 Q 빔 소켓(복제본 안) — 없으면 null
        public float BeamUnitLength = 1f;
        public float BeamHold;
        public float BeamTime = -1f;
        public float BeamLength;
        public bool BeamFired;
        public float BeamStopAt;
        public bool Aura;               // 어쌔신 R 변신 오라 — 보이는 동안 계속
        public Vector3 AuraOffset;
        public float AuraScale = 1f;
        public EffectHandle AuraLoop;
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
            StartAura(target);
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
        {
            if (_previews[i] == null)
                continue;
            StopAura(_previews[i]);
            if (_previews[i].Root != null && _previews[i].Root.activeSelf)
                _previews[i].Root.SetActive(false);
        }
        _shown = -1;
    }

    // 어쌔신 R 변신 오라 — 게임(AssassinSkillView.HandleTransformChanged)과 같은 엔트리·오프셋·배율로 복제본 루트를 따라간다.
    private static void StartAura(Preview preview)
    {
        if (!preview.Aura || preview.AuraLoop.IsSet || !EffectManager.TryGet(out EffectManager effects))
            return;
        EffectEntry entry = effects.Catalog.Assassin_R_BladeVortex;
        if (entry != null)
            preview.AuraLoop = effects.PlayLooping(entry, preview.Root.transform, preview.AuraOffset, preview.AuraScale);
    }

    // 카드 교체·패널 닫힘 — 따라갈 복제본이 꺼지므로 outro 없이 바로 반납.
    private static void StopAura(Preview preview)
    {
        if (preview.AuraLoop.IsSet && EffectManager.Instance != null)
            EffectManager.Instance.ReleaseImmediate(preview.AuraLoop);
        preview.AuraLoop = EffectHandle.None;
    }

    /// <summary>보이는 복제본의 등장 칸 넘김 → 루프(또는 홀드) + 빔. 매 프레임.</summary>
    public void Tick()
    {
        Preview shown = _shown >= 0 && _shown < _previews.Count ? _previews[_shown] : null;
        if (shown == null || shown.Animator == null)
            return;

        TickBeam(shown);
        if (shown.Step < 0 || shown.Animator.IsInTransition(0))
            return;

        AnimatorStateInfo state = shown.Animator.GetCurrentAnimatorStateInfo(0);
        if (state.shortNameHash != shown.EntryHashes[shown.Step])
        {
            shown.Step = -1; // 컨트롤러가 이미 넘겼다 — 다시 크로스페이드하면 두 번 바뀐다
            return;
        }

        bool last = shown.Step == shown.EntryHashes.Length - 1;
        if (last && shown.HoldLastPose)
        {
            // 홀드 = End 이벤트가 아니라 클립 끝 프레임까지 재생 후 정지. 컨트롤러 끝 전이(Exit Time 1 → Idle/Idle_Combat)가
            // 1 을 넘는 순간 먼저 바꾸므로, 다음 프레임에 1 에 닿을 때 직전(0.999)에 세운다 — 끝 프레임과 구분 안 됨.
            float step = Time.deltaTime * shown.Animator.speed * state.speed * state.speedMultiplier / Mathf.Max(state.length, 0.0001f);
            if (state.normalizedTime + step < HoldNormalizedTime)
                return;
            shown.Step = -1;
            shown.Animator.Play(state.shortNameHash, 0, HoldNormalizedTime);
            shown.Animator.speed = 0f;
            shown.Animator.Update(0f);
            return;
        }
        if (shown.StepEnd < 0f)
            shown.StepEnd = StepEndNormalized(shown.Animator, state, last ? shown.LoopBlend : shown.ChainBlend);
        // 컨트롤러 끝 전이(Exit Time 1, 0.05~0.1초 컷에 가까움)보다 먼저 잡는다 — 끝까지 기다리면 그 전이가 먼저 바꾸고
        // 코드가 한 번 더 바꿔(같은 루프 재시작) 뚝 끊겨 보였다(199 Play).
        if (state.normalizedTime < shown.StepEnd)
            return;

        if (!last)
        {
            shown.Step++;
            shown.StepEnd = -1f;
            shown.Animator.CrossFadeInFixedTime(shown.EntryHashes[shown.Step], shown.ChainBlend, 0);
            return;
        }

        shown.Step = -1;
        if (shown.LoopHash != 0)
            shown.Animator.CrossFadeInFixedTime(shown.LoopHash, shown.LoopBlend, 0);
    }

    /// <summary>
    /// 지금 칸에서 다음으로 넘길 정규화 시각 = 그 클립의 End 애니 이벤트(평타 <c>HandleDefaultAttackEvent(End)</c>·스킬 <c>HandleSkillEvent(End)</c>) —
    /// 게임에서 그 동작이 끝났다고 보는 곳(후딜 시작). 없으면 클립 끝 − 블렌드.
    /// </summary>
    private static float StepEndNormalized(Animator animator, AnimatorStateInfo state, float blendSeconds)
    {
        AnimatorClipInfo[] clips = animator.GetCurrentAnimatorClipInfo(0);
        AnimationClip clip = clips.Length > 0 ? clips[0].clip : null;
        if (clip != null && clip.length > 0f)
        {
            foreach (AnimationEvent e in clip.events)
            {
                bool end = (e.functionName == nameof(PlayerAnimationEventRelay.HandleDefaultAttackEvent) && e.intParameter == (int)DefaultAttackAnimationEventType.End)
                    || (e.functionName == nameof(PlayerAnimationEventRelay.HandleSkillEvent) && e.intParameter == (int)SkillAnimationEventType.End);
                if (end)
                    return e.time / clip.length;
            }
        }
        return state.length > 0f ? Mathf.Clamp01(1f - blendSeconds / state.length) : 1f;
    }

    // 거너 Q 빔 — 첫 등장 칸의 BeamTime 에 켜고, 켜진 동안 복제본 앞으로 겨눠 늘이고, 유지 시간이 지나면 끈다.
    // 끄기·풀 반납은 EffectSocketPlayer 가 한다(Stop, 복제본이 꺼지면 OnDisable).
    private static void TickBeam(Preview shown)
    {
        if (shown.Beam == null)
            return;

        if (!shown.BeamFired && shown.BeamTime >= 0f && shown.EntryHashes.Length > 0)
        {
            AnimatorStateInfo state = shown.Animator.GetCurrentAnimatorStateInfo(0);
            if (state.shortNameHash != shown.EntryHashes[0])
            {
                shown.BeamFired = true; // 첫 칸을 지나쳤다 — 이번 회차는 안 쏜다
            }
            else if (state.normalizedTime >= shown.BeamTime)
            {
                shown.BeamFired = true;
                shown.Beam.Play();
                shown.BeamStopAt = Time.time + shown.BeamHold;
            }
        }

        if (!shown.Beam.IsPlaying)
            return;
        if (Time.time >= shown.BeamStopAt)
        {
            shown.Beam.Stop();
            return;
        }
        Transform root = shown.Root.transform;
        GunnerBeamView.AimAndStretch(shown.Beam, shown.Beam.Socket.position + root.forward * shown.BeamLength, shown.BeamUnitLength);
    }

    public void Dispose()
    {
        HideAll();
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
        if (animation != null)
        {
            var hashes = new List<int>();
            if (animation.entryStates != null)
                foreach (string stateName in animation.entryStates)
                {
                    int hash = ResolveState(preview.Animator, stateName, prefab.name);
                    if (hash != 0)
                        hashes.Add(hash);
                }
            preview.EntryHashes = hashes.ToArray();
            preview.LoopHash = ResolveState(preview.Animator, animation.loopState, prefab.name);
            preview.LoopBlend = Mathf.Max(0f, animation.loopBlendSeconds);
            preview.ChainBlend = Mathf.Max(0f, animation.chainBlendSeconds);
            preview.HoldLastPose = animation.holdLastPose;
            if (animation.beamNormalizedTime >= 0f)
                BindBeam(preview, prefab, animation);
            if (animation.transformAura)
                BindAura(preview, prefab);
        }
        else
        {
            preview.LoopBlend = CharacterSelectPreviewAnimation.DefaultLoopBlendSeconds;
        }
        root.SetActive(false);
        return preview;
    }

    // 거너 빔 소켓 = 프리팹 GunnerBeamView 가 쓰는 Q 빔 EffectSocketPlayer 의 복제본 쪽 짝(같은 경로 — Armature 아래 총구).
    private static void BindBeam(Preview preview, GameObject prefab, CharacterSelectPreviewAnimation animation)
    {
        GunnerBeamView view = prefab.GetComponentInChildren<GunnerBeamView>(true);
        if (view == null || view.ChargeBeamPlayer == null)
        {
            Debug.LogWarning($"[CharacterSelectPreviewPool] {prefab.name} 에 GunnerBeamView Q 빔 소켓이 없다 — 미리보기 빔을 안 쏜다.");
            return;
        }
        string path = RelativePath(prefab.transform, view.ChargeBeamPlayer.transform);
        Transform found = path != null ? preview.Root.transform.Find(path) : null;
        preview.Beam = found != null ? found.GetComponent<EffectSocketPlayer>() : null;
        if (preview.Beam == null)
        {
            Debug.LogWarning($"[CharacterSelectPreviewPool] {prefab.name} 미리보기에 빔 소켓 {path} 이 없다(Armature·무기 슬롯 밖) — 빔을 안 쏜다.");
            return;
        }
        preview.BeamUnitLength = view.ChargeBeamUnitLength;
        preview.BeamHold = view.ChargeBeamHoldDuration;
        preview.BeamTime = animation.beamNormalizedTime;
        preview.BeamLength = animation.beamLength;
    }

    private static void BindAura(Preview preview, GameObject prefab)
    {
        AssassinSkillView view = prefab.GetComponentInChildren<AssassinSkillView>(true);
        if (view == null)
        {
            Debug.LogWarning($"[CharacterSelectPreviewPool] {prefab.name} 에 AssassinSkillView 가 없다 — 변신 오라를 안 켠다.");
            return;
        }
        preview.Aura = true;
        preview.AuraOffset = view.TransformEffectOffset;
        preview.AuraScale = view.TransformEffectScale;
    }

    private static string RelativePath(Transform root, Transform target)
    {
        string path = null;
        for (Transform t = target; t != null; t = t.parent)
        {
            if (t == root)
                return path;
            path = path == null ? t.name : $"{t.name}/{path}";
        }
        return null;
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
        if (preview.Beam != null)
            preview.Beam.Stop();
        preview.BeamFired = false;
        if (preview.Animator == null)
            return;
        preview.Animator.speed = 1f; // 지난 회차 홀드 풀기
        preview.StepEnd = -1f;
        if (preview.EntryHashes.Length > 0)
        {
            preview.Animator.Play(preview.EntryHashes[0], 0, 0f);
            preview.Step = 0;
        }
        else
        {
            preview.Step = -1;
            if (preview.LoopHash != 0)
                preview.Animator.Play(preview.LoopHash, 0, 0f);
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
