using System.Collections.Generic;
using TMPro;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 보스 B 승리 연출 로컬 표시(PLAN-boss-victory-cinematic) — 모든 피어. 판정·진행은 서버(<see cref="BossEncounterDirector"/> partial),
/// 여기는 복제 상태(<see cref="BossEncounterDirector.Victory"/>) + 서버 시각 기준 경과로 화면을 그리기만 한다. 월드 timeScale 은 건드리지 않는다.
/// <list type="bullet">
/// <item>카메라: 처치 순간 내 플레이 카메라 그대로에서 출발(튐 없음) → 0.22–0.72 서버가 정한 24° 구도 → 2.00–3.20 32° → 유지(D4).</item>
/// <item>슬로모션(0.12–2.00, 완전 정지 아님 — 경석 10-27): 플레이어·보스 Animator·승리 타격 VFX 를 <c>slowMotionScale</c> 배로. 사망 표현 서버 대기도 같은 배율.
///       플레이어는 처치 순간 클립이 끝나면(또는 Idle 로 넘어가려 하면) 그 마지막 프레임에 씬이 끝날 때까지 멈춘다.</item>
/// <item>색(0.72–0.80 들어옴 → 보스 디졸브 시작에 원복): 배경 주황 틴트(34%) · 인물·보스 회색 단색. 글리치(지직거림)는 설정 구간마다.</item>
/// <item>연출 동안 플레이어 실루엣·적 호버 외곽선을 끈다(보스에 가려진 윤곽선이 컷을 깬다).</item>
/// <item>타이포(0.92 부터 typoFadeInSeconds·typoSettleSeconds 동안 진입 / 2.00–3.20 소거): 카메라에 붙은 TextMeshPro 5조각(두 띠 어긋남) — 스텐실로 인물 뒤.</item>
/// <item>입력: 처치 순간부터 오너 입력만 막는다(Q1 — 공격 동작 유지). 2.00초 정식 잠금은 서버.</item>
/// </list>
/// 오브젝트는 런타임 생성(씬·프리팹 무수정). 정리는 씬이 끝날 때(OnDestroy) — 3.20초엔 타이포·VFX 만, 색은 보스 디졸브 뒤에 끝낸다(C14).
/// </summary>
[DefaultExecutionOrder(10001)]   // PlayerAttackSpeedAnimator(10000)가 Update 에서 쓰는 animator.speed 를 그 뒤에 다시 0 으로 덮는다(C5)
public sealed class BossVictoryPresentation : MonoBehaviour
{
    private const float WorldCanvasRescanSeconds = 0.5f;
    private const float TypoDistance = 1f;   // 카메라 앞 거리(m) — 근평면보다 충분히 뒤

    private BossEncounterDirector _director;
    private BossVictoryConfig _cfg;
    private int _sequence = -1;
    private bool _running;

    private CinematicOverlay _overlay;
    private Vector3 _startPos;
    private Quaternion _startRot;
    private float _startFov;

    private readonly List<Renderer> _tagged = new List<Renderer>();
    private bool _renderOn;

    private readonly List<Animator> _animators = new List<Animator>();
    private readonly List<float> _animatorSpeeds = new List<float>();   // 슬로모션 전 원래 속도
    private readonly List<bool> _animatorIsPlayer = new List<bool>();
    private readonly List<int> _animatorStartState = new List<int>();   // 처치 순간 상태(플레이어 — 공격 클립)
    private readonly List<bool> _animatorHeld = new List<bool>();       // 그 클립이 끝나 마지막 프레임에 멈췄다(경석 10-27 — Idle 로 안 돌아감)
    private bool _frozen;                                                // 슬로모션 구간(0.12–2.00)

    private GameObject _vfx;
    private readonly List<ParticleSystem> _vfxSystems = new List<ParticleSystem>();
    private readonly List<float> _vfxSpeeds = new List<float>();

    // Flat Kit 외곽선 얇게(보스·플레이어) — HitFlash 가 렌더러 전체 블록을 null 로 비우므로 매 프레임 읽어서 덮는다.
    private static readonly int IdOutlineWidth = Shader.PropertyToID("_OutlineWidth");
    private static readonly int IdOutlineColor = Shader.PropertyToID("_OutlineColor");
    private readonly List<Renderer> _outlineRenderers = new List<Renderer>();
    private readonly List<float> _outlineWidths = new List<float>();
    private readonly List<Color> _outlineColors = new List<Color>();
    private readonly List<bool> _outlineIsBoss = new List<bool>();
    private MaterialPropertyBlock _outlineBlock;
    private bool _outlineOn;

    private DissolveDeath _bossDissolve;
    private GameObject _bossGo;
    private float _dissolveAt = float.PositiveInfinity;   // 보스 디졸브가 시작된 경과(초) — 회색·주황을 이때 원래 색으로(경석 10-27)

    private Transform _typoRoot;
    private readonly List<TextMeshPro> _typoPieces = new List<TextMeshPro>();
    private readonly List<Material> _typoMaterials = new List<Material>();
    private readonly List<int> _typoBand = new List<int>();   // 0 = 본체, +1 = 띠 A(오른쪽으로 어긋남), −1 = 띠 B(왼쪽)
    private float _typoVisibleWidth;

    private readonly List<Canvas> _hiddenWorldCanvases = new List<Canvas>();
    private float _worldCanvasRescanAt;
    private Behaviour _impulseListener;
    private bool _impulseWasEnabled;

    private bool _sfxHitStopPlayed, _sfxTypoPlayed, _sfxResumePlayed;

    private void Awake()
    {
        _director = GetComponent<BossEncounterDirector>();
    }

    private void Update()
    {
        if (_director == null || !_director.IsSpawned) return;
        BossEncounterDirector.VictoryState v = _director.Victory;
        if (!v.Active) return;

        if (v.Sequence != _sequence)
        {
            Teardown();
            _sequence = v.Sequence;
            Begin(v);
        }

        if (!_running) return;
        float t = v.Elapsed(_director.NetworkManager.ServerTime.Time);
        Tick(v, t);
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
        float t0 = v.Elapsed(_director.NetworkManager.ServerTime.Time);

        // 입력 — 처치 순간부터(Q1). 오너가 아닌 플레이어엔 의미가 없다(정책이 오너만 적용).
        PlayerLifeInputPolicy.SetVictoryInputHeld(true);

        // 카메라 — 지금 렌더 카메라 그대로에서 출발한다(기획 §2 "튀면 안 된다"). 늦게 받았으면(0.72 지남) 출발점 = 최종 구도.
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

        // 마스크 대상 — 막타 / 팀원(사망 포함, D7) / 보스.
        GameObject bossGo = FindSpawned(v.BossNetworkObjectId);
        _bossGo = bossGo;
        _bossDissolve = bossGo != null ? bossGo.GetComponent<DissolveDeath>() : null;
        _dissolveAt = float.PositiveInfinity;
        foreach (Player p in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (p == null) continue;
            uint bit = p.OwnerClientId == v.KillerClientId ? BossVictoryRender.KillerMask : BossVictoryRender.TeammateMask;
            BossVictoryRender.Tag(p.gameObject, bit, _tagged);
            CollectAnimators(p.gameObject, isPlayer: true);
            if (_cfg.characterOutlineScale < 0.999f) CollectOutlines(p.gameObject, isBoss: false);
        }
        if (bossGo != null)
        {
            BossVictoryRender.Tag(bossGo, BossVictoryRender.BossMask, _tagged);
            CollectAnimators(bossGo, isPlayer: false);
            CollectOutlines(bossGo, isBoss: true);
            // 보스 공격·타격·바닥 예고 이펙트를 걷는다 — 훅 도중 처치되면 연출 컷에 남았다(경석 10-27).
            if (bossGo.TryGetComponent(out TwentyThreeBoss twentyThree))
                Edit.Log($"[BossVictory] 보스 이펙트 정리 — EffectManager {twentyThree.StopPresentationEffectsLocal()}개 + 소켓·바닥 예고", this);
        }

        // 색은 보스 디졸브가 끝날 때까지 쓰므로 3.20 이 지나 늦게 받았어도 켠다.
        {
            _renderOn = BossVictoryRender.Enable(_cfg);
            if (!_renderOn)
                Edit.LogWarning($"[BossVictory] 셰이더({BossVictoryRender.MaskShaderName}·{BossVictoryRender.CompositeShaderName})가 없어 색 전환 없이 돈다.", this);
            BuildTypo(main);
            if (t0 < BossVictoryRules.FreezeEnd) SpawnHitVfx(v.HitPoint);
        }

        _sfxHitStopPlayed = _sfxTypoPlayed = _sfxResumePlayed = t0 >= BossVictoryRules.FreezeStart;
        if (t0 >= BossVictoryRules.TypoStart) _sfxTypoPlayed = true;

        Edit.Log($"[BossVictory] 표시 시작 — 회차 {v.Sequence} · 경과 {t0:0.00}초 · 막타 client {v.KillerClientId} · " +
                 $"마스크 렌더러 {_tagged.Count}개 · Animator {_animators.Count}개 · 합성 {(_renderOn ? "켬" : "없음")}", this);
    }

    // ── 매 프레임 ──────────────────────────────────────────────────────────

    private void Tick(BossEncounterDirector.VictoryState v, float t)
    {
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

        // 슬로모션(완전 정지 아님) · 플레이어 공격 클립 끝 프레임 유지
        bool frozen = BossVictoryRules.IsWorldFrozen(t);
        if (frozen != _frozen) SetFrozen(frozen);
        TickAnimators();

        // 보스 디졸브 시작 — 이 순간부터 회색 인물·주황 배경을 원래 색으로(경석 10-27). 보스가 사라졌으면 그때로 본다.
        if (float.IsPositiveInfinity(_dissolveAt) && t >= BossVictoryRules.FreezeEnd &&
            (_bossGo == null || (_bossDissolve != null && _bossDissolve.HasStarted)))
        {
            _dissolveAt = t;
            Edit.Log($"[BossVictory] {t:0.00}초 — 보스 디졸브 시작, 색 원복", this);
        }

        // 외곽선 — 디졸브 전까지 얇고 짙게, 디졸브가 시작되면 원래대로(디졸브는 재질을 갈아끼운다).
        if (_outlineOn)
        {
            if (float.IsPositiveInfinity(_dissolveAt)) ApplyOutlines(true);
            else { ApplyOutlines(false); _outlineOn = false; }
        }

        // 색 · 글리치
        float colorWeight = BossVictoryRules.ColorWeight(t, _dissolveAt);
        float glitch = Glitch(t);
        if (_renderOn)
        {
            BossVictoryRender.SetWeight(colorWeight);
            BossVictoryRender.SetGlitch(glitch, Time.unscaledTime);
        }

        // 타이포
        TickTypo(t);

        // 사운드(K4 — 키가 비면 무음)
        if (!_sfxHitStopPlayed && t >= BossVictoryRules.FreezeStart) { _sfxHitStopPlayed = true; PlaySfx(_cfg.sfxHitStop, v.HitPoint); }
        if (!_sfxTypoPlayed && t >= BossVictoryRules.TypoStart) { _sfxTypoPlayed = true; PlaySfx(_cfg.sfxTypo, v.HitPoint); }
        if (!_sfxResumePlayed && t >= BossVictoryRules.FreezeEnd) { _sfxResumePlayed = true; PlaySfx(_cfg.sfxResume, v.HitPoint); }

        // 3.20초 — 타이포·VFX 정리. 색은 디졸브까지(경석 10-27), 카메라(32°)·HUD 숨김·입력 홀드는 씬이 끝날 때까지(D4·C14).
        if (t >= BossVictoryRules.End && (_typoRoot != null || _vfx != null))
        {
            EndTypoAndVfx();
            Edit.Log("[BossVictory] 3.20초 — 타이포 정리, 32° 구도 유지(색은 보스 디졸브까지)", this);
        }
        if (_renderOn && t >= _dissolveAt + BossVictoryRules.ColorOutSeconds && glitch <= 0f)
        {
            EndColorLayers();
            Edit.Log("[BossVictory] 색 원복 끝 — 합성 끔", this);
        }

        // 연출 도중 새로 뜬 월드 UI(피해 숫자 등)도 숨긴다.
        if (Time.unscaledTime >= _worldCanvasRescanAt) HideWorldCanvases();
    }

    // ── 정지 ───────────────────────────────────────────────────────────────

    private void CollectAnimators(GameObject root, bool isPlayer)
    {
        foreach (Animator a in root.GetComponentsInChildren<Animator>(true))
        {
            if (a == null || _animators.Contains(a)) continue;
            _animators.Add(a);
            _animatorSpeeds.Add(a.speed > 0f ? a.speed : 1f);
            _animatorIsPlayer.Add(isPlayer);
            // 처치 순간의 상태(전이 중이면 들어가는 쪽) — 이 클립이 끝나면 그 마지막 프레임에 멈춘다.
            int start = 0;
            if (isPlayer && a.isActiveAndEnabled && a.runtimeAnimatorController != null)
                start = a.IsInTransition(0) ? a.GetNextAnimatorStateInfo(0).fullPathHash : a.GetCurrentAnimatorStateInfo(0).fullPathHash;
            _animatorStartState.Add(start);
            _animatorHeld.Add(false);
        }
    }

    private void SetFrozen(bool frozen)
    {
        _frozen = frozen;
        float scale = Mathf.Clamp01(_cfg.slowMotionScale);
        for (int i = 0; i < _animators.Count; i++)
        {
            Animator a = _animators[i];
            if (a == null) continue;
            if (frozen) { if (a.speed > 0f) _animatorSpeeds[i] = a.speed; }
            else if (!_animatorHeld[i]) a.speed = _animatorSpeeds[i];   // 슬로모션 해제 — 멈춘 플레이어는 그대로
        }
        for (int i = 0; i < _vfxSystems.Count; i++)
        {
            ParticleSystem ps = _vfxSystems[i];
            if (ps == null) continue;
            ParticleSystem.MainModule main = ps.main;
            main.simulationSpeed = frozen ? _vfxSpeeds[i] * scale : _vfxSpeeds[i];
        }
    }

    // 매 프레임(실행 순서 10001 — 공속 배율 등 다른 계통이 Update 에서 쓴 속도를 그 뒤에 덮는다):
    //  · 플레이어: 처치 순간 클립이 끝나거나 다른 상태(Idle 등)로 넘어가려 하면 그 프레임에 멈춘다 — 씬이 끝날 때까지(경석 10-27).
    //  · 슬로모션 구간: 원래 속도 × 배율.
    private void TickAnimators()
    {
        float scale = Mathf.Clamp01(_cfg.slowMotionScale);
        for (int i = 0; i < _animators.Count; i++)
        {
            Animator a = _animators[i];
            if (a == null || !a.isActiveAndEnabled || a.runtimeAnimatorController == null) continue;

            if (_animatorIsPlayer[i] && !_animatorHeld[i] && ReachedClipEnd(a, _animatorStartState[i]))
                _animatorHeld[i] = true;

            if (_animatorHeld[i])
            {
                if (a.speed != 0f) a.speed = 0f;
                continue;
            }
            if (!_frozen) continue;

            float want = _animatorSpeeds[i] * scale;
            if (a.speed != want)
            {
                if (a.speed > 0f && !Mathf.Approximately(a.speed, want)) _animatorSpeeds[i] = a.speed;   // 다른 계통이 바꾼 "원래 값"은 풀 때 쓴다
                a.speed = _animatorSpeeds[i] * scale;
            }
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

    // ── 외곽선 ─────────────────────────────────────────────────────────────

    private void CollectOutlines(GameObject root, bool isBoss)
    {
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            Material m = FirstOutlineMaterial(r);
            if (m == null) continue;
            _outlineRenderers.Add(r);
            _outlineWidths.Add(m.GetFloat(IdOutlineWidth));
            _outlineColors.Add(m.HasProperty(IdOutlineColor) ? m.GetColor(IdOutlineColor) : Color.black);
            _outlineIsBoss.Add(isBoss);
        }
        _outlineOn = _outlineRenderers.Count > 0;
    }

    private static Material FirstOutlineMaterial(Renderer r)
    {
        Material[] mats = r.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
            if (mats[i] != null && mats[i].HasProperty(IdOutlineWidth)) return mats[i];
        return null;
    }

    // 렌더러 블록을 읽어(HitFlash 값 보존) 외곽선만 덮는다. thin=false 면 원래 값으로.
    private void ApplyOutlines(bool thin)
    {
        _outlineBlock ??= new MaterialPropertyBlock();
        for (int i = 0; i < _outlineRenderers.Count; i++)
        {
            Renderer r = _outlineRenderers[i];
            if (r == null) continue;
            float scale = _outlineIsBoss[i] ? _cfg.bossOutlineScale : _cfg.characterOutlineScale;
            r.GetPropertyBlock(_outlineBlock);
            _outlineBlock.SetFloat(IdOutlineWidth, thin ? _outlineWidths[i] * scale : _outlineWidths[i]);
            _outlineBlock.SetColor(IdOutlineColor, thin && _outlineIsBoss[i] ? _cfg.bossOutlineColor : _outlineColors[i]);
            r.SetPropertyBlock(_outlineBlock);
        }
    }

    // 글리치 세기 — 구간들 중 가장 센 것 × 프레임마다 흔들림(24fps 로 끊어서).
    private float Glitch(float t)
    {
        float g = 0f;
        Vector3[] bursts = _cfg.glitchBursts;
        if (bursts != null)
            for (int i = 0; i < bursts.Length; i++)
                g = Mathf.Max(g, BossVictoryRules.GlitchBurst(t, bursts[i].x, bursts[i].y) * Mathf.Clamp01(bursts[i].z));
        if (g <= 0f) return 0f;
        float frame = Mathf.Floor(Time.unscaledTime * 24f);
        float noise = Mathf.Repeat(Mathf.Sin(frame * 12.9898f) * 43758.5453f, 1f);
        return g * (1f - Mathf.Clamp01(_cfg.glitchFlicker) * noise);
    }

    // ── 핵심 타격 VFX(Q2) ─────────────────────────────────────────────────

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
            main.stopAction = ParticleSystemStopAction.None;   // 3.20초 정리 전에 스스로 사라지지 않게
            _vfxSpeeds.Add(main.simulationSpeed);
        }
    }

    // ── 타이포 ─────────────────────────────────────────────────────────────

    private void BuildTypo(Camera cam)
    {
        if (cam == null) return;
        TMP_FontAsset font = _cfg.typoFont != null ? _cfg.typoFont : TMP_Settings.defaultFontAsset;
        if (font == null) return;

        _typoRoot = new GameObject("BossVictoryTypo").transform;
        _typoRoot.SetParent(cam.transform, false);

        float aspect = cam.aspect > 0.01f ? cam.aspect : 16f / 9f;
        float visibleHeight = 2f * TypoDistance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        _typoVisibleWidth = visibleHeight * aspect;

        // 본체 3조각 + 띠 2조각. 같은 글자·같은 배치라 겹치지 않게 세로 클립 범위만 나눈다.
        Vector2 a = _cfg.typoBandA, b = _cfg.typoBandB;
        var slices = new (float lo, float hi, int band)[]
        {
            (-1f, a.x, 0), (a.x, a.y, 1), (a.y, b.x, 0), (b.x, b.y, -1), (b.y, 2f, 0),
        };

        Bounds textBounds = default;
        for (int i = 0; i < slices.Length; i++)
        {
            var go = new GameObject($"Piece{i}");
            go.transform.SetParent(_typoRoot, false);
            TextMeshPro tmp = go.AddComponent<TextMeshPro>();
            tmp.font = font;
            tmp.text = _cfg.typoText;
            tmp.fontSize = 10f;
            tmp.fontStyle = FontStyles.Italic;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.rectTransform.sizeDelta = new Vector2(200f, 50f);
            tmp.ForceMeshUpdate();
            textBounds = tmp.textBounds;
            _typoPieces.Add(tmp);
            _typoBand.Add(slices[i].band);
        }

        // 크기 — 폭 = 화면 폭 × 1.09, 단 높이 상한(화면 높이 × 비율).
        float w = Mathf.Max(0.001f, textBounds.size.x);
        float h = Mathf.Max(0.001f, textBounds.size.y);
        float scale = Mathf.Min(_typoVisibleWidth * _cfg.typoWidthRatio / w, visibleHeight * _cfg.typoMaxHeightRatio / h);
        _typoRoot.localScale = Vector3.one * scale;
        _typoRoot.localRotation = Quaternion.Euler(0f, 0f, _cfg.typoRotation);
        float centerY = (0.5f - _cfg.typoCenterFromTop) * visibleHeight;
        _typoRoot.localPosition = new Vector3(0f, centerY - textBounds.center.y * scale, TypoDistance);

        // 🔴 재질을 바꾸면 TMP 가 메시를 다시 만들어 정점 색(그라데이션)이 흰색으로 돌아간다 → 재질 먼저, 메시 갱신, 그 다음 색.
        for (int i = 0; i < _typoPieces.Count; i++)
        {
            TextMeshPro tmp = _typoPieces[i];
            float lo = Mathf.Lerp(textBounds.min.y, textBounds.max.y, slices[i].lo);
            float hi = Mathf.Lerp(textBounds.min.y, textBounds.max.y, slices[i].hi);
            Material m = NewTypoMaterial(font, new Vector4(-1e4f, lo, 1e4f, hi));
            tmp.fontSharedMaterial = m;
            _typoMaterials.Add(m);
            tmp.ForceMeshUpdate();
            ApplyGradient(tmp, textBounds);
        }

        SetTypoAlpha(0f);
    }

    private Material NewTypoMaterial(TMP_FontAsset font, Vector4 clipRect)
    {
        var m = new Material(font.material) { name = "BossVictoryTypo" };
        // 인물 뒤 — 마스크 패스가 남긴 스텐실 128 이 없는 곳에만(C10 대신 스텐실: RT·카메라 없이 같은 결과).
        m.SetFloat("_Stencil", BossVictoryRender.StencilBit);
        m.SetFloat("_StencilComp", (float)UnityEngine.Rendering.CompareFunction.NotEqual);
        m.SetFloat("_StencilOp", (float)UnityEngine.Rendering.StencilOp.Keep);
        m.SetFloat("_StencilReadMask", BossVictoryRender.StencilBit);
        m.SetFloat("_StencilWriteMask", 0f);
        // 두 띠 — 로컬 공간 세로 클립.
        m.EnableKeyword("UNITY_UI_CLIP_RECT");
        m.SetVector("_ClipRect", clipRect);
        m.SetFloat("_MaskSoftnessX", 0f);
        m.SetFloat("_MaskSoftnessY", 0f);
        // 얇은 밝은 테두리 · 우하단 짙은 그림자.
        m.EnableKeyword("OUTLINE_ON");
        m.SetColor("_OutlineColor", _cfg.typoOutlineColor);
        m.SetFloat("_OutlineWidth", _cfg.typoOutlineWidth);
        m.EnableKeyword("UNDERLAY_ON");
        m.SetColor("_UnderlayColor", _cfg.typoShadowColor);
        m.SetFloat("_UnderlayOffsetX", _cfg.typoShadowOffset.x);
        m.SetFloat("_UnderlayOffsetY", _cfg.typoShadowOffset.y);
        // 투명 패스에서 VFX(3000)보다 먼저 — 레이어: 배경 → 타이포 → 인물 → 핵심 이펙트.
        m.renderQueue = 2990;
        return m;
    }

    // 좌 → 우 4색 — 정점 x 위치로 고른다(글자마다 왼쪽·오른쪽 정점이 다른 색 → 단어 전체가 이어진 그라데이션).
    private void ApplyGradient(TextMeshPro tmp, Bounds bounds)
    {
        Color[] stops = _cfg.typoGradient;
        if (stops == null || stops.Length == 0) return;
        TMP_TextInfo info = tmp.textInfo;
        for (int m = 0; m < info.meshInfo.Length; m++)
        {
            Vector3[] verts = info.meshInfo[m].vertices;
            Color32[] colors = info.meshInfo[m].colors32;
            if (verts == null || colors == null) continue;
            for (int i = 0; i < verts.Length && i < colors.Length; i++)
            {
                float u = Mathf.InverseLerp(bounds.min.x, bounds.max.x, verts[i].x);
                colors[i] = Gradient(stops, u);
            }
        }
        tmp.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }

    private static Color Gradient(Color[] stops, float u)
    {
        if (stops.Length == 1) return stops[0];
        float x = Mathf.Clamp01(u) * (stops.Length - 1);
        int i = Mathf.Min(stops.Length - 2, Mathf.FloorToInt(x));
        return Color.Lerp(stops[i], stops[i + 1], x - i);
    }

    private void TickTypo(float t)
    {
        if (_typoRoot == null) return;
        SetTypoAlpha(BossVictoryRules.TypoAlpha(t, _cfg.typoFadeInSeconds));

        // 0.92–1.07: 화면 우측으로 밀린 자리 → 왼쪽으로 빠르게 안착, 두 띠는 짧게 어긋났다가 정렬.
        float settle = BossVictoryRules.TypoSettle(t, _cfg.typoSettleSeconds);
        float scale = Mathf.Max(0.0001f, _typoRoot.localScale.x);
        float enter = (1f - settle) * _cfg.typoEnterOffset * _typoVisibleWidth / scale;
        float band = (1f - settle) * _cfg.typoBandOffset * _typoVisibleWidth / scale;
        for (int i = 0; i < _typoPieces.Count; i++)
        {
            if (_typoPieces[i] == null) continue;
            _typoPieces[i].transform.localPosition = new Vector3(enter + band * _typoBand[i], 0f, 0f);
        }
    }

    private void SetTypoAlpha(float alpha)
    {
        // 🔴 TextMeshPro.enabled 를 토글하면 다시 켤 때 메시를 새로 만들어 그라데이션이 지워진다 — 렌더러만 끈다.
        bool visible = alpha > 0.001f;
        for (int i = 0; i < _typoPieces.Count; i++)
        {
            Renderer r = _typoPieces[i] != null ? _typoPieces[i].renderer : null;
            if (r != null && r.enabled != visible) r.enabled = visible;
        }
        if (!visible) return;

        Color face = new Color(1f, 1f, 1f, alpha);
        Color outline = _cfg.typoOutlineColor; outline.a *= alpha;
        Color shadow = _cfg.typoShadowColor; shadow.a *= alpha;
        for (int i = 0; i < _typoMaterials.Count; i++)
        {
            Material m = _typoMaterials[i];
            if (m == null) continue;
            m.SetColor("_FaceColor", face);
            m.SetColor("_OutlineColor", outline);
            m.SetColor("_UnderlayColor", shadow);
        }
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

    // ── 정리 ───────────────────────────────────────────────────────────────

    // 색(합성·마스크) — 보스 디졸브로 원래 색이 된 뒤.
    private void EndColorLayers()
    {
        if (_renderOn) { BossVictoryRender.Disable(); _renderOn = false; }
        BossVictoryRender.Untag(_tagged);
    }

    // 3.20초: 타이포·VFX.
    private void EndTypoAndVfx()
    {
        for (int i = 0; i < _typoMaterials.Count; i++) if (_typoMaterials[i] != null) Destroy(_typoMaterials[i]);
        _typoMaterials.Clear();
        _typoPieces.Clear();
        _typoBand.Clear();
        if (_typoRoot != null) Destroy(_typoRoot.gameObject);
        _typoRoot = null;

        if (_vfx != null) Destroy(_vfx);
        _vfx = null;
        _vfxSystems.Clear();
        _vfxSpeeds.Clear();
    }

    // 씬 종료·새 회차: 전부 되돌린다.
    private void Teardown()
    {
        if (!_running) return;
        _running = false;

        if (_frozen) SetFrozen(false);
        EndColorLayers();
        EndTypoAndVfx();
        for (int i = 0; i < _animators.Count; i++)
            if (_animators[i] != null && _animatorHeld[i]) _animators[i].speed = _animatorSpeeds[i];
        _animators.Clear();
        _animatorSpeeds.Clear();
        _animatorIsPlayer.Clear();
        _animatorStartState.Clear();
        _animatorHeld.Clear();
        if (_outlineOn) ApplyOutlines(false);
        _outlineOn = false;
        _outlineRenderers.Clear();
        _outlineWidths.Clear();
        _outlineColors.Clear();
        _outlineIsBoss.Clear();
        _bossGo = null;
        _bossDissolve = null;
        PlayerSilhouetteFeature.SuppressedByCinematic = false;
        EnemyOutlineFeature.SuppressedByCinematic = false;

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
