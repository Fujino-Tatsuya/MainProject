using UnityEngine;

// 존·보스방 밑 물(Flat Kit 물 조각 + 물 바닥)의 **수면 높이**를 인스펙터 한 칸으로 조절한다 (PLAN-flatkit.md 9단계 · 2026-10-01).
//
// 구조: 존 루트 └ Water(이 컴포넌트) ├ WaterWaves(물 격자) └ WaterBed(물 밑 바닥) — 둘 다 Water 기준 y 0.
// 수면 높이 = Water 의 로컬 y. 이 값을 바꾸면 물과 바닥이 같이 움직인다(바닥 깊이 = 수면 기준 상대값이라 색이 유지된다).
//
// 🔴 존마다 구멍 깊이가 달라 높이를 존별로 맞춰야 한다(팀장 10-01) — **존 프리팹에서** 바꿀 것.
//    Play 중 (Clone) 에서 바꾼 값은 종료 때 사라진다. Play 중에 맞췄다면 컴포넌트 ⋮ → Copy Component →
//    프리팹에서 Paste Component Values.
// 저작 도구(FlatKitWaterPatchAuthoring)를 다시 돌려도 이 값은 유지된다.
[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class ZoneWater : MonoBehaviour
{
    [Tooltip("수면 높이(존 루트 기준 로컬 Y, m). 물과 물 밑 바닥이 같이 움직인다.\n" +
             "벤트 창살이 있으면 창살 아랫면보다 약 0.56m 아래가 기준(보스방 확정값). 구멍이 깊은 존은 더 내린다.\n" +
             "Transform 의 Y 대신 이 값을 바꿀 것 — 에디터에서는 Transform Y 가 이 값으로 고정된다.")]
    [SerializeField] float waterHeight;

    // 존 회전별 물·물 밑 바닥 메시(0·90·180·270°) — 열린 면이 있는 존만 저작 도구가 채운다(비어 있으면 아무것도 안 함).
    // 🔴 같은 존 프리팹이 슬롯마다 90° 단위로 회전 배치되고(ZoneSlot.Rotations → MapContentSpawner Euler(0, 90·YawSteps, 0)),
    //    카메라는 회전 고정이라 '카메라 쪽 변'이 회전마다 다르다 — 열린 면 물 가장자리를 회전마다 따로 잘라 둔 메시를 고른다(10-01).
    [SerializeField, HideInInspector] Mesh[] waterByYaw = new Mesh[0];
    [SerializeField, HideInInspector] Mesh[] bedByYaw = new Mesh[0];
    const string WaterChild = "WaterWaves", BedChild = "WaterBed";   // FlatKitWaterPatchAuthoring 의 자식 이름과 같아야 한다

    public float WaterHeight
    {
        get => waterHeight;
        set { waterHeight = value; Apply(); }
    }

    public void SetYawMeshes(Mesh[] water, Mesh[] bed)
    {
        waterByYaw = water;
        bedByYaw = bed;
        ApplyYawMesh();
    }

    void OnEnable()
    {
        Apply();
        ApplyYawMesh();   // Instantiate(prefab, pos, rot) 는 OnEnable 전에 회전이 들어가 있다
    }

    void ApplyYawMesh()
    {
        if (waterByYaw == null || waterByYaw.Length != 4) return;
        int k = ((Mathf.RoundToInt(transform.eulerAngles.y / 90f) % 4) + 4) % 4;
        SetChildMesh(WaterChild, waterByYaw[k]);
        if (bedByYaw != null && bedByYaw.Length == 4) SetChildMesh(BedChild, bedByYaw[k]);
    }

    void SetChildMesh(string child, Mesh mesh)
    {
        Transform t = transform.Find(child);
        if (t == null || !t.TryGetComponent(out MeshFilter mf)) return;
        if (mf.sharedMesh != mesh) mf.sharedMesh = mesh;   // 같으면 건드리지 않는다(에디터에서 프리팹을 더럽히지 않게)
        if (t.TryGetComponent(out MeshRenderer mr)) mr.enabled = mesh != null;   // 그 회전에선 물이 다 잘린 경우
    }

#if UNITY_EDITOR
    void OnValidate() => Apply();

    // 에디터에서는 이 값이 기준 — Transform 을 끌어도 되돌린다(두 곳에서 높이가 갈리면 저장 때 어느 쪽이 남을지 헷갈린다).
    void Update()
    {
        if (!Application.isPlaying) Apply();
    }
#endif

    void Apply()
    {
        Vector3 p = transform.localPosition;
        if (Mathf.Approximately(p.y, waterHeight)) return;
        p.y = waterHeight;
        transform.localPosition = p;
    }
}
