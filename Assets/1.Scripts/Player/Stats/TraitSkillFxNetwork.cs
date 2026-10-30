using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 서버만 아는 특성 강화 연출 사건(파생 공격 판정·특성 밀치기 적중 등)을 전 피어 <see cref="TraitSkillFx.Raised"/> 로 내는 경로(할 일 145).
/// 서버 → <c>Player.TraitSkillFxRpc</c>(ClientsAndHost — 호스트도 1번) → <see cref="Receive"/>. 오프라인이면 바로 <see cref="Receive"/>.
/// 판정·피해는 보지 않는다 — 판정이 끝난 뒤 알리기만 한다.
/// 도형은 네트워크로 보내지 않고 받는 피어가 같은 데이터·같은 특성 수치(획득 행 — 전 피어 같은 값)로 다시 만든다(<see cref="ResolveShape"/>).
/// </summary>
public static class TraitSkillFxNetwork
{
    /// <summary>[서버] 사건 1건을 전 피어에 보낸다. 서버(또는 오프라인)가 아니면 무시. <paramref name="pose"/> 는 도형이 있는 특성만 의미가 있다.</summary>
    public static void ServerSend(Player caster, TraitFxTrait trait, TraitFxKind kind, Vector3 position, Vector3 direction, Vector3 end,
        AttackShapePose pose = default, Unit target = null, int variant = 0)
    {
        if (caster == null)
            return;

        bool hasTarget = target != null && target.IsSpawned;
        var message = new TraitSkillFxMessage(trait, kind, position, direction, end, pose,
            hasTarget ? target.NetworkObjectId : 0UL, hasTarget, variant);
        caster.ServerSendTraitSkillFx(message);
    }

    /// <summary>[전 피어] RPC(또는 오프라인 직접 호출) 수신 — 대상·도형을 이 피어에서 찾아 사건을 낸다.</summary>
    public static void Receive(Player caster, in TraitSkillFxMessage message)
    {
        if (caster == null)
            return;

        var trait = (TraitFxTrait)message.Trait;
        var kind = (TraitFxKind)message.Kind;
        Unit target = message.HasTarget ? FindUnit(message.TargetId) : null;
        AttackShapeSet shape = kind == TraitFxKind.Activated ? ResolveShape(caster, trait, message) : null;
        TraitSkillFx.Raise(new TraitFxEvent(trait, kind, caster, message.Position, message.Direction, message.End,
            target, shape, message.Pose, message.Variant));
    }

    /// <summary>네트워크 ID → 스폰된 유닛(없으면 null).</summary>
    private static Unit FindUnit(ulong networkObjectId)
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || manager.SpawnManager == null ||
            !manager.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject networkObject) || networkObject == null)
            return null;

        return networkObject.TryGetComponent(out Unit unit) ? unit : networkObject.GetComponentInChildren<Unit>();
    }

    /// <summary>
    /// 발동 사건의 판정 도형을 받는 피어에서 다시 만든다 — 서버 판정이 쓰는 같은 공용 함수·같은 데이터.
    /// 이중 출력(굵은 직선, 도형 아님)·모르는 특성 = null.
    /// </summary>
    private static AttackShapeSet ResolveShape(Player caster, TraitFxTrait trait, in TraitSkillFxMessage message)
    {
        switch (trait)
        {
            case TraitFxTrait.PaladinAdvanceImpact:
                // 진격의 충격 = 특성 정의의 파생 공격 hitArea 그대로(Q 끝 지점 기준 Point).
                return DerivedHitArea(TraitFxTrait.PaladinAdvanceImpact, PaladinTraitValues.QBlastKey);

            case TraitFxTrait.PaladinGuardWave:
            {
                FirstMeleeSubSkill guard = caster.GetComponentInChildren<FirstMeleeSubSkill>(true);
                return guard != null ? guard.TraitPushArea() : null;
            }

            case TraitFxTrait.AssassinEndlessBlade:
            {
                // Variant = 모드 × 16 + 단 번호(AssassinBasicAttack.ComboExtraFxVariant).
                AssassinBasicAttack basic = caster.GetComponentInChildren<AssassinBasicAttack>(true);
                var mode = (AssassinBasicAttackMode)(message.Variant / 16);
                return basic != null && basic.TryGetHitArea(mode, message.Variant % 16, message.Direction, out AttackShapeSet area, out _)
                    ? area
                    : null;
            }

            case TraitFxTrait.AssassinPursuitTrail:
            {
                // Variant = 그 돌진 스킬의 순서(플레이어 아래 AssassinDashStrikeSkill 목록 — 전 피어 같은 프리팹). 도형 = 지나온 선분 길이로 맞춘 경로 도형.
                AssassinDashStrikeSkill[] dashes = caster.GetComponentsInChildren<AssassinDashStrikeSkill>(true);
                if (message.Variant < 0 || message.Variant >= dashes.Length || dashes[message.Variant].DashData == null)
                    return null;
                Vector3 along = message.End - message.Position;
                along.y = 0f;
                return dashes[message.Variant].DashData.PathArea(along.magnitude);
            }

            default:
                return null;
        }
    }

    // 특성 정의(전 피어 같은 SO)의 파생 공격 칸 중 이름이 맞는 것의 hitArea. 디렉터·정의가 없으면 null.
    private static AttackShapeSet DerivedHitArea(TraitFxTrait trait, string derivedKey)
    {
        TraitDirector director = TraitDirector.Instance;
        TraitDefinition definition = director != null ? director.GetTrait(TraitIdHash.Of(TraitSkillFx.IdOf(trait))) : null;
        if (definition == null || definition.Effects == null)
            return null;

        for (int i = 0; i < definition.Effects.Count; i++)
        {
            if (definition.Effects[i] is TraitDerivedAttackEffect derived && derived.DerivedKey == derivedKey)
                return derived.HitArea;
        }

        return null;
    }
}
