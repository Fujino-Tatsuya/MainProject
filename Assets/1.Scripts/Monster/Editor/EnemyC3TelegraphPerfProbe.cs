using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>
/// 할 일 57 S5 — 셰이더 예고 경로의 CPU 비용 측정(에디터, EditMode). Play 진입 없음.
/// 각 경로를 <b>콜드</b>(정적 캐시 초기화 + 새 오브젝트 — JSON 파싱·Resources.Load·재질 인스턴스 생성 포함)와
/// <b>예열 후</b>(같은 오브젝트에 2회 부른 뒤)로 나눠 1회·10회 평균의 시간(Stopwatch)과 관리 힙 증가(GC.GetTotalMemory — 아래 측정 주석)를 잰다.
/// 비교 기준 = 에디터 전용 CPU Bake(EnemyC3BakeReference.Circle/Decal, 캐시 미스 1회).
/// 결과 = &lt;repo&gt;/../_lane-artifacts/&lt;repo 폴더&gt;/57-shader/S5/perf.txt·perf.tsv(레포 밖).
/// 🔴 타이밍은 머신마다 흔들린다 — 상시 테스트에 단언으로 넣지 않는다(결정적 단언은 EnemyC3TelegraphInstanceTests).
/// </summary>
public static class EnemyC3TelegraphPerfProbe
{
    const int Repeat = 10;
    static bool _ranThisDomain;   // 도메인 리로드 뒤 첫 실행이면 첫 행의 콜드 1회가 '도메인 첫 호출'이다

    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public struct Row
    {
        public string path, phase; public int n; public double ms, msMax; public long gc;
        public double gcBulk; public int bulk;   // 예열 후 n=10 행에만(나머지 bulk = 0)
    }

    sealed class PathSpec
    {
        public string name;
        public Action setup;       // 새 오브젝트(시간 밖)
        public Action<int> pre;    // 매 호출 전 준비(시간 밖 — 값 넣기·오브젝트 만들기)
        public Action<int> call;   // 잴 호출
        public Action teardown;    // 정리(시간 밖)
        public bool resetStatics = true;
        public int bulk = 200;     // 예열 후 할당 평균에 쓸 연속 호출 수
    }

    [MenuItem("Tools/Enemy C3/Measure shader telegraph cost")]
    static void Menu()
    {
        string report = Run(out string dir);
        UnityEngine.Debug.Log("[Enemy C3] perf → " + (dir ?? "(no _lane-artifacts)") + "\n" + report);
    }

    /// <summary>모든 경로를 재고 산출물을 쓴다. 반환 = perf.txt 본문.</summary>
    public static string Run(out string artifactDir)
    {
        bool domainFirst = !_ranThisDomain;
        _ranThisDomain = true;
        var rows = new List<Row>();
        foreach (var p in Paths()) Measure(p, rows);
        ResetStatics();

        string txt = Format(rows, domainFirst);
        artifactDir = ArtifactDir();
        if (artifactDir != null)
        {
            Directory.CreateDirectory(artifactDir);
            File.WriteAllText(Path.Combine(artifactDir, "perf.txt"), txt, new UTF8Encoding(false));
            var tsv = new StringBuilder("path\tphase\tn\tms_per_call\tms_max\theap_delta_bytes_4KB_granular\twarm_bulk_calls\twarm_bulk_gc_bytes_per_call\n");
            foreach (var r in rows)
                tsv.Append($"{r.path}\t{r.phase}\t{r.n}\t{r.ms:F4}\t{r.msMax:F4}\t{r.gc}\t{(r.bulk > 0 ? r.bulk.ToString() : "")}\t{(r.bulk > 0 ? r.gcBulk.ToString("F1") : "")}\n");
            File.WriteAllText(Path.Combine(artifactDir, "perf.tsv"), tsv.ToString(), new UTF8Encoding(false));
        }
        return txt;
    }

    // ── 할 일 57 R 예열 — 예열 후 콜드 첫 호출 ─────────────────────
    // 🔴 도메인 리로드 직후, C3 경로를 아무것도 부르지 않은 상태에서 실행해야 '콜드 첫 호출'이다(두 번째 실행은 JIT 이 끝나 있다).
    //    대조군(예열 없음)도 같은 조건 — 리로드 → control 메뉴. 정적 캐시는 비우지 않는다(게임에서도 예열 뒤 그대로 남는다).

    [MenuItem("Tools/Enemy C3/Measure prewarm then first calls")]
    static void PrewarmMenu() => UnityEngine.Debug.Log("[Enemy C3] prewarm perf →\n" + RunPrewarm(true, out _));

    [MenuItem("Tools/Enemy C3/Measure first calls (control, no prewarm)")]
    static void ControlMenu() => UnityEngine.Debug.Log("[Enemy C3] control perf →\n" + RunPrewarm(false, out _));

    /// <summary>예열(선택) → 경로별 첫 호출 → 같은 경로 새 오브젝트 9회 더(JIT 뒤 남는 생성 비용). 산출물 = S5/prewarm.txt·tsv(대조군은 control.*).</summary>
    public static string RunPrewarm(bool prewarm, out string artifactDir)
    {
        bool domainFirst = !_ranThisDomain;
        _ranThisDomain = true;
        var rows = new List<Row>();
        ResetStatics();   // Play 진입 직후처럼(ResetForPlay 가 예열 플래그도 내린다)

        double prewarmMs = 0;
        string breakdown = "";
        if (prewarm)
        {
            long t0 = Stopwatch.GetTimestamp();
            long g0 = GC.GetTotalMemory(false);
            EnemyC3Visuals.Prewarm();
            prewarmMs = Ms(Stopwatch.GetTimestamp() - t0);
            long gc = Math.Max(0, GC.GetTotalMemory(false) - g0);
            rows.Add(new Row { path = "EnemyC3Visuals.Prewarm() (예열 자체)", phase = "prewarm", n = 1, ms = prewarmMs, msMax = prewarmMs, gc = gc });
            var last = Type.GetType("EnemyC3Prewarm, Assembly-CSharp")?.GetField("Last", Any)?.GetValue(null);
            if (last != null)
                breakdown = $"예열 내역: data {Field<double>(last, "data"):F2} ms · shaders {Field<double>(last, "shaders"):F2} ms ({Field<int>(last, "variants")} 변형) · paths {Field<double>(last, "paths"):F2} ms";
            long t1 = Stopwatch.GetTimestamp();
            EnemyC3Visuals.Prewarm();
            double again = Ms(Stopwatch.GetTimestamp() - t1);
            rows.Add(new Row { path = "EnemyC3Visuals.Prewarm() 두 번째(무동작)", phase = "prewarm", n = 1, ms = again, msMax = again });
        }

        string phase = prewarm ? "first" : "control";
        foreach (var p in FirstCallPaths()) MeasureFirst(p, phase, rows);
        ResetStatics();

        var sb = new StringBuilder();
        sb.AppendLine(prewarm
            ? "할 일 57 R 예열 — 예열 후 콜드 첫 호출(에디터 EditMode, Tools/Enemy C3/Measure prewarm then first calls)"
            : "할 일 57 R 예열 — 대조군: 예열 없이 콜드 첫 호출(Tools/Enemy C3/Measure first calls (control, no prewarm))");
        sb.AppendLine($"측정 {DateTime.Now:yyyy-MM-dd HH:mm} · Unity {Application.unityVersion} · {SystemInfo.processorType} · {SystemInfo.graphicsDeviceName} · {SystemInfo.graphicsDeviceType}");
        sb.AppendLine(domainFirst
            ? "이번 실행 = 도메인 리로드 뒤 첫 실행 → first n=1 = 도메인 첫 호출(예열이 JIT 을 못 덮은 부분 포함)."
            : "⚠️ 같은 도메인의 두 번째 이후 실행 — JIT 이 이미 끝나 콜드가 아니다. 리로드 뒤 다시 잴 것.");
        sb.AppendLine("first n=1 = 경로 첫 호출(새 오브젝트) · n=10 = 새 오브젝트로 10회 평균(첫 호출 포함, max 함께) — n=10 이 남는 생성 비용의 눈금.");
        sb.AppendLine("정적 캐시는 예열 뒤 비우지 않는다(게임과 같음). 셰이더 GPU 준비·첫 그리기(PSO)는 Stopwatch 밖.");
        if (breakdown != "") sb.AppendLine(breakdown);
        sb.AppendLine();
        sb.AppendLine($"{"path",-52} {"phase",-7} {"n",3} {"ms/call",10} {"ms max",10} {"Δheap B",9} {"<1ms",5}");
        foreach (var r in rows)
            sb.AppendLine($"{r.path,-52} {r.phase,-7} {r.n,3} {r.ms,10:F4} {r.msMax,10:F4} {r.gc,9} {(r.phase == "prewarm" ? "" : r.n == 1 ? (r.ms < 1 ? "yes" : "NO") : ""),5}");
        string txt = sb.ToString();

        artifactDir = ArtifactDir();
        if (artifactDir != null)
        {
            Directory.CreateDirectory(artifactDir);
            string stem = prewarm ? "prewarm" : "control";
            File.WriteAllText(Path.Combine(artifactDir, stem + ".txt"), txt, new UTF8Encoding(false));
            var tsv = new StringBuilder("path\tphase\tn\tms_per_call\tms_max\theap_delta_bytes_4KB_granular\tunder_1ms\n");
            foreach (var r in rows)
                tsv.Append($"{r.path}\t{r.phase}\t{r.n}\t{r.ms:F4}\t{r.msMax:F4}\t{r.gc}\t{(r.phase != "prewarm" && r.n == 1 ? (r.ms < 1 ? "yes" : "no") : "")}\n");
            File.WriteAllText(Path.Combine(artifactDir, stem + ".tsv"), tsv.ToString(), new UTF8Encoding(false));
        }
        return txt;
    }

    // 첫 호출 1회(행 n=1) + 새 오브젝트로 9회 더 = 10회 평균(행 n=10). setup/teardown 은 시간 밖.
    static void MeasureFirst(PathSpec p, string phase, List<Row> rows)
    {
        double sum = 0, max = 0; long gcSum = 0;
        for (int k = 0; k < Repeat; k++)
        {
            p.setup?.Invoke();
            try
            {
                var (ms, gc) = Time(p, k);
                if (k == 0) rows.Add(new Row { path = p.name, phase = phase, n = 1, ms = ms, msMax = ms, gc = gc });
                sum += ms; max = Math.Max(max, ms); gcSum += gc;
            }
            finally { p.teardown?.Invoke(); }
        }
        rows.Add(new Row { path = p.name, phase = phase, n = Repeat, ms = sum / Repeat, msMax = max, gc = gcSum / Repeat });
    }

    // 사용자 must-fix 경로(23호 돌진 Show · 드론 ClientLock · CreateJump) + 방향 표식 · Paint(CodeShape), 그리고 남는 비용 쪼개기.
    static IEnumerable<PathSpec> FirstCallPaths()
    {
        GameObject go = null;
        BossAttackConeTelegraph t = null;
        void NewTelegraph() { go = new GameObject("EnemyC3Perf_Telegraph"); t = go.AddComponent<BossAttackConeTelegraph>(); }
        void DropTelegraph() { DestroyTelegraph(go); go = null; t = null; }

        yield return new PathSpec
        {
            name = "23호 돌진 Show(합집합 02, EnsureBuilt 포함)",
            setup = NewTelegraph, teardown = DropTelegraph,
            call = i => t.Show(2.5f, 360f, 0f, 18f, 1f, 18f, 1f),
        };
        // 남는 비용 쪼개기 — 보스 컴포넌트 첫 생성(프로젝터 6개 + 재질 인스턴스 6개)과 그 뒤 Show.
        var ensureBuilt = Method<Func<BossAttackConeTelegraph, bool>>(typeof(BossAttackConeTelegraph), "EnsureBuilt");
        yield return new PathSpec
        {
            name = "  └ 남는 비용: EnsureBuilt(프로젝터 6개 생성)",
            setup = NewTelegraph, teardown = DropTelegraph,
            call = i => ensureBuilt(t),
        };
        yield return new PathSpec
        {
            name = "  └ EnsureBuilt 뒤 Show(값 넣기·배치)",
            setup = () => { NewTelegraph(); ensureBuilt(t); }, teardown = DropTelegraph,
            call = i => t.Show(2.5f, 360f, 0f, 18f, 1f, 18f, 1f),
        };

        WellsDroneAttack w = null; ScriptableObject data = null;
        void NewWells()
        {
            go = new GameObject("EnemyC3Perf_Wells");
            w = go.AddComponent<WellsDroneAttack>();
            var f = typeof(WellsDroneAttack).GetField("_d", Any);
            data = ScriptableObject.CreateInstance(f.FieldType);
            f.SetValue(w, data);
        }
        void DropWells()
        {
            var root = Field<Transform>(w, "_viewRoot");
            if (root != null) Object.DestroyImmediate(root.gameObject);
            Object.DestroyImmediate(go); Object.DestroyImmediate(data);
            go = null; w = null; data = null;
        }
        yield return new PathSpec
        {
            name = "웰즈 드론 ClientLock(판 14, EnsureView 포함)",
            setup = NewWells, teardown = DropWells,
            call = i => w.ClientLock(new Vector3(i * 0.5f, 0f, 0f), 3f, 2f, true),
        };
        var ensureView = Method<Action<WellsDroneAttack>>(typeof(WellsDroneAttack), "EnsureView");
        yield return new PathSpec
        {
            name = "  └ 남는 비용: EnsureView(드론 뷰 생성)",
            setup = NewWells, teardown = DropWells,
            call = i => ensureView(w),
        };
        yield return new PathSpec
        {
            name = "  └ EnsureView 뒤 ClientLock",
            setup = () => { NewWells(); ensureView(w); }, teardown = DropWells,
            call = i => w.ClientLock(new Vector3(i * 0.5f, 0f, 0f), 3f, 2f, true),
        };

        var jumps = new List<GameObject>();
        yield return new PathSpec
        {
            name = "EnemyC3Visuals.CreateJump(→Begin)",
            teardown = () => { foreach (var j in jumps) if (j != null) Object.DestroyImmediate(j); jumps.Clear(); },
            call = i => jumps.Add(EnemyC3Visuals.CreateJump(new Vector3(i, 0f, 0f), 4f, 1.2f)),
        };

        BossDirectionIndicator ind = null; DecalProjector frontDecal = null;
        var applyDecal = Method<Action<BossDirectionIndicator, DecalProjector, Material, float, bool, float, float, float>>(typeof(BossDirectionIndicator), "ApplyDecal");
        var buildRenderers = Method<Action<BossDirectionIndicator>>(typeof(BossDirectionIndicator), "BuildRenderers");
        void NewIndicator()
        {
            go = new GameObject("EnemyC3Perf_Direction");
            ind = go.AddComponent<BossDirectionIndicator>();
            var source = Resources.Load<Material>("Telegraph/MA_TelegraphDecal");
            foreach (string f in new[] { "decalMaterial", "frontMaterial", "backMaterial" })
                typeof(BossDirectionIndicator).GetField(f, Any).SetValue(ind, source);
        }
        void DropIndicator() { DestroyTelegraph(go); go = null; ind = null; frontDecal = null; }
        yield return new PathSpec
        {
            name = "방향 표식 ApplyDecal(SetArcFill, 프로젝터 준비 후)",
            setup = () => { NewIndicator(); buildRenderers(ind); frontDecal = Field<DecalProjector>(ind, "_frontDecal"); },
            teardown = DropIndicator,
            call = i => applyDecal(ind, frontDecal, null, 40f, false, 1.6f, 2.6f, 5.2f),
        };
        yield return new PathSpec
        {
            name = "  └ 남는 비용: BuildRenderers(프로젝터 2개 생성)",
            setup = NewIndicator, teardown = DropIndicator,
            call = i => buildRenderers(ind),
        };

        Transform plateRoot = null; MeshRenderer flat = null;
        yield return new PathSpec
        {
            name = "BossPatternVisuals.Paint(CodeShape)",
            setup = () => { plateRoot = new GameObject("EnemyC3Perf_Flat").transform; flat = BossPatternVisuals.CreateFlat("Flat", plateRoot); },
            teardown = () => { Object.DestroyImmediate(plateRoot.gameObject); plateRoot = null; },
            call = i => BossPatternVisuals.Paint(flat, Color.red, BossPatternVisuals.CodeShape.Disc, 0.08f),
        };
    }

    // ── 측정 ─────────────────────────────────────────────
    // 🔴 GC.GetAllocatedBytesForCurrentThread 는 Unity Mono 에서 항상 0 이다(S5 실측 — 590KB 를 할당하는 CPU 굽기도 0).
    //    그래서 관리 힙 사용량(GC.GetTotalMemory(false), Boehm 4KB 블록 단위)의 증가로 잰다.
    //    1회 값은 4KB 단위라 거칠다 → 예열 후는 bulk 회 연속 호출의 증가 ÷ bulk(준비 단계 pre 의 할당은 따로 재서 뺀다).

    static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    static (double ms, long gc) Time(PathSpec p, int i)
    {
        p.pre?.Invoke(i);
        var call = p.call;
        long g0 = GC.GetTotalMemory(false);
        long t0 = Stopwatch.GetTimestamp();
        call(i);
        long t1 = Stopwatch.GetTimestamp();
        long g1 = GC.GetTotalMemory(false);
        return (Ms(t1 - t0), Math.Max(0, g1 - g0));
    }

    // 예열 후 bulk 회 평균 할당(B/call). 도중에 GC 가 돌면 다시 잰다(최대 3번, 끝내 돌면 -1).
    static double BulkGc(PathSpec p, int start)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int gen = GC.CollectionCount(0);
            long pre0 = GC.GetTotalMemory(false);
            if (p.pre != null) for (int i = 0; i < p.bulk; i++) p.pre(start + i);
            long pre1 = GC.GetTotalMemory(false);
            long all0 = GC.GetTotalMemory(false);
            for (int i = 0; i < p.bulk; i++) { p.pre?.Invoke(start + p.bulk + i); p.call(start + p.bulk + i); }
            long all1 = GC.GetTotalMemory(false);
            if (GC.CollectionCount(0) != gen) continue;
            return Math.Max(0, (all1 - all0) - (pre1 - pre0)) / (double)p.bulk;
        }
        return -1;
    }

    static void Measure(PathSpec p, List<Row> rows)
    {
        // 콜드 — 매번 정적 캐시 초기화 + 새 오브젝트.
        double sum = 0, max = 0; long gcSum = 0;
        for (int k = 0; k < Repeat; k++)
        {
            if (p.resetStatics) ResetStatics();
            p.setup?.Invoke();
            try
            {
                var (ms, gc) = Time(p, 0);
                if (k == 0) rows.Add(new Row { path = p.name, phase = "cold", n = 1, ms = ms, msMax = ms, gc = gc });
                sum += ms; max = Math.Max(max, ms); gcSum += gc;
            }
            finally { p.teardown?.Invoke(); }
        }
        rows.Add(new Row { path = p.name, phase = "cold", n = Repeat, ms = sum / Repeat, msMax = max, gc = gcSum / Repeat });

        // 예열 후 — 같은 오브젝트에 2회 부른 다음 1회, 이어서 10회.
        p.setup?.Invoke();
        try
        {
            Time(p, 0); Time(p, 1);
            var (ms1, gc1) = Time(p, 2);
            rows.Add(new Row { path = p.name, phase = "warm", n = 1, ms = ms1, msMax = ms1, gc = gc1 });
            sum = 0; max = 0; gcSum = 0;
            for (int i = 0; i < Repeat; i++)
            {
                var (ms, gc) = Time(p, 3 + i);
                sum += ms; max = Math.Max(max, ms); gcSum += gc;
            }
            rows.Add(new Row { path = p.name, phase = "warm", n = Repeat, ms = sum / Repeat, msMax = max, gc = gcSum / Repeat,
                               gcBulk = BulkGc(p, 3 + Repeat), bulk = p.bulk });
        }
        finally { p.teardown?.Invoke(); }
    }

    // 런타임 정적 캐시를 Play 진입 직후처럼 비운다(Resources 에셋은 엔진에 남아 있어 두 번째 Load 는 싸다 — 보고서 참고).
    static void ResetStatics()
    {
        typeof(EnemyC3Visuals).GetMethod("ResetForPlay", Any)?.Invoke(null, null);
        typeof(EnemyC3Visuals).GetField("plateMaterial", Any)?.SetValue(null, null);
        typeof(EnemyC3Visuals).GetField("decalMaterial", Any)?.SetValue(null, null);
        typeof(BossAttackConeTelegraph).GetField("_telegraphMaterial", Any)?.SetValue(null, null);
        typeof(BossAttackConeTelegraph).GetField("_triedLoadMaterial", Any)?.SetValue(null, false);
        // BossPatternVisuals 의 공유 재질·쿼드는 런타임이 new 로 만든 것 — 지우고 비운다(측정 오브젝트는 이미 정리됨).
        foreach (string f in new[] { "_mat", "_quad" })
        {
            var field = typeof(BossPatternVisuals).GetField(f, Any);
            if (field?.GetValue(null) is Object o && o != null) Object.DestroyImmediate(o);
            field?.SetValue(null, null);
        }
        typeof(BossPatternVisuals).GetField("_mpb", Any)?.SetValue(null, null);
    }

    // ── 경로 ─────────────────────────────────────────────

    static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, Any).GetValue(o);
    static TDelegate Method<TDelegate>(Type t, string name) where TDelegate : Delegate =>
        (TDelegate)Delegate.CreateDelegate(typeof(TDelegate), t.GetMethod(name, Any));

    static void DestroyTelegraph(GameObject go)
    {
        if (go == null) return;
        foreach (var d in go.GetComponentsInChildren<DecalProjector>(true))
        {
            var m = d.material; d.material = null;
            if (m != null) Object.DestroyImmediate(m);
        }
        Object.DestroyImmediate(go);
    }

    static IEnumerable<PathSpec> Paths()
    {
        GameObject go = null;
        BossAttackConeTelegraph t = null;
        void NewTelegraph() { go = new GameObject("EnemyC3Perf_Telegraph"); t = go.AddComponent<BossAttackConeTelegraph>(); }
        void DropTelegraph() { DestroyTelegraph(go); go = null; t = null; }

        // 23호 돌진 = 경로 띠 + 끝점 원 → 합집합(02). 콜드 = 프로젝터 6개·재질 인스턴스 6개 생성(EnsureBuilt) 포함.
        yield return new PathSpec
        {
            name = "23호 돌진 Show(합집합 02, 같은 크기)",
            setup = NewTelegraph, teardown = DropTelegraph,
            call = i => t.Show(2.5f, 360f, 0f, 18f, 1f, 18f, 1f),
        };
        // 길이를 바꾼 Show — 예전 Decal() 캐시 키(F4 크기) 미스 경로. 매 호출 길이가 다르다.
        yield return new PathSpec
        {
            name = "23호 돌진 Show(합집합 02, 길이 변경)",
            setup = NewTelegraph, teardown = DropTelegraph,
            call = i => { float len = 10f + i * 0.73f; t.Show(2.5f, 360f, 0f, len, 1f, len, 1f); },
        };
        yield return new PathSpec
        {
            name = "띠 Show(01, 길이 변경)",
            setup = NewTelegraph, teardown = DropTelegraph,
            call = i => t.Show(0f, 0f, 0f, 0f, 1.2f, 8f + i * 0.61f, 1f),
        };

        // Rebuild* 직접 — 값 넣기만(콜드 = 정적 캐시 비운 뒤 첫 호출, JSON 다시 읽기 포함).
        var rebuildUnion = Method<Action<BossAttackConeTelegraph>>(typeof(BossAttackConeTelegraph), "RebuildUnionOutline");
        var rebuildBand = Method<Action<BossAttackConeTelegraph>>(typeof(BossAttackConeTelegraph), "RebuildBandOutline");
        var rebuildCone = Method<Action<BossAttackConeTelegraph, float>>(typeof(BossAttackConeTelegraph), "RebuildConeOutline");
        var rebuildUnionFill = Method<Action<BossAttackConeTelegraph, float>>(typeof(BossAttackConeTelegraph), "RebuildUnionFill");
        var rebuildInward = Method<Action<BossAttackConeTelegraph, float>>(typeof(BossAttackConeTelegraph), "RebuildInwardConeFill");
        var applyFill = Method<Action<BossAttackConeTelegraph>>(typeof(BossAttackConeTelegraph), "ApplyFill");
        // _elapsed 쓰기 — FieldInfo.SetValue 는 float 를 박싱해 할당을 섞으므로 컴파일한 대입식으로.
        var tParam = System.Linq.Expressions.Expression.Parameter(typeof(BossAttackConeTelegraph));
        var vParam = System.Linq.Expressions.Expression.Parameter(typeof(float));
        var setElapsed = System.Linq.Expressions.Expression.Lambda<Action<BossAttackConeTelegraph, float>>(
            System.Linq.Expressions.Expression.Assign(System.Linq.Expressions.Expression.Field(tParam, "_elapsed"), vParam), tParam, vParam).Compile();

        PathSpec AfterShow(string name, Action<BossAttackConeTelegraph> show, Action<int> call) => new PathSpec
        {
            name = name, resetStatics = false,
            setup = () => { NewTelegraph(); show(t); ResetStatics(); },
            teardown = DropTelegraph, call = call,
        };
        yield return AfterShow("RebuildUnionOutline(합집합 외곽선)", x => x.Show(2.5f, 360f, 0f, 18f, 1f, 18f, 1f), i => rebuildUnion(t));
        yield return AfterShow("RebuildBandOutline(띠 외곽선)", x => x.Show(0f, 0f, 0f, 0f, 1.2f, 8f, 1f), i => rebuildBand(t));
        yield return AfterShow("RebuildConeOutline(부채꼴 외곽선)", x => x.Show(4f, 120f, 0f, 0f, 0f, 0f, 1f), i => rebuildCone(t, 30f + i * 7f));
        yield return AfterShow("RebuildUnionFill(합집합 채움)", x => x.Show(2.5f, 360f, 0f, 18f, 1f, 18f, 1f), i => rebuildUnionFill(t, (i % 25) / 24f));
        yield return AfterShow("RebuildInwardConeFill(잡기 고리)", x => x.Show(4f, 180f, 0f, 0f, 0f, 0f, 1f, fillInward: true), i => rebuildInward(t, 1f - (i % 25) / 24f));
        // Show 의 나머지 두 단계 — 할당 출처 가르기(C3 값 넣기 Rebuild* 는 0 B 이다).
        var applyStyle = Method<Action<BossAttackConeTelegraph>>(typeof(BossAttackConeTelegraph), "ApplyStyle");
        var place = Method<Action<BossAttackConeTelegraph>>(typeof(BossAttackConeTelegraph), "Place");
        yield return AfterShow("Show 내부: ApplyStyle(색·마스크, 합집합)", x => x.Show(2.5f, 360f, 0f, 18f, 1f, 18f, 1f), i => applyStyle(t));
        yield return AfterShow("Show 내부: Place(배치·바닥 탐색, 합집합)", x => x.Show(2.5f, 360f, 0f, 18f, 1f, 18f, 1f), i => place(t));
        yield return AfterShow("Show 내부: ApplyStyle(색·마스크, 띠 01)", x => x.Show(0f, 0f, 0f, 0f, 1.2f, 8f, 1f), i => applyStyle(t));
        // 합집합 Show 는 부채꼴·띠 프로젝터 4개를 켰다가 곧바로 끈다(57 이전부터의 흐름) — URP DecalProjector 의 OnEnable/OnDisable 비용.
        var toggled = new DecalProjector[4];
        yield return new PathSpec
        {
            name = "Show 내부: 프로젝터 4개 켜기→끄기(합집합)", resetStatics = false,
            setup = () =>
            {
                NewTelegraph(); t.Show(2.5f, 360f, 0f, 18f, 1f, 18f, 1f); ResetStatics();
                int k = 0;
                foreach (string n in new[] { "_coneOutline", "_coneFill", "_bandOutline", "_bandFill" }) toggled[k++] = Field<DecalProjector>(t, n);
            },
            teardown = DropTelegraph,
            call = i => { foreach (var d in toggled) { d.enabled = true; d.enabled = false; } },
        };

        // 합집합 진행 단계 변경 1회 = LateUpdate 의 ApplyFill 이 단계가 바뀐 프레임에 하는 일(_elapsed 는 시간 밖에서 넣는다).
        int step = 0;
        yield return new PathSpec
        {
            name = "합집합 진행 단계 변경(ApplyFill)", resetStatics = false,
            setup = () => { NewTelegraph(); t.Show(2.5f, 360f, 0f, 18f, 1f, 18f, 2.4f); ResetStatics(); step = 0; },
            teardown = DropTelegraph,
            pre = i => { step = step % 24 + 1; setElapsed(t, 2.4f * step / 24f); },
            call = i => applyFill(t),
        };

        // 웰즈 드론 고정 — WellsDroneAttack.ClientLock → PaintLockCircle → EnemyC3Visuals.PaintPlate("14").
        // 데이터는 빈 WellsDroneDataSO(드론 모델 없음 — 드론 비행 연출 제외, 판만).
        WellsDroneAttack w = null; ScriptableObject data = null;
        void NewWells()
        {
            go = new GameObject("EnemyC3Perf_Wells");
            w = go.AddComponent<WellsDroneAttack>();
            var f = typeof(WellsDroneAttack).GetField("_d", Any);
            data = ScriptableObject.CreateInstance(f.FieldType);
            f.SetValue(w, data);
        }
        void DropWells()
        {
            var root = Field<Transform>(w, "_viewRoot");
            if (root != null) Object.DestroyImmediate(root.gameObject);
            Object.DestroyImmediate(go); Object.DestroyImmediate(data);
            go = null; w = null; data = null;
        }
        yield return new PathSpec
        {
            name = "웰즈 드론 ClientLock(판 14, 콜드=EnsureView 포함)",
            setup = NewWells, teardown = DropWells,
            call = i => w.ClientLock(new Vector3(i * 0.5f, 0f, 0f), 3f + (i % 3) * 0.5f, 2f, (i & 1) == 0),
        };
        var paintLock = Method<Action<WellsDroneAttack>>(typeof(WellsDroneAttack), "PaintLockCircle");
        yield return new PathSpec
        {
            name = "PaintLockCircle", resetStatics = false,
            setup = () => { NewWells(); w.ClientLock(Vector3.zero, 3f, 2f, true); ResetStatics(); },
            teardown = DropWells, call = i => paintLock(w),
        };
        MeshRenderer outer = null, fill = null; Transform plateRoot = null;
        yield return new PathSpec
        {
            name = "EnemyC3Visuals.PaintPlate(\"14\")", resetStatics = false,
            setup = () =>
            {
                plateRoot = new GameObject("EnemyC3Perf_Plate").transform;
                outer = BossPatternVisuals.CreateFlat("Outer", plateRoot); fill = BossPatternVisuals.CreateFlat("Fill", plateRoot);
                ResetStatics();
            },
            teardown = () => { Object.DestroyImmediate(plateRoot.gameObject); plateRoot = null; },
            call = i => EnemyC3Visuals.PaintPlate(outer, fill, "14"),
        };

        // 점프 06·07 — CreateJump 은 매번 새 오브젝트를 만든다(게임에서도 그렇다). 예열 후도 생성 비용이 들어 있다.
        var jumps = new List<GameObject>();
        yield return new PathSpec
        {
            name = "EnemyC3Visuals.CreateJump(→Begin)",
            teardown = () => { foreach (var j in jumps) if (j != null) Object.DestroyImmediate(j); jumps.Clear(); },
            call = i => jumps.Add(EnemyC3Visuals.CreateJump(new Vector3(i, 0f, 0f), 4f + (i % 3), 1.2f)),
        };
        EnemyC3JumpView view = null;
        yield return new PathSpec
        {
            name = "EnemyC3JumpView.Begin(오브젝트 준비 후)",
            teardown = () => { foreach (var j in jumps) if (j != null) Object.DestroyImmediate(j); jumps.Clear(); },
            pre = i => { var j = new GameObject("EnemyC3Perf_Jump"); jumps.Add(j); view = j.AddComponent<EnemyC3JumpView>(); },
            call = i => view.Begin(4f + (i % 3), 1.2f),
        };

        // 보스 방향 표식 — ApplyDecal(크기·각도 변경). 데칼 재질 칸은 예고 재질(MA_TelegraphDecal)로 채운다(그리기 순서 출처일 뿐).
        BossDirectionIndicator ind = null; DecalProjector frontDecal = null;
        var applyDecal = Method<Action<BossDirectionIndicator, DecalProjector, Material, float, bool, float, float, float>>(typeof(BossDirectionIndicator), "ApplyDecal");
        var buildRenderers = Method<Action<BossDirectionIndicator>>(typeof(BossDirectionIndicator), "BuildRenderers");
        yield return new PathSpec
        {
            name = "BossDirectionIndicator.ApplyDecal(크기·각도 변경)", resetStatics = false,
            setup = () =>
            {
                go = new GameObject("EnemyC3Perf_Direction");
                ind = go.AddComponent<BossDirectionIndicator>();
                var source = Resources.Load<Material>("Telegraph/MA_TelegraphDecal");
                foreach (string f in new[] { "decalMaterial", "frontMaterial", "backMaterial" })
                    typeof(BossDirectionIndicator).GetField(f, Any).SetValue(ind, source);
                buildRenderers(ind);
                frontDecal = Field<DecalProjector>(ind, "_frontDecal");
                ResetStatics();
            },
            teardown = () => { DestroyTelegraph(go); go = null; ind = null; frontDecal = null; },
            call = i =>
            {
                float outerR = 2.6f + (i % 4) * 0.1f;
                applyDecal(ind, frontDecal, null, 30f + (i % 10) * 9f, (i & 1) == 1, 1.6f, outerR, outerR * 2f);
            },
        };

        // 판·조준 코드 모양(TelegraphQuad _CodeShape).
        MeshRenderer flat = null;
        yield return new PathSpec
        {
            name = "BossPatternVisuals.Paint(CodeShape)", resetStatics = false,
            setup = () => { plateRoot = new GameObject("EnemyC3Perf_Flat").transform; flat = BossPatternVisuals.CreateFlat("Flat", plateRoot); },
            teardown = () => { Object.DestroyImmediate(plateRoot.gameObject); plateRoot = null; },
            call = i => BossPatternVisuals.Paint(flat, Color.red, (BossPatternVisuals.CodeShape)(1 + i % 3), 0.05f + (i % 5) * 0.02f),
        };

        // 참고값 — 예전 런타임이 하던 CPU 384² 굽기(지금은 에디터 전용). 매 호출 캐시 미스가 되게 캐시를 비우거나 길이를 바꾼다.
        var bakeReset = typeof(EnemyC3BakeReference).GetMethod("ResetForPlay", Any);
        yield return new PathSpec
        {
            name = "[참고] EnemyC3BakeReference.Circle(\"14\") CPU 굽기", bulk = 3,
            teardown = () => bakeReset.Invoke(null, null),
            pre = i => bakeReset.Invoke(null, null),
            call = i => EnemyC3BakeReference.Circle("14"),
        };
        yield return new PathSpec
        {
            name = "[참고] EnemyC3BakeReference.Decal(합집합 02) CPU 굽기", bulk = 3,
            teardown = () => bakeReset.Invoke(null, null),
            call = i => { float len = 10f + i * 0.73f; EnemyC3BakeReference.Decal("02", new Vector2(5f, len + 2.5f), 2.5f, len, 180f, 2f, len); },
        };
    }

    // ── 산출물 ─────────────────────────────────────────────

    static string Format(List<Row> rows, bool domainFirst)
    {
        var sb = new StringBuilder();
        sb.AppendLine("할 일 57 S5 — 셰이더 예고 CPU 비용(에디터 EditMode, Tools/Enemy C3/Measure shader telegraph cost)");
        sb.AppendLine($"측정 {DateTime.Now:yyyy-MM-dd HH:mm} · Unity {Application.unityVersion} · {SystemInfo.processorType} · {SystemInfo.graphicsDeviceName}");
        sb.AppendLine("cold = 정적 캐시 초기화(JSON·MPB·재질 참조) + 새 오브젝트에서 첫 호출. warm = 같은 오브젝트에 2회 부른 뒤.");
        sb.AppendLine("n=1 은 첫 호출 1회, n=10 은 평균(max 함께).");
        sb.AppendLine("Δheap = 호출 전후 GC.GetTotalMemory 증가(Boehm 4KB 블록 단위라 1회 값은 거칠다 — 0 또는 4096 의 배수).");
        sb.AppendLine("GC/call(bulk) = 예열 후 N회 연속 호출의 힙 증가 ÷ N(준비 단계 할당 제외, 도중 GC 면 재측정) — 예고 1회당 GC 할당의 기준 값.");
        sb.AppendLine("(GC.GetAllocatedBytesForCurrentThread 는 Unity Mono 에서 항상 0 이라 쓰지 않는다.)");
        sb.AppendLine(domainFirst
            ? "이번 실행 = 도메인 리로드 뒤 첫 실행 → 첫 행 cold n=1 은 도메인 첫 호출(JIT·정적 생성자 포함)."
            : "이번 실행 = 같은 도메인의 두 번째 이후 실행 → JIT 은 이미 끝난 상태(첫 행도 정적 캐시 콜드만).");
        sb.AppendLine("셰이더 GPU 컴파일·첫 그리기는 Stopwatch 밖(렌더 스레드) — 빌드는 셰이더가 미리 컴파일된다.");
        sb.AppendLine();
        sb.AppendLine($"{"path",-52} {"phase",-5} {"n",3} {"ms/call",10} {"ms max",10} {"Δheap B",9} {"GC B/call(bulk)",18}");
        foreach (var r in rows)
            sb.AppendLine($"{r.path,-52} {r.phase,-5} {r.n,3} {r.ms,10:F4} {r.msMax,10:F4} {r.gc,9} {(r.bulk > 0 ? $"{r.gcBulk:F1} (N={r.bulk})" : ""),18}");
        return sb.ToString();
    }

    static string ArtifactDir()
    {
        var project = Directory.GetParent(Application.dataPath);
        var lanes = Path.Combine(project.Parent.FullName, "_lane-artifacts");
        return Directory.Exists(lanes) ? Path.Combine(lanes, project.Name, "57-shader", "S5") : null;
    }
}
