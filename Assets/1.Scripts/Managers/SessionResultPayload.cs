using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// MapSceneManager 의 "MapScene.GoToResult" named message 페이로드(가변 크기).
///
/// 형식: bool hasValue — false 면 끝.
///       true 면 byte outcome · float survivalSeconds · byte count · count × 행.
///       행 = ulong clientId · int characterId · int damage · int kills · int counters · int deaths.
/// 플레이어 수가 <see cref="MaxPlayers"/> 를 넘으면 경고 후 앞(clientId 작은 쪽)부터 자른다.
/// </summary>
public static class SessionResultPayload
{
    /// <summary>행 상한. 리슨 서버 파티 인원보다 넉넉하게 잡은 안전 상한이다.</summary>
    public const int MaxPlayers = 8;

    private const int HeaderSize = sizeof(bool) + sizeof(byte) + sizeof(float) + sizeof(byte);
    private const int RowSize = sizeof(ulong) + sizeof(int) * 5;

    /// <summary>행 수에 맞는 버퍼 크기(상한 적용 후).</summary>
    public static int GetSize(int playerCount)
    {
        return HeaderSize + Mathf.Clamp(playerCount, 0, MaxPlayers) * RowSize;
    }

    public static void Write(ref FastBufferWriter writer, bool hasValue, SessionOutcome outcome,
        float survivalSeconds, IReadOnlyList<PlayerSessionStats> players)
    {
        writer.WriteValueSafe(hasValue);
        if (!hasValue)
            return;

        int total = players != null ? players.Count : 0;
        int count = Mathf.Min(total, MaxPlayers);
        if (total > MaxPlayers)
            Debug.LogWarning($"[SessionResultPayload] 플레이어 {total}명 — 상한 {MaxPlayers}명으로 자른다.");

        writer.WriteValueSafe((byte)outcome);
        writer.WriteValueSafe(survivalSeconds);
        writer.WriteValueSafe((byte)count);
        for (int i = 0; i < count; i++)
        {
            PlayerSessionStats row = players[i];
            writer.WriteValueSafe(row.ClientId);
            writer.WriteValueSafe(row.CharacterId);
            writer.WriteValueSafe(row.Damage);
            writer.WriteValueSafe(row.Kills);
            writer.WriteValueSafe(row.Counters);
            writer.WriteValueSafe(row.Deaths);
        }
    }

    /// <summary>
    /// 읽기. 형식이 깨졌으면(알 수 없는 결과 종류·상한 초과·길이 부족) false — 호출부는 결과 없이 전환한다.
    /// hasValue=false 도 정상 페이로드라 true 를 돌려준다.
    /// </summary>
    public static bool TryRead(ref FastBufferReader reader, out bool hasValue, out SessionOutcome outcome,
        out float survivalSeconds, List<PlayerSessionStats> players)
    {
        hasValue = false;
        outcome = default;
        survivalSeconds = 0f;
        players.Clear();

        try
        {
            reader.ReadValueSafe(out hasValue);
            if (!hasValue)
                return true;

            reader.ReadValueSafe(out byte outcomeByte);
            reader.ReadValueSafe(out survivalSeconds);
            reader.ReadValueSafe(out byte count);

            if (!Enum.IsDefined(typeof(SessionOutcome), outcomeByte) || count > MaxPlayers)
            {
                hasValue = false;
                return false;
            }

            outcome = (SessionOutcome)outcomeByte;
            for (int i = 0; i < count; i++)
            {
                PlayerSessionStats row = default;
                reader.ReadValueSafe(out row.ClientId);
                reader.ReadValueSafe(out row.CharacterId);
                reader.ReadValueSafe(out row.Damage);
                reader.ReadValueSafe(out row.Kills);
                reader.ReadValueSafe(out row.Counters);
                reader.ReadValueSafe(out row.Deaths);
                players.Add(row);
            }

            return true;
        }
        catch (OverflowException)
        {
            hasValue = false;
            players.Clear();
            return false;
        }
    }
}
