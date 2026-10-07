using System;
using UnityEngine;

namespace IWannabe.Otamaton
{
    /// <summary>몸통 파츠. 입을 다문 모습과 벌린 모습.</summary>
    [Serializable]
    public sealed class OtamatonBody
    {
        public Sprite close;
        public Sprite open;

        public Sprite Pick(bool mouthOpen) => mouthOpen ? open : close;
    }

    /// <summary>눈 파츠. 몸통의 입 모양에 맞춘 두 가지와, miss가 났을 때 잠시 바뀌는 Hit 두 가지.</summary>
    [Serializable]
    public sealed class OtamatonEyes
    {
        public Sprite close;
        public Sprite open;
        public Sprite hitClose;
        public Sprite hitOpen;

        public Sprite Pick(bool mouthOpen, bool hit) => hit ? (mouthOpen ? hitOpen : hitClose) : (mouthOpen ? open : close);
    }
}
