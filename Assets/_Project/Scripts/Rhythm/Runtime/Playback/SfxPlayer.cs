using System.Collections.Generic;
using UnityEngine;

namespace IWannabe.Rhythm
{
    /// <summary>
    /// 효과음 재생기. 큐 효과음은 dsp 시각으로 예약해 정확한 박에 울리고,
    /// 타격음처럼 입력에 반응하는 소리는 즉시 재생한다.
    /// 예약한 큐 소리는 빠뜨리지 않는다. 빈 목소리가 없으면 목소리를 늘리고(앞서 예약한 소리를 빼앗지 않는다),
    /// 큐 소리 목소리는 우선순위를 가장 높게 둬 다른 소리에 밀려 꺼지지 않게 한다.
    /// </summary>
    public sealed class SfxPlayer : MonoBehaviour
    {
        /// <summary>AudioSource.priority의 가장 높은 값.</summary>
        const int CuePriority = 0;

        [SerializeField, Min(4)] int voiceCount = 16;

        readonly List<AudioSource> voices = new List<AudioSource>();
        readonly List<double> busyUntil = new List<double>();
        AudioSource immediate;

        void Awake()
        {
            for (int i = 0; i < voiceCount; i++) AddVoice();
            immediate = CreateSource();
        }

        AudioSource CreateSource()
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            return source;
        }

        int AddVoice()
        {
            var voice = CreateSource();
            voice.priority = CuePriority;
            voices.Add(voice);
            busyUntil.Add(0);
            return voices.Count - 1;
        }

        /// <summary>dsp 시각에 울리도록 예약한다. 이미 지난 시각이면 바로 울린다.</summary>
        public void PlayScheduled(AudioClip clip, double dspTime, float volume = 1f)
        {
            if (clip == null) return;
            double now = AudioSettings.dspTime;
            int index = FindFreeVoice(now);
            var voice = voices[index];
            voice.Stop();
            voice.clip = clip;
            voice.volume = volume;
            if (dspTime > now) voice.PlayScheduled(dspTime);
            else voice.Play();
            busyUntil[index] = System.Math.Max(dspTime, now) + clip.length;
        }

        public void PlayNow(AudioClip clip, float volume = 1f)
        {
            if (clip != null) immediate.PlayOneShot(clip, volume);
        }

        /// <summary>예약된 소리까지 모두 멈춘다(일시정지 등).</summary>
        public void StopAll()
        {
            for (int i = 0; i < voices.Count; i++)
            {
                voices[i].Stop();
                busyUntil[i] = 0;
            }
            immediate.Stop();
        }

        int FindFreeVoice(double now)
        {
            for (int i = 0; i < voices.Count; i++)
                if (busyUntil[i] <= now) return i;
            return AddVoice();
        }
    }
}
