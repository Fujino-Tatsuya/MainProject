using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 — ZZZ 글리치(PLAN-boss-victory-glitch) 로컬 표시. <see cref="BossEncounterDirector"/> 가 런타임에 붙인다(모든 피어).
/// 복제 상태 <see cref="BossEncounterDirector.VictoryState"/>(막타·보스·처치 시각)만 읽는다 — 새 RPC·복제 값 없음.
///
/// <para><b>2단계(지금)</b> — 처치 판정 즉시: 이 피어의 스킬 이펙트·장판 표시·투사체 소거(Q11) · 대상별 시간 배율(전역 timeScale 아님):
/// 처치자 1 · 그 외 플레이어 0.05 · 보스 0. 처치자 캐릭터에 처치 연출 스킬(Q9, 칸 = <see cref="PlayerVictoryFinisher"/>)을 표시로만 재생하고,
/// 그 클립 End 이벤트(이 피어 화면 기준, Q12)에서 처치자·보스 0.1 → <see cref="FinisherEnded"/>(글리치 시작 — 3단계가 구독).</para>
/// </summary>
[DefaultExecutionOrder(10001)]   // PlayerAttackSpeedAnimator(10000)가 Update 에서 쓰는 animator.speed 를 그 뒤에 덮는다
[DisallowMultipleComponent]
public sealed class BossVictoryGlitchPresentation : MonoBehaviour
{
    public enum Phase
    {
        None,       // 연출 없음
        Finisher,   // 처치 판정 ~ 처치 연출 스킬 End(처치자 1 · 그 외 0.05 · 보스 0)
        Slow,       // End 이후(처치자 0.1 · 그 외 0.05 · 보스 0.1) — 글리치가 이 순간 시작
    }

    enum Role { Killer, Other, Boss }

    /// <summary>[이 피어] 지금 연출(없으면 null).</summary>
    public static BossVictoryGlitchPresentation Instance { get; private set; }

    /// <summary>[이 피어] 처치자 처치 연출 스킬이 End 에 닿았다 = 처치자 0.1 순간 = 글리치 시작(Q10).</summary>
    public static event Action<BossEncounterDirector.VictoryState> FinisherEnded;

    BossEncounterDirector _director;
    BossVictoryGlitchSettings _settings;
    int _sequence = -1;
    bool _running;
    Phase _phase = Phase.None;
    BossEncounterDirector.VictoryState _state;

    Player _killer;
    GameObject _boss;
    PlayerSkillController _killerSkills;
    bool _finisherPending;
    bool _finisherWarned;

    readonly List<GameObject> _others = new List<GameObject>();
    readonly List<Animator> _animators = new List<Animator>();
    readonly List<Role> _animatorRoles = new List<Role>();
    readonly List<float> _animatorSpeeds = new List<float>();     // 덮기 전 원래 속도
    readonly List<float> _animatorLastSet = new List<float>();    // 이 연출이 마지막으로 쓴 속도(다르면 다른 계통이 바꾼 것)
    readonly List<bool> _animatorOverridden = new List<bool>();
    readonly Dictionary<ParticleSystem, float> _particleSpeeds = new Dictionary<ParticleSystem, float>();

    public Phase CurrentPhase => _phase;

    /// <summary>[이 피어] 보스 시간 배율 — 서버(호스트)의 사망 표현 대기도 이 값으로 흐른다(<see cref="BossEncounterDirector.VictoryDeathTimeScaleFor"/>).</summary>
    public float BossTimeScale => _phase switch
    {
        Phase.Finisher => 0f,
        Phase.Slow => _settings != null ? _settings.bossAfterFinisherScale : 0.1f,
        _ => 1f,
    };

    void Awake()
    {
        _director = GetComponent<BossEncounterDirector>();
    }

    void OnEnable() => Instance = this;

    void OnDisable()
    {
        Teardown();
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (_director == null || !_director.IsSpawned) return;
        BossEncounterDirector.VictoryState v = _director.Victory;
        if (!v.Active)
        {
            if (_running) Teardown();   // 실게임에선 씬 끝까지 켜져 있다 — 꺼지는 건 디버그 랩 원상복구뿐
            return;
        }
        if (v.Sequence != _sequence)
        {
            Teardown();
            _sequence = v.Sequence;
            Begin(v);
        }
        if (!_running) return;
        if (_finisherPending) TryStartFinisher();
        TickAnimators();
    }

    // ── 시작 ───────────────────────────────────────────────────────────────

    void Begin(BossEncounterDirector.VictoryState v)
    {
        _settings = BossVictoryGlitchSettings.Load();
        _state = v;
        _running = true;
        _phase = Phase.Finisher;
        _finisherPending = true;
        _finisherWarned = false;

        PlayerLifeInputPolicy.SetVictoryInputHeld(true);

        _boss = FindSpawned(v.BossNetworkObjectId);
        _killer = null;
        _others.Clear();
        foreach (Player p in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (p == null) continue;
            if (p.OwnerClientId == v.KillerClientId) _killer = p;
            else _others.Add(p.gameObject);
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
        // 디버그 랩(244) 표시용 팀원 복제본 — Player 가 아니라 위에서 안 잡힌다. 비어 있으면 실게임과 같다.
        foreach (GameObject mate in BossEncounterDirector.DevVictoryExtraTeammates)
            if (mate != null && mate.activeInHierarchy) _others.Add(mate);
#endif
        _killerSkills = _killer != null ? _killer.GetComponent<PlayerSkillController>() : null;

        int cleared = ClearEffectsLocal();

        if (_killer != null) CollectAnimators(_killer.gameObject, Role.Killer);
        for (int i = 0; i < _others.Count; i++) CollectAnimators(_others[i], Role.Other);
        if (_boss != null) CollectAnimators(_boss, Role.Boss);
        ApplyEffectRates();

        if (_killer == null)
            Edit.LogWarning($"[BossVictory] 막타 client {v.KillerClientId} 의 플레이어가 이 피어에 없다 — 처치 연출 스킬을 못 튼다.", this);
        Edit.Log($"[BossVictory] 글리치 연출 시작 — 회차 {v.Sequence} · 막타 client {v.KillerClientId} · 팀원 {_others.Count} · " +
                 $"Animator {_animators.Count} · 이펙트 소거 {cleared}", this);
    }

    // 처치 판정 즉시 이 피어 표시 소거(Q11). 서버 판정 장판·투사체는 디렉터가 서버에서 디스폰한다(TryStartVictoryServer).
    int ClearEffectsLocal()
    {
        int count = 0;
        if (EffectManager.TryGet(out EffectManager effects, this)) count += effects.ReleaseAllImmediate();
        if (_boss != null && _boss.TryGetComponent(out TwentyThreeBoss twentyThree)) count += twentyThree.StopPresentationEffectsLocal();
        foreach (DefaultAttackProjectile projectile in FindObjectsByType<DefaultAttackProjectile>(FindObjectsSortMode.None))
        {
            if (projectile == null) continue;
            Destroy(projectile.gameObject);
            count++;
        }
        return count;
    }

    // ── 처치 연출 스킬(Q9) ─────────────────────────────────────────────────

    void TryStartFinisher()
    {
        if (_killerSkills == null) { _finisherPending = false; return; }
        // 서버의 스킬 취소(연출 잠금)가 이 피어에 닿아 실행 중 스킬이 없어진 뒤에 튼다 — 취소의 자세 복귀가 처치 연출 애니를 덮지 않게.
        if (_killerSkills.ActiveSkill != null) return;

        _finisherPending = false;
        PlayerSkillSlot slot = PlayerVictoryFinisher.ResolveSlot(_killer.gameObject, _settings);
        PlayerSkillBase skill = _killerSkills.GetSkill(slot);
        if (skill == null || skill.Data == null)
        {
            Edit.LogError($"[BossVictory] 처치 연출 스킬이 없다 — {_killer.name} 칸 {slot}. 글리치가 시작되지 않는다.", this);
            return;
        }
        WarnIfNoEndEvent(skill);

        Vector3 dir = _boss != null ? _boss.transform.position - _killer.transform.position : _killer.transform.forward;
        dir.y = 0f;
        _killerSkills.PresentationSkillEnded += OnFinisherEnded;
        if (!_killerSkills.PlayPresentationOnly(skill, dir))
        {
            _killerSkills.PresentationSkillEnded -= OnFinisherEnded;
            Edit.LogError($"[BossVictory] 처치 연출 스킬 재생 실패 — {skill.name}", this);
            return;
        }
        Edit.Log($"[BossVictory] 처치 연출 스킬 — {_killer.name} {slot} {skill.Data.AnimatorStateName}", this);
    }

    // 고른 스킬 클립에 End 이벤트가 없으면 처치자가 영영 안 느려진다 — 대체 경로 없이 오류로 알린다(EditMode 테스트가 미리 잡는다).
    void WarnIfNoEndEvent(PlayerSkillBase skill)
    {
        if (_finisherWarned) return;
        Animator animator = _killer.GetComponentInChildren<Animator>(true);
        RuntimeAnimatorController controller = animator != null ? animator.runtimeAnimatorController : null;
        AnimationClip clip = controller != null ? AnimationEventRequirements.FindStateClip(controller, skill.Data.AnimatorStateName) : null;
        if (clip != null && AnimationEventRequirements.HasEvent(clip, nameof(PlayerAnimationEventRelay.HandleSkillEvent), (int)SkillAnimationEventType.End))
            return;
        _finisherWarned = true;
        Edit.LogError($"[BossVictory] 처치 연출 스킬 {skill.name}(상태 {skill.Data.AnimatorStateName}) 클립에 Skill End 이벤트가 없다 — 글리치가 시작되지 않는다.", this);
    }

    void OnFinisherEnded(PlayerSkillBase skill)
    {
        if (_killerSkills != null) _killerSkills.PresentationSkillEnded -= OnFinisherEnded;
        if (!_running || _phase != Phase.Finisher) return;
        _phase = Phase.Slow;
        ApplyEffectRates();
        Edit.Log($"[BossVictory] 처치 연출 스킬 End — 처치자 ×{_settings.killerAfterFinisherScale} · 보스 ×{_settings.bossAfterFinisherScale} · 글리치 시작", this);
        FinisherEnded?.Invoke(_state);
    }

    // ── 시간 배율 ──────────────────────────────────────────────────────────

    void CollectAnimators(GameObject root, Role role)
    {
        foreach (Animator a in root.GetComponentsInChildren<Animator>(true))
        {
            if (a == null || _animators.Contains(a)) continue;
            _animators.Add(a);
            _animatorRoles.Add(role);
            _animatorSpeeds.Add(a.speed > 0f ? a.speed : 1f);
            _animatorLastSet.Add(0f);
            _animatorOverridden.Add(false);
        }
    }

    float Scale(Role role) => role switch
    {
        Role.Killer => _phase == Phase.Slow ? _settings.killerAfterFinisherScale : -1f,   // 처치 연출 동안은 덮지 않는다(1)
        Role.Other => _settings.otherPlayersScale,
        Role.Boss => BossTimeScale,
        _ => -1f,
    };

    // 매 프레임(실행 순서 10001) — 다른 계통(공속 배율 등)이 Update 에서 쓴 값을 그 뒤에 덮는다.
    void TickAnimators()
    {
        for (int i = 0; i < _animators.Count; i++)
        {
            Animator a = _animators[i];
            if (a == null || !a.isActiveAndEnabled) continue;
            float scale = Scale(_animatorRoles[i]);
            if (scale < 0f)
            {
                if (_animatorOverridden[i]) { a.speed = _animatorSpeeds[i]; _animatorOverridden[i] = false; }
                continue;
            }
            // 덮기 전 값, 또는 덮는 동안 다른 계통이 바꾼 값 = 풀 때 돌릴 "원래 값".
            if (a.speed > 0f && (!_animatorOverridden[i] || !Mathf.Approximately(a.speed, _animatorLastSet[i])))
                _animatorSpeeds[i] = a.speed;
            float want = _animatorSpeeds[i] * scale;
            if (!Mathf.Approximately(a.speed, want)) a.speed = want;
            _animatorLastSet[i] = want;
            _animatorOverridden[i] = true;
        }
    }

    // 이펙트 — EffectManager 추종 이펙트 + 대상 몸 아래 파티클. 단계가 바뀔 때만(처치 연출 이펙트는 End 뒤 처치자 배율로).
    void ApplyEffectRates()
    {
        float killer = _phase == Phase.Slow ? _settings.killerAfterFinisherScale : 1f;
        if (_killer != null) SetEffectRate(_killer.transform, killer);
        for (int i = 0; i < _others.Count; i++)
            if (_others[i] != null) SetEffectRate(_others[i].transform, _settings.otherPlayersScale);
        if (_boss != null) SetEffectRate(_boss.transform, BossTimeScale);
    }

    void SetEffectRate(Transform root, float rate)
    {
        if (EffectManager.TryGet(out EffectManager effects, this)) effects.SetPlayRateForTarget(root, rate);
        foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (ps == null) continue;
            ParticleSystem.MainModule main = ps.main;
            if (!_particleSpeeds.TryGetValue(ps, out float original))
            {
                original = main.simulationSpeed;
                _particleSpeeds[ps] = original;
            }
            main.simulationSpeed = original * rate;
        }
    }

    // ── 정리 ───────────────────────────────────────────────────────────────

    void Teardown()
    {
        if (!_running) return;
        _running = false;
        _phase = Phase.None;
        _finisherPending = false;

        if (_killerSkills != null)
        {
            _killerSkills.PresentationSkillEnded -= OnFinisherEnded;
            if (_killerSkills.PresentationSkill != null) _killerSkills.StopPresentationOnly();
        }

        for (int i = 0; i < _animators.Count; i++)
            if (_animators[i] != null && _animatorOverridden[i]) _animators[i].speed = _animatorSpeeds[i];
        _animators.Clear();
        _animatorRoles.Clear();
        _animatorSpeeds.Clear();
        _animatorLastSet.Clear();
        _animatorOverridden.Clear();

        if (EffectManager.TryGet(out EffectManager effects, this))
        {
            if (_killer != null) effects.SetPlayRateForTarget(_killer.transform, 1f);
            for (int i = 0; i < _others.Count; i++)
                if (_others[i] != null) effects.SetPlayRateForTarget(_others[i].transform, 1f);
            if (_boss != null) effects.SetPlayRateForTarget(_boss.transform, 1f);
        }
        foreach (KeyValuePair<ParticleSystem, float> pair in _particleSpeeds)
        {
            if (pair.Key == null) continue;
            ParticleSystem.MainModule main = pair.Key.main;
            main.simulationSpeed = pair.Value;
        }
        _particleSpeeds.Clear();

        _killer = null;
        _killerSkills = null;
        _boss = null;
        _others.Clear();
        PlayerLifeInputPolicy.SetVictoryInputHeld(false);
    }

    static GameObject FindSpawned(ulong networkObjectId)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || nm.SpawnManager == null) return null;
        return nm.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject no) && no != null ? no.gameObject : null;
    }
}
