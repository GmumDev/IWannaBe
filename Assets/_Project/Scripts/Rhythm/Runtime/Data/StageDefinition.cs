using System;
using System.Collections.Generic;
using UnityEngine;

namespace IWannabe.Rhythm
{
    [Serializable]
    public sealed class CueSound
    {
        public string cueId;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 1f;
    }

    /// <summary>
    /// 스테이지 하나를 이루는 모든 것(곡, 채보, 연출 프리팹, 큐 효과음).
    /// 스테이지마다 Addressables 그룹 하나에 들어가며, 이 에셋을 로드하면 의존 에셋이 함께 로드된다.
    /// </summary>
    [CreateAssetMenu(menuName = "IWannabe/Rhythm/Stage Definition", fileName = "StageDefinition")]
    public sealed class StageDefinition : ScriptableObject
    {
        [SerializeField] string stageId;
        [SerializeField] string displayName;
        [SerializeField] SongData song;
        [SerializeField] ChartData chart;
        [SerializeField] StagePresenter presenterPrefab;

        [Tooltip("채보의 cueId별로 울릴 효과음. 코어가 오디오 시계에 맞춰 미리 예약해 재생한다.")]
        [SerializeField] List<CueSound> cueSounds = new List<CueSound>();

        [SerializeField, Range(0f, 1f)] float musicVolume = 0.9f;

        [Header("Camera")]
        [SerializeField] Color backgroundColor = new Color(0.99f, 0.94f, 0.84f);
        [SerializeField, Min(1f)] float cameraSize = 5f;
        [Tooltip("세로 화면처럼 좁은 비율에서도 이 반폭(월드 단위)은 보이도록 카메라를 넓힌다.")]
        [SerializeField, Min(1f)] float minVisibleHalfWidth = 7.5f;

        public string StageId => stageId;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public SongData Song => song;
        public ChartData Chart => chart;
        public StagePresenter PresenterPrefab => presenterPrefab;
        public IReadOnlyList<CueSound> CueSounds => cueSounds;
        public float MusicVolume => musicVolume;
        public Color BackgroundColor => backgroundColor;
        public float CameraSize => cameraSize;
        public float MinVisibleHalfWidth => minVisibleHalfWidth;

        public bool TryGetCueSound(string cueId, out CueSound sound)
        {
            foreach (var cue in cueSounds)
            {
                if (cue != null && cue.cueId == cueId)
                {
                    sound = cue;
                    return true;
                }
            }
            sound = null;
            return false;
        }

        public void Setup(string id, string title, SongData songData, ChartData chartData, StagePresenter presenter,
            List<CueSound> sounds, Color background)
        {
            stageId = id;
            displayName = title;
            song = songData;
            chart = chartData;
            presenterPrefab = presenter;
            cueSounds = sounds ?? new List<CueSound>();
            backgroundColor = background;
        }
    }
}
