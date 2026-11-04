using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 맵씬 복도 게이트의 레이저 시각(할 일 160, 비네트워크). <c>MapGateLaser.prefab</c> 루트에 붙는다.
/// 원본: Docs/tech/tutorial-director.md §7.
///
/// · <see cref="MapSceneBarriers"/> 가 Barrier 콜라이더(큐브) 옆에 형제로 만들고 <see cref="Fit"/> 으로 게이트 크기에 맞춘다.
///   막기·카브는 큐브 쪽 그대로 — 이 프리팹엔 콜라이더가 없다.
/// · 뷰 좌표 = 로컬 X 가 게이트 폭(가로), Y 가 위, Z 가 진행 방향. 원점 = 바닥 가운데.
/// · 크기는 메시 bounds 로 잰다(FBX 의 긴 축·피벗을 하드코딩하지 않음). 조각(발사대·빔)의 루트는 뷰 축과 90° 단위로만 돌려 둘 것.
/// · 빔 줄 = <c>Beams</c> 아래 자식 그대로(할 일 204 — 복제하지 않는다). 프리팹에 놓은 줄 수·높이·앞뒤 위치·(발사대 대비) 길이 비율을
///   그대로 두고 게이트 폭에만 맞춘다. 줄을 늘리거나 옮기려면 프리팹에서 Beam 을 복제·이동하면 된다.
/// · 열림 = 빔만 즉시 끈다(연출 없음). 발사대는 장식으로 남는다. 다시 닫히지 않는다.
/// · 이펙트 담당은 <c>Visual</c> 아래만 바꾸면 된다 — 칸(발사대·빔 부모)만 다시 연결.
/// </summary>
[DisallowMultipleComponent]
public sealed class MapGateLaserView : MonoBehaviour
{
    [Tooltip("발사대 몸체(Env_laser). 이 FBX 는 메시 하나에 양 끝 발사대 둘이 다 들어 있다 — 긴 축을 게이트 폭에 맞춰 늘인다.")]
    [SerializeField] private Transform posts;

    [Tooltip("빔 줄들의 부모(Visual/Beams). 바로 아래 자식 하나 = 빔 한 줄(Env_laser_lay). 프리팹에 놓은 배치 그대로 쓰고 복제하지 않는다 — " +
             "줄 높이·앞뒤 위치는 발사대 기준으로 유지, 길이는 프리팹의 (빔 길이 ÷ 발사대 폭) 비율로 게이트 폭에 맞춘다.")]
    [SerializeField] private Transform beamsRoot;

    [Tooltip("켜면 발사대는 FBX 높이 그대로 두고 빔 높이도 프리팹 그대로. 끄면 발사대를 게이트 높이로 늘리고 빔 높이도 같은 비율로 늘린다.")]
    [DataTableIgnore]
    [SerializeField] private bool keepPostHeight = true;

    // 프리팹에 저작된 빔 한 줄 — 발사대 bounds 기준 상대 배치.
    struct BeamLayout
    {
        public Transform t;
        public Vector3 baseScale;
        public float lengthRatio;   // 빔 뷰 X 길이 ÷ 발사대 뷰 X 폭
        public float dx, dz;        // 빔 중심 − 발사대 중심(뷰 X·Z, m)
        public float heightFrac;    // (빔 중심 y − 발사대 바닥) ÷ 발사대 높이
        public float y;             // 발사대가 없을 때 쓰는 저작 높이(뷰 Y)
    }

    readonly List<BeamLayout> _beams = new List<BeamLayout>();
    Vector3 _postsBaseScale;
    bool _baseCached;
    bool _open;

    public bool IsOpen => _open;
    public Transform Posts => posts;

    /// <summary>빔 줄 수(<c>Beams</c> 아래 메시가 있는 자식 수).</summary>
    public int BeamCount
    {
        get
        {
            CacheBase();
            return _beams.Count;
        }
    }

    public GameObject GetBeam(int i)
    {
        CacheBase();
        return _beams[i].t.gameObject;
    }

    /// <summary>
    /// 폭 <paramref name="width"/>(발사대 바깥 끝)·높이 <paramref name="height"/>·진행 축(<paramref name="alongX"/> = 월드 X)에 맞춘다.
    /// 다시 불러도 된다(프리팹 배치 기준으로 다시 계산). 열린 뒤에 불러도 빔은 꺼진 채다.
    /// </summary>
    public void Fit(float width, float height, bool alongX)
    {
        CacheBase();
        transform.rotation = alongX ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;

        Bounds pb = default;
        bool hasPosts = posts != null && HasMesh(posts);
        if (hasPosts)
        {
            pb = FitPiece(posts, _postsBaseScale, width, keepPostHeight ? 0f : height);
            MoveTo(posts, pb, new Vector3(0f, pb.extents.y, 0f));   // 바닥에 세운다
            pb = MeasureLocal(posts);
        }

        foreach (BeamLayout l in _beams)
        {
            Bounds tb = FitPiece(l.t, l.baseScale, width * (hasPosts ? l.lengthRatio : 1f), 0f);
            Vector3 center = hasPosts
                ? new Vector3(pb.center.x + l.dx, pb.min.y + l.heightFrac * pb.size.y, pb.center.z + l.dz)
                : new Vector3(0f, l.y, 0f);
            MoveTo(l.t, tb, center);
            l.t.gameObject.SetActive(!_open);
        }
    }

    /// <summary>열림 — 빔을 즉시 끈다. 발사대는 남는다.</summary>
    public void Open()
    {
        _open = true;
        CacheBase();
        foreach (BeamLayout l in _beams)
            if (l.t != null) l.t.gameObject.SetActive(false);
    }

    // 첫 맞춤 전 프리팹 배치(저작 상태)를 한 번 기록한다.
    void CacheBase()
    {
        if (_baseCached) return;
        _baseCached = true;
        Bounds pb = default;
        bool hasPosts = posts != null && HasMesh(posts);
        if (hasPosts)
        {
            _postsBaseScale = posts.localScale;
            pb = MeasureLocal(posts);
        }

        if (beamsRoot == null) return;
        foreach (Transform t in beamsRoot)
        {
            if (!HasMesh(t)) continue;
            Bounds b = MeasureLocal(t);
            _beams.Add(new BeamLayout
            {
                t = t,
                baseScale = t.localScale,
                lengthRatio = hasPosts && pb.size.x > 1e-4f ? b.size.x / pb.size.x : 1f,
                dx = hasPosts ? b.center.x - pb.center.x : 0f,
                dz = hasPosts ? b.center.z - pb.center.z : 0f,
                heightFrac = hasPosts && pb.size.y > 1e-4f ? (b.center.y - pb.min.y) / pb.size.y : 0f,
                y = b.center.y,
            });
        }
    }

    static bool HasMesh(Transform t)
    {
        foreach (MeshFilter mf in t.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null) return true;
        return false;
    }

    // 조각의 뷰 X 크기를 width 로, height > 0 이면 뷰 Y 크기를 height 로. 맞춘 뒤의 뷰 공간 bounds 를 돌려준다.
    Bounds FitPiece(Transform t, Vector3 baseScale, float width, float height)
    {
        t.localScale = baseScale;
        Bounds b = MeasureLocal(t);
        Vector3 s = baseScale;
        if (b.size.x > 1e-4f) s = ScaleAxis(t, s, Vector3.right, width / b.size.x);
        if (height > 0f && b.size.y > 1e-4f) s = ScaleAxis(t, s, Vector3.up, height / b.size.y);
        t.localScale = s;
        return MeasureLocal(t);
    }

    // 뷰 축 viewAxis 가 조각 로컬의 어느 축인지 골라 그 성분만 k 배.
    Vector3 ScaleAxis(Transform t, Vector3 s, Vector3 viewAxis, float k)
    {
        Vector3 d = t.InverseTransformDirection(transform.TransformDirection(viewAxis));
        float ax = Mathf.Abs(d.x), ay = Mathf.Abs(d.y), az = Mathf.Abs(d.z);
        if (ax >= ay && ax >= az) s.x *= k;
        else if (ay >= az) s.y *= k;
        else s.z *= k;
        return s;
    }

    // 조각의 bounds 중심(뷰 공간, bounds = 지금 위치 기준)을 center 로 옮긴다.
    void MoveTo(Transform t, Bounds measured, Vector3 center)
    {
        Vector3 delta = transform.TransformVector(center - measured.center);
        t.localPosition += t.parent != null ? t.parent.InverseTransformVector(delta) : delta;
    }

    // 조각 아래 모든 메시의 bounds 를 뷰(이 루트) 로컬 공간으로 모은다. sharedMesh.bounds 라 Read/Write 없이 빌드에서도 된다.
    Bounds MeasureLocal(Transform t)
    {
        bool any = false;
        var result = new Bounds();
        foreach (MeshFilter mf in t.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? mb.min.x : mb.max.x,
                                         (i & 2) == 0 ? mb.min.y : mb.max.y,
                                         (i & 4) == 0 ? mb.min.z : mb.max.z);
                Vector3 p = transform.InverseTransformPoint(mf.transform.TransformPoint(corner));
                if (!any) { result = new Bounds(p, Vector3.zero); any = true; }
                else result.Encapsulate(p);
            }
        }
        return result;
    }
}
