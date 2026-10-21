using UnityEngine;

/// <summary>
/// 로딩 씬에서도 정본 웰즈 드론 데이터를 찾기 위한 작은 Resources 인덱스.
/// 실제 수치·모델·클립은 <see cref="data"/>가 가리키는 WellsDroneData.asset 한 곳에만 있다.
/// </summary>
public sealed class WellsDronePrewarmCatalog : ScriptableObject
{
    public WellsDroneDataSO data;
}
