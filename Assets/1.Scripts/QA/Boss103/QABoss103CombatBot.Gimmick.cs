#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 할 일 103 C3b — 전투 봇의 기믹 처리(송전탑·드론 유도·자폭 실드). 공격 대상을 고르기만 하고, 이동·평타·스킬·조준 오차·스킬 빈도는 C2(<c>QABoss103CombatBot.cs</c>) 그대로,
/// 위험 회피·간파(C3, <c>QABoss103CombatBot.Evasion.cs</c>)는 탑 공격 중에도 먼저 돈다. 기믹 처리 '의지'는 단계와 무관하게 항상 시도한다.
/// <list type="bullet">
/// <item><b>송전탑</b>: 후보 = Live(피격 가능) 또는 상승 중인 <see cref="BossChargingPylon"/>. 클라는 탑 상태(서버 전용)를 모르므로 Live = 몸 콜라이더 켜짐(아웃라인 복제값을 따른다),
///   상승 = 숨김 높이보다 위에서 올라가는 중으로 본다(호스트는 <see cref="BossChargingPylon.State"/>). 배정은 <see cref="QABoss103PylonAssigner"/> — 봇마다 파티 전원 배정표를 같은 규칙으로 굴린다.
///   배정된 탑으로 거리 띠(근접 1~2m·거너 6~9m, 탑 몸 표면까지)를 맞추고, Live 면 평타·Q 를 탑에 쓴다. 상승 중(무적)이면 띠 안에서 대기.
///   R 은 대상 지정(팔라딘·거너)이면 보스 전용, 어쌔신 변신은 탑에도 쓴다(<see cref="QABoss103Gimmick.AllowR"/>). 탑이 없으면 보스 공격으로 돌아간다.</item>
/// <item><b>드론 유도</b>: 웰즈 드론 표식(추적) 대상이 나이고 Live 탑이 있으면, 반응 지연 뒤 탑 옆 유도 자리(<see cref="QABoss103Gimmick.GuideSpot"/>)로 가서 선다.
///   고정 순간(Lock) 판정은 그 자리 발 위치라 착탄 원에 탑이 들어간다. 고정 원이 떠 있는 동안 그 자리에 머물고, 피하기/맞기는 C3 회피 굴림 그대로(드론 위험 = 나를 노림).
///   호스트는 <see cref="TwentyThreeBoss.IsRaidDroneGuideActive"/> 도 로그에 남긴다(클라는 서버 전용 값이라 na).</item>
/// <item><b>자폭 실드</b>: <see cref="TwentyThreeBoss.SelfDestructShield01"/> &gt; 0 이면 전원 보스 집중(탑 배정보다 우선).</item>
/// </list>
/// 로그: <c>[QA103] event=gimmick kind=pylon|drone_guide|shield state=assign|attack|done|lost bot=… target=… serverT=…</c> — C4 기믹 집계 보조(정식 성공/실패는 C5 게임 알림).
/// </summary>
public sealed partial class QABoss103CombatBot
{
    private static readonly FieldInfo PylonHiddenField = typeof(BossChargingPylon).GetField("_hiddenLocal", AnyInst);

    /// <summary>탑·파티 목록을 다시 찾는 간격(초). 상태는 매 프레임 읽는다.</summary>
    private const float GimmickScanSeconds = 1f;
    /// <summary>탑 몸 반경을 아직 못 봤을 때(m).</summary>
    private const float PylonExtentFallback = 0.6f;
    /// <summary>숨김 높이보다 이만큼 위면 올라온 것으로 본다(m).</summary>
    private const float PylonRiseEpsilon = 0.02f;

    private struct AttackTarget
    {
        public Vector3 Pos;
        /// <summary>대상 몸 표면까지 수평 거리(m).</summary>
        public float Surface;
        public bool Hittable;
        public bool IsBoss;
        /// <summary>거리 띠 대신 이 점으로 간다(드론 유도).</summary>
        public bool HasSpot;
        public Vector3 Spot;
        public string Token;
    }

    private sealed class PylonView
    {
        public BossChargingPylon Pylon;
        public Collider Body;
        public ulong Id;
        public float Extent = PylonExtentFallback;
        public float HiddenY;
        public float LastY;
        public bool Rising;
        public bool Live;
    }

    private readonly List<PylonView> _pylonViews = new List<PylonView>();
    private readonly List<QABoss103PylonInfo> _pylonInfos = new List<QABoss103PylonInfo>();
    private readonly List<Player> _party = new List<Player>();
    private readonly List<ulong> _partyIds = new List<ulong>();
    private readonly List<QABoss103BotSeat> _seats = new List<QABoss103BotSeat>();
    private readonly QABoss103PylonAssigner _assigner = new QABoss103PylonAssigner();
    private float _nextGimmickScan;

    private QABoss103GimmickKind _gimmick = QABoss103GimmickKind.None;

    // 송전탑(내 배정)
    private bool _hasPylon;
    private ulong _pylonId;
    private bool _pylonWasLive, _pylonAttackLogged;

    // 드론 유도: 0 없음 · 1 표식 봄(반응 대기) · 2 유도 중 · 3 고정 원 동안 자리 지킴
    private int _guideStep;
    private double _guideActAt;
    private ulong _guidePylon;
    private bool _guideInSpotLogged;
    private Vector3 _guideHold;

    // 자폭 실드
    private bool _shieldOn, _shieldAttackLogged;

    private int _pylonAssigns, _pylonDone, _pylonLost, _guides, _guideReach, _guideMiss, _guideLost, _shields;

    /// <summary>C3b — 이번 프레임 공격 대상(기본 = 보스). 위험 회피 뒤, C2 이동·공격 전에 부른다.</summary>
    private AttackTarget TickGimmick(Player me, Vector3 myPos, Vector3 bossPos, float bossSurface, bool bossHittable, double now)
    {
        if (Time.realtimeSinceStartup >= _nextGimmickScan)
        {
            _nextGimmickScan = Time.realtimeSinceStartup + GimmickScanSeconds;
            ScanPylons();
            ScanParty();
        }
        UpdatePylons();
        UpdateSeats(me, myPos);
        _assigner.Tick(_pylonInfos, _seats);
        TrackPylonAssignment(myPos);

        bool anyLive = false;
        foreach (PylonView v in _pylonViews)
            anyLive |= v.Live;

        TrackShield();
        TickGuide(myPos, anyLive, now);

        bool guiding = _guideStep >= 2;
        _gimmick = QABoss103Gimmick.Decide(_shieldOn, guiding, anyLive || _guideStep == 3, _hasPylon);

        var boss = new AttackTarget { Pos = bossPos, Surface = bossSurface, Hittable = bossHittable, IsBoss = true, Token = "boss" };
        switch (_gimmick)
        {
            case QABoss103GimmickKind.Pylon:
                return PylonTarget(Find(_pylonId), myPos) ?? boss;
            case QABoss103GimmickKind.DroneGuide:
                AttackTarget? g = PylonTarget(Find(_guidePylon), myPos);
                if (g == null)
                    return boss;
                AttackTarget t = g.Value;
                t.HasSpot = true;
                t.Spot = _guideStep == 3 ? _guideHold : GuideSpotFor(Find(_guidePylon), myPos);
                return t;
            default:
                return boss;
        }
    }

    // ── 송전탑 관측 ─────────────────────────────────────────────

    private void ScanPylons()
    {
        _pylonViews.RemoveAll(v => v.Pylon == null || !v.Pylon.IsSpawned);
        foreach (BossChargingPylon p in FindObjectsByType<BossChargingPylon>(FindObjectsSortMode.None))
        {
            if (p == null || !p.IsSpawned || _pylonViews.Exists(v => v.Pylon == p))
                continue;
            float y = p.transform.localPosition.y;
            float hidden = PylonHiddenField != null && PylonHiddenField.GetValue(p) is Vector3 h ? h.y : y;
            _pylonViews.Add(new PylonView { Pylon = p, Body = p.GetComponent<Collider>(), Id = p.NetworkObjectId, HiddenY = hidden, LastY = y });
        }
        _pylonViews.Sort((a, b) => a.Id.CompareTo(b.Id));
    }

    private void UpdatePylons()
    {
        _pylonInfos.Clear();
        for (int i = _pylonViews.Count - 1; i >= 0; i--)
            if (_pylonViews[i].Pylon == null || !_pylonViews[i].Pylon.IsSpawned)
                _pylonViews.RemoveAt(i);

        foreach (PylonView v in _pylonViews)
        {
            BossChargingPylon p = v.Pylon;
            float y = p.transform.localPosition.y;
            if (p.IsServer)
            {
                v.Live = p.IsLive;
                v.Rising = p.State == BossChargingPylon.PylonState.Rising;
            }
            else
            {
                // 클라: Live = 몸 콜라이더(아웃라인 복제값을 따른다), 상승 = 숨김보다 위에서 올라가는 중(끈끈하게 — 보간 정지 프레임에 떨지 않게).
                v.Live = v.Body != null && v.Body.enabled;
                float dy = y - v.LastY;
                if (v.Live || y <= v.HiddenY + PylonRiseEpsilon || dy < -1e-4f)
                    v.Rising = false;
                else if (dy > 1e-4f)
                    v.Rising = true;
            }
            v.LastY = y;
            if (v.Live && v.Body != null && v.Body.enabled)
            {
                Bounds b = v.Body.bounds;
                v.Extent = Mathf.Max(b.extents.x, b.extents.z);
            }
            Vector3 pos = p.transform.position;
            _pylonInfos.Add(new QABoss103PylonInfo(v.Id, pos.x, pos.z, v.Rising, v.Live));
        }
    }

    private PylonView Find(ulong id)
    {
        foreach (PylonView v in _pylonViews)
            if (v.Id == id)
                return v;
        return null;
    }

    private static float PylonSurface(PylonView v, Vector3 myPos)
    {
        if (v.Body != null && v.Body.enabled)
            return InterruptOmniRangeModel.HorizontalDistanceToCollider(myPos, v.Body);
        return Mathf.Max(0f, QAPartyPlacement88.HorizontalDistance(myPos, v.Pylon.transform.position) - v.Extent);
    }

    private AttackTarget? PylonTarget(PylonView v, Vector3 myPos)
    {
        if (v == null)
            return null;
        return new AttackTarget
        {
            Pos = v.Pylon.transform.position,
            Surface = PylonSurface(v, myPos),
            Hittable = v.Live,
            IsBoss = false,
            Token = QABoss103Gimmick.PylonToken(v.Id),
        };
    }

    // ── 파티 자리 ───────────────────────────────────────────────

    private void ScanParty()
    {
        _party.Clear();
        _partyIds.Clear();
        foreach (Player p in FindObjectsByType<Player>(FindObjectsSortMode.None))
        {
            if (p == null || !p.IsSpawned)
                continue;
            _party.Add(p);
            _partyIds.Add(p.OwnerClientId);
        }
    }

    private void UpdateSeats(Player me, Vector3 myPos)
    {
        _seats.Clear();
        bool sawMe = false;
        foreach (Player p in _party)
        {
            if (p == null)
                continue;
            int slot = p == me ? _slot : QABoss103Plan.SlotOf(p.OwnerClientId, _partyIds);
            if (slot < 0)
                continue;
            sawMe |= p == me;
            Vector3 pos = p == me ? myPos : p.transform.position;
            _seats.Add(new QABoss103BotSeat(slot, pos.x, pos.z, BossPatternTargets.IsAlive(p)));
        }
        if (!sawMe)
            _seats.Add(new QABoss103BotSeat(_slot, myPos.x, myPos.z, true));   // 목록 갱신 전(첫 1초)
    }

    // ── 송전탑 배정 로그 ────────────────────────────────────────

    private void TrackPylonAssignment(Vector3 myPos)
    {
        bool has = _assigner.TryGet(_slot, out ulong id);
        if (_hasPylon && (!has || id != _pylonId))
        {
            // 배정은 탑이 내려가야(부서짐·차징 끝) 풀린다 — 내가 빠진 경우는 FlushGimmick 이 lost 로 남긴다.
            _pylonDone++;
            LogGimmick(QABoss103GimmickKind.Pylon, "done", QABoss103Gimmick.PylonToken(_pylonId),
                       $"wasLive={B(_pylonWasLive)} attacked={B(_pylonAttackLogged)} pylonsLeft={EngagedCount()}");
            _hasPylon = false;
        }
        if (has && !_hasPylon)
        {
            _hasPylon = true;
            _pylonId = id;
            _pylonWasLive = false;
            _pylonAttackLogged = false;
            _pylonAssigns++;
            PylonView v = Find(id);
            LogGimmick(QABoss103GimmickKind.Pylon, "assign", QABoss103Gimmick.PylonToken(id),
                       $"distM={F2(v != null ? PylonSurface(v, myPos) : double.NaN)} live={B(v != null && v.Live)} pylons={EngagedCount()} liveCount={LiveCount()} seats={_seats.Count}");
        }
        if (_hasPylon)
        {
            PylonView v = Find(_pylonId);
            _pylonWasLive |= v != null && v.Live;
        }
    }

    private int EngagedCount()
    {
        int n = 0;
        foreach (QABoss103PylonInfo p in _pylonInfos)
            if (p.Engaged)
                n++;
        return n;
    }

    private int LiveCount()
    {
        int n = 0;
        foreach (QABoss103PylonInfo p in _pylonInfos)
            if (p.Live)
                n++;
        return n;
    }

    // ── 드론 유도 ───────────────────────────────────────────────

    private void TickGuide(Vector3 myPos, bool anyLive, double now)
    {
        bool marking = _sensor.DroneMarkingMe;
        bool locked = _sensor.DroneLocked;

        if (_guideStep == 0)
        {
            if (!marking || !anyLive)
                return;
            _guideStep = 1;
            _guideActAt = now + _profile.ReactionMs / 1000.0;   // 반응 지연 — 표식을 보고 움직이기 시작할 때까지
            return;
        }

        if (_guideStep == 1 || _guideStep == 2)
        {
            if (locked)
            {
                OnGuideLock(myPos);
                return;
            }
            if (!marking)
            {
                EndGuide(_guideStep == 2 ? "lost" : null, "reason=mark_cancel");
                return;
            }
            if (_guideStep == 1 && now < _guideActAt)
                return;

            // 유도 탑: 내 배정이 Live 면 그것, 아니면 가장 가까운 Live 탑. 유도 중 탑이 내려가면 다시 고른다.
            PylonView cur = _guideStep == 2 ? Find(_guidePylon) : null;
            if (cur == null || !cur.Live)
            {
                PylonView pick = _hasPylon && Find(_pylonId) is PylonView mine && mine.Live ? mine : NearestLive(myPos);
                if (pick == null)
                {
                    EndGuide(_guideStep == 2 ? "lost" : null, "reason=no_live_pylon");
                    return;
                }
                bool first = _guideStep == 1;
                _guideStep = 2;
                _guidePylon = pick.Id;
                _guideInSpotLogged = false;
                if (first)
                    _guides++;
                LogGimmick(QABoss103GimmickKind.DroneGuide, "assign", QABoss103Gimmick.PylonToken(pick.Id),
                           $"distM={F2(PylonSurface(pick, myPos))} repick={B(!first)} raidGuide={RaidGuideFlag()} reactMs={F0(_profile.ReactionMs)}");
            }

            PylonView gp = Find(_guidePylon);
            if (!_guideInSpotLogged && gp != null)
            {
                Vector3 spot = GuideSpotFor(gp, myPos);
                if (QAPartyPlacement88.HorizontalDistance(myPos, spot) <= QABoss103Gimmick.GuideTolerance)
                {
                    _guideInSpotLogged = true;
                    LogGimmick(QABoss103GimmickKind.DroneGuide, "attack", QABoss103Gimmick.PylonToken(gp.Id), $"note=in_spot pylonDistM={F2(PylonSurface(gp, myPos))}");
                }
            }
            return;
        }

        // 3 — 고정 원 동안 자리 지킴(피하기는 C3 회피가 한다). 원이 사라지면 끝.
        if (!locked)
            EndGuide(null, null);
    }

    private void OnGuideLock(Vector3 myPos)
    {
        if (_guideStep != 2)
        {
            EndGuide(null, null);   // 반응 지연 안에 고정됐다 — 유도 시작 전
            return;
        }
        QABoss103Threat lockT = _sensor.DroneThreat;
        PylonView gp = Find(_guidePylon);
        bool reach = false;
        string detail = "lock=na";
        if (lockT != null && gp != null)
        {
            Vector3 pp = gp.Pylon.transform.position;
            reach = QABoss103Gimmick.InDroneReach(lockT.Center.x, lockT.Center.z, lockT.Radius, pp.x, pp.z);
            detail = $"lockToPylonM={F2(QAPartyPlacement88.HorizontalDistance(lockT.Center, pp))} radiusM={F2(lockT.Radius)} live={B(gp.Live)} droneId={lockT.Id}";
        }
        if (reach) _guideReach++;
        else _guideMiss++;
        LogGimmick(QABoss103GimmickKind.DroneGuide, "done", QABoss103Gimmick.PylonToken(_guidePylon),
                   $"inReach={B(reach)} inSpot={B(_guideInSpotLogged)} {detail} raidGuide={RaidGuideFlag()}");
        _guideStep = 3;
        _guideHold = myPos;
    }

    private void EndGuide(string state, string extra)
    {
        if (state != null)
        {
            _guideLost++;
            LogGimmick(QABoss103GimmickKind.DroneGuide, state, QABoss103Gimmick.PylonToken(_guidePylon), extra ?? "");
        }
        _guideStep = 0;
        _guideInSpotLogged = false;
    }

    private PylonView NearestLive(Vector3 myPos)
    {
        PylonView best = null;
        float bestD = float.MaxValue;
        foreach (PylonView v in _pylonViews)
        {
            if (!v.Live)
                continue;
            float d = QAPartyPlacement88.HorizontalDistance(myPos, v.Pylon.transform.position);
            if (d < bestD - 1e-4f)
            {
                bestD = d;
                best = v;
            }
        }
        return best;
    }

    private static Vector3 GuideSpotFor(PylonView v, Vector3 myPos)
    {
        if (v == null)
            return myPos;
        Vector3 p = v.Pylon.transform.position;
        QABoss103Gimmick.GuideSpot(p.x, p.z, v.Extent, myPos.x, myPos.z, out float x, out float z);
        return new Vector3(x, myPos.y, z);
    }

    private string RaidGuideFlag() => _boss != null && _boss.IsServer ? B(_boss.IsRaidDroneGuideActive) : "na";

    // ── 자폭 실드 ───────────────────────────────────────────────

    private void TrackShield()
    {
        float shield = _boss.SelfDestructShield01;
        bool on = shield > 0f;
        if (on && !_shieldOn)
        {
            _shields++;
            _shieldAttackLogged = false;
            LogGimmick(QABoss103GimmickKind.Shield, "assign", "boss", $"shield={F2(shield)} bossHp={_boss.CurrentHealth}");
        }
        else if (!on && _shieldOn)
        {
            LogGimmick(QABoss103GimmickKind.Shield, "done", "boss", $"attacked={B(_shieldAttackLogged)} bossHp={_boss.CurrentHealth}");
        }
        _shieldOn = on;
    }

    // ── 공통 ───────────────────────────────────────────────────

    /// <summary>평타 시작·스킬 시전 성공 — 기믹 대상 첫 공격을 1줄 남긴다.</summary>
    private void NoteAttack(in AttackTarget tgt, string how)
    {
        if (!tgt.IsBoss && _hasPylon && tgt.Token == QABoss103Gimmick.PylonToken(_pylonId) && !_pylonAttackLogged)
        {
            _pylonAttackLogged = true;
            LogGimmick(QABoss103GimmickKind.Pylon, "attack", tgt.Token, $"how={how} surfM={F2(tgt.Surface)} mode={QABoss103Gimmick.Token(_gimmick)}");
        }
        else if (tgt.IsBoss && _shieldOn && !_shieldAttackLogged)
        {
            _shieldAttackLogged = true;
            LogGimmick(QABoss103GimmickKind.Shield, "attack", "boss", $"how={how} shield={F2(_boss.SelfDestructShield01)}");
        }
    }

    /// <summary>R 을 지금 대상에 써도 되나 — 대상 지정 R 은 보스 전용.</summary>
    private static bool AllowRFor(PlayerSkillController skills, in AttackTarget tgt)
    {
        PlayerSkillBase r = skills != null ? skills.GetSkill(PlayerSkillSlot.Ultimate) : null;
        return r != null && QABoss103Gimmick.AllowR(tgt.IsBoss, r.Data.TargetingMode == SkillTargetingMode.SingleTarget);
    }

    /// <summary>싸우지 않게 됐다 — 진행 중 기믹을 lost 로 남기고 내 쪽 상태를 비운다(배정표는 유지 — 내가 빠지면 다음 틱에 풀린다).</summary>
    private void FlushGimmick(string reason)
    {
        string why = "reason=idle_" + QABoss103Plan.Token(reason);
        if (_hasPylon)
        {
            _pylonLost++;
            LogGimmick(QABoss103GimmickKind.Pylon, "lost", QABoss103Gimmick.PylonToken(_pylonId), $"{why} wasLive={B(_pylonWasLive)} attacked={B(_pylonAttackLogged)}");
            _hasPylon = false;
        }
        if (_guideStep == 2)
            EndGuide("lost", why);
        else
            EndGuide(null, null);
        if (_shieldOn)
        {
            LogGimmick(QABoss103GimmickKind.Shield, "lost", "boss", why);
            _shieldOn = false;
        }
        _gimmick = QABoss103GimmickKind.None;
    }

    private void LogGimmick(QABoss103GimmickKind kind, string state, string target, string extra)
    {
        NetworkManager nm = NetworkManager.Singleton;
        double serverT = nm != null && nm.IsListening ? nm.ServerTime.Time : -1.0;
        Debug.Log($"{Tag} event=gimmick kind={QABoss103Gimmick.Token(kind)} state={state} bot={_character} slot={_slot} tier={_profile.Tier} target={target} " +
                  $"serverT={F3(serverT)} {extra}".TrimEnd());
    }

    private string GimmickDiag() =>
        $"gimmick={QABoss103Gimmick.Token(_gimmick)} pylonAssigns={_pylonAssigns} pylonDone={_pylonDone} pylonLost={_pylonLost} guides={_guides} " +
        $"guideReach={_guideReach} guideMiss={_guideMiss} guideLost={_guideLost} shields={_shields}";

    private static string B(bool v) => v ? "true" : "false";
}
#endif
