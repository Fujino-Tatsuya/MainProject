#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

/// <summary>할 일 103 C3 — 감지한 보스 위험 1건(이 인스턴스가 본 것). id 는 이 봇의 종류별 회차(<c>dash#3</c>) — 봇끼리는 <see cref="ServerT"/>·종류로 맞춘다.</summary>
public sealed class QABoss103Threat
{
    public string Id;
    public QABoss103ThreatKind Kind;
    /// <summary>보스 공격 id — 서버(호스트)에서만 안다(<c>_currentEntry</c>). 클라는 "na".</summary>
    public string Attack = "na";
    /// <summary>감지 실시간(초, timeScale 무관).</summary>
    public double DetectedAt;
    /// <summary>감지 순간 <c>NetworkManager.ServerTime.Time</c> — 봇 3명의 같은 위험을 잇는 열쇠.</summary>
    public double ServerT;
    /// <summary>감지 순간 남은 예고(초) — 반응 지연보다 짧으면 늦은 회피다. 모르면 NaN.</summary>
    public double LeadSeconds = double.NaN;
    /// <summary>위험이 끝날 것으로 보는 실시간(초) — 예고가 떠 있는 동안 계속 늘어난다.</summary>
    public double EndsAt;
    public bool Active = true;

    // 원·칸 위험
    public Vector3 Center;
    public float Radius;
    public ulong Mask;
    /// <summary>드론: 표식 단계에서 내가 대상이었다.</summary>
    public bool TargetedMe;

    /// <summary>남은 위험 시간(초).</summary>
    public double Remaining(double now) => Math.Max(0.0, EndsAt - now);
}

/// <summary>
/// 할 일 103 C3 위험 감지(오너 클라 — 호스트·가상 플레이어 각자). 보스 코드는 바꾸지 않는다 — 전 피어에 이미 그려지는 예고를
/// <see cref="QABossProfileProbe"/> 처럼 리플렉션으로 읽거나, 공개 이벤트(<see cref="BossElectricFloor.ClientWarnShown"/>)를 듣는다.
/// <list type="bullet">
/// <item>부채꼴·띠 예고(<see cref="BossAttackConeTelegraph"/> <c>_active</c> 상승 또는 <c>_elapsed</c> 되감김 = 새 예고): 훅·어퍼·전진·네모·돌진(파랑)·레이지 돌진·잡기.
///   모양 필드(반경·반각·치우침·띠 길이·폭)로 범위를 매 프레임 보스 현재 정면 기준으로 판정한다(예고도 보스를 따라 돈다).</item>
/// <item>점프 착지(<c>TwentyThreeBoss._c3JumpTelegraph</c> 새 객체 → <see cref="EnemyC3JumpView"/> 반경·성장 시간).</item>
/// <item>드론(<see cref="WellsDroneAttack"/> <c>_view</c> = Lock 진입 → <c>_vPos·_vRadius·_vDur</c>). 표식(Mark) 단계는 나를 따라오므로 굴리지 않고 대상 여부만 기억한다.</item>
/// <item>전기 장판(<see cref="BossElectricFloor.ClientWarnShown"/> 마스크·예고 시간 → 보스 타일 격자).</item>
/// <item>그 밖 = 보스 복제 상태 <see cref="MonsterState.Attack"/> 진입 뒤 <see cref="GenericDelaySeconds"/> 안에 위 예고가 없으면 '예고 없는 공격'.</item>
/// </list>
/// </summary>
public sealed class QABoss103ThreatSensor
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static readonly FieldInfo ConeActive = typeof(BossAttackConeTelegraph).GetField("_active", Inst);
    private static readonly FieldInfo ConeElapsed = typeof(BossAttackConeTelegraph).GetField("_elapsed", Inst);
    private static readonly FieldInfo ConeGrow = typeof(BossAttackConeTelegraph).GetField("_growTime", Inst);
    private static readonly FieldInfo ConeRadius = typeof(BossAttackConeTelegraph).GetField("_coneRadius", Inst);
    private static readonly FieldInfo ConeHalfAngle = typeof(BossAttackConeTelegraph).GetField("_coneHalfAngle", Inst);
    private static readonly FieldInfo ConeOffsetAngle = typeof(BossAttackConeTelegraph).GetField("_coneOffsetAngle", Inst);
    private static readonly FieldInfo ConeForward = typeof(BossAttackConeTelegraph).GetField("_coneForwardOffset", Inst);
    private static readonly FieldInfo PathRadius = typeof(BossAttackConeTelegraph).GetField("_pathRadius", Inst);
    private static readonly FieldInfo PathLength = typeof(BossAttackConeTelegraph).GetField("_pathLength", Inst);
    private static readonly FieldInfo PathForward = typeof(BossAttackConeTelegraph).GetField("_pathForwardOffset", Inst);
    private static readonly FieldInfo PathLateral = typeof(BossAttackConeTelegraph).GetField("_pathLateralOffset", Inst);
    private static readonly FieldInfo ConePalette = typeof(BossAttackConeTelegraph).GetField("_palette", Inst);
    private static readonly FieldInfo ConeInward = typeof(BossAttackConeTelegraph).GetField("_fillInward", Inst);

    private static readonly FieldInfo JumpTelegraph = typeof(TwentyThreeBoss).GetField("_c3JumpTelegraph", Inst);
    private static readonly FieldInfo JumpRadius = typeof(EnemyC3JumpView).GetField("radius", Inst);
    private static readonly FieldInfo JumpDuration = typeof(EnemyC3JumpView).GetField("duration", Inst);
    private static readonly FieldInfo JumpElapsed = typeof(EnemyC3JumpView).GetField("elapsed", Inst);

    private static readonly FieldInfo DroneView = typeof(WellsDroneAttack).GetField("_view", Inst);
    private static readonly FieldInfo DronePos = typeof(WellsDroneAttack).GetField("_vPos", Inst);
    private static readonly FieldInfo DroneRadius = typeof(WellsDroneAttack).GetField("_vRadius", Inst);
    private static readonly FieldInfo DroneDur = typeof(WellsDroneAttack).GetField("_vDur", Inst);
    private static readonly FieldInfo DroneStart = typeof(WellsDroneAttack).GetField("_vStart", Inst);
    private static readonly FieldInfo DroneCross = typeof(WellsDroneAttack).GetField("_crossTarget", Inst);

    private static readonly FieldInfo CurrentEntry = typeof(TwentyThreeBoss).GetField("_currentEntry", Inst);

    /// <summary>공격 상태 진입 뒤 이 시간(초) 안에 예고가 없으면 '예고 없는 공격'으로 본다(RPC·복제 도착 순서 차이 흡수).</summary>
    public const double GenericDelaySeconds = 0.2;
    /// <summary>예고 없는 공격의 위험 시간(초).</summary>
    public const double GenericSeconds = 1.0;
    /// <summary>예고가 다 찬 뒤 판정이 나가는 꼬리(초).</summary>
    private const double TailSeconds = 0.3;
    /// <summary>끝난 위험을 목록에서 지우기 전 보관(초).</summary>
    private const double KeepSeconds = 3.0;

    private const int DroneLock = 2;   // WellsDroneAttack.View: None 0 · Mark 1 · Lock 2

    private TwentyThreeBoss _boss;
    private BossAttackConeTelegraph _cone;
    private WellsDroneAttack _drone;
    private BossElectricFloor _floor;

    private readonly List<QABoss103Threat> _threats = new List<QABoss103Threat>();
    private readonly Dictionary<QABoss103ThreatKind, int> _counts = new Dictionary<QABoss103ThreatKind, int>();

    private QABoss103Threat _coneThreat, _jumpThreat, _droneThreat, _floorThreat;
    private bool _coneWas;
    private float _coneLastElapsed;
    private GameObject _jumpWas;
    private int _droneViewWas;
    private float _droneStartWas = -1f;
    private bool _droneMarkedMe;

    private MonsterState _bossStateWas;
    private double _attackEdgeAt = -1.0;
    private double _lastShapeAt = -1.0;

    private bool _floorPending;
    private ulong _floorMask;
    private float _floorWarn;

    /// <summary>못 찾은 리플렉션 멤버(로그용, 비면 "none").</summary>
    public string Missing { get; }

    public IReadOnlyList<QABoss103Threat> Threats => _threats;
    public TwentyThreeBoss Boss => _boss;

    public QABoss103ThreatSensor()
    {
        var miss = new List<string>();
        if (ConeActive == null || ConeElapsed == null || ConeGrow == null || ConeRadius == null || ConeHalfAngle == null || ConeOffsetAngle == null ||
            ConeForward == null || PathRadius == null || PathLength == null || PathForward == null || PathLateral == null || ConePalette == null || ConeInward == null)
            miss.Add("cone");
        if (JumpTelegraph == null || JumpRadius == null || JumpDuration == null || JumpElapsed == null) miss.Add("jump");
        if (DroneView == null || DronePos == null || DroneRadius == null || DroneDur == null || DroneStart == null || DroneCross == null) miss.Add("drone");
        Missing = miss.Count > 0 ? string.Join("+", miss) : "none";
    }

    /// <summary>대상 보스를 바꾼다(장판 이벤트 구독 포함). null 이면 놓는다.</summary>
    public void Bind(TwentyThreeBoss boss)
    {
        if (boss == _boss)
            return;
        Unbind();
        _boss = boss;
        if (boss == null)
            return;
        _bossStateWas = boss.State;
        _floor = boss.GetComponent<BossElectricFloor>();
        if (_floor != null)
            _floor.ClientWarnShown += OnFloorWarn;
    }

    public void Unbind()
    {
        if (_floor != null)
            _floor.ClientWarnShown -= OnFloorWarn;
        _floor = null;
        _boss = null;
        _cone = null;
        _drone = null;
        _threats.Clear();
        _coneThreat = _jumpThreat = _droneThreat = _floorThreat = null;
        _coneWas = false;
        _jumpWas = null;
        _droneViewWas = 0;
        _droneStartWas = -1f;
        _floorPending = false;
    }

    private void OnFloorWarn(ulong mask, float warnTime)
    {
        _floorPending = true;
        _floorMask = mask;
        _floorWarn = warnTime;
    }

    /// <summary>매 프레임(전투 중). <paramref name="now"/> = 실시간(초).</summary>
    public void Tick(Player me, double now)
    {
        if (_boss == null)
            return;
        if (_cone == null)
            _cone = _boss.GetComponent<BossAttackConeTelegraph>();   // 보스가 첫 예고 때 붙인다
        if (_drone == null)
            _drone = _boss.GetComponent<WellsDroneAttack>();

        TickCone(now);
        TickJump(now);
        TickDrone(me, now);
        TickFloor(now);
        TickGeneric(now);

        for (int i = _threats.Count - 1; i >= 0; i--)
        {
            QABoss103Threat t = _threats[i];
            if (t.Active && now >= t.EndsAt && (t.Kind == QABoss103ThreatKind.Floor || t.Kind == QABoss103ThreatKind.Generic))
                t.Active = false;
            if (!t.Active && now - t.EndsAt > KeepSeconds)
                _threats.RemoveAt(i);
        }
    }

    // ── 종류별 감지 ─────────────────────────────────────────────

    private void TickCone(double now)
    {
        if (_cone == null || ConeActive == null || ConeElapsed == null)
            return;
        bool on = (bool)ConeActive.GetValue(_cone);
        float elapsed = (float)ConeElapsed.GetValue(_cone);
        bool fresh = on && (!_coneWas || elapsed + 0.05f < _coneLastElapsed);
        _coneWas = on;
        _coneLastElapsed = elapsed;

        if (fresh)
        {
            End(_coneThreat, now);
            float grow = F(ConeGrow);
            bool interrupt = ConePalette != null && Convert.ToInt32(ConePalette.GetValue(_cone)) == (int)BossAttackConeTelegraph.Palette.Interrupt;
            bool inward = ConeInward != null && (bool)ConeInward.GetValue(_cone);
            QABoss103ThreatKind kind = QABoss103Evasion.ClassifyTelegraph(interrupt, inward, F(ConeRadius), F(PathRadius), F(PathLength), grow);
            _coneThreat = Add(kind, now, Math.Max(0.0, grow - elapsed));
            _lastShapeAt = now;
        }
        if (_coneThreat != null && _coneThreat.Active)
        {
            if (on)
                _coneThreat.EndsAt = now + Math.Max(0.0, F(ConeGrow) - elapsed) + TailSeconds;
            else
                End(_coneThreat, now);
        }
    }

    private void TickJump(double now)
    {
        if (JumpTelegraph == null)
            return;
        GameObject go = JumpTelegraph.GetValue(_boss) as GameObject;
        if (go == null)
            go = null;   // 파괴된 객체(가짜 null) 정리
        if (go != null && go != _jumpWas)
        {
            End(_jumpThreat, now);
            EnemyC3JumpView view = go.GetComponent<EnemyC3JumpView>();
            float radius = view != null && JumpRadius != null ? (float)JumpRadius.GetValue(view) : 0f;
            float dur = view != null && JumpDuration != null ? (float)JumpDuration.GetValue(view) : 0f;
            float el = view != null && JumpElapsed != null ? (float)JumpElapsed.GetValue(view) : 0f;
            _jumpThreat = Add(QABoss103ThreatKind.Jump, now, Math.Max(0.0, dur - el));
            _jumpThreat.Center = go.transform.position;
            _jumpThreat.Radius = radius;
            _lastShapeAt = now;
        }
        if (_jumpThreat != null && _jumpThreat.Active)
        {
            if (go != null)
            {
                EnemyC3JumpView view = go.GetComponent<EnemyC3JumpView>();
                float dur = view != null && JumpDuration != null ? (float)JumpDuration.GetValue(view) : 0f;
                float el = view != null && JumpElapsed != null ? (float)JumpElapsed.GetValue(view) : 0f;
                _jumpThreat.EndsAt = now + Math.Max(0.0, dur - el) + TailSeconds;
            }
            else
            {
                End(_jumpThreat, now);
            }
        }
        _jumpWas = go;
    }

    private void TickDrone(Player me, double now)
    {
        if (_drone == null || DroneView == null)
            return;
        int view = Convert.ToInt32(DroneView.GetValue(_drone));
        if (view == 1 && DroneCross != null)
        {
            Transform cross = DroneCross.GetValue(_drone) as Transform;
            _droneMarkedMe = me != null && cross != null && cross.root == me.transform.root;
        }
        float start = DroneStart != null ? (float)DroneStart.GetValue(_drone) : 0f;
        bool fresh = view == DroneLock && (_droneViewWas != DroneLock || !Mathf.Approximately(start, _droneStartWas));
        if (fresh)
        {
            End(_droneThreat, now);
            float dur = DroneDur != null ? (float)DroneDur.GetValue(_drone) : 0f;
            float elapsed = Mathf.Max(0f, Time.time - start);
            _droneThreat = Add(QABoss103ThreatKind.Drone, now, Math.Max(0.0, dur - elapsed));
            _droneThreat.Center = DronePos != null ? (Vector3)DronePos.GetValue(_drone) : Vector3.zero;
            _droneThreat.Radius = DroneRadius != null ? (float)DroneRadius.GetValue(_drone) : 0f;
            _droneThreat.TargetedMe = _droneMarkedMe;
            _droneThreat.EndsAt = now + Math.Max(0.0, dur - elapsed) + TailSeconds;
            _droneStartWas = start;
        }
        if (_droneThreat != null && _droneThreat.Active && view != DroneLock)
            End(_droneThreat, now);
        if (view == 0)
            _droneMarkedMe = false;
        _droneViewWas = view;
    }

    private void TickFloor(double now)
    {
        if (!_floorPending)
            return;
        _floorPending = false;
        End(_floorThreat, now);
        _floorThreat = Add(QABoss103ThreatKind.Floor, now, _floorWarn);
        _floorThreat.Mask = _floorMask;
        _floorThreat.EndsAt = now + _floorWarn + 0.15;
    }

    private void TickGeneric(double now)
    {
        MonsterState state = _boss.State;   // 복제값
        if (state == MonsterState.Attack && _bossStateWas != MonsterState.Attack)
            _attackEdgeAt = now;
        _bossStateWas = state;

        if (_attackEdgeAt < 0.0 || now - _attackEdgeAt < GenericDelaySeconds)
            return;
        double edge = _attackEdgeAt;
        _attackEdgeAt = -1.0;
        // 공격 상태 진입 앞뒤로 예고가 떴으면 그 예고가 이 공격이다.
        if (_lastShapeAt >= edge - 0.3)
            return;
        QABoss103Threat t = Add(QABoss103ThreatKind.Generic, now, 0.0);
        t.Center = _boss.transform.position;
        t.EndsAt = now + GenericSeconds;
    }

    private QABoss103Threat Add(QABoss103ThreatKind kind, double now, double leadSeconds)
    {
        _counts.TryGetValue(kind, out int n);
        n++;
        _counts[kind] = n;
        NetworkManager nm = NetworkManager.Singleton;
        var t = new QABoss103Threat
        {
            Id = QABoss103Evasion.Token(kind) + "#" + n,
            Kind = kind,
            Attack = AttackId(),
            DetectedAt = now,
            ServerT = nm != null && nm.IsListening ? nm.ServerTime.Time : -1.0,
            LeadSeconds = leadSeconds,
            EndsAt = now + leadSeconds + TailSeconds,
        };
        _threats.Add(t);
        return t;
    }

    private static void End(QABoss103Threat t, double now)
    {
        if (t == null || !t.Active)
            return;
        t.Active = false;
        t.EndsAt = Math.Min(t.EndsAt, now + 0.1);
    }

    private string AttackId()
    {
        if (_boss == null || !_boss.IsServer || CurrentEntry == null)
            return "na";
        return CurrentEntry.GetValue(_boss) is BossAttackEntry e ? e.attackId.ToString() : "none";
    }

    private float F(FieldInfo f) => f != null && _cone != null ? (float)f.GetValue(_cone) : 0f;

    // ── 판정·방향 ──────────────────────────────────────────────

    private Vector3 BossForward()
    {
        Vector3 fwd = _boss != null ? _boss.transform.forward : Vector3.forward;
        fwd.y = 0f;
        return fwd.sqrMagnitude < 1e-4f ? Vector3.forward : fwd.normalized;
    }

    private bool IsConeKind(QABoss103ThreatKind k) =>
        k == QABoss103ThreatKind.Cone || k == QABoss103ThreatKind.Lunge || k == QABoss103ThreatKind.Band ||
        k == QABoss103ThreatKind.Dash || k == QABoss103ThreatKind.RageDash || k == QABoss103ThreatKind.Grab;

    /// <summary>
    /// 내가 이 위험 범위 안인가(대략). 부채꼴 계열 = 지금 보스 위치·정면 기준 띠/부채꼴, 점프·드론 = 원, 장판 = 예고 칸(±0.4m),
    /// 예고 없는 공격 = 보스 표면 3.5m 안·보스 앞. 드론은 표식 대상이었으면 원 밖이어도 위험으로 본다(나를 노린 공격).
    /// </summary>
    public bool Contains(QABoss103Threat t, Vector3 pos, float surfaceDistance)
    {
        if (_boss == null)
            return false;
        Vector3 bossPos = _boss.transform.position;
        Vector3 fwd = BossForward();
        QABoss103Evasion.WorldToLocal(fwd.x, fwd.z, pos.x - bossPos.x, pos.z - bossPos.z, out float f, out float r);

        if (IsConeKind(t.Kind))
        {
            if (_cone == null)
                return false;
            return QABoss103Evasion.InBand(f, r, F(PathForward), F(PathLength), F(PathRadius), F(PathLateral)) ||
                   QABoss103Evasion.InSector(f, r, F(ConeForward), F(ConeRadius), F(ConeHalfAngle), F(ConeOffsetAngle));
        }
        switch (t.Kind)
        {
            case QABoss103ThreatKind.Jump:
                return QABoss103Evasion.InCircle(pos.x - t.Center.x, pos.z - t.Center.z, t.Radius);
            case QABoss103ThreatKind.Drone:
                return t.TargetedMe || QABoss103Evasion.InCircle(pos.x - t.Center.x, pos.z - t.Center.z, t.Radius);
            case QABoss103ThreatKind.Floor:
                return InFloor(t.Mask, pos);
            default:
                return QABoss103Evasion.InGeneric(f, surfaceDistance);
        }
    }

    private bool InFloor(ulong mask, Vector3 pos)
    {
        if (_boss == null || !_boss.TryGetTileGrid(out BossTileGrid grid))
            return false;
        const float m = 0.4f;
        return grid.Contains(mask, pos) || grid.Contains(mask, pos + new Vector3(m, 0f, 0f)) || grid.Contains(mask, pos - new Vector3(m, 0f, 0f)) ||
               grid.Contains(mask, pos + new Vector3(0f, 0f, m)) || grid.Contains(mask, pos - new Vector3(0f, 0f, m));
    }

    /// <summary>
    /// 회피 방향(평면 단위). 콘·띠 = 옆(<see cref="QABoss103Evasion.TelegraphEscapeLocal"/>), 점프 = 착지 중심 반대, 드론 = 고정 지점 반대,
    /// 장판 = 가장 가까운 예고 밖 칸 쪽(없으면 보스 반대), 예고 없는 공격 = 보스 반대.
    /// </summary>
    public Vector3 EscapeDirection(QABoss103Threat t, Vector3 pos)
    {
        Vector3 bossPos = _boss != null ? _boss.transform.position : pos;
        Vector3 fwd = BossForward();
        float x, z;
        if (IsConeKind(t.Kind) && _cone != null)
        {
            QABoss103Evasion.WorldToLocal(fwd.x, fwd.z, pos.x - bossPos.x, pos.z - bossPos.z, out float f, out float r);
            QABoss103Evasion.TelegraphEscapeLocal(f, r, F(ConeRadius), F(ConeHalfAngle), F(ConeOffsetAngle), F(ConeForward), F(PathRadius), F(PathLateral),
                                                  out float ef, out float er);
            QABoss103Evasion.LocalToWorld(fwd.x, fwd.z, ef, er, out x, out z);
            return new Vector3(x, 0f, z);
        }
        switch (t.Kind)
        {
            case QABoss103ThreatKind.Jump:
            case QABoss103ThreatKind.Drone:
                QABoss103Evasion.Away(pos.x - t.Center.x, pos.z - t.Center.z, pos.x - bossPos.x, pos.z - bossPos.z, out x, out z);
                return new Vector3(x, 0f, z);
            case QABoss103ThreatKind.Floor:
                if (TryNearestSafeTile(t.Mask, pos, out Vector3 safe))
                {
                    QABoss103Evasion.Away(safe.x - pos.x, safe.z - pos.z, pos.x - bossPos.x, pos.z - bossPos.z, out x, out z);
                    return new Vector3(x, 0f, z);
                }
                break;
        }
        QABoss103Evasion.Away(pos.x - bossPos.x, pos.z - bossPos.z, -fwd.x, -fwd.z, out x, out z);
        return new Vector3(x, 0f, z);
    }

    private bool TryNearestSafeTile(ulong mask, Vector3 pos, out Vector3 best)
    {
        best = pos;
        if (_boss == null || !_boss.TryGetTileGrid(out BossTileGrid grid))
            return false;
        float bestSq = float.MaxValue;
        for (int r = 0; r < BossTileGrid.Size; r++)
            for (int c = 0; c < BossTileGrid.Size; c++)
            {
                if (BossTileGrid.Has(mask, r, c))
                    continue;
                Vector3 p = grid.TileCenter(r, c);
                float sq = (p.x - pos.x) * (p.x - pos.x) + (p.z - pos.z) * (p.z - pos.z);
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = p;
                }
            }
        return bestSq < float.MaxValue;
    }

    /// <summary>방어(E)를 향할 출처 — 부채꼴·예고 없는 공격 = 보스, 점프·드론 = 원 중심.</summary>
    public Vector3 Source(QABoss103Threat t)
    {
        if (t.Kind == QABoss103ThreatKind.Jump || t.Kind == QABoss103ThreatKind.Drone)
            return t.Center;
        return _boss != null ? _boss.transform.position : t.Center;
    }
}
#endif
