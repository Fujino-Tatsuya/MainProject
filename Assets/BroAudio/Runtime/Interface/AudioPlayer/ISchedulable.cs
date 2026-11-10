namespace Ami.BroAudio
{
    public interface ISchedulable
    {
        /// <summary>
        /// Schedules the clip to start playing at a specific time on the absolute timeline that AudioSettings.dspTime reads from .
        /// </summary>
        /// <param name="dspTime">The absolute time in seconds when the clip should start playing.</param>
        /// <remarks>If the clip hasn't played yet, this is equivalent to AudioSource.PlayScheduled, otherwise, it reschedules the time</remarks>
        internal IAudioPlayer SetScheduledStartTime(double dspTime);

        ///<inheritdoc cref="UnityEngine.AudioSource.SetScheduledEndTime(double)(double)"/>
        internal IAudioPlayer SetScheduledEndTime(double dspTime);

        /// <summary>
        /// Delays the playback start time by the specified duration in seconds.
        /// </summary>
        /// <param name="time">The delay duration in seconds.</param>
        internal IAudioPlayer SetDelay(float time);

        // MainProject(2026-11-10, PLAN-sound-spec-apply Q11): 클립 앞부분을 건너뛰고 재생한다 — 완성음(낙하→충돌)의 충돌을
        // 실제 사건에 맞추려는데 그만큼 일찍 시작할 사건이 없을 때. 엔티티 클립의 StartPosition 에 더해진다. 재생 시작 전에만 유효.
        internal IAudioPlayer SetStartOffset(float seconds);
    }
}
