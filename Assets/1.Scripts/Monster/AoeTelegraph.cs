using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;   // DecalProjector

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

    [Header("데칼 모드 — 같은 오브젝트에 DecalProjector 가 있으면 자동으로 그쪽으로 간다")]
    [SerializeField, Min(0.5f)]
    [Tooltip("데칼 투영 깊이(m). 🔴 아레나 프롭(송전기)을 덮을 만큼 커야 한다 — 얇게 잡으면 프롭 " +
             "측면에 안 칠해져서 카메라에서 여전히 프롭이 표식을 가린다. 박스는 오브젝트를 중심으로 " +
             "위아래로 절반씩 퍼지므로 4 면 위로 2m 를 덮는다.")]
    float projectionDepth = 4f;

    Mesh _squareMesh; // 프리팹의 원본(Quad) 메시
    Mesh _circleMesh; // 코드 생성 디스크(지름 1 — Quad와 동일 스케일 수식 호환)
    Coroutine _hideRoutine;

    /// <summary>
    /// 이 인스턴스의 알파만 바꾼다. 같은 프리팹을 **역할별로 두 개** 쓰기 때문에 필요하다 —
    /// 점프 예고는 "큰 원(연하게) + 채워지는 작은 원(진하게)" 두 벌이고, 프리팹은 하나다.
    ///
    /// ⚠️ `material`(인스턴스)을 쓴다. `sharedMaterial` 을 건드리면 **애셋이 오염되고** 두 인스턴스가
    ///    같은 값을 공유해 애초의 목적이 깨진다.
    /// </summary>
    public void SetAlpha(float alpha)
    {
        // 데칼은 재질 색이 아니라 프로젝터의 fadeFactor 로 흐리게 한다 — 인스턴스 재질을 만들지
        // 않아도 되고(오염 위험 0), 같은 프리팹을 역할별로 두 벌 쓰는 목적도 그대로 달성된다.
        if (TryDecal(out DecalProjector decal))
        {
            decal.fadeFactor = Mathf.Clamp01(alpha);
            return;
        }

        if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
        if (_renderer == null) return;

        Material m = _renderer.material;
        if (m == null || !m.HasProperty(BaseColorId)) return;

        Color c = m.GetColor(BaseColorId);
        c.a = Mathf.Clamp01(alpha);
        m.SetColor(BaseColorId, c);
    }

    MeshRenderer _renderer;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    // ─── 데칼 경로 ────────────────────────────────────────────────────
    //
    // 🔴 왜 데칼인가(2026-09-04 팀장 확정): 메시 장판은 바닥에 **띄워야** z-fighting 을 피하는데,
    //    띄운 만큼 탑다운에서 밀려 보이고(시차) 바닥 단차·프롭에 묻힌다. 데칼은 표면에 투영되므로
    //    시차가 0 이고 단차·프롭을 따라 붙는다. 자세한 근거는 PLAN.md 의 데칼 계획.
    //
    // 같은 컴포넌트에 두 경로를 둔 이유 — 프리팹만 갈아끼우면 되돌릴 수 있게 하려고다. 호출처
    // (보스·GauntletBot)는 무엇으로 그려지는지 몰라도 된다.
    DecalProjector _decal;
    bool _decalSearched;

    bool TryDecal(out DecalProjector decal)
    {
        if (!_decalSearched)
        {
            _decalSearched = true;
            TryGetComponent(out _decal);

            if (_decal != null)
            {
                // 수신자 마스크는 여기서 한 번 박는다 — 프리팹 저작 실수로 마스크가 전체(-1)면
                // 캐릭터 몸에도 칠해진다(확정 스펙 위반). 코드가 계약을 지킨다.
                _decal.renderingLayerMask = DecalReceivers.Mask;
                ApplyDecalShape();
            }
        }

        decal = _decal;
        return decal != null;
    }

    /// <summary>
    /// 데칼 모양을 <b>코드로 생성한 텍스처</b>로 넣는다(원형 디스크 + 부드러운 테두리).
    ///
    /// 🔴 텍스처를 아트로 받지 않는 이유 — 아크·원은 수식으로 정확히 나오고, 각도·반경이 런타임
    ///    값(SO)이라 이미지로 고정하면 저작이 코드와 갈린다. 아트 의존을 만들지 않는 쪽을 골랐다.
    /// ⚠️ 재질은 <b>인스턴스</b>로 복제해서 만진다. `DecalProjector.material` 은 프리팹이 물고 있는
    ///    애셋이므로 그대로 쓰면 애셋이 오염된다(메시 경로의 sharedMaterial 주의와 같은 함정).
    /// </summary>
    void ApplyDecalShape()
    {
        if (_decal == null || _decal.material == null) return;

        if (_discTexture == null) _discTexture = BuildDiscTexture(decalColor, 128);

        _decal.material = new Material(_decal.material);
        bool hasBaseMap = _decal.material.HasProperty(BaseMapId);
        if (hasBaseMap)
            _decal.material.SetTexture(BaseMapId, _discTexture);

        // 🔴 침묵은 성공이 아니다. 데칼은 안 보일 때 예외도 경고도 없이 그냥 안 그려지므로,
        //    "이 경로가 돌았고 무엇으로 그리는지"를 한 번 남긴다(인스턴스당 1회).
        Debug.Log($"[AoeTelegraph/데칼] {name} 초기화 — 재질 '{_decal.material.shader?.name}' · " +
                  $"Base_Map {(hasBaseMap ? "설정" : "🔴프로퍼티 없음")} · " +
                  $"mask 0x{(uint)_decal.renderingLayerMask:X} · 깊이 {projectionDepth}m", this);
    }

    [SerializeField]
    [Tooltip("데칼 모드에서 생성할 디스크 색(알파 포함). 메시 경로는 재질의 _BaseColor 를 쓴다.")]
    Color decalColor = new Color(1f, 0f, 0f, 0.35f);

    Texture2D _discTexture;
    static readonly int BaseMapId = Shader.PropertyToID("Base_Map");

    // 지름이 텍스처 폭에 꽉 차는 디스크. 테두리 1px 은 알파를 부드럽게 떨어뜨려 계단을 없앤다.
    static Texture2D BuildDiscTexture(Color color, int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "AoeDecalDisc",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

        float half = size * 0.5f;
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) - half;
                float dy = (y + 0.5f) - half;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / half;   // 0 = 중심, 1 = 테두리

                float a = color.a * Mathf.Clamp01((1f - d) * size * 0.05f);
                pixels[y * size + x] = new Color(color.r, color.g, color.b, a);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(false, false);
        return tex;
    }

    void OnDestroy()
    {
        if (_discTexture != null) Destroy(_discTexture);
    }

    // 반경 반영. 메시는 스케일로, 데칼은 프로젝터 크기로 — 나머지 흐름(Show/Grow/Hide)은 공유한다.
    void ApplyRadius(float radius)
    {
        float diameter = Mathf.Max(0.01f, radius) * 2f;

        if (TryDecal(out DecalProjector decal))
        {
            // ⚠️ size 는 (가로, 세로, 투영 깊이) 다. 깊이를 반경과 함께 키우지 않는다 —
            //    깊이는 "프롭을 덮는 높이"라 반경과 무관한 축이다.
            decal.size = new Vector3(diameter, diameter, projectionDepth);
            return;
        }

        transform.localScale = new Vector3(diameter, diameter, 1f);
    }

    // radius만큼 반영해 표시하고, duration초 후 자동으로 숨긴다.
    public void Show(float radius, float duration)
    {
        ApplyShape();

        ApplyRadius(radius);

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
            ApplyRadius(Mathf.Lerp(from, to, Mathf.Clamp01(t / dur)));
            yield return null;
        }

        ApplyRadius(to);

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
