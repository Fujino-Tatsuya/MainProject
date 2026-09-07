using System;
using UnityEngine;

/// <summary>
/// 메시 하나를 삼각형 단위 파편으로 미리 구워 둔 에셋.
///
/// <b>MeshExploder와의 차이</b>: MeshExploder는 터지는 순간 원본 메시의 정점을 읽어 파편 메시를 만들고
/// GameObject를 붙인다. 그 비용(메시 생성 + convex 쿠킹 + AddComponent)이 전부 한 프레임에 몰린다.
/// 여기서는 그 작업을 <b>에디터에서 한 번</b> 해서 에셋으로 굽고, 런타임은 미리 만들어 둔 것을 켜기만 한다.
///
/// 파편은 납작한 삼각형이 아니라 <see cref="thickness"/>만큼 두께를 준 <b>삼각기둥</b>이다.
/// 부피가 있어야 convex 콜라이더가 만들어지고(PhysX는 납작한 메시로 hull을 못 만든다),
/// 옆에서 봐도 종잇장처럼 보이지 않는다.
///
/// <b>굽고 나면 원본 메시의 Read/Write는 꺼도 된다</b> — 런타임에 정점을 읽을 일이 없어진다.
/// </summary>
[CreateAssetMenu(fileName = "FragmentSet", menuName = "Effects/Mesh Fragment Set")]
public class MeshFragmentSet : ScriptableObject
{
    /// <summary>파편 하나. 메시는 이 에셋의 서브에셋으로 저장된다.</summary>
    [Serializable]
    public class Fragment
    {
        [Tooltip("파편 메시. 정점은 자기 무게중심 기준이다")]
        public Mesh mesh;

        [Tooltip("원본 메시 로컬 기준, 이 파편이 원래 있던 자리")]
        public Vector3 center;

        [Tooltip("원본 삼각형의 면 법선. 폭발 방향을 면 기준으로 줄 때 쓴다")]
        public Vector3 normal;
    }

    [Header("굽기 설정")]
    [Tooltip("파편으로 쪼갤 원본 메시. Bake 하려면 이 메시의 Read/Write가 켜져 있어야 한다")]
    public Mesh sourceMesh;

    [Tooltip("파편에 줄 두께(원본 메시 로컬 단위). 0에 가까우면 convex 콜라이더가 만들어지지 않는다")]
    [Min(0.001f)] public float thickness = 0.05f;

    [Tooltip("구울 파편 개수 상한. 0이면 전부 굽는다. 삼각형이 많은 메시는 여기서 줄이는 게 낫다 " +
             "— 런타임에 솎아내면 파편이 듬성듬성해질 뿐 크기는 그대로다")]
    [Min(0)] public int maxFragments = 0;

    [Header("버스트 프리팹 굽기")]
    [Tooltip("파편에 쓸 머티리얼. 버스트 프리팹을 구울 때 MeshRenderer에 꽂힌다")]
    public Material fragmentMaterial;

    [Min(0.001f)] public float fragmentMass = 0.2f;
    public float linearDamping = 0.05f;
    public float angularDamping = 0.5f;

    [Header("결과 (Bake가 채운다)")]
    public Fragment[] fragments;

    [Tooltip("Bake가 만든 버스트 프리팹. EffectEntry의 파트로 이걸 지정한다")]
    public GameObject burstPrefab;

    /// <summary>구워진 파편이 있는가.</summary>
    public bool IsBaked => fragments != null && fragments.Length > 0;
}
