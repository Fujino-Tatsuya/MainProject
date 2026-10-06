// DevNetworkLag.cs — MPPM 인스턴스별 네트워크 지연 시뮬레이션 + 로딩 진단 로그 (에디터 전용)
//
// 왜: 핫스팟 3인 테스트에서 클라만 "로딩이 덜 된 채 시작"되는 증상(몬스터 모델 없음·원거리 공격 안 보임)이 났다.
//     호스트 단독 Play 로는 서버 관점만 보여 재현이 안 된다 → MPPM 클라이언트에만 지연을 걸어 재현한다.
//
// 쓰는 법
//   툴바 [지연: …] 드롭다운(Dev Boot 옆)에서 고른다 → 다음 Play 부터 각 인스턴스가 자기 칸 설정으로 시작.
//   칸 = Main Editor(1) · Player 2~4. 보통 메인(호스트)은 0, 가상 플레이어(클라)만 지연.
//   MPPM 태그로도 덮어쓸 수 있다: `Lag250` · `Lag250_100` · `Lag250_100_3` (지연_흔들림_손실%) · `NoLag`.
//
// 동작
//   - 설정 파일 = 메인 프로젝트 `Library/DevNetworkLag.json`(git 제외). 가상 플레이어는 `Library/VP/mppmXXXX` 에서 돌아도
//     같은 파일을 읽는다. 자기 번호는 메인 `Library/VP/SystemData.json` 의 VirtualProjectIdentifier 로 찾는다(읽기만).
//   - NetworkSimulator 는 특정 Transport 가 아니라 전역 어댑터(NetworkAdapters)에 붙고 나중 연결에도 적용된다
//     → Play 시작 때 만든 DDOL 오브젝트 하나에 붙이면 된다. NetworkManager·씬·프리팹 무수정.
//   - 진단: 맵 씬이 열리면 30초 동안 0.5초마다 "씬별 몬스터 수 · 렌더러 꺼진 몬스터 수 · 스폰된 NetworkObject 수"를
//     바뀔 때만 `[LagDiag]` 로 남긴다(`[NetDiag]` 는 기존 NetworkDiagnosticsLog 가 쓰는 접두어라 피함) + 로딩 씬 언로드 순간. 호스트·클라 로그를 나란히 보면
//     "클라에서 몬스터가 로딩 씬에 생겼다가 같이 사라지는가"가 드러난다.
//
// 빌드 영향 없음(파일 전체 UNITY_EDITOR).

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Unity.Multiplayer.PlayMode;
using Unity.Multiplayer.Tools.NetworkSimulator.Runtime;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>설정 파일 · 프리셋 · 플레이어 판별. 에디터 툴바와 런타임이 같이 쓴다.</summary>
public static class DevNetworkLagSettings
{
    public const int SlotCount = 4;   // MPPM: Main Editor + Player 2~4

    [Serializable]
    public struct Lag
    {
        public int delayMs, jitterMs, lossPercent;
        public Lag(int d, int j, int l) { delayMs = d; jitterMs = j; lossPercent = l; }
        public bool IsNone => delayMs <= 0 && jitterMs <= 0 && lossPercent <= 0;
        public override string ToString() => IsNone ? "없음" : $"{delayMs}ms ±{jitterMs} · 손실 {lossPercent}%";
    }

    [Serializable]
    public sealed class Data
    {
        public Lag[] slots = new Lag[SlotCount];
        public bool diagnostics = true;
    }

    // 프리셋 — Unity 기본 값(HomeBroadband·Mobile2_5G)은 Multiplayer Tools 2.2.8 소스에서 옮겼다.
    public static readonly (string name, Lag lag)[] Presets =
    {
        ("없음", new Lag(0, 0, 0)),
        ("집 와이파이 (32ms ±12 · 2%)", new Lag(32, 12, 2)),
        ("핫스팟 (250ms ±100 · 3%)", new Lag(250, 100, 3)),
        ("혼잡한 핫스팟 (400ms ±200 · 5%)", new Lag(400, 200, 5)),
        ("모바일 2.5G (480ms ±40 · 7%)", new Lag(480, 40, 7)),
    };

    public static string MainProjectRoot
    {
        get
        {
            // 가상 플레이어의 dataPath = <메인>/Library/VP/mppmXXXX/Assets
            string p = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            int i = p.IndexOf("/Library/VP/", StringComparison.OrdinalIgnoreCase);
            return i >= 0 ? p.Substring(0, i) : p;
        }
    }

    static string FilePath => Path.Combine(MainProjectRoot, "Library", "DevNetworkLag.json");

    public static Data Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var d = JsonUtility.FromJson<Data>(File.ReadAllText(FilePath));
                if (d != null)
                {
                    if (d.slots == null || d.slots.Length != SlotCount) Array.Resize(ref d.slots, SlotCount);
                    return d;
                }
            }
        }
        catch (Exception e) { Debug.LogWarning($"[NetLag] 설정 읽기 실패 — 지연 없음으로 시작: {e.Message}"); }
        return new Data();
    }

    public static void Save(Data d)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
        File.WriteAllText(FilePath, JsonUtility.ToJson(d, true));
    }

    /// <summary>현재 인스턴스의 칸 번호(1 = Main Editor, 2~4 = Player n). 모르면 0.</summary>
    public static int CurrentSlot()
    {
        if (CurrentPlayer.IsMainEditor) return 1;

        string p = Application.dataPath.Replace('\\', '/');
        var m = Regex.Match(p, @"/Library/VP/mppm([0-9a-fA-F]+)/", RegexOptions.IgnoreCase);
        if (!m.Success) return 0;
        string id = m.Groups[1].Value;

        string sys = Path.Combine(MainProjectRoot, "Library", "VP", "SystemData.json");
        if (!File.Exists(sys)) return 0;
        string text = File.ReadAllText(sys);
        // "Index": n 다음, 다음 "Index" 이전에 나오는 "m_Id": "id" 를 찾는다.
        var idx = Regex.Matches(text, "\"Index\"\\s*:\\s*(\\d+)");
        for (int k = 0; k < idx.Count; k++)
        {
            int start = idx[k].Index;
            int end = k + 1 < idx.Count ? idx[k + 1].Index : text.Length;
            if (text.IndexOf($"\"m_Id\": \"{id}\"", start, end - start, StringComparison.OrdinalIgnoreCase) >= 0)
                return int.Parse(idx[k].Groups[1].Value);
        }
        return 0;
    }

    /// <summary>MPPM 태그 `Lag250[_100[_3]]` / `NoLag` 를 해석한다. 해당 태그가 없으면 false.</summary>
    public static bool TryLagFromTags(out Lag lag, out string tag)
    {
        lag = default; tag = null;
        IReadOnlyList<string> tags;
        try { tags = CurrentPlayer.Tags; } catch { return false; }   // ReadOnlyTags() 는 6000.3 에서 폐기 예고
        if (tags == null) return false;
        foreach (var t in tags)
        {
            if (string.Equals(t, "NoLag", StringComparison.OrdinalIgnoreCase)) { tag = t; return true; }
            var m = Regex.Match(t, @"^Lag(\d+)(?:[_/](\d+))?(?:[_/](\d+))?$", RegexOptions.IgnoreCase);
            if (!m.Success) continue;
            lag = new Lag(int.Parse(m.Groups[1].Value),
                          m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0,
                          m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
            tag = t;
            return true;
        }
        return false;
    }
}

/// <summary>Play 시작 때 1개 생성(DDOL). 지연 적용 + 화면 표시 + 진단 로그.</summary>
[AddComponentMenu("")]
public sealed class DevNetworkLagRunner : MonoBehaviour
{
    const string MapSceneName = "4.MapScene";
    const string LoadingSceneName = "2.LoadingScene";
    const float DiagSeconds = 30f, DiagInterval = 0.5f;

    DevNetworkLagSettings.Lag _lag;
    int _slot;
    string _source;
    string _label;
    bool _diag;

    float _diagUntil = -1f, _nextSample;
    string _lastSample;
    GUIStyle _style;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("[Dev] NetworkLag");
        go.hideFlags = HideFlags.DontSave;
        DontDestroyOnLoad(go);
        go.AddComponent<DevNetworkLagRunner>();
    }

    void Awake()
    {
        var data = DevNetworkLagSettings.Load();
        _diag = data.diagnostics;
        _slot = DevNetworkLagSettings.CurrentSlot();

        if (DevNetworkLagSettings.TryLagFromTags(out var tagLag, out var tag))
        {
            _lag = tagLag; _source = $"태그 {tag}";
        }
        else
        {
            _lag = _slot >= 1 && _slot <= DevNetworkLagSettings.SlotCount ? data.slots[_slot - 1] : default;
            _source = _slot >= 1 ? $"툴바 설정 칸 {_slot}" : "칸 판별 실패 → 지연 없음";
        }

        string who = _slot == 1 ? "Main Editor" : _slot > 1 ? $"Player {_slot}" : "알 수 없음";
        if (!_lag.IsNone)
        {
            var sim = gameObject.AddComponent<NetworkSimulator>();
            sim.ConnectionPreset = NetworkSimulatorPreset.Create(
                $"Dev {_lag}", "DevNetworkLag", _lag.delayMs, _lag.jitterMs, 0, _lag.lossPercent);
        }
        _label = _lag.IsNone ? null : $"[{who}] 네트워크 지연 {_lag}";
        Debug.Log($"[NetLag] {who} — {_lag} ({_source})");

        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
    }

    void OnSceneLoaded(Scene s, LoadSceneMode mode)
    {
        if (!_diag) return;
        Sample($"씬 로드 {s.name}({mode})", force: true);
        if (s.name == MapSceneName)
        {
            _diagUntil = Time.unscaledTime + DiagSeconds; _nextSample = 0f;
            _nextTurretDump = Time.unscaledTime + 10f; _turretDumpUntil = Time.unscaledTime + 90f;
        }
    }

    void OnSceneUnloaded(Scene s)
    {
        if (!_diag) return;
        Sample($"씬 언로드 {s.name}", force: true);
        if (s.name == LoadingSceneName) _diagUntil = Mathf.Max(_diagUntil, Time.unscaledTime + 10f);
    }

    void Update()
    {
        if (!_diag) return;
        if (Time.unscaledTime <= _diagUntil && Time.unscaledTime >= _nextSample)
        {
            _nextSample = Time.unscaledTime + DiagInterval;
            Sample("주기", force: false);
        }
        if (_turretDumpUntil > 0f && Time.unscaledTime <= _turretDumpUntil && Time.unscaledTime >= _nextTurretDump)
        {
            _nextTurretDump = Time.unscaledTime + 5f;
            DumpTurrets();
        }
    }

    // ───────── 터렛 상세(10-06: 클라 하나에서만 PeekABot 몸체가 안 보이고 받침대 고리만 남음) ─────────
    // 같은 NetworkObjectId 를 호스트·클라 로그에서 짝지어 비교한다(Codex 권장). 애니메이터 상태는 두 클라가 같았으므로
    // 렌더러 단계(켜짐·카메라 가시성·바운드·셰이더)와 본 위치를 본다. 맵 로드 10초 뒤부터 5초마다, 90초까지.
    float _turretDumpUntil = -1f, _nextTurretDump;

    void DumpTurrets()
    {
        var sb = new StringBuilder(4096);
        int n = 0;
        foreach (var m in FindObjectsByType<MonsterBase>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (m.name.IndexOf("PeekABot", StringComparison.Ordinal) < 0) continue;
            n++;
            var no = m.GetComponent<NetworkObject>();
            Vector3 p = m.transform.position;
            sb.Append("\n  #").Append(no != null ? no.NetworkObjectId.ToString() : "?")
              .Append(" pos=(").Append(p.x.ToString("0.0")).Append(',').Append(p.y.ToString("0.00")).Append(',').Append(p.z.ToString("0.0")).Append(')')
              .Append(m.gameObject.activeInHierarchy ? "" : " [비활성]");

            var a = m.GetComponentInChildren<Animator>(true);
            if (a != null)
                sb.Append(" | anim en=").Append(a.enabled ? 1 : 0).Append(" init=").Append(a.isInitialized ? 1 : 0)
                  .Append(" cull=").Append(a.cullingMode).Append(" spd=").Append(a.speed.ToString("0.##"))
                  .Append(" t0=").Append(a.isInitialized ? a.GetCurrentAnimatorStateInfo(0).normalizedTime.ToString("0.00") : "-");

            foreach (var t in m.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "Column01" && t.name != "HeadRotator" && t.name != "Head") continue;
                sb.Append(" | ").Append(t.name).Append(" y=").Append(t.position.y.ToString("0.00"))
                  .Append(" s=").Append(t.lossyScale.y.ToString("0.##"));
            }

            foreach (var r in m.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                var b = r.bounds;
                string shader = r.sharedMaterial != null && r.sharedMaterial.shader != null ? r.sharedMaterial.shader.name : "머티리얼 없음";
                sb.Append("\n     ").Append(r is SkinnedMeshRenderer ? "SMR " : "MR ").Append(r.name)
                  .Append(" en=").Append(r.enabled ? 1 : 0).Append(" act=").Append(r.gameObject.activeInHierarchy ? 1 : 0)
                  .Append(" vis=").Append(r.isVisible ? 1 : 0)
                  .Append(r.forceRenderingOff ? " forceOff" : "")
                  .Append(" b=(").Append(b.center.y.ToString("0.00")).Append('|').Append(b.size.x.ToString("0.0")).Append('x')
                  .Append(b.size.y.ToString("0.0")).Append('x').Append(b.size.z.ToString("0.0")).Append(')')
                  .Append(" sh=").Append(shader);
                if (r is SkinnedMeshRenderer smr)
                    sb.Append(" root=").Append(smr.rootBone != null ? smr.rootBone.name : "없음")
                      .Append(" bones=").Append(smr.bones != null ? smr.bones.Length : 0)
                      .Append(" mesh=").Append(smr.sharedMesh != null ? smr.sharedMesh.name : "없음")
                      .Append(smr.updateWhenOffscreen ? " offscreen" : "");
            }
        }
        var nm = NetworkManager.Singleton;
        string role = nm == null || !nm.IsListening ? "오프라인" : nm.IsHost ? "호스트" : "클라";
        Debug.Log($"[LagDiag-Turret] {role} P{_slot} t={Time.unscaledTime:0.0} PeekABot {n}개" + sb);
    }

    readonly Dictionary<string, int> _perScene = new Dictionary<string, int>();
    readonly Dictionary<string, int> _turret = new Dictionary<string, int>();
    readonly StringBuilder _sb = new StringBuilder(256);

    // 고정 터렛(PeekABot·TeslaBot)은 아트 컨트롤러에 Hide/Raise 상태가 있어 데이터로 컨트롤러를 갈아 끼운다
    // (MonsterBase.OnNetworkSpawn). 클라에서 받침대 고리만 보이는 증상(10-06 MPPM 재현) → 컨트롤러·클립을 피어별로 본다.
    static string TurretKey(MonsterBase m)
    {
        string n = m.name;
        if (n.IndexOf("PeekABot", StringComparison.Ordinal) < 0 && n.IndexOf("TeslaBot", StringComparison.Ordinal) < 0) return null;
        var a = m.GetComponentInChildren<Animator>(true);
        if (a == null) return n.Replace("(Clone)", "") + " 애니메이터 없음";
        string ctrl = a.runtimeAnimatorController != null ? a.runtimeAnimatorController.name : "컨트롤러 없음";
        var sb = new StringBuilder();
        sb.Append(n.Replace("(Clone)", "")).Append(" ctrl=").Append(ctrl);
        for (int l = 0; l < a.layerCount && l < 2; l++)
        {
            var clips = a.GetCurrentAnimatorClipInfo(l);
            sb.Append(" L").Append(l).Append('=').Append(clips.Length > 0 && clips[0].clip != null ? clips[0].clip.name : "-");
        }
        if (!a.enabled) sb.Append(" (Animator 꺼짐)");
        return sb.ToString();
    }

    // 바뀔 때만 남긴다(진단이 신호를 덮지 않게 — lessons #8).
    void Sample(string why, bool force)
    {
        _perScene.Clear();
        _turret.Clear();
        int hiddenModel = 0, total = 0;
        foreach (var m in FindObjectsByType<MonsterBase>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            total++;
            string tk = TurretKey(m);
            if (tk != null) _turret[tk] = _turret.TryGetValue(tk, out int tc) ? tc + 1 : 1;
            string sn = m.gameObject.scene.name;
            _perScene[sn] = _perScene.TryGetValue(sn, out int c) ? c + 1 : 1;
            bool anyVisible = false;
            foreach (var r in m.GetComponentsInChildren<Renderer>(false))
                if (r.enabled && !(r is ParticleSystemRenderer)) { anyVisible = true; break; }
            if (!anyVisible || !m.gameObject.activeInHierarchy) hiddenModel++;
        }

        var nm = NetworkManager.Singleton;
        int spawned = nm != null && nm.SpawnManager != null ? nm.SpawnManager.SpawnedObjects.Count : -1;
        string role = nm == null || !nm.IsListening ? "오프라인" : nm.IsHost ? "호스트" : nm.IsServer ? "서버" : "클라";

        _sb.Clear();
        _sb.Append("몬스터 ").Append(total);
        foreach (var kv in _perScene) _sb.Append(" [").Append(kv.Key).Append(' ').Append(kv.Value).Append(']');
        _sb.Append(" · 모델 꺼짐 ").Append(hiddenModel)
           .Append(" · NetObj ").Append(spawned)
           .Append(" · 활성씬 ").Append(SceneManager.GetActiveScene().name);
        foreach (var kv in _turret) _sb.Append("\n   터렛 ").Append(kv.Value).Append("× ").Append(kv.Key);
        string s = _sb.ToString();

        if (!force && s == _lastSample) return;
        _lastSample = s;
        Debug.Log($"[LagDiag] {role} P{_slot} t={Time.unscaledTime:0.0} ({why}) {s}");
    }

    void OnGUI()
    {
        if (_label == null) return;
        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.box) { fontSize = 13, alignment = TextAnchor.MiddleCenter };
            _style.normal.textColor = new Color(1f, 0.75f, 0.3f);
        }
        const float w = 360f, h = 24f;
        GUI.Box(new Rect((Screen.width - w) * 0.5f, 4f, w, h), _label, _style);
    }
}
#endif
