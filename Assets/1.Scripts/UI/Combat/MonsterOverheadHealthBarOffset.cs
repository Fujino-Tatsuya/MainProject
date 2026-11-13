using UnityEngine;

/// <summary>
/// 몬스터 프리팹 루트에 붙이면 머리 위(발밑) 체력바 위치를 공용 설정 대신 이 값으로 쓴다(할 일 274 — 로비 훈련장 봇 전용 튜닝).
/// 없으면 지금처럼 <see cref="MonsterOverheadHealthBarSettings"/> 의 등급별 값(footScreenOffset·midBossFootScreenOffset).
/// 스폰 때 한 번 읽는다(<see cref="MonsterOverheadHealthBarRule.TryInstall"/>) — Play 중 바꾼 값은 다음 스폰부터.
/// </summary>
[DisallowMultipleComponent]
public sealed class MonsterOverheadHealthBarOffset : MonoBehaviour
{
    [Tooltip("발밑에서 화면 아래로 내릴 픽셀(1080p 캔버스 단위). 공용 설정의 등급별 값을 이 몬스터만 덮어쓴다.")]
    [SerializeField, Min(0f)] float footScreenOffset = 50f;

    public float FootScreenOffset => footScreenOffset;
}
