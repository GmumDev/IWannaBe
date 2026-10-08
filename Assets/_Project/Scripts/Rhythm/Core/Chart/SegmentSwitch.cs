using System;
using System.Collections.Generic;

namespace IWannabe.Rhythm
{
    /// <summary>
    /// 리믹스 구간 전환 시각을 정한다. 기본은 구간 시작 박이지만, 입력(누름·뗌)과 겹치거나 너무 가까우면
    /// 그 시각이 들어 있는 입력 사이 빈틈으로 옮긴다. 빈틈이 넉넉하면 구간 시작 박에서 가장 가까우면서 양쪽 입력과
    /// <see cref="ClearanceSeconds"/>만큼 떨어진 시각으로, 좁으면 빈틈 한가운데로 옮긴다.
    /// 전환은 앞 구간의 큐가 모두 지난 뒤, 다음 구간의 첫 큐보다 앞이어야 한다(큐 모습이 그 큐의 미니게임에서 나오도록).
    /// 채보 생성기는 이 조건을 지킬 수 있는 자리만 만든다(<see cref="Charting.ChartGenerator"/>).
    /// </summary>
    public static class SegmentSwitch
    {
        /// <summary>전환과 입력 사이에 두려는 간격(초). 판정 범위(아슬아슬 ±0.1초)보다 조금 넓다.</summary>
        public const double ClearanceSeconds = 0.12;

        /// <param name="boundaryTime">구간 시작 박의 시각.</param>
        /// <param name="earliest">앞 구간의 마지막 큐 시각. 이보다 앞에서 바꾸면 그 큐가 다음 미니게임으로 간다.</param>
        /// <param name="latest">다음 구간의 첫 큐(사건) 시각. 이보다 뒤에서 바꾸면 그 큐 모습이 늦게 나온다.</param>
        /// <param name="inputs">모든 입력 시각(시간순).</param>
        public static double Find(double boundaryTime, double earliest, double latest, IReadOnlyList<double> inputs)
        {
            double before = double.NegativeInfinity;
            double after = double.PositiveInfinity;
            foreach (double t in inputs)
            {
                if (t <= boundaryTime)
                {
                    before = t;
                    continue;
                }
                after = t;
                break;
            }

            double time = boundaryTime;
            if (boundaryTime - before < ClearanceSeconds || after - boundaryTime < ClearanceSeconds)
            {
                time = after - before >= 2 * ClearanceSeconds
                    ? Math.Max(before + ClearanceSeconds, Math.Min(boundaryTime, after - ClearanceSeconds))
                    : (before + after) / 2;
            }

            // 큐 조건이 우선이다. 둘이 어긋나는 채보(손으로 고친 경우 등)는 앞 구간 큐가 다음 미니게임으로 새지 않는 쪽을 택한다.
            return Math.Max(Math.Min(time, latest), earliest);
        }

        /// <summary>시각에서 가장 가까운 입력까지의 거리(초). 입력이 없으면 양의 무한대.</summary>
        public static double Clearance(double time, IReadOnlyList<double> inputs)
        {
            double nearest = double.PositiveInfinity;
            foreach (double t in inputs) nearest = Math.Min(nearest, Math.Abs(t - time));
            return nearest;
        }
    }
}
