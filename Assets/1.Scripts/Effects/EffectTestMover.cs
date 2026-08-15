using UnityEngine;

/// <summary>
/// 검증용 프록시를 좌우로 왕복시킨다. 케이스 5(부착 추종)에서 "따라오는가"를 눈으로 보기 위한 도구.
/// </summary>
public class EffectTestMover : MonoBehaviour
{
    [SerializeField] private Vector3 axis = Vector3.right;
    [SerializeField, Min(0f)] private float distance = 3f;
    [SerializeField, Min(0f)] private float speed = 1f;

    private Vector3 _origin;

    private void Awake() => _origin = transform.position;

    private void Update()
    {
        transform.position = _origin + axis.normalized * (Mathf.Sin(Time.time * speed) * distance);
    }
}
