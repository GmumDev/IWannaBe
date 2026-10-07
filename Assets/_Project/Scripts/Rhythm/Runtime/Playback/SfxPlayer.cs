using UnityEngine;

namespace IWannabe.Rhythm
{
    /// <summary>
    /// 효과음 재생기. 큐 효과음은 dsp 시각으로 예약해 정확한 박에 울리고,
    /// 타격음처럼 입력에 반응하는 소리는 즉시 재생한다.
    /// </summary>
    public sealed class SfxPlayer : MonoBehaviour
    {
        [SerializeField, Min(4)] int voiceCount = 16;

        AudioSource[] voices;
        double[] busyUntil;
        AudioSource immediate;

        void Awake()
        {
            voices = new AudioSource[voiceCount];
            busyUntil = new double[voiceCount];
            for (int i = 0; i < voiceCount; i++) voices[i] = CreateSource();
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

        public void PlayScheduled(AudioClip clip, double dspTime, float volume = 1f)
        {
            if (clip == null) return;
            int index = FindVoice();
            var voice = voices[index];
            voice.Stop();
            voice.clip = clip;
            voice.volume = volume;
            voice.PlayScheduled(dspTime);
            busyUntil[index] = dspTime + clip.length;
        }

        public void PlayNow(AudioClip clip, float volume = 1f)
        {
            if (clip != null) immediate.PlayOneShot(clip, volume);
        }

        /// <summary>예약된 소리까지 모두 멈춘다(일시정지 등).</summary>
        public void StopAll()
        {
            for (int i = 0; i < voices.Length; i++)
            {
                voices[i].Stop();
                busyUntil[i] = 0;
            }
            immediate.Stop();
        }

        int FindVoice()
        {
            double now = AudioSettings.dspTime;
            int oldest = 0;
            for (int i = 0; i < voices.Length; i++)
            {
                if (busyUntil[i] <= now) return i;
                if (busyUntil[i] < busyUntil[oldest]) oldest = i;
            }
            return oldest;
        }
    }
}
