using UnityEngine;

public class SpawnPointer : MonoBehaviour
{
    // 디버깅을 위해 임시로 SerializeField 처리
    [SerializeField] Vector3 _spawnPoint;
    public Vector3 SpawnPoint { get { return _spawnPoint; } }

    /// <summary>
    /// 스폰 포인트를 설정하는 함수
    /// </summary>
    /// <param name="point">새로운 스폰 포인트</param>
    public void SetSpawnPoint(Vector3 point)
    {
        _spawnPoint = point;
    }
}
