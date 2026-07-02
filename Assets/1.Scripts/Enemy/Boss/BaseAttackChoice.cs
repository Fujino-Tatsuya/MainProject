using System;
using System.Collections.Generic;
using UnityEngine;

public abstract class BaseAttackChoice : MonoBehaviour
{
    /// <summary>
    /// 거리와 확률에 따라 랜덤으로 enum의 상태를 int로 반환
    /// </summary>
    /// <param name="currentDistance">enemy와 player 간의 거리</param>
    /// <returns>특정 enum의 랜덤 상태를 int로 반환</returns>
    public abstract int GetRandomAttack(float currentDistance);
}