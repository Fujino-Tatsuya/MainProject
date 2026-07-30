using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 추락 감지·피해(서버 권한). (PLAN §13 / W10)
///
/// - 서버가 매 물리 tick Player 위치 Y를 FallBoundarySettings.threshold와 비교해 1회 추락 피해를 적용한다.
/// - 피해: BreakShield → ceil(FinalMaxHp * ratio) 직접 피해(방어력·쉴드·일반 무적 우회). 공격 Passive/Hit 반응 없음.
/// - 사망 시 <see cref="ServerFallDeath"/>(FallDeathContext), 생존 시 <see cref="ServerFallSurvived"/>를 발행한다.
///   이 이벤트는 병합 시 Soul 생명주기(추락 사망 원인)와 W11 안전지점 복귀가 구독하는 seam이다.
/// - Alive 전용 게이트(Soul/Dead 제외)는 현재 HP>0 proxy — 병합 시 LifeState==Alive로 대체.
/// - 추락 순간 Float Camera 전환은 W12.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Player))]
public sealed class PlayerFallController : NetworkBehaviour
{
    private Player player;
    private PlayerEncounterLock encounterLock;
    private bool _fallHandled;
    private bool _warnedMissingBoundary;

    /// <summary>서버 전용. 추락 피해로 사망한 순간 발행. Soul/LifeCount 작업이 소비.</summary>
    public event Action<FallDeathContext> ServerFallDeath;

    /// <summary>서버 전용. 추락 피해를 입었지만 생존한 순간 발행(fallPoint 전달). W11 안전지점 복귀가 소비.</summary>
    public event Action<Vector3> ServerFallSurvived;

    private void Awake()
    {
        player = GetComponent<Player>();
        encounterLock = GetComponent<PlayerEncounterLock>();
    }

    private void FixedUpdate()
    {
        if (!IsSpawned || !IsServer || player == null)
            return;

        // 연출 잠금(보스룸 이동·등장) 중에는 추락 판정을 멈춘다. 보스룸은 생성맵 밖 좌표라
        // 이동 중 경계 아래로 스쳐도 추락 피해·안전지점 복귀가 걸리면 도착 계약이 깨진다.
        if (encounterLock != null && encounterLock.IsCinematicLocked)
            return;

        FallBoundarySettings boundary = FallBoundarySettings.Instance;
        if (boundary == null)
        {
            if (!_warnedMissingBoundary)
            {
                _warnedMissingBoundary = true;
                Debug.LogWarning("[FallAlert] FallBoundarySettings가 씬에 없어 추락 감지를 비활성화합니다.", this);
            }
            return;
        }

        // Alive 전용(Soul/Dead/Corpse는 별도 규칙). 병합 시 LifeState==Alive로 교체.
        if (player.CurrentHealth <= 0)
            return;

        float y = transform.position.y;
        if (y >= boundary.FallThresholdY)
        {
            _fallHandled = false; // 경계 위로 복귀하면 재무장
            return;
        }

        if (_fallHandled)
            return;

        _fallHandled = true;
        HandleFall(boundary);
    }

    private void HandleFall(FallBoundarySettings boundary)
    {
        Vector3 fallPoint = transform.position;

        player.ApplyFallDamage(boundary.FallDamageRatio); // BreakShield + 직접 피해(무적 우회)

        if (player.CurrentHealth <= 0)
        {
            Vector3 deathPos = transform.position;
            FallDeathContext context = new FallDeathContext(
                NetworkObjectId,
                deathPos,
                fallPoint,
                new Vector2(deathPos.x, deathPos.z),
                gameObject.scene.handle);
            ServerFallDeath?.Invoke(context);
        }
        else
        {
            ServerFallSurvived?.Invoke(fallPoint);
        }
    }
}
