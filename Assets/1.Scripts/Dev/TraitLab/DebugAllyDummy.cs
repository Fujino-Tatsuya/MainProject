#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 특성 랩 아군 허수아비(2026-11-02 민경 — 나누는 불굴 전이 연출을 1인 랩에서 보기 위해). DebugTraitLab 씬에만 둔다.
///
/// 실게임의 나누는 불굴 후보는 접속한 클라의 플레이어뿐이라 1인 랩에는 받을 아군이 없다. 이 유닛은 스폰 때
/// <see cref="PlayerTraitEffects.DevShareRecipients"/> 에 등록돼 후보에 끼고, 받으면 자식 <see cref="PlayerOneShotVfxPresenter"/> 로
/// 공용 힐 원샷을 낸다(플레이어와 같은 길).
///
/// - 맞지 않는다(Hurtbox 없음) · 자연 회복 없음 · 죽지 않음. 체력은 랩 패널 '플레이어 체력' 버튼으로만 바꾼다.
/// - 호스트가 소유한 진짜 플레이어를 더 만들지 않는 이유: 그 플레이어도 오너라 카메라·입력·HUD 를 가로챈다.
/// 게이트 = <c>UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS</c>(Docs/tech/debug-hotkeys.md).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[DisallowMultipleComponent]
public sealed class DebugAllyDummy : Unit
{
    [SerializeField, Min(1)] int maxHp = 1000;
    [Tooltip("스폰 때 체력 비율 — 처음부터 덜 찬 상태로 둬 바로 전달받게 한다")]
    [SerializeField, Range(0.01f, 1f)] float startHealthRatio = 0.5f;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (!IsServer)
            return;

        // Initialize 를 부르지 않으면 _health 가 null 이라 회복이 조용히 버려진다. 공격하지 않으므로 공격력·속도 0.
        Initialize(0, 0f, 0f, maxHp, 0);
        SetCurrentHealthOnServer(Mathf.Max(1, Mathf.CeilToInt(maxHp * startHealthRatio)));
        PlayerTraitEffects.DevShareRecipients.Add(this);
    }

    public override void OnNetworkDespawn()
    {
        PlayerTraitEffects.DevShareRecipients.Remove(this);
        base.OnNetworkDespawn();
    }

    // 랩 아군은 맞지 않는다(Hurtbox 가 없어 실제로 오지 않지만, 폴백 경로로 와도 버린다).
    public override bool ReceiveAttack(AttackInfo attackInfo, AttackHitContext hitContext) => false;
}
#endif
