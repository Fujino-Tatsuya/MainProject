using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 맵씬 복도 게이트의 레이저 시각(할 일 160, 비네트워크). <c>MapGateLaser.prefab</c> 루트에 붙는다.
/// 원본: Docs/tech/tutorial-director.md §7.
///
/// · <see cref="MapSceneBarriers"/> 가 Barrier 콜라이더(큐브) 옆에 형제로 만들고 <see cref="Fit"/> 으로 게이트 크기에 맞춘다.
///   막기·카브는 큐브 쪽 그대로 — 이 프리팹엔 콜라이더가 없다.
/// · 뷰 좌표 = 로컬 X 가 게이트 폭(가로), Y 가 위, Z 가 진행 방향. 원점 = 바닥 가운데.
/// · 크기는 메시 bounds 로 잰다(FBX 의 긴 축·피벗을 하드코딩하지 않음). 조각(발사대·빔 템플릿)의 루트는 뷰 축과 90° 단위로만 돌려 둘 것.
/// · 열림 = 빔만 즉시 끈다(연출 없음). 발사대는 장식으로 남는다. 다시 닫히지 않는다.
/// · 이펙트 담당은 <c>Visual</c> 아래만 바꾸면 된다 — 칸(발사대·빔 템플릿)만 다시 연결.
/// </summary>
[DisallowMultipleComponent]
public sealed class MapGateLaserView : MonoBehaviour
{
    [Tooltip("발사대 몸체(Env_laser). 이 FBX 는 메시 하나에 양 끝 발사대 둘이 다 들어 있다 — 긴 축을 게이트 폭에 맞춰 늘인다.")]
    [SerializeField] private Transform posts;

    [Tooltip("빔 줄 템플릿(Env_laser_lay). 줄 개수만큼 같은 부모 아래에 복제하고, 긴 축 = 게이트 폭.")]
    [SerializeField] private Transform beamTemplate;

    [Tooltip("빔 줄 사이 높이(m) — 연출 전용.")]
    [DataTableIgnore]
    [SerializeField, Min(0.05f)] private float beamSpacing = 0.25f;

    [Tooltip("빔 줄 개수 상한 — 연출 전용.")]
    [DataTableIgnore]
    [SerializeField, Min(1)] private int maxBeams = 8;

    [Tooltip("맨 아래·맨 위 줄과 바닥·꼭대기 사이 최소 여유(m) — 연출 전용.")]
    [DataTableIgnore]
    [SerializeField, Min(0f)] private float beamMargin = 0.3f;

    [Tooltip("켜면 발사대는 FBX 높이 그대로 두고 빔은 그 높이 안에 깐다. 끄면 발사대를 게이트 높이로 늘린다.")]
    [DataTableIgnore]
    [SerializeField] private bool keepPostHeight = true;

    readonly List<GameObject> _clones = new List<GameObject>();
    Vector3 _postsBaseScale, _beamBaseScale;
    bool _baseCached;
    bool _open;

    public bool IsOpen => _open;
    public Transform Posts => posts;

    /// <summary>빔 줄 수(템플릿 포함). 템플릿이 없으면 0.</summary>
    public int BeamCount => beamTemplate != null ? 1 + _clones.Count : 0;

    public GameObject GetBeam(int i) => i == 0 ? beamTemplate.gameObject : _clones[i - 1];

    /// <summary>
    /// 폭 <paramref name="width"/>(빔 줄 길이 = 발사대 사이 바깥 끝)·높이 <paramref name="height"/>·진행 축(<paramref name="alongX"/> = 월드 X)에 맞춘다.
    /// 다시 불러도 된다(복제 줄은 지우고 새로). 열린 뒤에 불러도 빔은 꺼진 채다.
    /// </summary>
    public void Fit(float width, float height, bool alongX)
    {
        CacheBaseScales();
        transform.rotation = alongX ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;

        float visualHeight = height;
        if (posts != null)
        {
            Bounds b = FitPiece(posts, _postsBaseScale, width, keepPostHeight ? 0f : height);
            MoveTo(posts, b, new Vector3(0f, b.extents.y, 0f));   // 바닥에 세운다
            if (keepPostHeight) visualHeight = b.size.y;
        }

        ClearClones();
        if (beamTemplate == null) return;

        Bounds tb = FitPiece(beamTemplate, _beamBaseScale, width, 0f);
        int n = BeamRows(visualHeight, beamSpacing, beamMargin, maxBeams);
        float y0 = (visualHeight - (n - 1) * beamSpacing) * 0.5f;
        Vector3 templatePos = beamTemplate.localPosition;
        for (int i = 0; i < n; i++)
        {
            Transform t = i == 0 ? beamTemplate : Instantiate(beamTemplate.gameObject, beamTemplate.parent).transform;
            if (i > 0)
            {
                t.name = $"{beamTemplate.name}_{i}";
                t.gameObject.hideFlags = beamTemplate.gameObject.hideFlags;
                _clones.Add(t.gameObject);
            }
            t.localPosition = templatePos;
            MoveTo(t, tb, new Vector3(0f, y0 + i * beamSpacing, 0f));
            t.gameObject.SetActive(!_open);
        }
    }

    /// <summary>열림 — 빔을 즉시 끈다. 발사대는 남는다.</summary>
    public void Open()
    {
        _open = true;
        if (beamTemplate != null) beamTemplate.gameObject.SetActive(false);
        foreach (GameObject c in _clones)
            if (c != null) c.SetActive(false);
    }

    /// <summary>높이 <paramref name="height"/> 안에 간격 <paramref name="spacing"/> 으로 들어가는 줄 수(위아래 여유 제외, 1 ~ 상한).</summary>
    public static int BeamRows(float height, float spacing, float margin, int max)
    {
        int n = Mathf.FloorToInt((height - 2f * margin) / Mathf.Max(spacing, 0.01f)) + 1;
        return Mathf.Clamp(n, 1, Mathf.Max(1, max));
    }

    void CacheBaseScales()
    {
        if (_baseCached) return;
        _baseCached = true;
        if (posts != null) _postsBaseScale = posts.localScale;
        if (beamTemplate != null) _beamBaseScale = beamTemplate.localScale;
    }

    void ClearClones()
    {
        foreach (GameObject c in _clones)
        {
            if (c == null) continue;
            if (Application.isPlaying) Destroy(c);
            else DestroyImmediate(c);
        }
        _clones.Clear();
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
