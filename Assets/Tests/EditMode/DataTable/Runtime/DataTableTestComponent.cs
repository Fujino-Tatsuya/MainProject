#if UNITY_EDITOR
using Unity.Netcode;
using UnityEngine;

// 프리팹 컴포넌트 대상 테스트용(D3·D4 — 테이블이 프리팹 인라인 값을 직접 덮어쓴다).
// Editor 폴더(에디터 어셈블리)의 MonoBehaviour 는 AddComponent·프리팹 저장이 안 돼서 런타임 어셈블리에 두고, 빌드에서는 빠지게 감싼다.
public sealed class DataTableTestComponent : MonoBehaviour
{
    public float speed = 3f;
    public int damage = 10;
    [DataTableIgnore] public int bufferSize = 16;
    public NetworkVariable<int> networkHp = new NetworkVariable<int>(5); // 네트워크 상태 — 템플릿에서 빠져야 한다
}
#endif
