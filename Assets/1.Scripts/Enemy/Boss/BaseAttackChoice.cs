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

    /// <summary>
    /// 특정 공격 타입을 추가합니다. 이미 존재하는 공격 타입을 추가하려고 하면 경고 메시지를 출력하고 아무 작업도 수행하지 않습니다.
    /// </summary>
    /// <param name="type">추가할 공격 타입</param>
    /// <param name="minDistance">공격의 최소 거리</param>
    /// <param name="maxDistance">공격의 최대 거리</param>
    /// <param name="percentage">공격의 확률</param>
    public abstract void AddType<T>(T type) where T : Enum;

    /// <summary>
    /// 특정 공격 타입을 제거합니다. 제거할 공격 타입이 존재하지 않으면 아무 작업도 수행하지 않습니다.
    /// </summary>
    /// <param name="type">제거할 공격 타입</param>
    public abstract void RemoveType<T>(T type) where T : Enum;

    /// <summary>
    /// 페이즈가 증가할 때마다 호출되는 함수.
    /// </summary>
    /// <param name="page">페이즈 넘버</param>
    /// <returns></returns>
    public abstract void PageEvent(int page);
}
