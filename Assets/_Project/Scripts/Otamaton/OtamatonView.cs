using System;
using UnityEngine;

namespace IWannabe.Otamaton
{
    /// <summary>
    /// 플레이어 캐릭터 오타마톤. 몸통 위에 눈을 겹쳐 그린다(눈이 항상 앞).
    /// 평소엔 입을 다물고 있다가 입력 중에만 벌리고, miss가 나면 잠시 Hit 눈으로 바뀐다.
    /// Hit 눈은 일정 시간이 지나거나 성공 판정이 나면 돌아온다. 시간은 모두 곡 시간(초) 기준이다.
    /// </summary>
    public sealed class OtamatonView : MonoBehaviour
    {
        [SerializeField] SpriteRenderer bodyRenderer;
        [SerializeField] SpriteRenderer eyeRenderer;
        [SerializeField] OtamatonBody body = new OtamatonBody();
        [SerializeField] OtamatonEyes eyes = new OtamatonEyes();

        [Tooltip("짧게 톡 눌러도 입 벌린 모습이 보이도록 최소한 벌리고 있는 시간(초).")]
        [SerializeField, Min(0f)] float minOpenSeconds = 0.1f;
        [Tooltip("miss 뒤 Hit 눈을 유지하는 시간(초). 그 전에 성공 판정이 나면 바로 돌아온다.")]
        [SerializeField, Min(0f)] float hitSeconds = 1f;

        int heldInputs;
        double openUntil = double.NegativeInfinity;
        double hitUntil = double.NegativeInfinity;

        void Awake()
        {
            // 꾸미기에서 고른 모습이 있으면 그걸 쓰고, 없으면 프리팹에 넣어 둔 기본 모습을 쓴다.
            if (OtamatonAppearance.Body != null) body = OtamatonAppearance.Body;
            if (OtamatonAppearance.Eyes != null) eyes = OtamatonAppearance.Eyes;
            Rest();
        }

        /// <summary>입력원이 눌렸을 때. 여러 입력원을 겹쳐 누르면 모두 뗄 때까지 입을 벌리고 있다.</summary>
        public void Press(double time)
        {
            heldInputs++;
            openUntil = time + minOpenSeconds;
        }

        public void Release() => heldInputs = Math.Max(0, heldInputs - 1);

        /// <summary>노트 판정에 반응한다. miss면 Hit 눈으로 바꾸고, 성공이면 바로 원래 눈으로 돌린다.</summary>
        public void Judged(bool missed, double time) => hitUntil = missed ? time + hitSeconds : double.NegativeInfinity;

        /// <summary>입을 다물고 원래 눈으로 돌린다(일시정지·종료). Tick이 멈춰도 바로 반영된다.</summary>
        public void Rest()
        {
            heldInputs = 0;
            openUntil = double.NegativeInfinity;
            hitUntil = double.NegativeInfinity;
            Show(false, false);
        }

        public void Tick(double time) => Show(heldInputs > 0 || time < openUntil, time < hitUntil);

        void Show(bool open, bool hit)
        {
            bodyRenderer.sprite = body.Pick(open);
            eyeRenderer.sprite = eyes.Pick(open, hit);
        }
    }
}
