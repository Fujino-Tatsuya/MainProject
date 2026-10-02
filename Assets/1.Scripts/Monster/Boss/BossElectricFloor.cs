using System;
using UnityEngine;

/// <summary>
/// 23호 전기 장판 — 일반 전투 교차 장판 + 송전기 충전 기믹 A/B 장판.
/// 기획: Docs/design/boss/boss-electric-floor.md · PLAN-boss-electric-drone.md.
///
/// <b>권한</b>: 선택·타이머·피해 = 서버. 클라는 23호 ClientRpc 로 받은 마스크만 그린다(<see cref="ClientShowWarn"/> 등).
/// 이 컴포넌트는 NetworkBehaviour 가 아니다 — 23호가 스폰 때 붙이고(<c>OnNetworkSpawn</c>) RPC 를 대신 보낸다.
/// 그래서 네트워크 프리팹의 NetworkBehaviour 구성이 바뀌지 않는다.
///
/// <b>모드</b>(서버, 매 프레임 23호 상태에서 다시 계산 — 진입·이탈 지점을 23호에 흩뿌리지 않으려고 폴링한다):
/// - Paused: 23호 그로기 종류가 <see cref="BossElectricFloorDataSO.pauseOn"/> 에 걸림 → 예고·VFX 제거, 멈춤.
/// - ChargePrep: 송전기 진입 점프 출발 ~ 착지 → 일반 장판 제거, 아무것도 안 낸다(팀장 10-02: 기믹 시작 = 점프 출발).
/// - Charge: 송전기 차징 대기 중(착지 후) → A/B 교대.
/// - Normal: 그 밖(취약·과충전·잡기 포함 — §8) → 교차 장판.
/// 모드가 Charge/Paused 에서 Normal 로 돌아오면 첫 예고 대기(4초)부터 다시 센다(§8). 과충전 진입·종료는
/// 모드 변화가 아니므로 주기·직전 줄 기록이 그대로 이어진다.
/// </summary>
// 🔴 23호(MonsterBase.Update — FSM·TickCharge) **다음에** 돈다. 마지막 송전탑이 부서진 프레임에 장판 예고도 끝나면,
//    순서가 반대일 때 아직 ChargeWait 로 읽혀 취소돼야 할 공격이 나간다(Codex 교차검증 10-02 · 기획 §5.4).
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public sealed class BossElectricFloor : MonoBehaviour
{
    // ChargePrep = 송전기 진입 점프 중(팀장 10-02: 기믹 시작 = 점프 출발) — 일반 장판을 정리하고 아무것도 안 낸다.
    enum Mode { Off, Normal, ChargePrep, Charge, Paused }
    enum Step { Wait, Warn, Vfx }

    TwentyThreeBoss _boss;
    BossElectricFloorDataSO _d;
    BossElectricFloorPatterns _patterns;

    // ── 서버 ──
    Mode _mode = Mode.Off;
    Step _step;
    float _t;
    ulong _mask;
    bool _shown;   // 클라에 예고/VFX 가 떠 있다 — 모드 전환 때 지울지
    BossElectricFloorPatterns.ChargeGroup _group;
    int _bStage;   // 0 = A, 1 = B 안쪽 5×5, 2 = B 홀수 4줄

    /// <summary>[서버] 장판 1회 발동에서 실제로 맞은 인원(무적·잡힘·쓰러짐 제외, 보호막이 막아도 포함). 보스 고유 게이지(B6) 훅 — 지금 구독자 없음.</summary>
    public event Action<int> ServerFloorHit;

    BossElectricFloorDataSO _ownedDefaults;   // 데이터가 비어 직접 만든 기본값 — 우리가 지운다

    public void Init(TwentyThreeBoss boss, BossElectricFloorDataSO data)
    {
        _boss = boss;
        if (data != null) _d = data;
        else _d = _ownedDefaults != null ? _ownedDefaults : (_ownedDefaults = ScriptableObject.CreateInstance<BossElectricFloorDataSO>());
        _patterns = new BossElectricFloorPatterns(new System.Random(Environment.TickCount ^ GetInstanceID()));
        ResetState();
    }

    /// <summary>스폰/디스폰 경계 — 서버 진행 상태와 화면 연출을 처음으로(풀 재사용 대비).</summary>
    public void ResetState()
    {
        _mode = Mode.Off;
        _step = Step.Wait;
        _t = 0f;
        _shown = false;
        _patterns?.ResetAll();
        HideAllTiles();
    }

    void Update()
    {
        if (_boss == null) return;
        if (_boss.IsServer) ServerTick(Time.deltaTime);
        ClientTick();
    }

    void OnDisable()
    {
        // 보스가 꺼지면(디스폰·씬 정리) 바닥에 남지 않게.
        HideAllTiles();
    }

    #region 서버

    void ServerTick(float dt)
    {
        if (_boss.IsDead)
        {
            if (_mode != Mode.Off)
            {
                Clear();
                _patterns.ResetAll();
                _mode = Mode.Off;
                Debug.Log("[전기장판] 보스 사망 — 예고·VFX·기록 전부 제거", this);
            }
            return;
        }
        if (!_boss.IsFightActive) return;

        // 살아 있는 플레이어가 없으면(전원 유령 — 목숨은 남았지만 부활 입력 전) 멈춘다(팀장 10-02).
        // 드론은 대상 선정에서 자연히 기다리지만 장판은 타이머만 보고 계속 나왔다. 부활하면 4초 대기부터 다시.
        Mode want = !AnyPlayerAlive() ? Mode.Paused
                  : (_boss.ActivePauseConditions & _d.pauseOn) != 0 ? Mode.Paused
                  : _boss.IsChargeGimmickActive ? Mode.Charge
                  : _boss.IsChargeJumpActive ? Mode.ChargePrep
                  : Mode.Normal;
        if (want != _mode) Transition(want);

        if (_mode == Mode.Normal) TickNormal(dt);
        else if (_mode == Mode.Charge) TickCharge(dt);
    }

    static bool AnyPlayerAlive()
    {
        foreach (Player p in BossPatternTargets.AllPlayers())
            if (BossPatternTargets.IsAlive(p)) return true;
        return false;
    }

    void Transition(Mode to)
    {
        Mode from = _mode;
        Clear();   // 진행 중 예고·VFX 제거(§5.4 · §8) — 발동 전이면 공격도 안 나간다
        if (from == Mode.Charge) _patterns.ResetCharge();
        _mode = to;

        switch (to)
        {
            case Mode.Normal:
                _step = Step.Wait;
                _t = _d.firstDelay;   // 전투 시작 · 제압 종료 · 기믹 종료 모두 4초부터(§4.2 · §8)
                break;
            case Mode.Charge:
                BeginChargeGroup();
                break;
        }
        Debug.Log($"[전기장판] 모드 {from} → {to}", this);
    }

    void TickNormal(float dt)
    {
        _t -= dt;
        if (_t > 0f) return;

        switch (_step)
        {
            case Step.Wait:
                _mask = _patterns.NextNormal();
                Warn(_mask, _d.normalWarnTime);
                break;
            case Step.Warn:
                Fire(_mask);
                break;
            case Step.Vfx:
                EndVfx();
                _step = Step.Wait;
                _t = _d.normalGapAfterVfx;
                break;
        }
    }

    void TickCharge(float dt)
    {
        _t -= dt;
        if (_t > 0f) return;

        switch (_step)
        {
            case Step.Wait:   // 그룹 사이 간격 끝
                BeginChargeGroup();
                break;
            case Step.Warn:
                Fire(_mask);
                break;
            case Step.Vfx:
                EndVfx();
                if (_group == BossElectricFloorPatterns.ChargeGroup.B && _bStage == 1)
                {
                    // B 1단계 VFX 가 끝나면 바로 2단계 예고(§5.3). 전멸로 기믹이 끝났다면 모드가 이미
                    // Normal 로 바뀌어 여기 오지 않는다 — "1단계 뒤 전멸이면 2단계 생략"이 그렇게 성립한다.
                    _bStage = 2;
                    _mask = _patterns.NextBStage2(out _);
                    Warn(_mask, _d.chargeWarnTime);
                }
                else
                {
                    _step = Step.Wait;
                    _t = _d.chargeGroupGap;
                }
                break;
        }
    }

    void BeginChargeGroup()
    {
        _group = _patterns.NextGroup();
        if (_group == BossElectricFloorPatterns.ChargeGroup.A)
        {
            _bStage = 0;
            _mask = _patterns.NextA();
        }
        else
        {
            _bStage = 1;
            _mask = BossElectricFloorPatterns.BInner;
        }
        Warn(_mask, _d.chargeWarnTime);
    }

    void Warn(ulong mask, float time)
    {
        _step = Step.Warn;
        _t = time;
        _shown = true;
        _boss.SendFloorWarn(mask, time);
    }

    // §6: 발동 순간의 발 위치 · 공격 1회당 대상당 1회 · 플레이어만.
    void Fire(ulong mask)
    {
        _step = Step.Vfx;
        _t = _d.vfxDuration;
        _boss.SendFloorFire(mask, _d.vfxDuration);

        if (!_boss.TryGetTileGrid(out BossTileGrid grid))
        {
            Debug.LogWarning("[전기장판] 보스방 경계를 못 찾아 판정을 건너뛴다(InvisibleBoundaries).", this);
            return;
        }

        int hit = 0, gauge = 0;
        foreach (Player p in BossPatternTargets.AllPlayers())
        {
            if (!BossPatternTargets.IsValid(p, _boss)) continue;
            if (!grid.Contains(mask, BossPatternTargets.FootPosition(p))) continue;
            hit++;
            if (!BossPatternTargets.IsInvulnerable(p)) gauge++;
            // 출처 = 발 위치(맞은 타일). 보스 위치를 넘기면 보호막 파문 등이 보스 쪽에서 맞은 것처럼 보인다.
            Vector3 foot = BossPatternTargets.FootPosition(p);
            BossPatternTargets.Damage(p, _d.damage, foot, _boss.transform);
        }
        ServerFloorHit?.Invoke(gauge);
        Debug.Log($"[전기장판] 발동 {_mode}{(_mode == Mode.Charge ? $"/{_group}{(_bStage > 0 ? _bStage.ToString() : "")}" : "")} — " +
                  $"{BossTileGrid.Count(mask)}칸 · 적중 {hit}명(게이지 인원 {gauge})", this);
    }

    void EndVfx()
    {
        _shown = false;   // 클라는 vfxDuration 뒤 스스로 지운다
    }

    void Clear()
    {
        if (!_shown) return;
        _shown = false;
        _boss.SendFloorClear();
    }

    #endregion

    #region 클라(전 피어) — 임시 연출

    // 표시 = 판정 칸 그대로(1.0). 줄이면 경계 안쪽 몇 cm 가 "안 보이는데 맞는" 구역이 된다(Codex 교차검증 10-02, PLAN §8).
    // 칸 구분은 텍스처 안쪽 테두리가 한다.
    const float FullTile = 1f;

    MeshRenderer[] _outer, _fill;
    GameObject[] _vfx;
    float[] _tileY;
    BossTileGrid _viewGrid;
    ulong _viewMask;
    float _viewStart, _viewDur;
    bool _viewFiring;

    public void ClientShowWarn(ulong mask, float warnTime)
    {
        if (!EnsureView()) return;
        HideAllTiles();
        _viewMask = mask;
        _viewStart = Time.time;
        _viewDur = Mathf.Max(0.01f, warnTime);
        _viewFiring = false;
        ForEachTile(mask, (r, c, i) =>
        {
            PlaceTile(_outer[i], r, c, i, FullTile);
            BossPatternVisuals.Paint(_outer[i], _d.warnOuterColor, BossPatternVisuals.SquareTexture);
            _outer[i].gameObject.SetActive(true);
            PlaceTile(_fill[i], r, c, i, 0f);
            BossPatternVisuals.Paint(_fill[i], _d.warnFillColor, BossPatternVisuals.SquareTexture);
            _fill[i].gameObject.SetActive(true);
        });
    }

    public void ClientFire(ulong mask, float vfxTime)
    {
        if (!EnsureView()) return;
        HideAllTiles();
        _viewMask = mask;
        _viewStart = Time.time;
        _viewDur = Mathf.Max(0.01f, vfxTime);
        _viewFiring = true;
        ForEachTile(mask, (r, c, i) =>
        {
            PlaceTile(_outer[i], r, c, i, FullTile);
            BossPatternVisuals.Paint(_outer[i], _d.electricColor, BossPatternVisuals.SquareTexture);
            _outer[i].gameObject.SetActive(true);

            if (_d.electricVfxPrefab != null)
            {
                _vfx[i] = Instantiate(_d.electricVfxPrefab, _outer[i].transform.position, _viewGrid.Rotation);
                Destroy(_vfx[i], _viewDur);
            }
        });
    }

    public void ClientClear() => HideAllTiles();

    void ClientTick()
    {
        if (_viewMask == 0 || _outer == null) return;
        float k = (Time.time - _viewStart) / _viewDur;

        // 예고가 다 차면 서버의 Fire 가 곧 덮어쓴다 — 꽉 찬 채로 둔다. VFX 는 끝나면 원래 바닥으로(§9-7).
        if (k >= 1f && _viewFiring) { HideAllTiles(); return; }

        Color col = _d.electricColor;
        if (_d.electricFlickerSpeed > 0f)
            col.a *= 0.55f + 0.45f * Mathf.Sin(Time.time * _d.electricFlickerSpeed);   // 임시 전기 — 빠른 깜빡임
        float s = FullTile * Mathf.Clamp01(k);                      // 안쪽 진한 사각형이 중심에서 차오른다(§9-2)

        // 매 프레임 도는 곳이라 람다(ForEachTile) 대신 루프 — 할당 0.
        for (int r = 0; r < BossTileGrid.Size; r++)
            for (int c = 0; c < BossTileGrid.Size; c++)
            {
                if (!BossTileGrid.Has(_viewMask, r, c)) continue;
                int i = BossTileGrid.Bit(r, c);
                if (_viewFiring) BossPatternVisuals.Paint(_outer[i], col, null);
                else PlaceTile(_fill[i], r, c, i, s);
            }
    }

    bool EnsureView()
    {
        if (_outer != null) return true;
        if (_boss == null || !_boss.TryGetTileGrid(out _viewGrid)) return false;

        _outer = new MeshRenderer[BossTileGrid.TileCount];
        _fill = new MeshRenderer[BossTileGrid.TileCount];
        _vfx = new GameObject[BossTileGrid.TileCount];
        _tileY = new float[BossTileGrid.TileCount];

        // 보스 자식으로 두지 않는다 — 보스가 움직이고 돈다. 씬 루트에 두고 보스 수명에 맞춰 정리한다.
        var root = new GameObject("BossElectricFloorView").transform;
        _viewRoot = root;
        for (int r = 0; r < BossTileGrid.Size; r++)
            for (int c = 0; c < BossTileGrid.Size; c++)
            {
                int i = BossTileGrid.Bit(r, c);
                Vector3 center = _viewGrid.TileCenter(r, c);
                _tileY[i] = BossPatternVisuals.SampleFloorY(center, center.y) + _d.surfaceOffset;
                _outer[i] = BossPatternVisuals.CreateFlat($"Tile{r}{c}_Outer", root);
                _fill[i] = BossPatternVisuals.CreateFlat($"Tile{r}{c}_Fill", root);
            }
        return true;
    }

    Transform _viewRoot;

    void OnDestroy()
    {
        if (_viewRoot != null) Destroy(_viewRoot.gameObject);
        if (_ownedDefaults != null) Destroy(_ownedDefaults);
    }

    void PlaceTile(MeshRenderer m, int r, int c, int i, float scale01)
    {
        Vector3 p = _viewGrid.TileCenter(r, c);
        p.y = _tileY[i] + (m == _fill[i] ? 0.01f : 0f);   // 채움이 바깥 위
        Transform t = m.transform;
        t.SetPositionAndRotation(p, _viewGrid.Rotation);
        Vector2 e = _viewGrid.TileExtent;
        t.localScale = new Vector3(e.x * scale01, 1f, e.y * scale01);
    }

    void HideAllTiles()
    {
        _viewMask = 0;
        if (_outer == null) return;
        for (int i = 0; i < _outer.Length; i++)
        {
            if (_outer[i] != null) _outer[i].gameObject.SetActive(false);
            if (_fill[i] != null) _fill[i].gameObject.SetActive(false);
            if (_vfx[i] != null) { Destroy(_vfx[i]); _vfx[i] = null; }
        }
    }

    static void ForEachTile(ulong mask, Action<int, int, int> f)
    {
        for (int r = 0; r < BossTileGrid.Size; r++)
            for (int c = 0; c < BossTileGrid.Size; c++)
                if (BossTileGrid.Has(mask, r, c)) f(r, c, BossTileGrid.Bit(r, c));
    }

    #endregion

#if UNITY_EDITOR
    // 타일 번호 확인용(PLAN §7 — 회전 배치된 방에서 칸이 바닥과 맞는가). 기획 표기 (행+1, 열+1).
    void OnDrawGizmosSelected()
    {
        if (_boss == null || !_boss.TryGetTileGrid(out BossTileGrid g)) return;
        Vector2 e = g.TileExtent;
        for (int r = 0; r < BossTileGrid.Size; r++)
            for (int c = 0; c < BossTileGrid.Size; c++)
            {
                Vector3 p = g.TileCenter(r, c);
                Gizmos.color = (r + c) % 2 == 0 ? new Color(1f, 0.6f, 0f, 0.5f) : new Color(1f, 1f, 0f, 0.5f);
                Gizmos.matrix = Matrix4x4.TRS(p, g.Rotation, Vector3.one);
                Gizmos.DrawWireCube(Vector3.zero, new Vector3(e.x, 0.05f, e.y));
                Gizmos.matrix = Matrix4x4.identity;
                UnityEditor.Handles.Label(p + Vector3.up * 0.2f, $"({r + 1},{c + 1})");
            }
    }
#endif
}
