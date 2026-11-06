using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using VeyTrace.Rendering.Occlusion;

/// <summary>
/// 보스 B 승리 연출 로컬 표시(PLAN-boss-victory-cinematic) — 모든 피어. 판정·진행은 서버(<see cref="BossEncounterDirector"/> partial),
/// 여기는 복제 상태(<see cref="BossEncounterDirector.Victory"/>) + 서버 시각 기준 경과로 화면을 그리기만 한다. 월드 timeScale 은 건드리지 않는다.
/// 시간 = <b>단계 시각</b>(경과 − 추가 연출 구간, <see cref="BossVictoryRules.StageTime"/>). 추가 연출 구간 동안 음수.
/// <list type="bullet">
/// <item>추가 연출 구간(할 일 200 — 길이 <c>preludeSeconds</c>, 기본 0, 아직 내용 없음): <see cref="PreludeStarted"/>·<see cref="PreludeEnded"/> 에 연출을 꽂는다.
///       이 동안 보스 몸 Animator 는 멈춰 있다(사망 애니는 그 뒤 — 웰즈는 그대로).</item>
/// <item>카메라: 처치 순간 내 플레이 카메라 그대로에서 출발(튐 없음) → 0.22–0.72 서버가 정한 24° 구도 → 2.00–3.20 32° → 유지(D4).</item>
/// <item>슬로모션(0.12–2.00, 완전 정지 아님 — 경석 10-27): 플레이어·보스 Animator 를 <c>slowMotionScale</c> 배로. 사망 표현 서버 대기도 같은 배율.
///       플레이어는 처치 순간 클립이 끝나면(또는 Idle 로 넘어가려 하면) 그 마지막 프레임에 씬이 끝날 때까지 멈춘다.</item>
/// <item>색(0.72–0.80 들어옴 → 씬 끝까지 유지, 할 일 200 D3): 배경 단색 주황 틴트(34%) · 인물·무기·이펙트 단색(D2). 글리치 없음(D4).</item>
/// <item>외곽선: 연출 동안 플레이어·보스 Flat Kit 외곽선 폭 0(D5) · 플레이어 실루엣·적 호버 외곽선 끔(D6).</item>
/// <item>벽: 벽 가림 처리(WallOcclusion·구역 벽 투명화) 끔 · 앞면이 확실히 카메라를 향한 벽만 남기고 나머지 벽 렌더러를 끈다(할 일 200 추가 — 234 벽 제한 대신).</item>
/// <item>입력: 처치 순간부터 오너 입력만 막는다(Q1 — 공격 동작 유지). 단계 2.00초 정식 잠금은 서버.</item>
/// </list>
/// 'BOSS DOWN' 글자·승리 타격 VFX 는 할 일 200 E·F 로 뺐다(VFX 칸은 비워 둠). 오브젝트는 런타임 생성(씬·프리팹 무수정). 정리는 씬이 끝날 때(OnDestroy).
/// </summary>
[DefaultExecutionOrder(10001)]   // PlayerAttackSpeedAnimator(10000)가 Update 에서 쓰는 animator.speed 를 그 뒤에 다시 덮는다(C5)
public sealed class BossVictoryPresentation : MonoBehaviour
{
    private const float WorldCanvasRescanSeconds = 0.5f;

    /// <summary>[이 피어] 추가 연출 구간 시작(할 일 200 — 연출을 꽂는 자리). 처치 순간 상태를 받는다. 늦게 받아 구간이 지났으면 안 온다.</summary>
    public static event Action<BossEncounterDirector.VictoryState> PreludeStarted;

    /// <summary>[이 피어] 추가 연출 구간 끝 — 이 뒤로 슬로모션·카메라 하강·보스 사망 애니가 이어진다. 구간이 0초면 시작 없이 끝만 올 수 있다.</summary>
    public static event Action<BossEncounterDirector.VictoryState> PreludeEnded;

    private enum AnimatorRole { Player, BossBody, Wells }
    private enum TimeMode { Normal, Prelude, Slow }

    private BossEncounterDirector _director;
    private BossVictoryConfig _cfg;
    private int _sequence = -1;
    private bool _running;
    private bool _preludeEnded;

    private CinematicOverlay _overlay;
    private Vector3 _startPos;
    private Quaternion _startRot;
    private float _startFov;

    private readonly List<Renderer> _tagged = new List<Renderer>();
    private bool _renderOn;

    private readonly List<Animator> _animators = new List<Animator>();
    private readonly List<float> _animatorSpeeds = new List<float>();   // 늦추기 전 원래 속도
    private readonly List<AnimatorRole> _animatorRole = new List<AnimatorRole>();
    private readonly List<int> _animatorStartState = new List<int>();   // 처치 순간 상태(플레이어 — 공격 클립)
    private readonly List<bool> _animatorHeld = new List<bool>();       // 그 클립이 끝나 마지막 프레임에 멈췄다(경석 10-27 — Idle 로 안 돌아감)
    private readonly List<bool> _animatorOverridden = new List<bool>(); // 지금 이 연출이 속도를 덮어쓰는 중
    private readonly List<float> _animatorLastSet = new List<float>();   // 이 연출이 마지막으로 쓴 속도 — 다르면 다른 계통이 바꾼 것
    private TimeMode _mode = TimeMode.Normal;

    private GameObject _vfx;
    private readonly List<ParticleSystem> _vfxSystems = new List<ParticleSystem>();
    private readonly List<float> _vfxSpeeds = new List<float>();

    // Flat Kit 외곽선 폭 0(D5) — HitFlash 가 렌더러 전체 블록을 null 로 비우므로 매 프레임 읽어서 덮는다.
    private static readonly int IdOutlineWidth = Shader.PropertyToID("_OutlineWidth");
    private readonly List<Renderer> _outlineRenderers = new List<Renderer>();
    private readonly List<float> _outlineWidths = new List<float>();
    private MaterialPropertyBlock _outlineBlock;

    // 벽 숨김 — 후보(벽 렌더러·방 안쪽 법선)와 이미 끈 것. 한 번 끈 벽은 씬 끝까지 꺼 둔다.
    private readonly List<Renderer> _wallCandidates = new List<Renderer>();
    private readonly List<Vector3> _wallNormals = new List<Vector3>();
    private readonly List<Vector3> _wallCenters = new List<Vector3>();
    private readonly List<Renderer> _hiddenWalls = new List<Renderer>();

    private readonly List<Canvas> _hiddenWorldCanvases = new List<Canvas>();
    private float _worldCanvasRescanAt;
    private Behaviour _impulseListener;
    private bool _impulseWasEnabled;

    private bool _sfxHitStopPlayed, _sfxMidPlayed, _sfxResumePlayed;

    private void Awake()
    {
        _director = GetComponent<BossEncounterDirector>();
    }

    private void Update()
    {
        if (_director == null || !_director.IsSpawned) return;
        BossEncounterDirector.VictoryState v = _director.Victory;
        if (!v.Active)
        {
            // 실게임에선 한 번 켜지면 씬이 끝날 때까지 켜져 있다 — 꺼지는 건 디버그 랩(244) 원상복구뿐.
            if (_running) Teardown();
            return;
        }

        if (v.Sequence != _sequence)
        {
            Teardown();
            _sequence = v.Sequence;
            Begin(v);
        }

        if (!_running) return;
        Tick(v, v.StageTime(_director.NetworkManager.ServerTime.Time));
    }

    private void OnDestroy()
    {
        Teardown();
    }

    // ── 시작 ───────────────────────────────────────────────────────────────

    private void Begin(BossEncounterDirector.VictoryState v)
    {
        _cfg = BossVictoryConfig.Load();
        _running = true;
        float t0 = v.StageTime(_director.NetworkManager.ServerTime.Time);

        // 입력 — 처치 순간부터(Q1). 오너가 아닌 플레이어엔 의미가 없다(정책이 오너만 적용).
        PlayerLifeInputPolicy.SetVictoryInputHeld(true);

        // 카메라 — 지금 렌더 카메라 그대로에서 출발한다(기획 §2 "튀면 안 된다"). 늦게 받았으면(단계 0.72 지남) 출발점 = 최종 구도.
        Camera main = Camera.main;
        _overlay ??= new CinematicOverlay("BossVictory", null);
        if (main != null && t0 < BossVictoryRules.DescendEnd)
        {
            _startPos = main.transform.position;
            _startRot = main.transform.rotation;
            _startFov = main.fieldOfView;
        }
        else
        {
            _startPos = v.FinalPosition;
            _startRot = v.FinalRotation;
            _startFov = v.Fov;
        }
        if (!_overlay.EnterCamera(_startPos, _startRot, _startFov, 0f))
            Edit.LogWarning("[BossVictory] CameraTargetSwitcher 가 없어 연출 카메라를 못 켰다.", this);
        _overlay.HideHud();
        SuppressImpulse(main);
        HideWorldCanvases();
        // 보스에 가려진 플레이어 윤곽선·적 호버 외곽선이 컷을 깬다 — 연출 동안만 끈다(경석 10-27).
        PlayerSilhouetteFeature.SuppressedByCinematic = true;
        EnemyOutlineFeature.SuppressedByCinematic = true;
        // 벽 가림 처리(플레이어 주변 디더·구역 벽 투명화)도 끈다 — 연출 카메라는 벽 숨김(아래)으로 시야를 연다(할 일 200 추가).
        WallOcclusionDriver.SuppressedByCinematic = true;
        WallTransparencyGroup.SuppressedByCinematic = true;

        // 마스크 대상 — 막타 / 팀원(사망 포함, D7) / 보스.
        GameObject bossGo = FindSpawned(v.BossNetworkObjectId);
        foreach (Player p in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (p == null) continue;
            uint bit = p.OwnerClientId == v.KillerClientId ? BossVictoryRender.KillerMask : BossVictoryRender.TeammateMask;
            BossVictoryRender.Tag(p.gameObject, bit, _tagged);
            CollectAnimators(p.gameObject, isPlayer: true);
            CollectOutlines(p.gameObject);
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
        // 디버그 랩(244) 표시용 팀원 복제본 — Player 가 아니라 위 루프에 안 잡힌다. 비어 있으면 실게임과 같다.
        foreach (GameObject mate in BossEncounterDirector.DevVictoryExtraTeammates)
        {
            if (mate == null || !mate.activeInHierarchy) continue;
            BossVictoryRender.Tag(mate, BossVictoryRender.TeammateMask, _tagged);
            CollectAnimators(mate, isPlayer: true);
            CollectOutlines(mate);
        }
#endif
        if (bossGo != null)
        {
            BossVictoryRender.Tag(bossGo, BossVictoryRender.BossMask, _tagged);
            CollectAnimators(bossGo, isPlayer: false);
            CollectOutlines(bossGo);
            // 보스 공격·타격·바닥 예고 이펙트를 걷는다 — 훅 도중 처치되면 연출 컷에 남았다(경석 10-27).
            if (bossGo.TryGetComponent(out TwentyThreeBoss twentyThree))
                Edit.Log($"[BossVictory] 보스 이펙트 정리 — EffectManager {twentyThree.StopPresentationEffectsLocal()}개 + 소켓·바닥 예고", this);
        }
        ApplyOutlines(zero: true);

        // 색은 씬 끝까지 쓰므로 늦게 받았어도 켠다.
        _renderOn = BossVictoryRender.Enable(_cfg);
        if (!_renderOn)
            Edit.LogWarning($"[BossVictory] 셰이더({BossVictoryRender.MaskShaderName}·{BossVictoryRender.CompositeShaderName})가 없어 색 전환 없이 돈다.", this);
        if (t0 < BossVictoryRules.FreezeEnd) SpawnHitVfx(v.HitPoint);   // 할 일 200 F: 칸이 비어 있으면 없음

        CollectWalls(v.HitPoint);

        _sfxHitStopPlayed = t0 >= BossVictoryRules.FreezeStart;
        _sfxMidPlayed = t0 >= BossVictoryRules.MidCueAt;
        _sfxResumePlayed = t0 >= BossVictoryRules.FreezeEnd;

        _preludeEnded = !BossVictoryRules.IsPrelude(t0);
        if (!_preludeEnded) PreludeStarted?.Invoke(v);

        Edit.Log($"[BossVictory] 표시 시작 — 회차 {v.Sequence} · 단계 시각 {t0:0.00}초(추가 연출 구간 {v.PreludeSeconds:0.00}초) · 막타 client {v.KillerClientId} · " +
                 $"마스크 렌더러 {_tagged.Count}개 · Animator {_animators.Count}개 · 외곽선 {_outlineRenderers.Count}개 · 벽 후보 {_wallCandidates.Count}개 · " +
                 $"합성 {(_renderOn ? "켬" : "없음")}", this);
    }

    // ── 매 프레임 ──────────────────────────────────────────────────────────

    private void Tick(BossEncounterDirector.VictoryState v, float t)
    {
        if (!_preludeEnded && !BossVictoryRules.IsPrelude(t))
        {
            _preludeEnded = true;
            PreludeEnded?.Invoke(v);
        }

        // 카메라
        Vector3 pos;
        Quaternion rot;
        float fov;
        if (t < BossVictoryRules.FreezeEnd)
        {
            float k = BossVictoryRules.DescendBlend(t);
            pos = Vector3.Lerp(_startPos, v.FinalPosition, k);
            rot = Quaternion.Slerp(_startRot, v.FinalRotation, k);
            fov = Mathf.Lerp(_startFov, v.Fov, k);
        }
        else
        {
            float k = BossVictoryRules.RiseBlend(t);
            pos = Vector3.Lerp(v.FinalPosition, v.EndPosition, k);
            rot = Quaternion.Slerp(v.FinalRotation, v.EndRotation, k);
            fov = v.Fov;
        }
        _overlay?.PlaceCamera(pos, rot, fov);
        TickWalls(pos, rot * Vector3.forward);

        // 추가 연출 구간(보스 몸 멈춤) · 슬로모션 · 플레이어 공격 클립 끝 프레임 유지
        TimeMode mode = BossVictoryRules.IsPrelude(t) ? TimeMode.Prelude
            : BossVictoryRules.IsWorldFrozen(t) ? TimeMode.Slow : TimeMode.Normal;
        if (mode != _mode) SetMode(mode);
        TickAnimators();

        // 외곽선 폭 0 — HitFlash 가 블록을 비울 수 있어 매 프레임(D5).
        ApplyOutlines(zero: true);

        // 색 — 씬 끝까지 유지(D3).
        if (_renderOn) BossVictoryRender.SetWeight(BossVictoryRules.ColorWeight(t));

        // 사운드(K4 — 키가 비면 무음)
        if (!_sfxHitStopPlayed && t >= BossVictoryRules.FreezeStart) { _sfxHitStopPlayed = true; PlaySfx(_cfg.sfxHitStop, v.HitPoint); }
        if (!_sfxMidPlayed && t >= BossVictoryRules.MidCueAt) { _sfxMidPlayed = true; PlaySfx(_cfg.sfxTypo, v.HitPoint); }
        if (!_sfxResumePlayed && t >= BossVictoryRules.FreezeEnd) { _sfxResumePlayed = true; PlaySfx(_cfg.sfxResume, v.HitPoint); }

        // 단계 3.20초 — 승리 VFX 정리. 색·카메라(32°)·HUD 숨김·입력 홀드·벽 숨김은 씬이 끝날 때까지(D4·C14).
        if (t >= BossVictoryRules.End && _vfx != null)
        {
            EndVfx();
            Edit.Log("[BossVictory] 단계 3.20초 — 승리 VFX 정리, 32° 구도·색 유지", this);
        }

        // 연출 도중 새로 뜬 월드 UI(피해 숫자 등)도 숨긴다.
        if (Time.unscaledTime >= _worldCanvasRescanAt) HideWorldCanvases();
    }

    // ── 속도(추가 연출 구간·슬로모션) ─────────────────────────────────────

    private void CollectAnimators(GameObject root, bool isPlayer)
    {
        foreach (Animator a in root.GetComponentsInChildren<Animator>(true))
        {
            if (a == null || _animators.Contains(a)) continue;
            _animators.Add(a);
            _animatorSpeeds.Add(a.speed > 0f ? a.speed : 1f);
            // 웰즈(23호에 탄 기수)는 사망 때 Fear 를 그대로 튼다 — 추가 연출 구간에도 멈추지 않는다(할 일 200 G).
            _animatorRole.Add(isPlayer ? AnimatorRole.Player
                : a.GetComponentInParent<BossWells>(true) != null ? AnimatorRole.Wells : AnimatorRole.BossBody);
            // 처치 순간의 상태(전이 중이면 들어가는 쪽) — 이 클립이 끝나면 그 마지막 프레임에 멈춘다.
            int start = 0;
            if (isPlayer && a.isActiveAndEnabled && a.runtimeAnimatorController != null)
                start = a.IsInTransition(0) ? a.GetNextAnimatorStateInfo(0).fullPathHash : a.GetCurrentAnimatorStateInfo(0).fullPathHash;
            _animatorStartState.Add(start);
            _animatorHeld.Add(false);
            _animatorOverridden.Add(false);
            _animatorLastSet.Add(0f);
        }
    }

    // 구간이 바뀔 때: 덮어쓰던 속도를 원래대로 돌리고(멈춘 플레이어 제외), 승리 VFX 속도를 맞춘다.
    private void SetMode(TimeMode mode)
    {
        _mode = mode;
        for (int i = 0; i < _animators.Count; i++)
        {
            Animator a = _animators[i];
            if (a == null || _animatorHeld[i] || !_animatorOverridden[i]) continue;
            a.speed = _animatorSpeeds[i];
            _animatorOverridden[i] = false;
        }
        float scale = Mathf.Clamp01(_cfg.slowMotionScale);
        for (int i = 0; i < _vfxSystems.Count; i++)
        {
            ParticleSystem ps = _vfxSystems[i];
            if (ps == null) continue;
            ParticleSystem.MainModule main = ps.main;
            main.simulationSpeed = mode == TimeMode.Slow ? _vfxSpeeds[i] * scale : _vfxSpeeds[i];
        }
    }

    // 이 Animator 를 지금 얼마로 덮어쓸까. 음수 = 덮어쓰지 않음.
    private float WantedSpeed(int i)
    {
        if (_animatorHeld[i]) return 0f;
        if (_mode == TimeMode.Slow) return _animatorSpeeds[i] * Mathf.Clamp01(_cfg.slowMotionScale);
        if (_mode == TimeMode.Prelude && _animatorRole[i] == AnimatorRole.BossBody) return 0f;   // 사망 애니는 추가 연출 뒤(G)
        return -1f;
    }

    // 매 프레임(실행 순서 10001 — 공속 배율 등 다른 계통이 Update 에서 쓴 속도를 그 뒤에 덮는다):
    //  · 플레이어: 처치 순간 클립이 끝나거나 다른 상태(Idle 등)로 넘어가려 하면 그 프레임에 멈춘다 — 씬이 끝날 때까지(경석 10-27).
    //  · 추가 연출 구간: 보스 몸 0 · 슬로모션 구간: 원래 속도 × 배율.
    private void TickAnimators()
    {
        for (int i = 0; i < _animators.Count; i++)
        {
            Animator a = _animators[i];
            if (a == null || !a.isActiveAndEnabled || a.runtimeAnimatorController == null) continue;

            if (_animatorRole[i] == AnimatorRole.Player && !_animatorHeld[i] && ReachedClipEnd(a, _animatorStartState[i]))
                _animatorHeld[i] = true;

            float want = WantedSpeed(i);
            if (want < 0f) continue;
            // 덮기 전 값, 또는 덮는 동안 다른 계통이 바꾼 값 = 풀 때 돌릴 "원래 값".
            if (a.speed > 0f && (!_animatorOverridden[i] || !Mathf.Approximately(a.speed, _animatorLastSet[i])))
            {
                _animatorSpeeds[i] = a.speed;
                want = WantedSpeed(i);
            }
            if (!Mathf.Approximately(a.speed, want)) a.speed = want;
            _animatorLastSet[i] = want;
            _animatorOverridden[i] = true;
        }
    }

    // 처치 순간 클립이 끝났는가 — 다른 상태로 넘어가기 시작했거나, 이미 다른 상태거나, 루프 아닌 클립이 끝 프레임에 닿았다.
    private static bool ReachedClipEnd(Animator a, int startState)
    {
        if (startState == 0) return false;
        if (a.IsInTransition(0)) return a.GetNextAnimatorStateInfo(0).fullPathHash != startState;
        AnimatorStateInfo st = a.GetCurrentAnimatorStateInfo(0);
        if (st.fullPathHash != startState) return true;
        return !st.loop && st.normalizedTime >= 0.98f;
    }

    // ── 외곽선(D5 — 폭 0) ──────────────────────────────────────────────────

    private void CollectOutlines(GameObject root)
    {
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (_outlineRenderers.Contains(r)) continue;
            Material m = FirstOutlineMaterial(r);
            if (m == null) continue;
            _outlineRenderers.Add(r);
            _outlineWidths.Add(m.GetFloat(IdOutlineWidth));
        }
    }

    private static Material FirstOutlineMaterial(Renderer r)
    {
        Material[] mats = r.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
            if (mats[i] != null && mats[i].HasProperty(IdOutlineWidth)) return mats[i];
        return null;
    }

    // 렌더러 블록을 읽어(HitFlash 값 보존) 외곽선 폭만 덮는다. zero=false 면 원래 값으로.
    private void ApplyOutlines(bool zero)
    {
        _outlineBlock ??= new MaterialPropertyBlock();
        for (int i = 0; i < _outlineRenderers.Count; i++)
        {
            Renderer r = _outlineRenderers[i];
            if (r == null) continue;
            r.GetPropertyBlock(_outlineBlock);
            _outlineBlock.SetFloat(IdOutlineWidth, zero ? 0f : _outlineWidths[i]);
            r.SetPropertyBlock(_outlineBlock);
        }
    }

    // ── 벽 숨김(할 일 200 추가) ───────────────────────────────────────────

    // 벽 = Wall 레이어·Wall 태그, 또는 이름이 wall 로 시작하는 조상 아래(보스방 아트 벽 — BossRoomAuthoring.LooksLikeWall 과 같은 기준).
    private static bool IsWall(Transform t, int wallLayer)
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

    // 타격점 반경 안의 켜진 벽 렌더러를 후보로 모은다(한 번). 법선 = 바운즈의 얇은 축, 부호 = 타격점(방 안) 쪽.
    private void CollectWalls(Vector3 interior)
    {
        int wallLayer = LayerMask.NameToLayer(GameLayers.Wall);
        float r2 = _cfg.wallSearchRadius * _cfg.wallSearchRadius;
        foreach (Renderer r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r == null || !r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (_tagged.Contains(r)) continue;   // 인물·보스
            Bounds b = r.bounds;
            if (b.SqrDistance(interior) > r2) continue;
            if (!IsWall(r.transform, wallLayer)) continue;
            Vector3 n = BossVictoryRules.WallInwardNormal(b, interior);
            if (n == Vector3.zero) continue;
            _wallCandidates.Add(r);
            _wallNormals.Add(n);
            _wallCenters.Add(b.center);
        }
    }

    // 지금 연출 카메라에서 앞면이 확실히 카메라를 향하지 않는 벽(뒷면·옆면)을 끈다 — 한 번 끄면 씬 끝까지.
    private void TickWalls(Vector3 camPos, Vector3 camForward)
    {
        float threshold = _cfg.wallFacingThreshold;
        int hiddenBefore = _hiddenWalls.Count;
        for (int i = _wallCandidates.Count - 1; i >= 0; i--)
        {
            Renderer r = _wallCandidates[i];
            if (r == null) { RemoveWallCandidate(i); continue; }
            if (!BossVictoryRules.ShouldHideWall(_wallNormals[i], _wallCenters[i], camPos, camForward, threshold)) continue;
            r.enabled = false;
            _hiddenWalls.Add(r);
            RemoveWallCandidate(i);
        }
        if (_hiddenWalls.Count != hiddenBefore)
            Edit.Log($"[BossVictory] 벽 숨김 +{_hiddenWalls.Count - hiddenBefore}개(누적 {_hiddenWalls.Count}, 남은 후보 {_wallCandidates.Count})", this);
    }

    private void RemoveWallCandidate(int i)
    {
        _wallCandidates.RemoveAt(i);
        _wallNormals.RemoveAt(i);
        _wallCenters.RemoveAt(i);
    }

    // ── 승리 타격 VFX(Q2 — 할 일 200 F: 칸 비움) ──────────────────────────

    private void SpawnHitVfx(Vector3 point)
    {
        if (_cfg.hitVfxPrefab == null) return;
        _vfx = Instantiate(_cfg.hitVfxPrefab, point, Quaternion.identity);
        _vfx.name = "BossVictoryHitVfx";
        _vfx.transform.localScale *= Mathf.Max(0.01f, _cfg.hitVfxScale);
        _vfx.GetComponentsInChildren(true, _vfxSystems);
        _vfxSpeeds.Clear();
        for (int i = 0; i < _vfxSystems.Count; i++)
        {
            ParticleSystem.MainModule main = _vfxSystems[i].main;
            main.stopAction = ParticleSystemStopAction.None;   // 정리 전에 스스로 사라지지 않게
            _vfxSpeeds.Add(main.simulationSpeed);
        }
    }

    private void EndVfx()
    {
        if (_vfx != null) Destroy(_vfx);
        _vfx = null;
        _vfxSystems.Clear();
        _vfxSpeeds.Clear();
    }

    // ── 주변 정리(HUD·월드 UI·흔들림) ─────────────────────────────────────

    private void HideWorldCanvases()
    {
        _worldCanvasRescanAt = Time.unscaledTime + WorldCanvasRescanSeconds;
        foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (c == null || !c.enabled || !c.isRootCanvas || c.renderMode != RenderMode.WorldSpace) continue;
            c.enabled = false;
            _hiddenWorldCanvases.Add(c);
        }
    }

    private void SuppressImpulse(Camera main)
    {
        if (main == null || _impulseListener != null) return;
        _impulseListener = main.GetComponent<CinemachineExternalImpulseListener>();
        if (_impulseListener == null) return;
        _impulseWasEnabled = _impulseListener.enabled;
        _impulseListener.enabled = false;   // 0.72 이후 잔여 흔들림까지 멈춘다(기획 §2·C11)
    }

    private void PlaySfx(string key, Vector3 at)
    {
        if (!string.IsNullOrEmpty(key) && AudioManager.Instance != null)
            AudioManager.Instance.Play(key, at);
    }

    private static GameObject FindSpawned(ulong networkObjectId)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || nm.SpawnManager == null) return null;
        return nm.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject no) && no != null ? no.gameObject : null;
    }

    // ── 정리(씬 종료·새 회차) ───────────────────────────────────────────────

    private void Teardown()
    {
        if (!_running) return;
        _running = false;

        if (_mode != TimeMode.Normal) SetMode(TimeMode.Normal);
        if (_renderOn) { BossVictoryRender.Disable(); _renderOn = false; }
        BossVictoryRender.Untag(_tagged);
        EndVfx();
        for (int i = 0; i < _animators.Count; i++)
            if (_animators[i] != null && _animatorHeld[i]) _animators[i].speed = _animatorSpeeds[i];
        _animators.Clear();
        _animatorSpeeds.Clear();
        _animatorRole.Clear();
        _animatorStartState.Clear();
        _animatorHeld.Clear();
        _animatorOverridden.Clear();
        _animatorLastSet.Clear();
        ApplyOutlines(zero: false);
        _outlineRenderers.Clear();
        _outlineWidths.Clear();

        for (int i = 0; i < _hiddenWalls.Count; i++)
            if (_hiddenWalls[i] != null) _hiddenWalls[i].enabled = true;
        _hiddenWalls.Clear();
        _wallCandidates.Clear();
        _wallNormals.Clear();
        _wallCenters.Clear();

        PlayerSilhouetteFeature.SuppressedByCinematic = false;
        EnemyOutlineFeature.SuppressedByCinematic = false;
        WallOcclusionDriver.SuppressedByCinematic = false;
        WallTransparencyGroup.SuppressedByCinematic = false;

        for (int i = 0; i < _hiddenWorldCanvases.Count; i++)
            if (_hiddenWorldCanvases[i] != null) _hiddenWorldCanvases[i].enabled = true;
        _hiddenWorldCanvases.Clear();

        if (_impulseListener != null) _impulseListener.enabled = _impulseWasEnabled;
        _impulseListener = null;

        _overlay?.Dispose();
        _overlay = null;
        PlayerLifeInputPolicy.SetVictoryInputHeld(false);
    }
}
