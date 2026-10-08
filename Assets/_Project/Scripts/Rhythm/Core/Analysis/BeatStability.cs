using System;
using System.Collections.Generic;

namespace IWannabe.Rhythm.Charting
{
    public enum BeatStabilityVerdict
    {
        /// <summary>BPM 하나짜리 격자가 곡 전체에 맞는다.</summary>
        Stable,
        /// <summary>격자 하나로는 안 맞지만 템포가 천천히 흔들릴 뿐이라 비트별 시각으로 따라갈 수 있다(라이브 녹음 등).</summary>
        Variable,
        /// <summary>템포가 크게 바뀌거나 박자가 자유로워 비트를 믿을 수 없다.</summary>
        Unstable,
    }

    [Flags]
    public enum BeatStabilityFlags
    {
        None = 0,
        /// <summary>비트를 찾은 구간이 곡의 일부뿐이다(긴 인트로·아웃트로, 중간에 박이 없는 구간).</summary>
        LowCoverage = 1,
        /// <summary>3박자 곡으로 보인다. 마디는 4박 기준으로 나뉜다.</summary>
        TripleMeter = 2,
        /// <summary>박 위치의 온셋이 주변보다 별로 두드러지지 않는다.</summary>
        WeakPulse = 4,
    }

    /// <summary>
    /// 박자가 안정적인지 판정하는 기준. "내 음악으로 플레이"에서 분석한 곡을 받을지 정한다.
    /// 기본값은 합성 곡(정답 비트를 아는 곡)과 실제 곡에 분석기를 돌려 정한 초안이다.
    /// </summary>
    [Serializable]
    public sealed class BeatStabilityCriteria
    {
        /// <summary>고정 격자와 이 시간(초) 안에서 맞는 비트를 맞는 비트로 센다.</summary>
        public double gridTolerance = 0.03;
        /// <summary>Stable: 고정 격자에 맞는 비트 비율의 최솟값.</summary>
        public double minGridInlierRatio = 0.9;
        /// <summary>Stable: 고정 격자와의 차 90번째 백분위의 최댓값(초).</summary>
        public double maxGridErrorP90 = 0.025;
        /// <summary>온셋 격자: 8박 창마다 가장 잘 맞는 어긋남이 이 시간(초) 안이면 그 창은 격자에 맞는 것으로 센다.</summary>
        public double onsetWindowTolerance = 0.02;
        /// <summary>Stable: 온셋 격자에 맞는 창 비율의 최솟값. 원래 비트가 격자에 안 맞아도 이걸 넘으면 고정 템포다.</summary>
        public double minOnsetGridRatio = 0.9;
        /// <summary>Variable: 지역 회귀(±4박)와의 차 중앙값의 최댓값(초).</summary>
        public double maxLocalJitter = 0.015;
        /// <summary>Variable: 이웃한 8박 창 사이 템포 변화의 최댓값(비율).</summary>
        public double maxTempoJump = 0.06;
        /// <summary>Variable: 곡 전체 지역 템포 폭(5~95%)의 최댓값(비율).</summary>
        public double maxTempoRange = 0.12;
        /// <summary>박 위치 온셋 강도가 격자 평균의 이 배수보다 작으면 박이 약한 것으로 본다.</summary>
        public double minBeatContrast = 1.6;
        /// <summary>비트를 찾은 구간 비율이 이보다 작으면 LowCoverage.</summary>
        public double minCoverage = 0.7;
        /// <summary>3박 위상 대비가 4박 위상 대비의 이 배수보다 크면 TripleMeter.</summary>
        public double tripleMeterRatio = 1.5;
    }

    /// <summary>
    /// 곡 분석 중에 잰 박자 안정성 지표와 판정.
    /// 분석기는 동적 계획법으로 비트를 따라간 뒤(원래 비트) 고정 템포면 직선 격자로, 아니면 지역 회귀로 다듬는다.
    /// 여기 지표는 원래 비트가 그 두 방식에 얼마나 맞는지와 박이 얼마나 뚜렷한지를 잰다.
    /// </summary>
    public sealed class BeatStability
    {
        public BeatStabilityVerdict Verdict;
        public BeatStabilityFlags Flags;
        /// <summary>판정 이유(사람이 읽는 문장).</summary>
        public string Reason = "";

        /// <summary>원래 비트 개수.</summary>
        public int TrackedBeats;
        /// <summary>원래 비트의 첫 박~마지막 박 길이 / 곡 길이.</summary>
        public double Coverage;
        /// <summary>원래 비트 간격의 변동계수(간격이 중앙값의 0.8~1.2배인 것만).</summary>
        public double IntervalCv;
        /// <summary>고정 격자(직선 회귀)와 허용 오차 안에서 맞는 원래 비트 비율.</summary>
        public double GridInlierRatio;
        /// <summary>고정 격자와의 차(절댓값) 90번째 백분위(초).</summary>
        public double GridErrorP90;
        /// <summary>
        /// 곡의 온셋에 직접 맞춘 고정 격자를 8박 창마다 밀어 봤을 때 어긋남이 허용 오차 안인 창 비율(온셋이 거의 없는 창은 뺀다).
        /// 원래 비트가 인트로에서 엇박으로 미끄러지거나 박을 하나 더 넣어도, 곡의 템포가 일정하면 높다.
        /// </summary>
        public double OnsetGridRatio;
        /// <summary>창마다의 온셋 격자 어긋남(절댓값) 90번째 백분위(초).</summary>
        public double OnsetGridErrorP90;
        /// <summary>지역 회귀(±4박)와의 차(절댓값) 중앙값(초). 비트 추적이 흔들리는 정도.</summary>
        public double LocalJitter;
        /// <summary>8박 창 지역 템포의 5~95% 폭 / 중앙값.</summary>
        public double TempoRange;
        /// <summary>이웃한 8박 창(4박씩 이동) 사이 템포 변화의 최댓값(비율).</summary>
        public double TempoJump;
        /// <summary>온셋 자기상관의 템포 주기 피크 / 0지연 값(0~1). 클수록 주기성이 뚜렷하다.</summary>
        public double PulseClarity;
        /// <summary>박 위치(격자 0번 칸)의 평균 온셋 강도 / 박 안 12칸 전체 평균.</summary>
        public double BeatContrast;
        /// <summary>다운비트 위상 점수 1위와 2위의 차 / 1위.</summary>
        public double DownbeatConfidence;
        /// <summary>3박 위상 대비 / 4박 위상 대비. 클수록 3박자에 가깝다.</summary>
        public double TripleMeterScore;

        public bool Accepts => Verdict != BeatStabilityVerdict.Unstable;

        /// <summary>BPM 하나짜리 격자가 곡에 맞는지(원래 비트로든 온셋으로든). 분석기가 고정 템포로 다듬을지를 이것으로 정한다.</summary>
        public bool FitsConstantGrid(BeatStabilityCriteria criteria) => FitsByBeats(criteria) || FitsByOnsets(criteria);

        /// <summary>원래 비트가 직선 격자에 맞는지. 맞으면 분석기는 원래 비트로 격자를 정한다.</summary>
        public bool FitsByBeats(BeatStabilityCriteria criteria)
        {
            criteria = criteria ?? new BeatStabilityCriteria();
            return GridInlierRatio >= criteria.minGridInlierRatio && GridErrorP90 <= criteria.maxGridErrorP90;
        }

        /// <summary>온셋에 맞춘 격자가 곡 전체에서 온셋에 맞는지. 원래 비트가 안 맞을 때 분석기는 이 격자를 쓴다.</summary>
        public bool FitsByOnsets(BeatStabilityCriteria criteria)
        {
            criteria = criteria ?? new BeatStabilityCriteria();
            return OnsetGridRatio >= criteria.minOnsetGridRatio;
        }

        /// <summary>지표로 판정과 표시를 정한다.</summary>
        public void Judge(BeatStabilityCriteria criteria)
        {
            criteria = criteria ?? new BeatStabilityCriteria();
            Flags = BeatStabilityFlags.None;
            if (Coverage < criteria.minCoverage) Flags |= BeatStabilityFlags.LowCoverage;
            if (TripleMeterScore > criteria.tripleMeterRatio) Flags |= BeatStabilityFlags.TripleMeter;
            if (BeatContrast < criteria.minBeatContrast) Flags |= BeatStabilityFlags.WeakPulse;

            var reasons = new List<string>();
            bool gridFits = FitsConstantGrid(criteria);
            bool smooth = LocalJitter <= criteria.maxLocalJitter && TempoJump <= criteria.maxTempoJump && TempoRange <= criteria.maxTempoRange;
            bool pulse = (Flags & BeatStabilityFlags.WeakPulse) == 0;

            if (!pulse) reasons.Add($"박이 뚜렷하지 않음(대비 {BeatContrast:0.00} < {criteria.minBeatContrast:0.00})");
            if (pulse && gridFits)
            {
                Verdict = BeatStabilityVerdict.Stable;
            }
            else if (pulse && smooth)
            {
                Verdict = BeatStabilityVerdict.Variable;
                reasons.Add(GridMismatch());
            }
            else
            {
                Verdict = BeatStabilityVerdict.Unstable;
                if (pulse) reasons.Add(GridMismatch());
                if (LocalJitter > criteria.maxLocalJitter) reasons.Add($"비트 추적이 흔들림({LocalJitter * 1000:0}ms)");
                if (TempoJump > criteria.maxTempoJump) reasons.Add($"템포가 갑자기 바뀜({TempoJump:P0})");
                if (TempoRange > criteria.maxTempoRange) reasons.Add($"템포 폭이 큼({TempoRange:P0})");
            }
            if ((Flags & BeatStabilityFlags.LowCoverage) != 0) reasons.Add($"비트를 찾은 구간이 곡의 {Coverage:P0}");
            if ((Flags & BeatStabilityFlags.TripleMeter) != 0) reasons.Add($"3박자로 보임({TripleMeterScore:0.0})");
            Reason = string.Join(", ", reasons);
        }

        string GridMismatch() =>
            $"고정 격자와 어긋남(비트 {GridInlierRatio:P0}·90% 오차 {GridErrorP90 * 1000:0}ms, 온셋 창 {OnsetGridRatio:P0}·90% 어긋남 {OnsetGridErrorP90 * 1000:0}ms)";

        public override string ToString() =>
            $"{Verdict}{(Flags != BeatStabilityFlags.None ? $" [{Flags}]" : "")} " +
            $"격자 {GridInlierRatio:P0}/{GridErrorP90 * 1000:0}ms, 온셋 격자 {OnsetGridRatio:P0}/{OnsetGridErrorP90 * 1000:0}ms, 흔들림 {LocalJitter * 1000:0.0}ms, " +
            $"템포 폭 {TempoRange:P1}·변화 {TempoJump:P1}, 주기성 {PulseClarity:0.00}, 박 대비 {BeatContrast:0.00}, " +
            $"구간 {Coverage:P0}, 다운비트 {DownbeatConfidence:0.00}, 3박 {TripleMeterScore:0.0}";
    }
}
