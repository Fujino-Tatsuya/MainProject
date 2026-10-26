#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 할 일 103 C2 — 봇이 마우스·키보드 없이 넣는 입력(게임 코드 무수정, QA 전용 리플렉션). 포커스 없는 MPPM 클론은 가상 키보드·마우스
/// 상태 이벤트가 막힐 수 있어(기본 InputSettings — 게임 뷰 포커스 따름) 입력 장치 대신 각 컨트롤러가 입력에서 읽는 값을 직접 넣는다.
/// <list type="bullet">
/// <item>조준: <see cref="PlayerAimIndicator"/> 를 끄고(마우스로 덮어쓰지 않게) <c>AimDirection·AimGroundPoint</c> 를 쓴다 — 평타·스킬 방향이 전부 여기서 나온다.
///   끝나면 다시 켠다.</item>
/// <item>평타 '누르고 있음': 시작은 공용 <see cref="IPlayerBasicAttack.TryStart"/>. 이어치기는 각 캐릭터가 누름을 보고 보내는 것과 같은 RPC 를 보낸다 —
///   팔라딘 = 매 프레임 다음 타 예약(<c>RequestQueueNextAttackRpc</c>), 어쌔신 = 오너 창 안에서 1회 예약(같은 RPC + <c>ownerQueued</c>),
///   거너 = 오너 이어쏘기 래치(<c>ownerQueued</c>) + 홀드 신호(<c>RequestQueueShotRpc</c>), 놓을 때 <c>ReleaseRpc</c>.</item>
/// <item>홀드 스킬(팔라딘 Q): <see cref="PlayerInputReader"/> 의 HUD 가상 홀드 배열(<c>virtualSkillHeld</c>)만 켠다 — 누름 엣지는 만들지 않는다.</item>
/// <item>거너 Q 발사: 스킬 중 좌클릭과 같은 <c>NotifySkillPrimaryRpc</c>.</item>
/// </list>
/// 못 찾은 멤버는 <see cref="Missing"/> 에 남고 그 기능만 빠진다(평타는 첫 타 반복, 거너 Q 는 게임 자동 발사).
/// </summary>
public sealed class QABoss103InputDriver
{
    private const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static readonly FieldInfo AimDirField = typeof(PlayerAimIndicator).GetField("<AimDirection>k__BackingField", Inst);
    private static readonly FieldInfo AimPointField = typeof(PlayerAimIndicator).GetField("<AimGroundPoint>k__BackingField", Inst);
    private static readonly FieldInfo HasAimPointField = typeof(PlayerAimIndicator).GetField("<HasAimGroundPoint>k__BackingField", Inst);
    private static readonly FieldInfo VirtualHeldField = typeof(PlayerInputReader).GetField("virtualSkillHeld", Inst);

    private static readonly MethodInfo PaladinQueueRpc = typeof(DefaultAttackController).GetMethod("RequestQueueNextAttackRpc", Inst);

    private static readonly FieldInfo AssassinActive = typeof(AssassinBasicAttack).GetField("active", Inst);
    private static readonly FieldInfo AssassinOwnerQueued = typeof(AssassinBasicAttack).GetField("ownerQueued", Inst);
    private static readonly MethodInfo AssassinInWindow = typeof(AssassinBasicAttack).GetMethod("IsInCurrentComboWindow", Inst);
    private static readonly MethodInfo AssassinQueueRpc = typeof(AssassinBasicAttack).GetMethod("RequestQueueNextAttackRpc", Inst);

    private static readonly FieldInfo GunnerActive = typeof(GunnerBasicAttack).GetField("active", Inst);
    private static readonly FieldInfo GunnerOwnerQueued = typeof(GunnerBasicAttack).GetField("ownerQueued", Inst);
    private static readonly MethodInfo GunnerQueueRpc = typeof(GunnerBasicAttack).GetMethod("RequestQueueShotRpc", Inst);
    private static readonly MethodInfo GunnerReleaseRpc = typeof(GunnerBasicAttack).GetMethod("ReleaseRpc", Inst);

    private static readonly MethodInfo SkillPrimaryRpc = typeof(PlayerSkillController).GetMethod("NotifySkillPrimaryRpc", Inst);

    /// <summary>거너 홀드 신호 간격(초) — 게임 홀드 신호(발사 간격 × 0.5)보다 촘촘하게.</summary>
    private const float GunnerHoldSignalSeconds = 0.1f;

    private Player _player;
    private PlayerAimIndicator _aim;
    private PlayerInputReader _reader;
    private PlayerSkillController _skills;
    private IPlayerBasicAttack _basic;
    private DefaultAttackController _paladin;
    private AssassinBasicAttack _assassin;
    private GunnerBasicAttack _gunner;
    private bool _aimWasEnabled;
    private bool _gunnerHolding;
    private float _nextGunnerSignal;
    private readonly bool[] _heldByBot = new bool[4];

    /// <summary>못 찾은 리플렉션 멤버(로그용, 비면 "none").</summary>
    public string Missing { get; }

    public QABoss103InputDriver()
    {
        var miss = new List<string>();
        if (AimDirField == null || AimPointField == null || HasAimPointField == null) miss.Add("aim");
        if (VirtualHeldField == null) miss.Add("virtualSkillHeld");
        if (PaladinQueueRpc == null) miss.Add("paladinQueue");
        if (AssassinActive == null || AssassinOwnerQueued == null || AssassinInWindow == null || AssassinQueueRpc == null) miss.Add("assassinQueue");
        if (GunnerActive == null || GunnerOwnerQueued == null || GunnerQueueRpc == null || GunnerReleaseRpc == null) miss.Add("gunnerHold");
        if (SkillPrimaryRpc == null) miss.Add("skillPrimary");
        Missing = miss.Count > 0 ? string.Join("+", miss) : "none";
    }

    public Player Player => _player;

    /// <summary>대상 플레이어를 바꾼다(스폰·교체). 이전 대상은 원복한다.</summary>
    public void Bind(Player player)
    {
        if (player == _player)
            return;
        Release();
        _player = player;
        if (player == null)
            return;
        _aim = player.GetComponent<PlayerAimIndicator>();
        _reader = player.GetComponent<PlayerInputReader>();
        _skills = player.GetComponent<PlayerSkillController>();
        _basic = player.GetComponent<IPlayerBasicAttack>();
        _paladin = _basic as DefaultAttackController;
        _assassin = _basic as AssassinBasicAttack;
        _gunner = _basic as GunnerBasicAttack;
        if (_aim != null)
        {
            _aimWasEnabled = _aim.enabled;
            if (AimDirField != null)
                _aim.enabled = false;
        }
    }

    /// <summary>원복 — 조준 표시를 다시 켜고, 봇이 켠 가상 홀드·거너 홀드를 놓는다.</summary>
    public void Release()
    {
        if (_player == null)
            return;
        for (int i = 0; i < _heldByBot.Length; i++)
            if (_heldByBot[i])
                SetSkillHeld((PlayerSkillSlot)i, false);
        ReleaseBasic();
        if (_aim != null)
            _aim.enabled = _aimWasEnabled;
        _player = null;
        _aim = null;
        _reader = null;
        _skills = null;
        _basic = null;
        _paladin = null;
        _assassin = null;
        _gunner = null;
    }

    // ── 조준 ───────────────────────────────────────────────────

    /// <summary>조준 지점(월드)을 넣는다 — 방향 = 지점 − 나(수평).</summary>
    public void SetAim(Vector3 point)
    {
        if (_aim == null || AimDirField == null)
            return;
        Vector3 dir = point - _player.transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude >= 0.001f)
            AimDirField.SetValue(_aim, dir.normalized);
        AimPointField.SetValue(_aim, point);
        HasAimPointField.SetValue(_aim, true);
    }

    public Vector3 AimDirection => _aim != null ? _aim.AimDirection : Vector3.zero;

    // ── 평타 ───────────────────────────────────────────────────

    /// <summary>평타가 진행 중인가(오너가 본 상태).</summary>
    public bool IsBasicActive
    {
        get
        {
            if (_assassin != null && AssassinActive != null)
                return (bool)AssassinActive.GetValue(_assassin);
            if (_gunner != null && GunnerActive != null)
                return (bool)GunnerActive.GetValue(_gunner);
            return _player != null && (_player.CurrentState == PlayerActionState.Attack);
        }
    }

    /// <summary>
    /// 평타 '누르고 있음' 한 틱. 진행 중이 아니면 시작을 시도(시작 요청이 나갔으면 true), 진행 중이면 캐릭터별 이어치기 신호를 보낸다.
    /// </summary>
    public bool HoldBasic()
    {
        if (_basic == null || _player == null)
            return false;
        if (!IsBasicActive)
        {
            bool started = _basic.TryStart();
            if (started && _gunner != null)
            {
                _gunnerHolding = true;
                _nextGunnerSignal = 0f;
            }
            return started;
        }

        if (_paladin != null && PaladinQueueRpc != null)
        {
            if (_paladin.IsAttacking)
                Invoke(PaladinQueueRpc, _paladin, AimDirection, _player.CreateStatReport(), default(RpcParams));
        }
        else if (_assassin != null && AssassinQueueRpc != null)
        {
            if (!(bool)AssassinOwnerQueued.GetValue(_assassin) && (bool)AssassinInWindow.Invoke(_assassin, null))
            {
                AssassinOwnerQueued.SetValue(_assassin, true);
                Invoke(AssassinQueueRpc, _assassin, AimDirection, Vector3.zero, _player.CreateStatReport(), default(RpcParams));
            }
        }
        else if (_gunner != null && GunnerQueueRpc != null)
        {
            _gunnerHolding = true;
            GunnerOwnerQueued.SetValue(_gunner, true);
            if (Time.time >= _nextGunnerSignal)
            {
                _nextGunnerSignal = Time.time + GunnerHoldSignalSeconds;
                Invoke(GunnerQueueRpc, _gunner, default(RpcParams));
            }
        }
        return false;
    }

    /// <summary>평타를 '뗀다' — 근접은 신호를 끊기만 하면 이번 타로 끝나고, 거너는 서버 홀드를 내린다.</summary>
    public void ReleaseBasic()
    {
        if (!_gunnerHolding)
            return;
        _gunnerHolding = false;
        if (_gunner != null && GunnerReleaseRpc != null && _gunner.IsSpawned)
        {
            GunnerOwnerQueued.SetValue(_gunner, false);
            Invoke(GunnerReleaseRpc, _gunner, default(RpcParams));
        }
    }

    // ── 스킬 ───────────────────────────────────────────────────

    /// <summary>HUD 가상 홀드 배열로 그 슬롯을 누르고 있음/뗌(누름 엣지 없음).</summary>
    public void SetSkillHeld(PlayerSkillSlot slot, bool held)
    {
        int i = (int)slot;
        _heldByBot[i] = held;
        if (_reader == null || VirtualHeldField == null || !(VirtualHeldField.GetValue(_reader) is bool[] arr) || i >= arr.Length)
            return;
        arr[i] = held;
    }

    /// <summary>스킬 중 좌클릭(거너 Q 발사). 보냈으면 true.</summary>
    public bool SkillPrimary()
    {
        if (_skills == null || SkillPrimaryRpc == null || !_skills.IsSpawned)
            return false;
        Invoke(SkillPrimaryRpc, _skills, AimDirection, default(RpcParams));
        return true;
    }

    private static readonly HashSet<string> LoggedErrors = new HashSet<string>();

    private static void Invoke(MethodInfo m, object target, params object[] args)
    {
        try
        {
            m.Invoke(target, args);
        }
        catch (System.Exception e)
        {
            // 매 프레임 부르는 경로라 메서드마다 한 번만 남긴다.
            if (LoggedErrors.Add(m.DeclaringType?.Name + "." + m.Name))
                Debug.LogWarning($"{QABoss103Plan.Tag} event=bot_input_error method={m.Name} error={QABoss103Plan.Token((e.InnerException ?? e).Message)}");
        }
    }
}
#endif
