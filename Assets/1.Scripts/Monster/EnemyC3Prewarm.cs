using System;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

/// <summary>
/// 할 일 57 R 예열(사용자 결정 10-20) — 로딩 화면이 떠 있는 동안 C3 예고의 첫 호출 비용을 미리 치른다.
/// 진입점은 <see cref="EnemyC3Visuals.Prewarm"/> 하나(1회 플래그, Play 마다 ResetForPlay 로 초기화)이고,
/// 호출 위치는 <c>NetworkLoadingFlowController.HandleLoadStarted</c>(타깃 씬 로드 시작 — 호스트·클라 모두) 한 줄이다.
/// 세 단계:
///   (a) 데이터 — JSON(Get), 판·데칼 재질(Resources), 판 층별 MaterialPropertyBlock, 스매시 09 sprite.
///   (b) 셰이더 — EnemyC3Plate·SG_EnemyC3Decal·TelegraphQuad 의 변형을 코드로 만든 ShaderVariantCollection 으로 WarmUp.
///       에셋이 아니라 코드 SVC 다 — 세 셰이더는 이미 Resources 재질(MA_EnemyC3Plate·MA_EnemyC3Decal·MA_TelegraphQuad)로 빌드에
///       들어가고, 그래픽 설정(Preloaded Shaders)·새 에셋을 건드리지 않는다. 데칼 키워드 = PC_Renderer 데칼 설정이 켜는 조합
///       (아래 DecalKeywords). 이 플랫폼에 없는 변형은 건너뛴다.
///       🔴 에디터는 처음 쓰는 변형을 여기서 동기 컴파일한다(셰이더 캐시가 빌 때 1회 ~10 s) — 빌드는 미리 컴파일돼 있다.
///       🔴 DX12·Vulkan·Metal 에서 WarmUp 은 셰이더 프로그램까지만 준비한다 — 파이프라인 상태(PSO)는 첫 그리기에서 만든다(남는 비용).
///   (c) JIT — 화면 밖 더미에서 C3 칠하기 경로(NewDecalInstance·SetDecalProperties·채움 Set*·PaintPlate/PaintLayer/PaintActive·
///       CreateJump/Begin·BossPatternVisuals.Paint(CodeShape)·ApplySmash)를 한 번씩 돌리고 즉시 지운다(재질 인스턴스 포함).
///       보스 컴포넌트(BossAttackConeTelegraph·WellsDroneAttack·BossDirectionIndicator)는 세우지 않는다 — 그쪽 첫 생성 비용
///       (프로젝터 6개 EnsureBuilt 등)은 남는 비용으로 둔다.
/// 런타임 텍스처를 만들지 않는다(EnemyC3RuntimeTextureGuardTests). 결과 시간은 <see cref="Last"/> 와 로그 한 줄.
/// </summary>
static class EnemyC3Prewarm
{
    /// <summary>마지막 실행의 단계별 시간(ms)과 WarmUp 한 변형 수 — 측정 메뉴(EnemyC3TelegraphPerfProbe)가 읽는다.</summary>
    internal struct Timing { public double data, shaders, paths, total; public int variants; }
    internal static Timing Last;

    // 판 층(id, 사각, 채움) — 호출부가 실제로 칠하는 조합(스카이 12 · 드론 14 · 전기 장판 15·16 · 점프 06·07).
    static readonly (string id, bool square, bool fill)[] PlateLayers =
    {
        ("12", false, false), ("12", false, true), ("14", false, false), ("14", false, true),
        ("15", true, false), ("15", true, true), ("16", true, false),
        ("06", false, false), ("06", false, true), ("07", false, true),
    };

    // SG_EnemyC3Decal 키워드 조합(PassType.ScriptableRenderPipeline — DBufferProjector 등 LightMode 패스 전부가 이 종류다).
    // = PC_Renderer 의 Decal 기능 설정: technique Automatic(Windows 데스크톱 → DBuffer) · surfaceData AlbedoNormalMAOS(_DBUFFER_MRT3)
    //   · decalLayers 켜짐(_DECAL_LAYERS). ScreenSpace 대체 경로(normalBlend Low)는 조명 키워드 조합까지는 못 덮는다(남는 비용).
    //   🔴 렌더러 데칼 설정을 바꾸면 이 목록도 바꾼다(handoff-enemy-indicator §9.4).
    static readonly string[][] DecalKeywords =
    {
        new[] { "_DBUFFER_MRT3", "_DECAL_LAYERS" }, new[] { "_DBUFFER_MRT3" },
        new[] { "_DECAL_NORMAL_BLEND_LOW", "_DECAL_LAYERS" }, new[] { "_DECAL_NORMAL_BLEND_LOW" },
    };

    const string TelegraphDecalPath = "Telegraph/MA_TelegraphDecal";   // BossAttackConeTelegraph 의 예고 재질(그리기 순서 출처)
    const string TelegraphQuadPath = "Telegraph/MA_TelegraphQuad";     // BossPatternVisuals 판 재질

    internal static void Run()
    {
        var sw = Stopwatch.StartNew();
        var t = new Timing();

        WarmData();
        t.data = Lap(sw);
        t.variants = WarmShaders();
        t.shaders = Lap(sw);
        WarmPaths();
        t.paths = Lap(sw);
        t.total = t.data + t.shaders + t.paths;
        Last = t;
        Debug.Log($"[Enemy C3] Prewarm {t.total:F1} ms (data {t.data:F1} · shaders {t.shaders:F1}, {t.variants} variants · paths {t.paths:F1})");
    }

    static double Lap(Stopwatch sw)
    {
        double ms = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        return ms;
    }

    // (a) 데이터 — 정적 캐시를 채운다(게임이 그대로 쓴다).
    static void WarmData()
    {
        EnemyC3Visuals.Get("01");                       // JSON 전부 로드
        _ = EnemyC3Visuals.PlateMaterial;
        _ = EnemyC3Visuals.DecalMaterial;
        _ = EnemyC3Visuals.SmashSprite;
        var probe = new MaterialPropertyBlock();
        foreach (var (id, square, fill) in PlateLayers) EnemyC3Visuals.SetPlateProperties(probe, id, square, fill);
    }

    // (b) 셰이더 변형 — 코드 SVC 로 WarmUp 한 뒤 SVC 는 지운다(준비된 GPU 프로그램은 남는다).
    static int WarmShaders()
    {
        var svc = new ShaderVariantCollection { name = "EnemyC3Prewarm" };
        try
        {
            Material plate = EnemyC3Visuals.PlateMaterial, decal = EnemyC3Visuals.DecalMaterial;
            Material quad = Resources.Load<Material>(TelegraphQuadPath);
            // 판·조준 셰이더는 LightMode 없는 패스 하나 — SVC 에서는 PassType.Normal 이다.
            Add(svc, plate != null ? plate.shader : null, PassType.Normal, new string[0]);
            Add(svc, quad != null ? quad.shader : null, PassType.Normal, new string[0]);
            foreach (var kw in DecalKeywords) Add(svc, decal != null ? decal.shader : null, PassType.ScriptableRenderPipeline, kw);
            svc.WarmUp();
            return svc.variantCount;
        }
        finally { Object.DestroyImmediate(svc); }
    }

    static void Add(ShaderVariantCollection svc, Shader shader, PassType pass, string[] keywords)
    {
        if (shader == null) return;
        try { svc.Add(new ShaderVariantCollection.ShaderVariant(shader, pass, keywords)); }
        catch (ArgumentException) { }   // 이 플랫폼·빌드에 없는(스트립된) 변형
    }

    // (c) JIT — 화면 밖 더미에서 한 번씩 부르고 바로 지운다. 공유 캐시(BossPatternVisuals 판 재질·쿼드)만 남는다.
    static void WarmPaths()
    {
        var root = new GameObject("EnemyC3_Prewarm") { hideFlags = HideFlags.HideAndDontSave };
        root.transform.position = new Vector3(0f, -10000f, 0f);
        GameObject jump = null;
        Material decalInstance = null;
        try
        {
            // 데칼 — 인스턴스 생성 + 외곽선(부채꼴·띠·대시 띠·합집합) + 채움(부채꼴·띠·합집합·원판) 값 넣기 + 프로젝터 켜고 끄기.
            decalInstance = EnemyC3Visuals.NewDecalInstance(Resources.Load<Material>(TelegraphDecalPath), Color.clear);
            if (decalInstance != null)
            {
                var go = new GameObject("Decal") { hideFlags = HideFlags.HideAndDontSave };
                go.transform.SetParent(root.transform, false);
                go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var decal = go.AddComponent<DecalProjector>();
                decal.material = decalInstance;
                DecalReceivers.ConfigureFloorProjector(decal);
                decal.fadeFactor = 1f;
                decal.size = new Vector3(1f, 1f, 1f);
                Vector2 cone = new Vector2(8f, 8f), band = new Vector2(2f, 10f), union = new Vector2(5f, 20.5f);
                EnemyC3Visuals.SetDecalProperties(decalInstance, "03", cone, 4f, 0f, 60f, 0f, 0f);
                EnemyC3Visuals.SetDecalProperties(decalInstance, "01", band, 0f, 0f, 0f, 2f, 10f);
                EnemyC3Visuals.SetDecalProperties(decalInstance, "04", band, 0f, 0f, 0f, 2f, 10f);
                EnemyC3Visuals.SetDecalProperties(decalInstance, "02", union, 2.5f, 18f, 180f, 2f, 18f);
                _ = EnemyC3Visuals.DecalPadding("02", union, 2.5f, 2f, 18f);
                _ = EnemyC3Visuals.FillColor("02");
                EnemyC3Visuals.SetArcFill(decalInstance, 60f, 0f);
                EnemyC3Visuals.SetArcFill(decalInstance, 30f, 0.6f, isBack: true);   // 방향 표식 뒤 호
                EnemyC3Visuals.SetRectFill(decalInstance);
                EnemyC3Visuals.SetUnionFill(decalInstance, union, 1f, 18f, 18f, 2.5f, 10f);
                EnemyC3Visuals.SetDiscFill(decalInstance, 0.35f, 0.12f);
                decal.enabled = false;
                decal.enabled = true;
            }

            // 바닥 판 — PaintPlate·PaintLayer·PaintActive·OuterScale(MPB 캐시를 채운다).
            MeshRenderer outer = BossPatternVisuals.CreateFlat("Outer", root.transform);
            MeshRenderer fill = BossPatternVisuals.CreateFlat("Fill", root.transform);
            EnemyC3Visuals.PaintPlate(outer, fill, "14");
            EnemyC3Visuals.PaintPlate(outer, fill, "12");
            EnemyC3Visuals.PaintPlate(outer, fill, "15", true);
            EnemyC3Visuals.PaintActive(outer, 0.5f);
            EnemyC3Visuals.PaintPlate(outer, fill, "06");
            EnemyC3Visuals.PaintLayer(fill, "07", false, true);
            _ = EnemyC3Visuals.OuterScale("14") + EnemyC3Visuals.OuterScale("15", true);

            // 판·조준 코드 모양(TelegraphQuad _CodeShape).
            MeshRenderer flat = BossPatternVisuals.CreateFlat("Flat", root.transform);
            BossPatternVisuals.Paint(flat, Color.clear, BossPatternVisuals.CodeShape.Square, 0.1f);
            BossPatternVisuals.Paint(flat, Color.clear, BossPatternVisuals.CodeShape.Disc);
            BossPatternVisuals.Paint(flat, Color.clear, BossPatternVisuals.CodeShape.Crosshair);

            // 점프 06·07 — 게임처럼 새 오브젝트를 만든다.
            jump = EnemyC3Visuals.CreateJump(root.transform.position, 1f, 1f);
            jump.hideFlags = HideFlags.HideAndDontSave;

            // 스매시 09 — sprite 교체(이미 같은 sprite 면 그대로).
            var smash = new GameObject("Smash") { hideFlags = HideFlags.HideAndDontSave };
            smash.transform.SetParent(root.transform, false);
            var sr = smash.AddComponent<SpriteRenderer>();
            sr.sprite = EnemyC3Visuals.SmashSprite;
            if (sr.sprite != null) EnemyC3Visuals.ApplySmash(smash);
        }
        finally
        {
            // 즉시 지운다 — 더미는 한 프레임도 그려지지 않는다(셰이더 준비는 위 WarmUp 몫).
            if (jump != null) Object.DestroyImmediate(jump);
            Object.DestroyImmediate(root);
            if (decalInstance != null) Object.DestroyImmediate(decalInstance);
        }
    }
}
