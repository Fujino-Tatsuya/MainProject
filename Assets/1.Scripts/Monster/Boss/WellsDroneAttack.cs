using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// 웰즈 자폭 드론 — 표식(크로스헤어) 3초 추적 → 발 위치 고정 → 원형 범위 1초 → 드론 충돌 → 범위 피해(플레이어 + 23호).
/// 기획: Docs/design/boss/wells-suicide-drone.md · PLAN-boss-electric-drone.md.
///
/// <b>권한</b>: 대상 선정·타이머·피해 = 서버. 클라는 23호 ClientRpc 로 받은 것만 그린다.
/// NetworkBehaviour 가 아니다(23호가 스폰 때 붙이고 RPC 를 대신 보낸다).
/// 드론 모델은 <b>연출 전용 로컬 오브젝트</b> — 부술 수도 밀 수도 없고 충돌·판정이 없다(§8). 피해는 고정된 원 기준.
///
/// <b>타이머</b>(§4 · §11): 첫 5초 → 공격/취소 <b>종료 후</b> 7초. 제압(pauseOn)·충전 기믹 중에는 멈추고 진행 중 공격은 제거,
/// 끝나면 멈춘 타이머부터 이어서(최소 2초). 과충전·잡기·취약은 영향 없음.
/// </summary>
// 23호(MonsterBase.Update) 다음에 — 같은 프레임의 그로기·차징 진입을 먼저 보게(BossElectricFloor 와 같은 이유).
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public sealed class WellsDroneAttack : MonoBehaviour
{
    enum Phase { Idle, WaitTarget, Track, Lock, Explode }

    TwentyThreeBoss _boss;
    WellsDroneDataSO _d;

    // ── 서버 ──
    Phase _phase = Phase.Idle;
    float _t;
    bool _started, _paused;
    Player _target, _lastTarget;
    Vector3 _lockPos;
    float _radius;
    readonly List<Player> _valid = new List<Player>(4);
    readonly HashSet<Unit> _hitOnce = new HashSet<Unit>();
    readonly Collider[] _overlap = new Collider[64];

    WellsDroneDataSO _ownedDefaults;   // 데이터가 비어 직접 만든 기본값 — 우리가 지운다

    public void Init(TwentyThreeBoss boss, WellsDroneDataSO data)
    {
        _boss = boss;
        if (data != null) _d = data;
        else _d = _ownedDefaults != null ? _ownedDefaults : (_ownedDefaults = ScriptableObject.CreateInstance<WellsDroneDataSO>());
        ResetState();
    }

    /// <summary>스폰/디스폰 경계 — 타이머·직전 대상·연출을 처음으로(풀 재사용 대비).</summary>
    public void ResetState()
    {
        _phase = Phase.Idle;
        _t = 0f;
        _started = _paused = false;
        _target = _lastTarget = null;
        ClientCancel();
        DestroyBoomVfx();
    }

    void Update()
    {
        if (_boss == null) return;
        if (_boss.IsServer) ServerTick(Time.deltaTime);
        ClientTick();
    }

    #region 서버

    void ServerTick(float dt)
    {
        if (_boss.IsDead)
        {
            if (_started)
            {
                CancelInProgress("보스 사망");
                _started = false;
                _lastTarget = null;   // 보스전 종료 시 직전 대상 기록 초기화(§5.2)
            }
            return;
        }
        if (!_boss.IsFightActive) return;

        if (!_started)
        {
            _started = true;
            _phase = Phase.Idle;
            _t = _d.firstDelay;
        }

        // 송전기는 점프 출발 순간부터 멈춘다(팀장 10-02) — 점프 이동 + 차징 대기 전체.
        bool pause = (_boss.ActivePauseConditions & _d.pauseOn) != 0 || _boss.IsChargeSequenceActive;
        if (pause)
        {
            if (!_paused)
            {
                _paused = true;
                // 진행 중 공격 제거(§11.1·§11.2). 공격이 이미 나간 뒤라 남은 대기는 0 으로 본다 →
                // 재개 때 아래 최소 보정(2초)이 그대로 다음 대기가 된다("멈춘 타이머부터 이어서 · 2초 미만이면 2초").
                // ⚠️ 7초(§4)는 "공격 종료 · 대상 무효 취소" 두 경우뿐이다 — 정지 취소에 7초를 쓰면 재개가 늦다
                //    (Claude·Codex 교차검증 10-02 공통 지적으로 고쳤다).
                // 대기 중이었다면 남은 시간이 그대로 얼어 있다.
                if (_phase != Phase.Idle && _phase != Phase.WaitTarget)
                {
                    CancelInProgress(_boss.IsChargeSequenceActive ? "송전기 진입(점프 출발)" : "제압 진입");
                    _phase = Phase.Idle;
                    _t = 0f;
                }
            }
            return;   // 타이머 정지
        }
        if (_paused)
        {
            _paused = false;
            if (_phase == Phase.Idle || _phase == Phase.WaitTarget)
            {
                _phase = Phase.Idle;
                _t = Mathf.Max(_t, _d.resumeMinDelay);   // 재개 직후 즉시 공격 방지
            }
        }

        switch (_phase)
        {
            case Phase.Idle:
                _t -= dt;
                if (_t > 0f) return;
                _phase = Phase.WaitTarget;
                goto case Phase.WaitTarget;

            case Phase.WaitTarget:
                // 유효 대상이 없으면 생길 때까지 여기서 기다린다(§5.3).
                if (!PickTarget(out _target)) return;
                _lastTarget = _target;
                _phase = Phase.Track;
                _t = _d.trackTime;
                _boss.SendDroneMark(_target.NetworkObject, _d.trackTime);
                Debug.Log($"[자폭드론] 표식 → {_target.name} ({_d.trackTime:0.#}초 추적)", this);
                return;

            case Phase.Track:
                // 추적 중 대상 무효(쓰러짐·사망·붙잡힘) → 취소, 다른 사람에게 넘기지 않는다(§10.1).
                if (!BossPatternTargets.IsValid(_target, _boss))
                {
                    CancelInProgress("추적 중 대상 무효");
                    _phase = Phase.Idle;
                    _t = _d.cooldown;
                    return;
                }
                _t -= dt;
                if (_t > 0f) return;
                // 위치 고정 — 이 순간의 발 위치. 이후 대상과 분리된다(§7 · §10.2). 방 밖이어도 보정하지 않는다.
                _lockPos = BossPatternTargets.FootPosition(_target);
                _radius = Radius();
                _phase = Phase.Lock;
                _t = _d.lockTime;
                // 날아오는 쪽(화면 좌하단/우하단)은 서버가 정해 보낸다 — 피어마다 다르게 보이지 않게(팀장 10-02: 매번 무작위).
                _boss.SendDroneLock(_lockPos, _radius, _d.lockTime, Random.value < 0.5f);
                return;

            case Phase.Lock:
                _t -= dt;
                if (_t > 0f) return;
                Impact();
                _phase = Phase.Explode;
                _t = _d.explosionDuration;
                return;

            case Phase.Explode:
                _t -= dt;
                if (_t > 0f) return;
                _phase = Phase.Idle;
                _t = _d.cooldown;   // 폭발 연출 종료 후부터 센다(§3-11)
                return;
        }
    }

    bool PickTarget(out Player target)
    {
        _valid.Clear();
        foreach (Player p in BossPatternTargets.AllPlayers())
            if (BossPatternTargets.IsValid(p, _boss) && p.NetworkObject != null) _valid.Add(p);

        // 2명 이상이면 직전 대상을 한 번 제외(§5.2). 1명뿐이면 연속 지정 허용.
        if (_valid.Count >= 2 && _lastTarget != null) _valid.Remove(_lastTarget);

        target = _valid.Count > 0 ? _valid[Random.Range(0, _valid.Count)] : null;
        return target != null;
    }

    float Radius()
    {
        float tile = _boss.TryGetTileGrid(out BossTileGrid g) ? g.TileSize : 4f;
        return tile * _d.diameterInTiles * 0.5f;
    }

    // §9: 충돌 순간 1회 · 대상당 1회 · 충돌 범위와 원이 겹치면 적중 · 플레이어와 23호 각각 독립.
    void Impact()
    {
        _boss.SendDroneImpact(_lockPos, _radius);

        // 원(바닥) 판정을 세로 캡슐로 근사한다 — 캐릭터 콜라이더가 원 위 어디에 걸쳐도 잡히게.
        int n = Physics.OverlapCapsuleNonAlloc(_lockPos, _lockPos + Vector3.up * 3f, _radius, _overlap,
                                               _boss.DroneHitMask, QueryTriggerInteraction.Collide);
        _hitOnce.Clear();
        int players = 0;
        bool bossHit = false;
        for (int i = 0; i < n; i++)
        {
            Collider c = _overlap[i];
            if (c == null) continue;
            // "충돌 범위" = 몸(비트리거) 또는 피격용 Hurtbox. 몸 밖으로 뻗은 센서·무기 판정 트리거는 제외한다 —
            // 그게 원에 걸쳐 맞으면 몸이 원 밖인데 피해가 들어간다(Claude·Codex 교차검증 10-02).
            if (c.isTrigger && c.GetComponent<Hurtbox>() == null) continue;

            Player p = c.GetComponentInParent<Player>();
            if (p != null)
            {
                // 붙잡힘·쓰러짐·사망은 면역(§9.2). 무적은 수신측이 막는다.
                if (!BossPatternTargets.IsValid(p, _boss) || !_hitOnce.Add(p)) continue;
                BossPatternTargets.Damage(p, _d.playerDamage, _lockPos, _boss.transform);
                players++;
                continue;
            }

            TwentyThreeBoss b = c.GetComponentInParent<TwentyThreeBoss>();
            if (b == _boss && _hitOnce.Add(b))
            {
                // 일반 보스 피해 — 인터럽트 아님(간파·취약 판정 안 탐), 출처가 플레이어가 아니라 제압 ×1.2 도 없다.
                BossPatternTargets.Damage(b, _d.bossDamage, _lockPos, _boss.transform);
                bossHit = true;
            }
        }
        if (n == _overlap.Length)
            Debug.LogWarning("[자폭드론] 겹침 버퍼가 찼다 — 일부 대상이 잘렸을 수 있다(버퍼 확대 필요).", this);
        Debug.Log($"[자폭드론] 충돌 — 플레이어 {players}명{(bossHit ? " + 23호" : "")} 적중 (반경 {_radius:0.##}m)", this);
    }

    void CancelInProgress(string why)
    {
        if (_phase == Phase.Track || _phase == Phase.Lock || _phase == Phase.Explode)
        {
            _boss.SendDroneCancel();
            Debug.Log($"[자폭드론] 취소 — {why}", this);
        }
        _target = null;
    }

    #endregion

    #region 클라(전 피어) — 임시 연출

    MeshRenderer _cross, _outer, _fill, _boom;
    Transform _crossTarget;
    float _vStart, _vDur;
    Vector3 _vPos;
    float _vRadius, _vFloorY;
    enum View { None, Mark, Lock, Boom }
    View _view;
    GameObject _droneGo, _boomVfx;
    Transform _viewRoot;
    Vector3 _vFrom, _vTo;   // 드론 비행 출발점(대각선 위) → 충돌 지점
    Color _crossColor;

    /// <summary>
    /// 비행 진행도(시간 0~1 → 거리 0~1) — 처음엔 천천히, 뒤에서 급가속(팀장 10-02 "보일 땐 느리고 갑자기 빨라지며 내려온다").
    /// 느린 구간 = 등속(s 시간에 d 거리). 빠른 구간 = 그 속도에서 시작하는 가속(이음매에서 속도가 끊기지 않는다).
    /// </summary>
    static float ApproachProgress(float x, float s, float d)
    {
        if (x <= 0f) return 0f;
        if (x >= 1f) return 1f;
        if (x < s) return d * (x / s);
        float u = (x - s) / (1f - s);          // 빠른 구간 0~1
        float v0 = d / s * (1f - s);           // 이음매 속도를 빠른 구간 단위로 환산
        float a = Mathf.Max(0f, 1f - d - v0);  // 남은 거리를 가속으로
        return d + v0 * u + a * u * u;
    }

    // 드론 애니 — FBX 에 Animator 컨트롤러가 없어 PlayableGraph 로 클립을 직접 튼다. DashStart 1회 → DashLoop 반복.
    PlayableGraph _droneGraph;
    AnimationClipPlayable _droneClip;
    bool _droneLooping;

    void PlayDroneClips()
    {
        StopDroneClips();
        AnimationClip first = _d.dashStartClip != null ? _d.dashStartClip : _d.dashLoopClip;
        if (first == null || _droneGo == null) return;
        Animator anim = _droneGo.GetComponentInChildren<Animator>(true);
        if (anim == null) anim = _droneGo.AddComponent<Animator>();
        anim.applyRootMotion = false;   // 위치는 비행 경로(Lerp)가 정한다 — 클립 루트 이동이 덮어쓰지 않게
        _droneClip = AnimationPlayableUtilities.PlayClip(anim, first, out _droneGraph);
        _droneLooping = first == _d.dashLoopClip;
    }

    void TickDroneClips()
    {
        if (!_droneGraph.IsValid() || !_droneClip.IsValid()) return;
        AnimationClip cur = _droneClip.GetAnimationClip();
        if (cur == null || cur.length <= 0f) return;
        double t = _droneClip.GetTime();
        if (_droneLooping)
        {
            if (t >= cur.length) _droneClip.SetTime(t % cur.length);   // 임포트 설정과 무관하게 반복
        }
        else if (t >= cur.length && _d.dashLoopClip != null)
        {
            // DashStart 끝 → DashLoop 로 갈아 끼운다.
            _droneLooping = true;
            var loop = AnimationClipPlayable.Create(_droneGraph, _d.dashLoopClip);
            var output = _droneGraph.GetOutput(0);
            _droneClip.Destroy();
            _droneClip = loop;
            output.SetSourcePlayable(loop);
        }
    }

    void StopDroneClips()
    {
        if (_droneGraph.IsValid()) _droneGraph.Destroy();
        _droneLooping = false;
    }

    public void ClientMark(Transform target, float trackTime)
    {
        EnsureView();
        ClientCancel();
        _crossTarget = target;
        _view = View.Mark;
        _vStart = Time.time;
        _vDur = Mathf.Max(0.01f, trackTime);
        _crossColor = _d.crosshairStartColor;
        BossPatternVisuals.Paint(_cross, _crossColor,
                                 _d.crosshairTexture != null ? _d.crosshairTexture : BossPatternVisuals.CrosshairTexture);
        _cross.enabled = true;
        _cross.gameObject.SetActive(true);
    }

    public void ClientLock(Vector3 pos, float radius, float lockTime, bool fromRight)
    {
        EnsureView();
        ClientCancel();
        _view = View.Lock;
        _vStart = Time.time;
        _vDur = Mathf.Max(0.01f, lockTime);
        _vPos = pos;
        _vRadius = radius;
        _vFloorY = BossPatternVisuals.SampleFloorY(pos, pos.y) + 0.07f;

        PlaceDisc(_outer, radius, 0f);
        BossPatternVisuals.Paint(_outer, _d.circleOuterColor, BossPatternVisuals.DiscTexture);
        _outer.gameObject.SetActive(true);
        PlaceDisc(_fill, 0.01f, 0.01f);
        BossPatternVisuals.Paint(_fill, _d.circleFillColor, BossPatternVisuals.DiscTexture);
        _fill.gameObject.SetActive(true);

        if (_d.droneModel != null)
        {
            // 출발점 = 화면 아래 + 좌/우 대각선(애니가 대각선 접근을 상정하고 만들어졌다 — 팀장 10-02).
            // 화면 방향은 이 피어의 카메라로 잡는다(탑다운 카메라 — 화면 "아래" = 카메라 정면의 반대를 바닥에 눕힌 것).
            Camera cam = Camera.main;
            Vector3 fwd = cam != null ? Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up) : Vector3.forward;
            Vector3 right = cam != null ? Vector3.ProjectOnPlane(cam.transform.right, Vector3.up) : Vector3.right;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
            Vector3 side = (-fwd.normalized + (fromRight ? right.normalized : -right.normalized)).normalized;
            _vFrom = new Vector3(pos.x, _vFloorY, pos.z) + side * _d.approachDistance + Vector3.up * _d.dropHeight;
            _vTo = new Vector3(pos.x, _vFloorY, pos.z);

            Vector3 travel = _vTo - _vFrom;
            Quaternion face = travel.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(travel.normalized, Vector3.up) : Quaternion.identity;
            _droneGo = Instantiate(_d.droneModel, _vFrom, face * Quaternion.Euler(_d.droneRotationOffset));
            _droneGo.transform.localScale *= _d.droneScale;
            // 연출 전용 — 혹시 프리팹에 콜라이더·리지드바디가 있어도 판정·물리에 끼지 않게 끈다(§8).
            foreach (Collider c in _droneGo.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (Rigidbody rb in _droneGo.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
            // 낙하 시작 전까지 숨긴다 — 크로스헤어가 사라진 "다음에" 드론이 나타나 내려온다(팀장 10-02).
            _droneGo.SetActive(false);
        }
    }

    public void ClientImpact(Vector3 pos, float radius)
    {
        EnsureView();
        ClientCancel();
        _view = View.Boom;
        _vStart = Time.time;
        _vDur = Mathf.Max(0.01f, _d.explosionDuration);
        _vPos = pos;
        _vRadius = radius;
        _vFloorY = BossPatternVisuals.SampleFloorY(pos, pos.y) + 0.08f;

        if (_d.explosionVfxPrefab != null)
        {
            // 참조를 쥐고 있는다 — 제압·사망 취소(ClientCancel)와 파괴에서 회수해야 한다(Codex 교차검증 10-02).
            // 수명 = explosionDuration. 프리팹 파티클이 더 길면 데이터 값을 늘린다(서버 대기와 같은 기준).
            DestroyBoomVfx();
            _boomVfx = Instantiate(_d.explosionVfxPrefab, new Vector3(pos.x, _vFloorY, pos.z), Quaternion.identity);
        }
        else
        {
            BossPatternVisuals.Paint(_boom, _d.explosionColor, BossPatternVisuals.DiscTexture);
            _boom.gameObject.SetActive(true);
        }
    }

    /// <summary>서버 취소 RPC — 진행 중 표식·원·드론에 더해 폭발 VFX 까지 거둔다.</summary>
    public void ClientCancel()
    {
        HideTemp();
        DestroyBoomVfx();
    }

    void DestroyBoomVfx()
    {
        if (_boomVfx != null) { Destroy(_boomVfx); _boomVfx = null; }
    }

    // 단계 전환용 — 임시 판·드론만 지운다(폭발 VFX 는 자기 수명대로 둔다).
    void HideTemp()
    {
        _view = View.None;
        _crossTarget = null;
        if (_cross != null) _cross.gameObject.SetActive(false);
        if (_outer != null) _outer.gameObject.SetActive(false);
        if (_fill != null) _fill.gameObject.SetActive(false);
        if (_boom != null) _boom.gameObject.SetActive(false);
        StopDroneClips();
        if (_droneGo != null) { Destroy(_droneGo); _droneGo = null; }
    }

    void ClientTick()
    {
        if (_view == View.None) return;
        float k = Mathf.Clamp01((Time.time - _vStart) / _vDur);

        switch (_view)
        {
            case View.Mark:
                if (_crossTarget == null) { ClientCancel(); return; }
                TickCrosshair(k);
                break;

            case View.Lock:
                PlaceDisc(_fill, _vRadius * k, 0.01f);   // 안쪽 진한 원이 1초 동안 차오른다(§7)
                if (_droneGo != null)
                {
                    // lockTime 의 마지막 fallTime 동안 대각선 출발점 → 충돌 지점 직선 비행 — 끝 = 충돌 순간.
                    float fallStart = 1f - Mathf.Clamp01(_d.fallTime / _vDur);
                    float f = fallStart >= 1f ? 1f : Mathf.Clamp01((k - fallStart) / (1f - fallStart));
                    float p = ApproachProgress(f, _d.approachSlowTimePortion, _d.approachSlowDistancePortion);
                    _droneGo.transform.position = Vector3.Lerp(_vFrom, _vTo, p);
                    if (f > 0f && !_droneGo.activeSelf)
                    {
                        _droneGo.SetActive(true);
                        PlayDroneClips();
                    }
                    TickDroneClips();
                }
                break;

            case View.Boom:
                if (k >= 1f) { ClientCancel(); return; }
                // 임시 폭발 — 원이 반경 → explosionGrowScale 배로 퍼지며 사라진다.
                PlaceDisc(_boom, Mathf.Lerp(_vRadius, _vRadius * _d.explosionGrowScale, k), 0.02f);
                Color boom = _d.explosionColor;
                boom.a *= 1f - k;
                BossPatternVisuals.Paint(_boom, boom, null);
                break;
        }
    }

    // 크로스헤어: 대상 몸 중심을 따라가며 크게 시작해 줄어들고, 다 줄면 그 크기로 멈춰 있다가
    // 위치 고정 순간(ClientLock) 사라진다. **점멸 없음**(팀장 10-02). 줄어드는 구간·배율은 SO 값.
    // 캐릭터에 가려지지 않게 카메라 쪽으로 조금 당겨 그린다(§6.1 "모델보다 앞") — 셰이더 ZTest 를 바꾸지 않는 임시 방법.
    void TickCrosshair(float k)
    {
        Camera cam = Camera.main;
        Vector3 center = BossPatternTargets.BodyCenter(_crossTarget);
        Vector3 toCam = cam != null ? -cam.transform.forward : Vector3.up;
        Transform t = _cross.transform;
        t.position = center + toCam * _d.crosshairTowardCamera;
        t.rotation = Quaternion.FromToRotation(Vector3.up, toCam);

        float shrink = Mathf.Clamp01(k / Mathf.Max(0.01f, _d.crosshairShrinkPortion));

        // 색: 초록 → 주황 → 빨강(다 줄어 멈춤). 단계마다 딱 바뀐다 — 터렛 조준선과 같은 읽기(팀장 10-02).
        Color col = shrink >= 1f ? _d.crosshairColor
                  : shrink >= _d.crosshairMidColorAt ? _d.crosshairMidColor
                  : _d.crosshairStartColor;
        if (col != _crossColor) { _crossColor = col; BossPatternVisuals.Paint(_cross, col, null); }
        float size = _d.crosshairSize * Mathf.Lerp(_d.crosshairStartScale, _d.crosshairEndScale, shrink);
        // 최소 표시 크기 — 카메라가 기준 거리보다 멀면 그만큼 키운다.
        if (cam != null)
            size *= Mathf.Max(1f, Vector3.Distance(cam.transform.position, center) / _d.crosshairReferenceDistance);
        t.localScale = new Vector3(size, 1f, size);
    }

    void PlaceDisc(MeshRenderer m, float radius, float lift)
    {
        Transform t = m.transform;
        t.SetPositionAndRotation(new Vector3(_vPos.x, _vFloorY + lift, _vPos.z), Quaternion.identity);
        float d = Mathf.Max(0.01f, radius * 2f);
        t.localScale = new Vector3(d, 1f, d);
    }

    void EnsureView()
    {
        if (_viewRoot != null) return;
        _viewRoot = new GameObject("WellsDroneView").transform;
        _cross = BossPatternVisuals.CreateFlat("Crosshair", _viewRoot);
        _outer = BossPatternVisuals.CreateFlat("CircleOuter", _viewRoot);
        _fill = BossPatternVisuals.CreateFlat("CircleFill", _viewRoot);
        _boom = BossPatternVisuals.CreateFlat("Explosion", _viewRoot);
    }

    void OnDisable() => ClientCancel();

    void OnDestroy()
    {
        StopDroneClips();
        if (_droneGo != null) Destroy(_droneGo);
        DestroyBoomVfx();
        if (_viewRoot != null) Destroy(_viewRoot.gameObject);
        if (_ownedDefaults != null) Destroy(_ownedDefaults);
    }

    #endregion
}
