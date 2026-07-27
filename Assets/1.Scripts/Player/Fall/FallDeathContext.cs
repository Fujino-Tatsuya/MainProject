using UnityEngine;

/// <summary>
/// 추락 사망이 실제로 발생한 순간의 정보. Fall 트랙이 발행하고 Soul/LifeCount 작업이 소비한다.
/// (PLAN §13, Soul 요청서 §7) — 두 트랙의 공용 계약이다.
/// </summary>
public readonly struct FallDeathContext
{
    public readonly ulong PlayerNetworkObjectId;
    public readonly Vector3 DeathWorldPosition; // 추락 피해가 사망시킨 순간의 위치
    public readonly Vector3 FallPoint;          // Threshold를 넘어 추락을 시작/판정한 지점
    public readonly Vector2 SoulStartXZ;        // 부활 시 SoulPlane에 투영할 X/Z
    public readonly int SourceSceneHandle;

    public FallDeathContext(
        ulong playerNetworkObjectId,
        Vector3 deathWorldPosition,
        Vector3 fallPoint,
        Vector2 soulStartXZ,
        int sourceSceneHandle)
    {
        PlayerNetworkObjectId = playerNetworkObjectId;
        DeathWorldPosition = deathWorldPosition;
        FallPoint = fallPoint;
        SoulStartXZ = soulStartXZ;
        SourceSceneHandle = sourceSceneHandle;
    }
}
