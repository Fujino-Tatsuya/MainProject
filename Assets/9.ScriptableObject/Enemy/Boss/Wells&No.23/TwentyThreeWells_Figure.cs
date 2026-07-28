using UnityEngine;

/// <summary>
/// No.23 보스 평타 damage 슬롯.
/// </summary>
public enum TwentyThreeDamageType
{
    Hook,
    Upper,
    ChargingFloor,
    Dash,
    Rage,
    Jump
}

/// <summary>
/// No.23 보스의 모든 튜닝 수치(블랙보드 스탯 + 공격 damage + Grab 설정)를 담는 통합 데이터 SO.
/// TwentyThreeInitializer가 spawn 시 목적별로(블랙보드 / 공격 컴포넌트 / GrabController) 주입한다.
/// </summary>
[CreateAssetMenu(fileName = "TwentyThreeWells_Figure", menuName = "Scriptable Objects/TwentyThreeWells_Figure")]
public class TwentyThreeWells_Figure : ScriptableObject
{
    [Header("블랙보드 - 레이지 / 회복 (int)")]
    public int maxRageCount;
    public int increaseHpAmount;
    public int increaseShieldAmount;

    [Header("블랙보드 - 그로기 (int)")]
    public int maxGroggyCount;

    [Header("블랙보드 - 대시 (float)")]
    public float dashSpeed;

    [Header("블랙보드 - 잡기 (float)")]
    public float grabCoolTime;

    [Header("블랙보드 - 상태 지속 시간 (float)")]
    public float chargingTime;
    public float groggyTime;
    public float breakTime;

    [Header("블랙보드 - 점프 (float)")]
    public float jumpingTime;

    [Header("damage (flat, int)")]
    public int hookDamage;
    public int upperDamage;
    public int chargingFloorDamage;
    public int dashDamage;
    public int rageDamage;
    public int jumpDamage;

    [Header("Grab (대상 체력 대비 %)")]
    public int grabDamagePercentage;
    public int holdDamagePercentage;
    public int landingDamagePercentage;
    [Tooltip("홀드 중 데미지가 반복 적용되는 주기(초)")]
    public float holdAttackPeriod;

    [Header("Wells 폭탄 투척 설정 (BombLauncher)")]
    public Vector3 throwLocalDirection = Vector3.forward;
    public float throwDistance;
    public float flyingDuration;
    public float arcHeight;
    [Tooltip("좌우 랜덤 살포 각도 (forward 기준 ± 도)")]
    public float spreadAngle;

    /// <summary>슬롯 타입에 해당하는 평타 damage 값을 반환한다.</summary>
    public int GetDamage(TwentyThreeDamageType type)
    {
        switch (type)
        {
            case TwentyThreeDamageType.Hook: return hookDamage;
            case TwentyThreeDamageType.Upper: return upperDamage;
            case TwentyThreeDamageType.ChargingFloor: return chargingFloorDamage;
            case TwentyThreeDamageType.Dash: return dashDamage;
            case TwentyThreeDamageType.Rage: return rageDamage;
            case TwentyThreeDamageType.Jump: return jumpDamage;
            default: return 0;
        }
    }
}
