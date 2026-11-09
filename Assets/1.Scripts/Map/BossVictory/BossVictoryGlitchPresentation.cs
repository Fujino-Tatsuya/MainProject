using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using VeyTrace.Rendering.Occlusion;

/// <summary>
/// 보스 처치 연출 — ZZZ 글리치(PLAN-boss-victory-glitch) 로컬 표시. <see cref="BossEncounterDirector"/> 가 런타임에 붙인다(모든 피어).
/// 복제 상태 <see cref="BossEncounterDirector.VictoryState"/>(막타·보스·처치 시각)만 읽는다 — 새 RPC·복제 값 없음.
///
/// <para><b>2단계(지금)</b> — 처치 판정 즉시: 이 피어의 스킬 이펙트·장판 표시·투사체 소거(Q11) · 대상별 시간 배율(전역 timeScale 아님):
/// 처치자 1 · 그 외 플레이어 0.05 · 보스 0. 처치자 캐릭터에 처치 연출 스킬(Q9, 칸 = <see cref="PlayerVictoryFinisher"/>)을 표시로만 재생하고,
/// 그 클립 End 이벤트(이 피어 화면 기준, Q12)에서 처치자·보스 0.1 → <see cref="FinisherEnded"/>.</para>
/// <para><b>3단계</b> — <b>처치 판정 순간부터</b>(11-09 사양 변경 — 처치 연출 스킬은 글리치 속에서 재생) 화면 글리치(<see cref="BossVictoryGlitchRender"/>,
/// 세기 = 설정 SO 타임라인 곡선, 시계 <see cref="GlitchTime"/> = 복제 처치 시각 기준) ·
/// 플레이어 흑백·보스 노이즈(렌더링 레이어 비트) · HUD 숨김.</para>
/// <para><b>4단계</b> — 글리치 시계로 카메라 컷(설정 SO 목록, <see cref="BossVictoryCameraCuts"/>): 첫 컷부터 연출 vcam(블렌드 없음) · 컷마다 그 순간 위치로 구도 고정 ·
/// 연출 동안 월드 UI·윤곽선·벽 가림·카메라 흔들림 끔 · 카메라를 향하지 않는 벽 숨김.</para>
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

    /// <summary>[이 피어] 처치자 처치 연출 스킬이 End 에 닿았다 = 처치자·보스 0.1 순간(글리치 시작 신호는 아니다 — 글리치는 처치 판정 순간부터).</summary>
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

    // 3단계 — 화면 글리치(처치 판정 순간부터)
    bool _glitchOn;
    bool _renderOn;
    float _glitchTime;
    bool _hudHidden;
    CinematicOverlay _overlay;
    readonly List<Renderer> _tagged = new List<Renderer>();
    BossVictoryRectifyText _text;   // 5단계

    // 4단계 — 카메라 컷
    int _cutIndex = -1;
    bool _cutSuppressed;
    CinemachineExternalImpulseListener _impulseListener;
    bool _impulseWasEnabled;
    readonly List<Canvas> _hiddenWorldCanvases = new List<Canvas>();
    readonly List<Renderer> _wallCandidates = new List<Renderer>();
    readonly List<Vector3> _wallNormals = new List<Vector3>();
    readonly List<Vector3> _wallCenters = new List<Vector3>();
    readonly List<Renderer> _hiddenWalls = new List<Renderer>();

    /// <summary>[이 피어] 지금 카메라 컷 번호(설정 목록 — 게임 카메라면 -1).</summary>
    public int CutIndex => _cutIndex;

    /// <summary>[이 피어] 처치 판정 뒤 초(연출 없으면 -1). 컷·글자가 같은 시계를 읽는다.</summary>
    public float GlitchTime => _glitchOn ? _glitchTime : -1f;

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
        if (_glitchOn) TickGlitch();
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

        // 글리치 = 처치 판정 순간부터(복제 처치 시각 기준 — 늦게 받은 피어는 그만큼 앞에서 시작). 이후는 게임 시간(랩 배속·일시정지에 같이 멈춤).
        double serverNow = _director.NetworkManager != null ? _director.NetworkManager.ServerTime.Time : v.StartServerTime;
        StartGlitch(Mathf.Max(0f, v.Elapsed(serverNow)));

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
        Edit.LogError($"[BossVictory] 처치 연출 스킬 {skill.name}(상태 {skill.Data.AnimatorStateName}) 클립에 Skill End 이벤트가 없다 — 처치자·보스가 0.1 로 느려지지 않는다.", this);
    }

    void OnFinisherEnded(PlayerSkillBase skill)
    {
        if (_killerSkills != null) _killerSkills.PresentationSkillEnded -= OnFinisherEnded;
        if (!_running || _phase != Phase.Finisher) return;
        _phase = Phase.Slow;
        ApplyEffectRates();
        Edit.Log($"[BossVictory] 처치 연출 스킬 End — 처치자 ×{_settings.killerAfterFinisherScale} · 보스 ×{_settings.bossAfterFinisherScale} (글리치 t {_glitchTime:0.00})", this);
        FinisherEnded?.Invoke(_state);
    }

    // ── 화면 글리치(3단계) ─────────────────────────────────────────────────

    void StartGlitch(float startTime)
    {
        _glitchOn = true;
        _glitchTime = startTime;
        _overlay ??= new CinematicOverlay("BossVictoryGlitch", null);
        _text = new BossVictoryRectifyText(_overlay.Root, _settings, _director != null ? _director.IntroFont : null);
        _renderOn = BossVictoryGlitchRender.Enable(_settings);
        if (!_renderOn)
        {
            Edit.LogWarning($"[BossVictory] 셰이더({BossVictoryGlitchRender.MaskShaderName}·{BossVictoryGlitchRender.CompositeShaderName})가 없어 글리치 없이 돈다.", this);
            return;
        }
        // 흑백 = 플레이어(처치자·팀원, 렌더링 레이어 비트) · 노이즈 = 보스.
        if (_killer != null) BossVictoryGlitchRender.Tag(_killer.gameObject, BossVictoryGlitchRender.PlayerMask, _tagged);
        for (int i = 0; i < _others.Count; i++) BossVictoryGlitchRender.Tag(_others[i], BossVictoryGlitchRender.PlayerMask, _tagged);
        if (_boss != null) BossVictoryGlitchRender.Tag(_boss, BossVictoryGlitchRender.BossMask, _tagged);
    }

    // 글리치 시계 = 게임 시간(랩 배속·일시정지에 같이 멈춘다). 실게임은 timeScale 1.
    void TickGlitch()
    {
        _glitchTime += Time.deltaTime;
        float t = Mathf.Min(_glitchTime, _settings.glitchDuration);
        BossVictoryGlitchSettings s = _settings;
        TickCuts();
        _text?.Tick(t, _glitchTime, s);

        bool hide = s.hudHide.Evaluate(t) > 0.5f;
        if (hide && !_hudHidden)
        {
            _overlay ??= new CinematicOverlay("BossVictoryGlitch", null);
            _overlay.HideHud();
            _hudHidden = true;
        }

        if (!_renderOn) return;
        var frame = new BossVictoryGlitchRender.Frame
        {
            Master = s.master.Evaluate(t),
            Time = _glitchTime,
            Desaturate = s.desaturate.Evaluate(t),
            Amber = s.amber.Evaluate(t),
            Scanline = s.scanline.Evaluate(t),
            Wave = s.wave.Evaluate(t),
            Step = s.stepShift.Evaluate(t),
            Stretch = s.stretch.Evaluate(t),
            Block = s.block.Evaluate(t),
            Smear = s.smear.Evaluate(t),
            Slice = s.slice.Evaluate(t),
            RgbSplit = s.rgbSplit.Evaluate(t),
            Flash = s.flash.Evaluate(t),
            CharGlitch = s.characterGlitch.Evaluate(t),
            BossNoise = s.bossNoise.Evaluate(t),
            BossJiggle = s.bossJiggle.Evaluate(t),
        };
        BossScreenRect(out frame.BossScreenCenter, out frame.BossScreenSize);
        BossVictoryGlitchRender.SetFrame(frame);
    }

    // ── 카메라 컷(4단계) ───────────────────────────────────────────────────

    // 컷 시계 = 글리치 시계(길이로 자르지 않는다 — 마지막 컷이 끝까지 유지). 구도는 컷이 바뀌는 순간 한 번 계산해 고정한다(흔들림 없음).
    void TickCuts()
    {
        int index = BossVictoryCameraCuts.ActiveIndex(_settings.cuts, _glitchTime);
        if (index == _cutIndex || index < 0) return;
        _cutIndex = index;
        BossVictoryCameraCuts.Cut cut = _settings.cuts[index];
        BossVictoryCameraCuts.Subjects subjects = CutSubjects();
        BossVictoryCameraCuts.Pose(cut, subjects, out Vector3 pos, out Quaternion rot);

        _overlay ??= new CinematicOverlay("BossVictoryGlitch", null);
        if (!_overlay.IsCameraActive)
        {
            if (!_overlay.EnterCamera(pos, rot, cut.fov, 0f))
                Edit.LogWarning("[BossVictory] CameraTargetSwitcher 가 없어 컷 카메라를 못 켰다.", this);
            SuppressForCuts(subjects);
        }
        else _overlay.PlaceCamera(pos, rot, cut.fov);
        _overlay.SetDimFocus(BossVictoryCameraCuts.Focus(cut, subjects));
        TickWalls(pos, rot * Vector3.forward);
        Edit.Log($"[BossVictory] 컷 {index} {cut.kind} — t {_glitchTime:0.00}", this);
    }

    BossVictoryCameraCuts.Subjects CutSubjects()
    {
        var s = new BossVictoryCameraCuts.Subjects();
        s.Killer = _killer != null ? _killer.transform.position : _state.HitPoint;
        s.BossBase = _boss != null ? _boss.transform.position : _state.HitPoint;
        s.BossCenter = s.BossBase + Vector3.up;
        if (_boss != null && TryBossBounds(out Bounds b)) s.BossCenter = b.center;
        return s;
    }

    bool TryBossBounds(out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (Renderer r in _boss.GetComponentsInChildren<Renderer>())
        {
            if (r == null || !r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return any;
    }

    // 컷 카메라에 들어갈 때 한 번 — 머리 위 UI·윤곽선·벽 가림·잔여 흔들림이 컷을 깬다(옛 연출과 같은 목록).
    void SuppressForCuts(BossVictoryCameraCuts.Subjects subjects)
    {
        if (_cutSuppressed) return;
        _cutSuppressed = true;
        PlayerSilhouetteFeature.SuppressedByCinematic = true;
        EnemyOutlineFeature.SuppressedByCinematic = true;
        WallOcclusionDriver.SuppressedByCinematic = true;
        WallTransparencyGroup.SuppressedByCinematic = true;

        foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (c == null || !c.enabled || !c.isRootCanvas || c.renderMode != RenderMode.WorldSpace) continue;
            c.enabled = false;
            _hiddenWorldCanvases.Add(c);
        }

        Camera main = Camera.main;
        _impulseListener = main != null ? main.GetComponent<CinemachineExternalImpulseListener>() : null;
        if (_impulseListener != null)
        {
            _impulseWasEnabled = _impulseListener.enabled;
            _impulseListener.enabled = false;
        }

        CollectWalls(Vector3.Lerp(subjects.Killer, subjects.BossBase, 0.5f));
    }

    // 벽 = Wall 레이어·Wall 태그, 또는 이름이 wall 로 시작하는 조상 아래(BossRoomAuthoring.LooksLikeWall 과 같은 기준).
    static bool IsWall(Transform t, int wallLayer)
    {
        for (; t != null; t = t.parent)
        {
            GameObject go = t.gameObject;
            if ((wallLayer >= 0 && go.layer == wallLayer) || go.CompareTag(GameLayers.Wall)) return true;
            string n = t.name;
            if (n.Length >= 4 && n.StartsWith("wall", StringComparison.OrdinalIgnoreCase) &&
                n.IndexOf("walltransparency", StringComparison.OrdinalIgnoreCase) < 0)
                return true;
        }
        return false;
    }

    void CollectWalls(Vector3 interior)
    {
        int wallLayer = LayerMask.NameToLayer(GameLayers.Wall);
        float r2 = _settings.wallSearchRadius * _settings.wallSearchRadius;
        foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r == null || !r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (_tagged.Contains(r)) continue;   // 인물·보스
            Bounds b = r.bounds;
            if (b.SqrDistance(interior) > r2) continue;
            if (!IsWall(r.transform, wallLayer)) continue;
            Vector3 n = BossVictoryCameraCuts.WallInwardNormal(b, interior);
            if (n == Vector3.zero) continue;
            _wallCandidates.Add(r);
            _wallNormals.Add(n);
            _wallCenters.Add(b.center);
        }
    }

    // 지금 컷 카메라에서 앞면이 확실히 카메라를 향하지 않는 벽(뒷면·옆면)을 끈다 — 한 번 끄면 연출 끝까지.
    void TickWalls(Vector3 camPos, Vector3 camForward)
    {
        for (int i = _wallCandidates.Count - 1; i >= 0; i--)
        {
            Renderer r = _wallCandidates[i];
            bool remove = r == null;
            if (!remove && BossVictoryCameraCuts.ShouldHideWall(_wallNormals[i], _wallCenters[i], camPos, camForward, _settings.wallFacingThreshold))
            {
                r.enabled = false;
                _hiddenWalls.Add(r);
                remove = true;
            }
            if (!remove) continue;
            _wallCandidates.RemoveAt(i);
            _wallNormals.RemoveAt(i);
            _wallCenters.RemoveAt(i);
        }
    }

    void RestoreCuts()
    {
        _cutIndex = -1;
        for (int i = 0; i < _hiddenWalls.Count; i++)
            if (_hiddenWalls[i] != null) _hiddenWalls[i].enabled = true;
        _hiddenWalls.Clear();
        _wallCandidates.Clear();
        _wallNormals.Clear();
        _wallCenters.Clear();
        for (int i = 0; i < _hiddenWorldCanvases.Count; i++)
            if (_hiddenWorldCanvases[i] != null) _hiddenWorldCanvases[i].enabled = true;
        _hiddenWorldCanvases.Clear();
        if (_impulseListener != null) _impulseListener.enabled = _impulseWasEnabled;
        _impulseListener = null;
        if (!_cutSuppressed) return;
        _cutSuppressed = false;
        PlayerSilhouetteFeature.SuppressedByCinematic = false;
        EnemyOutlineFeature.SuppressedByCinematic = false;
        WallOcclusionDriver.SuppressedByCinematic = false;
        WallTransparencyGroup.SuppressedByCinematic = false;
    }

    // 보스 화면 위치·높이(뷰포트) — 노이즈 "보스 기준" 공간용. 못 구하면 화면 가운데·높이 1.
    void BossScreenRect(out Vector2 center, out float height)
    {
        center = new Vector2(0.5f, 0.5f);
        height = 1f;
        Camera cam = Camera.main;
        if (cam == null || _boss == null) return;
        Bounds b = new Bounds(_boss.transform.position, Vector3.zero);
        foreach (Renderer r in _boss.GetComponentsInChildren<Renderer>())
            if (r != null && !(r is ParticleSystemRenderer)) b.Encapsulate(r.bounds);
        Vector3 top = cam.WorldToViewportPoint(b.center + Vector3.up * b.extents.y);
        Vector3 bottom = cam.WorldToViewportPoint(b.center - Vector3.up * b.extents.y);
        Vector3 mid = cam.WorldToViewportPoint(b.center);
        if (mid.z <= 0f) return;
        center = new Vector2(mid.x, mid.y);
        height = Mathf.Max(0.05f, Mathf.Abs(top.y - bottom.y));
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

        _glitchOn = false;
        BossVictoryGlitchRender.Untag(_tagged);
        if (_renderOn) { BossVictoryGlitchRender.Disable(); _renderOn = false; }
        _hudHidden = false;
        _text?.Dispose();
        _text = null;
        RestoreCuts();
        _overlay?.Dispose();   // HUD 복구 · 컷 카메라 나감(블렌드 없음) · 디밍 기준점 복구
        _overlay = null;

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
