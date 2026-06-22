using Unity.Netcode;
using UnityEngine;

// 맵 생성 네트워크 진입점.
//  - 서버: 시드/난이도 결정 → NetworkVariable에 기록 + 로컬 Generate.
//  - 클라: OnNetworkSpawn 시 이미 ready면 즉시 Generate, 아니면 ready 변경 시 Generate
//    (NetworkVariable 복제 → 동시 시작/레이트 조인 모두 같은 시드로 생성. 비네트워크 시각물은 양쪽 로컬,
//     NetworkObject(몬스터)는 서버 Spawn → 복제).
public class MapNetworkSync : NetworkBehaviour
{
    [SerializeField] private MapGenerator mapGenerator;

    [Header("=== 콘텐츠 난이도 (서버 결정 — level-system.md §3) ===")]
    [Tooltip("사전 난이도 선택 + 상승 난이도(Ascension/Heat). 추후 메타 세이브에서 로드.")]
    [SerializeField] private int ascensionLevel = 0;
    [Tooltip("스테이지 진행도. Stage1=0, Stage2=1...")]
    [SerializeField] private int stageIndex = 0;
    [Tooltip("스테이지당 난이도 가산량.")]
    [SerializeField] private int stageStep = 2;

    // 서버가 결정한 값 — 복제되어 늦게 합류한 클라도 동일 시드/난이도로 생성
    private readonly NetworkVariable<int> _seed = new NetworkVariable<int>();
    private readonly NetworkVariable<int> _difficulty = new NetworkVariable<int>();
    private readonly NetworkVariable<bool> _ready = new NetworkVariable<bool>(false);

    // DifficultyLevel = Ascension + StageIndex*StageStep (level-system.md §3)
    private int ComposeDifficultyLevel() => Mathf.Max(0, ascensionLevel + stageIndex * stageStep);

    public override void OnNetworkSpawn()
    {
        if (mapGenerator == null)
        {
            Debug.LogError("[MapNetworkSync] mapGenerator 미배선 — 맵 생성 불가.");
            return;
        }

        if (IsServer)
        {
            int seed = Random.Range(int.MinValue, int.MaxValue);
            _seed.Value = seed;
            _difficulty.Value = ComposeDifficultyLevel();
            _ready.Value = true;                         // 복제 → 클라 트리거(레이트 조인 포함)
            mapGenerator.Generate(seed, _difficulty.Value);
        }
        else
        {
            if (_ready.Value) GenerateFromState();        // 이미 준비된 상태로 합류
            else _ready.OnValueChanged += OnReadyChanged; // 준비되면 생성
        }
    }

    public override void OnNetworkDespawn()
    {
        _ready.OnValueChanged -= OnReadyChanged;
    }

    private void OnReadyChanged(bool previous, bool current)
    {
        if (current) GenerateFromState();
    }

    private void GenerateFromState()
    {
        if (mapGenerator == null) return;
        mapGenerator.Generate(_seed.Value, _difficulty.Value);
    }
}
