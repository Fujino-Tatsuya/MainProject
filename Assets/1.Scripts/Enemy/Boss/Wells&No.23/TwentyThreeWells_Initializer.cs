using System;
using Unity.Netcode;
using Unity.Behavior;
using UnityEngine;

/// <summary>
/// No.23 보스의 통합 수치 SO(TwentyThreeFigure)를 읽어 spawn 시 목적별로 주입하는 컴포넌트. (서버 전용)
/// - 블랙보드 값 → BehaviorGraphAgent 블랙보드
/// - 평타 damage → 각 공격 컴포넌트(IDamageSettable)
/// - Grab 설정 → GrabController
/// 공용 공격 컴포넌트는 중립 세터(SetDamage / SetGrabFigures)만 호출당하며 코드 변경이 없다.
/// </summary>
public class TwentyThreeWells_Initializer : NetworkBehaviour
{
    [Serializable]
    struct DamageEntry
    {
        [Tooltip("damage를 주입할 대상 컴포넌트 (IDamageSettable 구현). 컴포넌트 헤더를 직접 드래그해 지정")]
        public MonoBehaviour target;
        public TwentyThreeDamageType type;
    }

    [SerializeField] TwentyThreeWells_Figure figure;

    [Header("블랙보드")]
    [SerializeField] BehaviorGraphAgent bt;

    [Header("평타 damage 주입 대상")]
    [SerializeField] DamageEntry[] damageEntries;

    [Header("Grab (퍼센티지 3개 + 주기)")]
    [SerializeField] GrabController grabController;

    [Header("Wells 폭탄 투척")]
    [SerializeField] BombLauncher bombLauncher;

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        if (figure == null)
        {
            Edit.LogError("[No.23] TwentyThreeInitializer에 figure SO가 연결되어 있지 않습니다.", this);
            return;
        }

        ApplyBlackboard();
        ApplyDamages();
        ApplyGrab();
        ApplyBomb();
    }

    void ApplyBlackboard()
    {
        if (bt == null)
        {
            Edit.LogError("[No.23] TwentyThreeInitializer에 bt(BehaviorGraphAgent)가 연결되어 있지 않습니다.", this);
            return;
        }

        SetInt("MaxRageCount", figure.maxRageCount);
        SetInt("IncreaseHpAmount", figure.increaseHpAmount);
        SetInt("IncreaseShieldAmount", figure.increaseShieldAmount);

        SetInt("MaxGroggyCount", figure.maxGroggyCount);

        SetFloat("DashSpeed", figure.dashSpeed);
        SetFloat("GrabCoolTime", figure.grabCoolTime);
        SetFloat("ChargingTime", figure.chargingTime);
        SetFloat("GroggyTime", figure.groggyTime);
        SetFloat("BreakTime", figure.breakTime);
        SetFloat("JumpingTime", figure.jumpingTime);
    }

    void ApplyDamages()
    {
        foreach (DamageEntry entry in damageEntries)
        {
            if (entry.target == null)
            {
                Edit.LogError("[No.23] damageEntries의 target이 비어 있습니다.", this);
                continue;
            }

            if (entry.target is not IDamageSettable settable)
            {
                Edit.LogError($"[No.23] {entry.target.name}은 IDamageSettable을 구현하지 않습니다.", this);
                continue;
            }

            settable.SetDamage(figure.GetDamage(entry.type));
        }
    }

    void ApplyGrab()
    {
        if (grabController == null) return;

        grabController.SetGrabFigures(
            figure.grabDamagePercentage,
            figure.holdDamagePercentage,
            figure.landingDamagePercentage,
            figure.holdAttackPeriod);
    }

    void ApplyBomb()
    {
        if (bombLauncher == null) return;

        bombLauncher.SetThrowFigures(
            figure.throwLocalDirection,
            figure.throwDistance,
            figure.flyingDuration,
            figure.arcHeight,
            figure.spreadAngle);
    }

    void SetInt(string name, int value)
    {
        if (bt.BlackboardReference.GetVariable<int>(name, out BlackboardVariable<int> variable))
            variable.Value = value;
        else
            Edit.LogError($"[No.23] Blackboard variable '{name}'(int) not found.", this);
    }

    void SetFloat(string name, float value)
    {
        if (bt.BlackboardReference.GetVariable<float>(name, out BlackboardVariable<float> variable))
            variable.Value = value;
        else
            Edit.LogError($"[No.23] Blackboard variable '{name}'(float) not found.", this);
    }
}
