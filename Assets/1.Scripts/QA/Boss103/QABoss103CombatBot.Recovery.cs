#if UNITY_EDITOR
using System.Reflection;
using UnityEngine;

/// <summary>
/// 할 일 119 — 전투 봇의 복구 행동(오너 인스턴스 — 호스트·가상 플레이어 각자 자기 플레이어).
/// <list type="bullet">
/// <item><b>F10 부활</b>: 내 플레이어가 Soul 이 되고 <see cref="TutorialDeathRulesModel.DefaultReviveDelaySeconds"/>(3초, 기존 QA 자동 부활과 같은 지연) 뒤
///   F10 디버그 부활과 <b>같은 경로</b> — <see cref="PlayerReviveController"/> 의 오너 RPC <c>RequestDebugReviveRpc</c>(키 입력 대신 리플렉션 호출, 포커스 없는 MPPM 클론 대비)
///   → 서버 <see cref="PlayerReviveController.TryCompleteReviveOnServer()"/>: 공용 목숨 1 차감, 0 이면 거부. 아직 Soul 이면 2초마다 다시 요청(성공할 때만 목숨이 준다).
///   공용 목숨 0 이면 요청하지 않는다(게임도 거부 → 전멸 판정). 부활 무적은 게임 F10 경로대로 주지 않는다. 캐릭터별 부활 수·쓴 목숨은 호스트 계측기가 센다(<c>qa_revives</c>).</item>
/// <item><b>벽 끼임 탈출</b>: 매 전투 틱 내 캡슐이 벽/환경(Wall·Env 레이어 — <c>GameplayInvariantDetector</c> P8 과 같은 검사, 스킨 0.03m)에 박혔거나
///   보스방 경계(<c>InvisibleBoundaries/Boundary_*</c> 안쪽 면) 밖이면 <see cref="QABoss103WallWatch"/> — 0.25초 이어지면 기다리지 않고 QA 서버 재배치
///   (<see cref="QABoss103Relocator.Request"/> 사유 Wall = 보스 둘레 유효 지점 무작위). 요청하면 2초 옆걸음·6초 대시·20초 재배치 끼임 단계를 처음으로 되돌리고,
///   끼임을 보는 중·탈출 직후 2초는 그 단계를 돌리지 않는다(같은 끼임에 두 복구가 겹치지 않게).</item>
/// </list>
/// 로그: <c>[QA103] event=qa_f10_revive_request</c> · <c>event=qa_wall_escape_request</c>. 결과는 호스트 <c>event=qa_revive path=f10</c> · <c>event=qa_relocate reason=wall</c>.
/// </summary>
public sealed partial class QABoss103CombatBot
{
    private static readonly MethodInfo DebugReviveRpc = typeof(PlayerReviveController).GetMethod("RequestDebugReviveRpc", AnyInst, null, System.Type.EmptyTypes, null);
    private static readonly LayerMask WallMask = LayerMask.GetMask("Wall", "Env");
    /// <summary>P8 과 같은 스킨 — 벽 스윕이 남기는 정상 접촉 간격은 끼임이 아니다.</summary>
    private const float WallSkin = 0.03f;
    /// <summary>경계 안쪽 면 밖으로 이만큼(m) 넘어야 '경계 밖'(면에 붙어 선 몸은 봐준다).</summary>
    private const float ArenaSlack = 0.1f;
    private const float ArenaRescanSeconds = 2f;

    private readonly QABoss103ReviveTimer _reviveTimer = new QABoss103ReviveTimer(TutorialDeathRulesModel.DefaultReviveDelaySeconds);
    private bool _reviveNoLivesLogged;

    private readonly QABoss103WallWatch _wall = new QABoss103WallWatch();
    private readonly Collider[] _wallHits = new Collider[8];
    private Transform _arenaRoot;
    private float _arenaMinX, _arenaMaxX, _arenaMinZ, _arenaMaxZ;
    private bool _arenaOk;
    private float _nextArenaScan;

    // ── F10 부활 ──────────────────────────────────────────────

    /// <summary>매 프레임(싸우지 않는 동안 포함). 보스 Combat 중 내 플레이어가 Soul 이면 지연 뒤 F10 경로로 부활 요청.</summary>
    private void TickRevive(Player me)
    {
        PlayerLifeCycleController life = me != null ? me.GetComponent<PlayerLifeCycleController>() : null;
        BossEncounterDirector d = BossEncounterDirector.Instance;
        bool soul = life != null && life.State == PlayerLifeState.Soul && d != null && d.IsSpawned && d.Phase == BossEncounterPhase.Combat;
        MultiGameRule rule = MultiGameRule.Current;
        int lives = rule != null && rule.IsSpawned ? rule.PartyLives : -1;
        if (!soul)
            _reviveNoLivesLogged = false;
        else if (lives == 0 && !_reviveNoLivesLogged)
        {
            _reviveNoLivesLogged = true;
            Debug.Log($"{Tag} event=qa_f10_revive_request character={_character} n={_reviveTimer.Requests} sent=false reason=no_lives");
        }
        if (!_reviveTimer.Tick(Time.realtimeSinceStartupAsDouble, soul, lives))
            return;

        PlayerReviveController revive = me.GetComponent<PlayerReviveController>();
        string skip = revive == null ? "no_revive_controller"
            : DebugReviveRpc == null ? "rpc_missing"
            : !revive.IsSpawned || !revive.IsOwner ? "not_owner"
            : null;
        if (skip == null)
        {
            try { DebugReviveRpc.Invoke(revive, null); }
            catch (System.Exception e) { skip = "invoke_" + e.GetType().Name; }
        }
        Debug.Log($"{Tag} event=qa_f10_revive_request character={_character} n={_reviveTimer.Requests} sent={(skip == null ? "true" : "false")} " +
                  $"livesSeen={(lives >= 0 ? lives.ToString() : "na")} delayS={_reviveTimer.DelaySeconds:0.#}" + (skip != null ? $" reason={skip}" : ""));
    }

    // ── 벽 끼임 탈출 ──────────────────────────────────────────

    /// <summary>
    /// 전투 틱마다. 벽 속·경계 밖이 이어지면 재배치 요청(그 틱 이동은 하지 않는다). 기존 끼임 단계와 겹치지 않게 <see cref="WallBusy"/> 로 막는다.
    /// 요청했으면 true.
    /// </summary>
    private bool TickWall(Player me, Vector3 myPos, double now)
    {
        bool stuck = InWall(me, myPos, out string what);
        if (!_wall.Tick(now, stuck))
            return false;
        bool sent = QABoss103Relocator.Request(QABoss103RelocateReason.Wall);
        _stuck.Reset();
        _escalation.Reset();
        _input.SetMove(Vector3.zero);
        Debug.Log($"{Tag} event=qa_wall_escape_request character={_character} n={_wall.Escapes} sent={(sent ? "true" : "false")} what={QABoss103Plan.Token(what)} pos={Fmt(myPos)}");
        return true;
    }

    /// <summary>벽 끼임을 보는 중이거나 탈출 직후 — 2초 옆걸음·6초 대시·20초 재배치를 돌리지 않는다.</summary>
    private bool WallBusy(double now) => _wall.Busy(now);

    private bool InWall(Player me, Vector3 myPos, out string what)
    {
        what = null;
        CapsuleCollider capsule = me.GetComponent<CapsuleCollider>();
        if (capsule != null && capsule.enabled)
        {
            Transform t = capsule.transform;
            Vector3 lossy = t.lossyScale;
            float fullRadius = capsule.radius * Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.z));
            float radius = Mathf.Max(0.01f, fullRadius - WallSkin);
            float height = Mathf.Max(capsule.height * Mathf.Abs(lossy.y), fullRadius * 2f);
            float half = Mathf.Max(0f, height * 0.5f - fullRadius);
            Vector3 center = t.TransformPoint(capsule.center);
            int n = Physics.OverlapCapsuleNonAlloc(center + t.up * half, center - t.up * half, radius, _wallHits, WallMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = _wallHits[i];
                if (c == null || c.transform == t || c.transform.IsChildOf(me.transform))
                    continue;
                what = "wall_" + c.name;
                return true;
            }
        }
        if (ResolveArena())
        {
            Vector3 local = _arenaRoot.InverseTransformPoint(myPos);
            if (QABoss103WallWatch.OutsideArena(local.x, local.z, _arenaMinX, _arenaMaxX, _arenaMinZ, _arenaMaxZ, ArenaSlack))
            {
                what = "outside_arena";
                return true;
            }
        }
        return false;
    }

    /// <summary>보스방 경계(방 로컬 안쪽 면) — 23호 <c>ResolveArena</c> 와 같은 원본(<see cref="BossRoomRejoin.TryReadArenaLocal"/>). 못 찾으면 주기적으로 다시 찾는다.</summary>
    private bool ResolveArena()
    {
        if (_arenaOk && _arenaRoot != null)
            return true;
        if (Time.realtimeSinceStartup < _nextArenaScan)
            return false;
        _nextArenaScan = Time.realtimeSinceStartup + ArenaRescanSeconds;
        GameObject root = GameObject.Find(BossRoomRejoin.BoundariesName);
        _arenaRoot = root != null ? root.transform : null;
        _arenaOk = _arenaRoot != null && BossRoomRejoin.TryReadArenaLocal(_arenaRoot, out _arenaMinX, out _arenaMaxX, out _arenaMinZ, out _arenaMaxZ, out _);
        return _arenaOk;
    }

    private string RecoveryDiag() => $"wallEscapes={_wall.Escapes} f10Requests={_reviveTimer.Requests} arena={(_arenaOk ? "ok" : "none")}";
}
#endif
