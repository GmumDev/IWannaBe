using System;
using UnityEngine;

namespace IWannabe.Stages
{
    /// <summary>
    /// 따라 치기(echo) 응답 대상의 배치. 콜이 울릴 때마다 대상이 하나씩 나타나 선반 위에 리듬 모양대로 줄서고,
    /// 응답 박 직전에 타격 지점으로 떨어져 정확히 그 박에 도착한다.
    /// </summary>
    public static class EchoShelf
    {
        /// <summary>콜 위치에서 선반 자리까지 날아가는 시간(박).</summary>
        public const double PopBeats = 0.25;
        /// <summary>선반을 떠나 타격 지점에 닿기까지의 시간(박).</summary>
        public const double DropBeats = 0.5;

        /// <summary>응답 구간 안에서 노트가 놓인 위치(0~1)를 선반 왼쪽→오른쪽 위의 점으로 바꾼다.</summary>
        public static Vector3 Slot(Vector3 left, Vector3 right, double noteBeat, double windowStart, double windowBeats)
        {
            float t = (float)((noteBeat - windowStart) / Math.Max(1e-3, windowBeats));
            return Vector3.LerpUnclamped(left, right, t);
        }

        /// <summary>
        /// 현재 박에서의 대상 위치. 콜 직후엔 콜 위치에서 선반으로 튀어 오르고, 선반에서 살짝 흔들리다가
        /// 응답 박 <see cref="DropBeats"/>박 전부터 가속하며 떨어진다. 응답 박을 지나면 그 방향으로 계속 떨어진다.
        /// </summary>
        public static Vector3 Position(Vector3 callPoint, Vector3 slot, Vector3 strike, double beat, double callBeat, double noteBeat)
        {
            double dropStart = noteBeat - DropBeats;
            if (beat >= dropStart)
            {
                float u = (float)((beat - dropStart) / DropBeats);
                return Vector3.LerpUnclamped(slot, strike, u * u);
            }

            float pop = Mathf.Clamp01((float)((beat - callBeat) / PopBeats));
            float eased = 1f - (1f - pop) * (1f - pop);
            var position = Vector3.Lerp(callPoint, slot, eased);
            position.y += 0.06f * Mathf.Sin((float)(beat * Math.PI * 2.0)) * pop;
            return position;
        }
    }
}
