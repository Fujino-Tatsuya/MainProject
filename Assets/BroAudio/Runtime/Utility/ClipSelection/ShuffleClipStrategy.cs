using System.Collections.Generic;
using Ami.BroAudio.Data;
using UnityEngine;

namespace Ami.BroAudio.Runtime
{
    public class ShuffleClipStrategy : IClipSelectionStrategy
    {
        private BroAudioClip _lastUsed;
        private readonly HashSet<BroAudioClip> _used = new HashSet<BroAudioClip>();

        // MainProject(2026-11-10, PLAN-sound-spec-apply 2단계): 원본 선택 로직은 직전 클립을 다시 고를 수 있었다
        // (반환 경로에서 _lastUsed 미갱신 · 직전을 만나면 _lastUsed = null). 사운드 명세 "직전 파일 제외 랜덤"을 보장하도록
        // 이 메서드만 다시 썼다 — 유효 클립(IsSet) 중 직전을 뺀 후보에서 고르고, 한 바퀴(셔플 백) 안에서는 안 쓴 것을 먼저 고른다.
        // 유효 클립이 1개면 그대로 반복한다. BroAudio 패키지를 갱신하면 이 수정이 사라진다 — Docs/tech/handoff-sound-spec-apply.md.
        private readonly List<int> _candidates = new List<int>();

        public IBroAudioClip SelectClip(BroAudioClip[] clips, ClipSelectionContext context, out int index)
        {
            index = 0;
            if (Utility.ClipListIsNullOrEmpty(clips, context.EntityName))
            {
                return null;
            }

            int validCount = 0;
            int onlyValid = -1;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null && clips[i].IsSet)
                {
                    validCount++;
                    onlyValid = i;
                }
            }

            if (validCount == 0)
            {
                return null;
            }

            if (validCount == 1)
            {
                index = onlyValid;
                _lastUsed = clips[index];
                return _lastUsed;
            }

            // 1순위: 이번 바퀴에 안 쓴 것 중 직전 제외. 없으면 바퀴를 새로 시작(직전 제외는 유지).
            CollectCandidates(clips, skipUsed: true);
            if (_candidates.Count == 0)
            {
                Reset();
                CollectCandidates(clips, skipUsed: true);
            }

            index = _candidates[Random.Range(0, _candidates.Count)];
            BroAudioClip result = clips[index];
            _used.Add(result);
            _lastUsed = result;
            return result;
        }

        private void CollectCandidates(BroAudioClip[] clips, bool skipUsed)
        {
            _candidates.Clear();
            for (int i = 0; i < clips.Length; i++)
            {
                BroAudioClip clip = clips[i];
                if (clip == null || !clip.IsSet || clip == _lastUsed)
                {
                    continue;
                }

                if (skipUsed && _used.Contains(clip))
                {
                    continue;
                }

                _candidates.Add(i);
            }
        }

        public void Reset()
        {
            _used.Clear();
        }
    }
}
