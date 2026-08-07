using System.Collections;
using UnityEngine;

// 장판 모양. Circle=원형 디스크(코드 생성 메시), Square=사각(프리팹의 Quad 메시).
public enum AoeTelegraphShape
{
    Circle,
    Square,
}

// 범위 공격(AoE) 텔레그래프(장판) 비주얼.
// 순수 로컬 MonoBehaviour — NetworkBehaviour 아님. GauntletBot 등이 서버에서 계산한
// 반경/지속시간을 ClientRpc로 각 피어에 전달하면, 피어별로 이 컴포넌트를 로컬 재생한다
// (복제할 상태가 없으므로 연출 전용 컴포넌트로 충분).
//
// Quad(기본 1x1, 로컬 XY 평면) 자식으로 부착해 쓰는 것을 전제로 한다.
// 부모(또는 자신)가 X축 90도 회전돼 있으면 로컬 X/Y 스케일이 월드 X/Z 반경으로 매핑된다.
[DisallowMultipleComponent]
public class AoeTelegraph : MonoBehaviour
{
    [SerializeField]
    [Tooltip("장판 모양. Circle=원형 디스크(코드 생성), Square=사각(Quad). 플레이 중 바꿔도 다음 표시부터 반영.")]
    AoeTelegraphShape shape = AoeTelegraphShape.Circle;
    [SerializeField, Range(8, 96)]
    [Tooltip("원형 디스크 세그먼트 수(부드러움).")]
    int circleSegments = 48;
    [SerializeField]
    [Tooltip("비주얼 MeshFilter(비우면 자기 자신에서 탐색).")]
    MeshFilter meshFilter;

    Mesh _squareMesh; // 프리팹의 원본(Quad) 메시
    Mesh _circleMesh; // 코드 생성 디스크(지름 1 — Quad와 동일 스케일 수식 호환)
    Coroutine _hideRoutine;

    // radius만큼 반영해 표시하고, duration초 후 자동으로 숨긴다.
    public void Show(float radius, float duration)
    {
        ApplyShape();

        float diameter = Mathf.Max(0.01f, radius) * 2f;
        transform.localScale = new Vector3(diameter, diameter, 1f);

        gameObject.SetActive(true);

        if (_hideRoutine != null)
            StopCoroutine(_hideRoutine);
        _hideRoutine = StartCoroutine(HideAfter(duration));
    }

    // 반경을 fromRadius → toRadius로 growTime초 동안 점증시키고, 그 뒤 holdAfter초 유지 후 숨긴다.
    //
    // 정본(boss-fsm-detailed-spec.md §10.5.2)이 말하는 **"시간 성장 인디케이터"** 가 이것이다 —
    // JumpAttack 의 빨간 장판2가 이 경로다. ⚠️ 이건 예고 표시이지 지속 영역(AreaZone)이 아니다.
    // 두 축을 섞지 말 것: 여기는 "언제 떨어지는가"를 보여 주고, AreaZone 은 실제 피해를 준다.
    public void ShowGrowing(float fromRadius, float toRadius, float growTime, float holdAfter)
    {
        ApplyShape();
        gameObject.SetActive(true);

        if (_hideRoutine != null)
            StopCoroutine(_hideRoutine);
        _hideRoutine = StartCoroutine(GrowRoutine(fromRadius, toRadius, growTime, holdAfter));
    }

    IEnumerator GrowRoutine(float fromRadius, float toRadius, float growTime, float holdAfter)
    {
        float from = Mathf.Max(0.01f, fromRadius);
        float to = Mathf.Max(from, toRadius);
        float dur = Mathf.Max(0.01f, growTime);

        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float r = Mathf.Lerp(from, to, Mathf.Clamp01(t / dur));
            float d = r * 2f;
            transform.localScale = new Vector3(d, d, 1f);
            yield return null;
        }

        float full = to * 2f;
        transform.localScale = new Vector3(full, full, 1f);

        if (holdAfter > 0f)
            yield return new WaitForSeconds(holdAfter);

        _hideRoutine = null;
        gameObject.SetActive(false);
    }

    // 선택된 모양의 메시를 MeshFilter에 반영(지연 초기화 — 오브젝트가 비활성 시작이라 Awake 의존 금지).
    void ApplyShape()
    {
        if (meshFilter == null) meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null) return;

        if (_squareMesh == null) _squareMesh = meshFilter.sharedMesh; // 프리팹의 Quad 보존
        if (shape == AoeTelegraphShape.Circle && _circleMesh == null)
            _circleMesh = BuildDiscMesh(Mathf.Clamp(circleSegments, 8, 96));

        Mesh target = shape == AoeTelegraphShape.Circle && _circleMesh != null ? _circleMesh : _squareMesh;
        if (target != null && meshFilter.sharedMesh != target)
            meshFilter.sharedMesh = target;
    }

    // XY 평면 지름 1짜리 디스크(법선 -Z) — Unity Quad와 같은 좌표계/윈딩이라 동일 회전(X=90)·스케일로 동작.
    static Mesh BuildDiscMesh(int segments)
    {
        var verts = new Vector3[segments + 1];
        var uvs = new Vector2[segments + 1];
        var normals = new Vector3[segments + 1];
        var tris = new int[segments * 3];

        verts[0] = Vector3.zero;
        uvs[0] = new Vector2(0.5f, 0.5f);
        normals[0] = Vector3.back;
        for (int i = 0; i < segments; i++)
        {
            float ang = i / (float)segments * Mathf.PI * 2f;
            Vector3 p = new Vector3(Mathf.Cos(ang) * 0.5f, Mathf.Sin(ang) * 0.5f, 0f);
            verts[i + 1] = p;
            uvs[i + 1] = new Vector2(p.x + 0.5f, p.y + 0.5f);
            normals[i + 1] = Vector3.back;

            int next = (i + 1) % segments;
            // Quad와 동일하게 -Z가 앞면이 되도록 시계방향 와인딩(center → next → current).
            // (반대로 감으면 X=90 회전 후 앞면이 바닥을 향해 위에서 안 보인다 — 실측 버그 수정.)
            tris[i * 3 + 0] = 0;
            tris[i * 3 + 1] = next + 1;
            tris[i * 3 + 2] = i + 1;
        }

        var mesh = new Mesh { name = "AoeTelegraphDisc" };
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.normals = normals;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }

    // 즉시 숨김(자동 소멸 대기 없이).
    public void Hide()
    {
        if (_hideRoutine != null)
        {
            StopCoroutine(_hideRoutine);
            _hideRoutine = null;
        }
        gameObject.SetActive(false);
    }

    IEnumerator HideAfter(float duration)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, duration));
        _hideRoutine = null;
        gameObject.SetActive(false);
    }
}
