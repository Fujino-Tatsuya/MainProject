using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 이펙트를 붙일 수 있는 특성 강화 스킬·추가 공격(할 일 145). 값은 네트워크로 byte 로 나간다 — ⚠️ 끝에만 추가한다.
/// 특성 ID 는 <see cref="TraitSkillFx.IdOf"/>. 목록·발화 위치 = Docs/tech/trait-effect-events.md.
/// </summary>
public enum TraitFxTrait : byte
{
    /// <summary>진격의 충격(PAL_4_Q_BLAST) — Q 끝 지점 추가 판정.</summary>
    PaladinAdvanceImpact = 0,
    /// <summary>돌파의 방패(PAL_2_DASH) — Q 가 고정 거리 돌진으로 바뀜.</summary>
    PaladinShieldDash = 1,
    /// <summary>수호의 파동(PAL_4_E_PUSH) — E 반격 순간 주변 원 밀치기.</summary>
    PaladinGuardWave = 2,
    /// <summary>광폭 레이저(GUN_1_WIDTH) — Q 판정 폭 증가.</summary>
    GunnerWideLaser = 3,
    /// <summary>잔류 포격(GUN_4_ECHO_Q) — 맞은 대상 몸에 붙는 에너지 폭탄.</summary>
    GunnerEchoBomb = 4,
    /// <summary>이중 출력(GUN_4_EXTRA_BASIC) — 기본 공격 N발마다 추가 레이저.</summary>
    GunnerDualOutput = 5,
    /// <summary>칼날의 영역(ASN_3_E_AREA) — 일반 E 강타 범위·변신 E 원 반경 증가.</summary>
    AssassinBladeDomain = 6,
    /// <summary>끝없는 칼날(ASN_4_COMBO_EXTRA) — 기본 공격 콤보 추가 베기.</summary>
    AssassinEndlessBlade = 7,
    /// <summary>추격의 궤적(ASN_4_Q_TRAIL) — Q 돌진이 지나온 선분 추가 판정.</summary>
    AssassinPursuitTrail = 8,
}

/// <summary>특성 강화 연출 사건 종류(할 일 145). ⚠️ 끝에만 추가한다(네트워크 byte).</summary>
public enum TraitFxKind : byte
{
    /// <summary>강화 스킬·추가 공격이 나갔다(판정 순간 또는 시작 순간 — 특성마다 다르다, 문서 표 참조).</summary>
    Activated = 0,
    /// <summary>그 추가 공격·강화 효과가 대상에 들어갔다(대상별 1번).</summary>
    Hit = 1,
    /// <summary>대상에 붙었다(잔류 포격 폭탄).</summary>
    Attached = 2,
    /// <summary>붙은 것이 터졌다(잔류 포격 폭탄 — 대상이 먼저 사라진 빈 폭발 포함).</summary>
    Exploded = 3,
}

/// <summary>
/// 특성 강화 연출 사건 1건(할 일 145) — 이펙트가 읽기만 하는 값. 판정·피해와 무관하다(서버 판정은 이걸 보지 않는다).
/// </summary>
public readonly struct TraitFxEvent
{
    public readonly TraitFxTrait Trait;
    public readonly TraitFxKind Kind;
    /// <summary>시전한 플레이어. 오너 화면만 거르려면 <c>Caster.IsInputSource</c>.</summary>
    public readonly Player Caster;
    /// <summary>연출 기준점(특성마다 — 레이저 발사 원점·돌진 시작점·적중 지점·폭탄 위치 등, 문서 표 참조).</summary>
    public readonly Vector3 Position;
    /// <summary>바닥 평면 방향(단위 벡터). 의미가 없으면 시전자 정면.</summary>
    public readonly Vector3 Direction;
    /// <summary>선형 공격의 끝점(레이저 끝·돌진/궤적 끝). 의미가 없으면 <see cref="Position"/>.</summary>
    public readonly Vector3 EndPosition;
    /// <summary>맞은·붙은 대상(없거나 이 피어에서 못 찾으면 null).</summary>
    public readonly Unit Target;
    /// <summary>판정 도형(할 일 153) — 서버 판정과 같은 도형. 없으면 null(도형이 아닌 판정·위치만 아는 사건).</summary>
    public readonly AttackShapeSet Shape;
    /// <summary>판정 기준 포즈 — <see cref="Shape"/> 가 있을 때만 의미가 있다(<c>AttackShapeDrawing</c> 등에 그대로 넘긴다).</summary>
    public readonly AttackShapePose Pose;
    /// <summary>특성별 보조 값(칼날의 영역 0 = 일반 E 강타 · 1 = 변신 E 원 등 — 문서 표 참조). 보통 0.</summary>
    public readonly int Variant;

    public TraitFxEvent(TraitFxTrait trait, TraitFxKind kind, Player caster, Vector3 position, Vector3 direction,
        Vector3 endPosition, Unit target = null, AttackShapeSet shape = null, AttackShapePose pose = default, int variant = 0)
    {
        Trait = trait;
        Kind = kind;
        Caster = caster;
        Position = position;
        Direction = direction;
        EndPosition = endPosition;
        Target = target;
        Shape = shape;
        Pose = pose;
        Variant = variant;
    }

    /// <summary>특성 영구 ID(PAL_4_Q_BLAST 등).</summary>
    public string TraitId => TraitSkillFx.IdOf(Trait);
    public bool HasShape => Shape != null && !Shape.IsEmpty;
}

/// <summary>
/// [전 피어] 특성 강화 스킬·추가 공격 연출 사건(할 일 145) — 이펙트 쪽은 <see cref="Raised"/> 를 구독만 한다(OnEnable 구독 / OnDisable 해제).
/// 한 사건은 피어마다 1번 난다(호스트도 1번). 발화 위치·한계 = Docs/tech/trait-effect-events.md.
/// 스킬 쪽: 모든 피어에서 도는 경로는 <see cref="Raise"/>, 서버만 아는 판정은 <c>TraitSkillFxNetwork.ServerSend</c>(공용 RPC 1개).
/// </summary>
public static class TraitSkillFx
{
    /// <summary>[전 피어] 특성 강화 연출 사건.</summary>
    public static event Action<TraitFxEvent> Raised;

    // 도메인 리로드를 끈 Play 진입에서도 이전 세션 구독자가 남지 않게 비운다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticEvents() => Raised = null;

    /// <summary>이 피어에서 사건을 낸다. 구독자 예외는 로그만 남기고 다음 구독자로 넘어간다 — 스킬 흐름을 끊지 않는다.</summary>
    public static void Raise(in TraitFxEvent e)
    {
        Action<TraitFxEvent> handlers = Raised;
        if (handlers == null)
            return;

        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<TraitFxEvent>)handler)(e);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }

    /// <summary>특성 종류 → 영구 ID.</summary>
    public static string IdOf(TraitFxTrait trait) => trait switch
    {
        TraitFxTrait.PaladinAdvanceImpact => "PAL_4_Q_BLAST",
        TraitFxTrait.PaladinShieldDash => "PAL_2_DASH",
        TraitFxTrait.PaladinGuardWave => "PAL_4_E_PUSH",
        TraitFxTrait.GunnerWideLaser => "GUN_1_WIDTH",
        TraitFxTrait.GunnerEchoBomb => "GUN_4_ECHO_Q",
        TraitFxTrait.GunnerDualOutput => "GUN_4_EXTRA_BASIC",
        TraitFxTrait.AssassinBladeDomain => "ASN_3_E_AREA",
        TraitFxTrait.AssassinEndlessBlade => "ASN_4_COMBO_EXTRA",
        TraitFxTrait.AssassinPursuitTrail => "ASN_4_Q_TRAIL",
        _ => string.Empty,
    };

    /// <summary>영구 ID → 특성 종류. 연출 사건이 없는 특성이면 false.</summary>
    public static bool TryParse(string traitId, out TraitFxTrait trait)
    {
        foreach (TraitFxTrait value in (TraitFxTrait[])Enum.GetValues(typeof(TraitFxTrait)))
        {
            if (IdOf(value) == traitId)
            {
                trait = value;
                return true;
            }
        }

        trait = default;
        return false;
    }

    /// <summary>그 플레이어가 이 특성을 가졌나(획득 행 — 전 피어 같은 값). 모든 피어 경로에서 사건을 낼지 가른다.</summary>
    public static bool Has(Player caster, TraitFxTrait trait) =>
        caster != null && caster.TraitModifiers != null && caster.TraitModifiers.HasTrait(IdOf(trait));
}

/// <summary>
/// 서버만 아는 특성 사건을 전 피어로 보내는 메시지(할 일 145) — <c>Player.TraitSkillFxRpc</c> 하나가 싣는다.
/// 도형은 보내지 않는다 — 받는 피어가 같은 데이터·같은 특성 수치로 다시 만든다(<c>TraitSkillFxNetwork</c>).
/// </summary>
public struct TraitSkillFxMessage : INetworkSerializable
{
    public byte Trait;
    public byte Kind;
    public Vector3 Position;
    public Vector3 Direction;
    public Vector3 End;
    public Vector3 PosePosition;
    public Vector3 PoseForward;
    public ulong TargetId;
    public bool HasTarget;
    public int Variant;

    public TraitSkillFxMessage(TraitFxTrait trait, TraitFxKind kind, Vector3 position, Vector3 direction, Vector3 end,
        AttackShapePose pose, ulong targetId, bool hasTarget, int variant)
    {
        Trait = (byte)trait;
        Kind = (byte)kind;
        Position = position;
        Direction = direction;
        End = end;
        PosePosition = pose.Position;
        PoseForward = pose.Forward;
        TargetId = targetId;
        HasTarget = hasTarget;
        Variant = variant;
    }

    public AttackShapePose Pose => new AttackShapePose(PosePosition, PoseForward);

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref Trait);
        s.SerializeValue(ref Kind);
        s.SerializeValue(ref Position);
        s.SerializeValue(ref Direction);
        s.SerializeValue(ref End);
        s.SerializeValue(ref PosePosition);
        s.SerializeValue(ref PoseForward);
        s.SerializeValue(ref HasTarget);
        if (HasTarget)
            s.SerializeValue(ref TargetId);
        s.SerializeValue(ref Variant);
    }
}
