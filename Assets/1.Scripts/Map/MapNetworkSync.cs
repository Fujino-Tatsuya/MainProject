using Unity.Netcode;
using UnityEngine;

// 맵 생성 네트워크 진입점.
//  - 서버: 시드 결정 → Generate (비네트워크 시각물 로컬 생성 + NetworkObject 프리팹은 서버 Spawn → NGO 복제)
//  - 클라: 같은 시드로 Generate → 비네트워크 시각물만 로컬 생성 (NetworkObject는 복제로 수신,
//          MapContentSpawner가 클라에서 네트워크 프리팹 생성을 건너뜀)
// 로딩씬(은희) 통합 시 OnNetworkSpawn 대신 로딩 플로우에서 호출하는 형태로 바뀔 수 있음.
public class MapNetworkSync : NetworkBehaviour
{
    [SerializeField] private MapGenerator mapGenerator;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            int randomSeed = Random.Range(int.MinValue, int.MaxValue);
            Difficulty selectedDiff = Difficulty.Normal; // 임시 난이도 고정

            mapGenerator.Generate(randomSeed, selectedDiff);

            // 클라이언트도 같은 시드로 시각물 생성
            GenerateMapClientRpc(randomSeed, selectedDiff);
        }
    }

    [ClientRpc]
    private void GenerateMapClientRpc(int seed, Difficulty difficulty)
    {
        if (IsServer) return; // 호스트는 위에서 이미 실행

        mapGenerator.Generate(seed, difficulty);
    }
}
