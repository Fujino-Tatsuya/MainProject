#if UNITY_EDITOR || DEVELOPMENT_BUILD || DEV_HOTKEYS
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 할 일 221 바닥 블렌딩 확인 씬(DebugDecalFloorBlendLab — 빌드 목록에 없다)의 구동기.
/// ▶ 하면 밝은·어두운·무늬 바닥 3장 위에 같은 세트를 나란히 놓는다:
///   플레이어 = 사거리 원·대상 원(투영 데칼) · 거너 Q 직선·대시 화살표(SkillLineIndicator 판) · 조준 표시(URP Lit 판)
///   예고     = C3 부채꼴·경로 띠(외곽선 + 채움, 투영 데칼) · 원판(23호 차징 오라 모양) · C3 원 판(드론)·사각 판(전기 장판)
/// 값은 <c>Resources/Rendering/DecalFloorBlendSettings</c> 를 Play 중 고치면 바로 보인다(<see cref="DecalFloorBlendDriver"/>).
/// 키: B = 누르는 동안 블렌딩 끔(221 이전 화면과 비교). 씬은 <c>Tools/Debug/221 바닥 블렌딩 확인 씬 만들기</c> 로 다시 만든다.
/// 표시 전용 — 네트워크·게임플레이 없음. 만든 재질 인스턴스는 OnDestroy 에서 지운다.
/// </summary>
[DisallowMultipleComponent]
public sealed class DebugDecalFloorBlendLab : MonoBehaviour
{
    [Header("바닥 3종 (비우면 URP Lit 단색)")]
    [SerializeField] private Material brightFloor;
    [SerializeField] private Material darkFloor;
    [Tooltip("무늬 바닥 — 맵 바닥 재질(Generic_01_A, SVN).")]
    [SerializeField] private Material patternFloor;

    [Header("플레이어 인디케이터 재질")]
    [SerializeField] private Material skillRange;
    [SerializeField] private Material targetCircle;
    [SerializeField] private Material skillLine;
    [SerializeField] private Material skillArrow;
    [SerializeField] private Material aimQuad;
    [SerializeField] private Texture aimTexture;

    [Header("배치")]
    [SerializeField] private float floorSpacing = 16f;

    static readonly int WorldSizeId = Shader.PropertyToID("_WorldSize");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    const float DecalDepth = 3f;

    readonly List<Object> _owned = new List<Object>();
    readonly List<Material> _aimMaterials = new List<Material>();
    Color _aimBase = Color.white;

    void Start()
    {
        string[] names = { "밝은 바닥", "어두운 바닥", "무늬 바닥" };
        Material[] floors = { FloorOr(brightFloor, new Color(0.85f, 0.85f, 0.82f)), FloorOr(darkFloor, new Color(0.12f, 0.12f, 0.14f)), FloorOr(patternFloor, new Color(0.5f, 0.45f, 0.4f)) };
        for (int i = 0; i < floors.Length; i++)
            BuildRow(names[i], floors[i], new Vector3((i - 1) * floorSpacing, 0f, 0f));
    }

    Material FloorOr(Material m, Color color)
    {
        if (m != null) return m;
        var lit = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "LabFloor" };
        lit.SetColor(BaseColorId, color);
        _owned.Add(lit);
        return lit;
    }

    void BuildRow(string label, Material floorMat, Vector3 origin)
    {
        var row = new GameObject(label).transform;
        row.SetParent(transform, false);
        row.localPosition = origin;

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.SetParent(row, false);
        floor.transform.localScale = new Vector3(1.4f, 1f, 2.6f);   // 14 × 26 m
        floor.GetComponent<Renderer>().sharedMaterial = floorMat;
        DecalReceivers.Tag(floor);

        // ── 플레이어(위쪽 절반, +z)
        Decal(row, "SkillRange", skillRange, new Vector3(-3f, 0f, 8f), 6f, 6f);
        Decal(row, "TargetCircle", targetCircle, new Vector3(3f, 0f, 8f), 3f, 3f);
        LineQuad(row, "SkillLine", skillLine, new Vector3(2.5f, 0.05f, 1.5f), 1.6f, 6f);
        LineQuad(row, "DashArrow", skillArrow, new Vector3(4.5f, 0.05f, 2.5f), 0.9f, 4f);
        AimQuad(row, new Vector3(-3f, 0.08f, 3f));

        // ── 예고(아래쪽 절반, -z)
        Material decal = EnemyC3Visuals.DecalMaterial;
        if (decal == null)
        {
            Debug.LogError("[DecalFloorBlendLab] C3 데칼 재질(Resources/Telegraph/MA_EnemyC3Decal)이 없다.");
            return;
        }
        const float coneR = 3f, coneHalf = 45f;
        var coneExtent = new Vector2(coneR * 2f, coneR * 2f);
        float conePad = EnemyC3Visuals.DecalPadding("03", coneExtent, coneR, 0f, 0f);
        Material coneOutline = C3Instance(decal, Color.white);
        EnemyC3Visuals.SetDecalProperties(coneOutline, "03", coneExtent, coneR, 0f, coneHalf, 0f, 0f);
        Material coneFill = C3Instance(decal, EnemyC3Visuals.FillColor("03"));
        EnemyC3Visuals.SetArcFill(coneFill, coneHalf, 0f);
        EnemyC3Visuals.SetDistanceFade(coneFill, "03");
        Decal(row, "ConeOutline", coneOutline, new Vector3(-3.5f, 0f, -3f), (coneR + conePad) * 2f, (coneR + conePad) * 2f);
        Decal(row, "ConeFill", coneFill, new Vector3(-3.5f, 0f, -3f), coneR * 2f, coneR * 2f);

        const float bandW = 2f, bandL = 7f;
        var bandExtent = new Vector2(bandW, bandL);
        float bandPad = EnemyC3Visuals.DecalPadding("04", bandExtent, 0f, bandW, bandL);
        Material bandOutline = C3Instance(decal, Color.white);
        EnemyC3Visuals.SetDecalProperties(bandOutline, "04", bandExtent, 0f, 0f, 0f, bandW, bandL);
        Material bandFill = C3Instance(decal, EnemyC3Visuals.FillColor("04"));
        EnemyC3Visuals.SetRectFill(bandFill);
        EnemyC3Visuals.SetDistanceFade(bandFill, "04");
        Decal(row, "BandOutline", bandOutline, new Vector3(3.5f, 0f, -4f), bandW + bandPad * 2f, bandL + bandPad * 2f);
        Decal(row, "BandFill", bandFill, new Vector3(3.5f, 0f, -4f), bandW, bandL);

        // 원판(AoeTelegraph 코드 원판 = 23호 차징 오라 모양 — 채움 0.45·테두리 0.15)
        Material disc = C3Instance(decal, new Color(1f, 0.25f, 0.2f, 0.35f));
        EnemyC3Visuals.SetDiscFill(disc, 0.45f, 0.15f);
        Decal(row, "AuraDisc", disc, new Vector3(0f, 0f, -9f), 4f, 4f);

        // C3 바닥 판 — 드론 원(12)·전기 장판 사각(15)
        Plate(row, "DronePlate", "12", false, new Vector3(-4f, 0.07f, -10f), 3f);
        Plate(row, "FloorTile", "15", true, new Vector3(4f, 0.07f, -10f), 2.5f);
    }

    Material C3Instance(Material shape, Color color)
    {
        Material m = EnemyC3Visuals.NewDecalInstance(shape, color);
        _owned.Add(m);
        return m;
    }

    void Decal(Transform parent, string name, Material mat, Vector3 localPos, float sizeX, float sizeY)
    {
        if (mat == null) return;
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos + Vector3.up * 1f;
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var d = go.AddComponent<DecalProjector>();
        d.material = mat;
        d.size = new Vector3(sizeX, sizeY, DecalDepth);
        d.pivot = new Vector3(0f, 0f, DecalDepth * 0.5f);
        DecalReceivers.ConfigureFloorProjector(d);
    }

    void LineQuad(Transform parent, string name, Material mat, Vector3 localPos, float width, float length)
    {
        if (mat == null) return;
        GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = name;
        Destroy(q.GetComponent<Collider>());
        q.transform.SetParent(parent, false);
        // 쿼드 uv.y 0 = 시전자 쪽 — 중심을 길이 절반만큼 앞으로
        q.transform.localPosition = localPos + Vector3.forward * (length * 0.5f);
        q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        q.transform.localScale = new Vector3(width, length, 1f);
        var r = q.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        var block = new MaterialPropertyBlock();
        block.SetVector(WorldSizeId, new Vector4(width, length, 0f, 0f));
        r.SetPropertyBlock(block);
    }

    void AimQuad(Transform parent, Vector3 localPos)
    {
        if (aimQuad == null) return;
        GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = "AimIndicator";
        Destroy(q.GetComponent<Collider>());
        q.transform.SetParent(parent, false);
        q.transform.localPosition = localPos;
        q.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        q.transform.localScale = new Vector3(1.2f, 1.2f, 1f);
        var m = new Material(aimQuad) { name = "AimIndicator3D (Lab)" };
        if (aimTexture != null && m.HasProperty(BaseMapId)) m.SetTexture(BaseMapId, aimTexture);
        _aimBase = m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId) : Color.white;
        q.GetComponent<MeshRenderer>().sharedMaterial = m;
        _owned.Add(m);
        _aimMaterials.Add(m);
    }

    void Plate(Transform parent, string name, string id, bool square, Vector3 localPos, float half)
    {
        var root = new GameObject(name).transform;
        root.SetParent(parent, false);
        root.localPosition = localPos;
        MeshRenderer outer = BossPatternVisuals.CreateFlat("Boundary", root);
        MeshRenderer fill = BossPatternVisuals.CreateFlat("Fill", root);
        EnemyC3Visuals.PaintPlate(outer, fill, id, square);
        float outerSize = half * 2f * EnemyC3Visuals.OuterScale(id, square);
        outer.transform.localScale = new Vector3(outerSize, 1f, outerSize);
        fill.transform.localPosition = Vector3.up * 0.01f;
        fill.transform.localScale = new Vector3(half * 2f, 1f, half * 2f);
        outer.gameObject.SetActive(true);
        fill.gameObject.SetActive(true);
    }

    void Update()
    {
        DecalFloorBlendDriver.Bypass = Keyboard.current != null && Keyboard.current.bKey.isPressed;
        // 조준 표시는 URP Lit 이라 PlayerAimIndicator 와 같은 식으로 알파만 곱한다.
        float aimAlpha = DecalFloorBlendDriver.Bypass ? 1f : DecalFloorBlend.PlateFill(Shader.GetGlobalVector(DecalFloorBlend.SkillGlobalId));
        foreach (Material m in _aimMaterials)
            m.SetColor(BaseColorId, PlayerAimIndicator.AimIndicatorColor(_aimBase, Color.white, aimAlpha));
    }

    void OnGUI()
    {
        Vector4 s = Shader.GetGlobalVector(DecalFloorBlend.SkillGlobalId);
        Vector4 t = Shader.GetGlobalVector(DecalFloorBlend.TelegraphGlobalId);
        GUI.Label(new Rect(12f, 12f, 900f, 80f),
            "221 바닥 블렌딩 — B 누르는 동안 끔(221 이전)\n" +
            $"스킬 채움 {s.x:0.00} · 소프트 {s.y:0.00} · 판 {s.z:0.00} (유효 {s.w:0})   예고 채움 {t.x:0.00} · 소프트 {t.y:0.00} · 판 {t.z:0.00} (유효 {t.w:0})\n" +
            "값: Resources/Rendering/DecalFloorBlendSettings — Play 중 바로 반영");
    }

    void OnDestroy()
    {
        DecalFloorBlendDriver.Bypass = false;
        foreach (Object o in _owned)
            if (o != null) Destroy(o);
        _owned.Clear();
    }
}
#endif
