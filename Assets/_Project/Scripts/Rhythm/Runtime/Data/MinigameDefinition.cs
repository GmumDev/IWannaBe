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
    /// 미니게임(꿈) 하나: 연출 프리팹, 큐 효과음, 화면 색. 곡·채보는 갖지 않으며, 미니게임 스테이지와 리믹스가 함께 쓴다.
    /// 미니게임마다 Addressables 그룹 하나에 들어가, 여러 스테이지 번들이 복사하지 않고 공유한다.
    /// </summary>
    [CreateAssetMenu(menuName = "IWannabe/Rhythm/Minigame Definition", fileName = "Minigame")]
    public sealed class MinigameDefinition : ScriptableObject
    {
        [Tooltip("채보 구간(ChartSegment.minigameId)이 가리키는 ID.")]
        [SerializeField] string minigameId;
        [SerializeField] string displayName;
        [SerializeField] StagePresenter presenterPrefab;

        [Tooltip("이 미니게임의 cueId별 효과음. cueId는 미니게임 안에서만 유일하면 된다. 코어가 오디오 시계에 맞춰 미리 예약해 재생한다.")]
        [SerializeField] List<CueSound> cueSounds = new List<CueSound>();

        [Header("Camera / HUD")]
        [SerializeField] Color backgroundColor = new Color(0.99f, 0.94f, 0.84f);
        [Tooltip("배경 위에 올라가는 HUD 글자색. 어두운 배경이면 밝게 둔다.")]
        [SerializeField] Color hudInkColor = new Color(0.15f, 0.27f, 0.33f);
        [SerializeField, Min(1f)] float cameraSize = 5f;
        [Tooltip("세로 화면처럼 좁은 비율에서도 이 반폭(월드 단위)은 보이도록 카메라를 넓힌다.")]
        [SerializeField, Min(1f)] float minVisibleHalfWidth = 7.5f;

        public string MinigameId => minigameId;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public StagePresenter PresenterPrefab => presenterPrefab;
        public IReadOnlyList<CueSound> CueSounds => cueSounds;
        public Color BackgroundColor => backgroundColor;
        public Color HudInkColor => hudInkColor;
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

        public void Setup(string id, string title, StagePresenter presenter, List<CueSound> sounds, Color background, Color hudInk)
        {
            minigameId = id;
            displayName = title;
            presenterPrefab = presenter;
            cueSounds = sounds ?? new List<CueSound>();
            backgroundColor = background;
            hudInkColor = hudInk;
        }
    }
}
