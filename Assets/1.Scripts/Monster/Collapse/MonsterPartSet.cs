using System;
using UnityEngine;

/// <summary>
/// 몬스터 메시 하나를 <b>부위 덩어리</b>로 미리 갈라 둔 에셋.
///
/// <b><see cref="MeshFragmentSet"/>(상자)와의 차이</b>: 저쪽은 삼각형 하나하나를 기둥으로 구워
/// <b>산산조각</b>을 낸다. 여기는 모델러가 만든 <b>부위 경계</b>를 살려 큰 덩어리 몇 개로 가른다.
/// 로봇이 터지는 게 아니라 해체돼 무너지는 그림이라, 조각 하나하나가 뭔지 알아볼 수 있어야 한다.
///
/// 🔴 <b>자르지 않는다 — 가른다.</b> 이 팩의 로봇들은 버텍스가 본 하나에 거의 100% 묶인
/// 강체 리깅이라, 본 인덱스로 삼각형을 모으면 모델러의 파츠 경계가 그대로 나온다.
/// ChompBot·HumanoidBot 은 본 경계를 가로지르는 폴리곤이 <b>0개</b>다.
///
/// 🔴 <b>조각 정점은 '따라갈 트랜스폼의 로컬 공간'에 들어 있다.</b> 강체 가중치에서 스킨 결과는
/// 정확히 <c>bones[b].localToWorldMatrix * bindposes[b] * v</c> 이므로, 구울 때 <c>bindposes[b]</c> 를
/// 미리 먹여 두면 런타임은 조각을 <b>그 본의 월드 트랜스폼에 놓기만</b> 하면 된다.
/// 사망 애니메이션이 끝난 <b>그 자세</b>가 근사 없이 재현된다 — 미리 잘라 둔 스태틱 파츠(바인드 포즈)로는
/// 안 되는 지점이고, 이 방식을 고른 가장 큰 이유다.
///
/// 🔴 <b>고정 메시(무기·건틀릿)도 조각이 된다.</b> HumanoidBot 의 총, GauntletBot 의 건틀릿 두 짝은
/// 스킨이 아니라 본에 매달린 평범한 <c>MeshRenderer</c> 다. 이걸 빼먹으면 몸은 무너지는데
/// 무기만 공중에서 증발한다. 통째로 한 덩어리가 되어 같이 떨어진다.
/// </summary>
[CreateAssetMenu(fileName = "MonsterPartSet", menuName = "Effects/Monster Part Set")]
public class MonsterPartSet : ScriptableObject
{
    /// <summary>부위 하나. 메시는 이 에셋의 서브에셋으로 저장된다.</summary>
    [Serializable]
    public class Part
    {
        [Tooltip("조각 메시. 아래 트랜스폼의 로컬 공간이고, 정점은 자기 무게중심 기준이다")]
        public Mesh mesh;

        [Tooltip("이 조각이 따라갈 트랜스폼의 경로(몬스터 루트 기준). 스킨이면 본, 고정 메시면 그 오브젝트")]
        public string followPath;

        [Tooltip("경로가 안 맞을 때의 보조 — 이름으로 다시 찾는다")]
        public string followName;

        [Tooltip("따라갈 트랜스폼 기준 무게중심. 리지드바디의 회전 중심이 조각 한가운데여야 자연스럽게 구른다")]
        public Vector3 boneOffset;

        // 🔴 **머티리얼은 런타임에 살아 있는 렌더러에서 읽는다.** 구워 둔 값을 쓰면
        //    프리팹 오버라이드·런타임 교체·리디자인 교체를 놓쳐 죽을 때만 다른 색으로 보인다
        //    (2026-10-06 실제로 그랬다). 아래 둘이 그 출처를 가리킨다.
        [Tooltip("머티리얼을 읽어 올 원본 렌더러의 경로(몬스터 루트 기준)")]
        public string rendererPath;

        [Tooltip("조각 서브메시 -> 원본 렌더러의 머티리얼 슬롯 번호")]
        public int[] submeshIndices;

        [Tooltip("폴백 — 런타임에 원본 렌더러를 못 찾았을 때만 쓴다(구운 시점의 값)")]
        public Material[] materials;

        [Tooltip("스킨이 아니라 통째로 떨어지는 고정 메시인가(무기·건틀릿)")]
        public bool isStaticMesh;

        public int triangleCount;
    }

    [Header("굽기 설정")]
    [Tooltip("가를 원본. 몬스터 프리팹 그대로 넣으면 된다 — 자식의 모든 렌더러를 훑는다")]
    public GameObject sourcePrefab;

    // 🔴 조각이 많다고 좋은 게 아니다. GauntletBot 은 본이 41개라 그대로 가르면 40조각이 나오는데,
    //    그건 '무너진다'가 아니라 '부품이 쏟아진다'로 읽히고 리지드바디도 그만큼 는다.
    //    넘치는 만큼 작은 조각부터 **부모 본으로 합친다**(볼트가 그 팔뚝에 붙는 식) —
    //    합치면 경계를 가로지르던 삼각형도 같이 사라져 구멍 위험까지 준다.
    [Tooltip("조각 수 상한. 넘으면 작은 것부터 부모 본에 합친다. 0 = 제한 없음")]
    [Min(0)] public int maxParts = 12;

    [Tooltip("이 값보다 삼각형이 적은 조각은 부모 본에 합친다. 너무 작은 조각은 바닥에서 떨리기만 한다")]
    [Min(0)] public int minTriangles = 20;

    [Header("조각 물리")]
    [Min(0.001f)] public float partMass = 1f;
    public float linearDamping = 0.1f;

    [Tooltip("각속도 감쇠. 상자 파편(0.5)보다 높게 잡는다 — 덩어리가 팽이처럼 돌면 '무너진다'가 아니다")]
    public float angularDamping = 1.5f;

    [Header("결과 (Bake 가 채운다)")]
    public Part[] parts;

    /// <summary>구워진 조각이 있는가.</summary>
    public bool IsBaked => parts != null && parts.Length > 0;
}
