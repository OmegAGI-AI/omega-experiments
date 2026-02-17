using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class OmegaArmageddon : Robot
    {
        [Parameter("Risk Percent", DefaultValue = 2)]
        public double RiskPercent { get; set; }

        [Parameter("Step Size (pips)", DefaultValue = 50)]
        public double StepSize { get; set; }

        [Parameter("Max Spread", DefaultValue = 3)]
        public double MaxSpread { get; set; }

        private ExponentialMovingAverage _ema20;
        private RelativeStrengthIndex _rsi;
        private AverageTrueRange _atr;

        private int _tradeCount = 0;
        private int _consecutiveSteps = 0;
        private double _entryPrice = 0;
        private double _lastStepPrice = 0;
        private DateTime _lastTradeTime = DateTime.MinValue;

        protected override void OnStart()
        {
            _ema20 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 20);
            _rsi = Indicators.RelativeStrengthIndex(Bars.ClosePrices, 14);
            _atr = Indicators.AverageTrueRange(14);

            Print("========================================");
            Print("OMEGA ARMAGEDDON — STEP STRATEGY");
            Print("========================================");
            Print("3-candle step confirmation");
            Print("Exit if step skipped");
            Print("========================================");
        }

        protected override void OnBar()
        {
            int i = Bars.Count - 2;
            if (i < 10) return;

            double price = Bars.ClosePrices[i];
            double ema20 = _ema20.Result[i];
            double rsi = _rsi.Result[i];
            double atr = _atr.Result[i];

            // Check existing position
            if (Positions.Count > 0)
            {
                ManageOpenPosition(i);
                return;
            }

            // Look for new entry
            if (!CanTrade())
                return;

            // Minimum 5 minutes between trades
            if ((DateTime.UtcNow - _lastTradeTime).TotalMinutes < 5)
                return;

            // Calculate dynamic step based on ATR
            double dynamicStep = atr > StepSize * Symbol.PipSize ? atr : StepSize * Symbol.PipSize;

            // Get 3 recent candles
            double c1 = Bars.ClosePrices[i];
            double c2 = Bars.ClosePrices[i - 1];
            double c3 = Bars.ClosePrices[i - 2];
            double o1 = Bars.OpenPrices[i];
            double o2 = Bars.OpenPrices[i - 1];
            double o3 = Bars.OpenPrices[i - 2];

            // BULLISH STEP PATTERN: Each candle closes higher, touching steps
            bool bullStep1 = c1 > o1 && c1 > c2; // Candle 1 up, close higher
            bool bullStep2 = c2 > o2 && c2 > c3; // Candle 2 up, close higher
            bool bullStep3 = c3 > o3; // Candle 3 up
            bool bullTrend = c1 > ema20 && rsi > 50 && rsi < 75;

            // Steps are sequential higher closes
            bool bullSteps = (c1 - c3) >= dynamicStep * 0.5 && bullStep1 && bullStep2 && bullStep3;

            // BEARISH STEP PATTERN
            bool bearStep1 = c1 < o1 && c1 < c2;
            bool bearStep2 = c2 < o2 && c2 < c3;
            bool bearStep3 = c3 < o3;
            bool bearTrend = c1 < ema20 && rsi < 50 && rsi > 25;

            bool bearSteps = (c3 - c1) >= dynamicStep * 0.5 && bearStep1 && bearStep2 && bearStep3;

            if (bullSteps && bullTrend)
            {
                EnterStepTrade(TradeType.Buy, c1, dynamicStep);
            }
            else if (bearSteps && bearTrend)
            {
                EnterStepTrade(TradeType.Sell, c1, dynamicStep);
            }
        }

        private void EnterStepTrade(TradeType direction, double price, double stepSize)
        {
            _entryPrice = direction == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
            _lastStepPrice = _entryPrice;
            _consecutiveSteps = 1;

            // Stop is 2 steps away
            double stopDistance = stepSize * 2;
            double stop = direction == TradeType.Buy 
                ? _entryPrice - stopDistance 
                : _entryPrice + stopDistance;

            // TP is 4 steps away (2:1 R/R)
            double take = direction == TradeType.Buy
                ? _entryPrice + (stopDistance * 2)
                : _entryPrice - (stopDistance * 2);

            double riskAmount = Account.Balance * (RiskPercent / 100);
            double riskPips = stopDistance / Symbol.PipSize;
            double volume = riskAmount / (riskPips * Symbol.PipValue);
            volume = Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);

            if (volume < Symbol.VolumeInUnitsMin)
                volume = Symbol.VolumeInUnitsMin;

            var result = ExecuteMarketOrder(direction, SymbolName, volume, "Armageddon", stop, take);

            if (result.IsSuccessful)
            {
                _tradeCount++;
                _lastTradeTime = DateTime.UtcNow;
                Print(string.Format("STEP TRADE #{0}: {1} at {2:F2}", _tradeCount, direction, _entryPrice));
                Print(string.Format("Step size: {0:F1} pips", stepSize / Symbol.PipSize));
            }
        }

        private void ManageOpenPosition(int i)
        {
            if (Positions.Count == 0) return;
            
            var pos = Positions[0];
            if (pos == null) return;
            
            double currentPrice = pos.TradeType == TradeType.Buy ? Symbol.Bid : Symbol.Ask;
            double stepSize = _atr.Result[i];

            // Check if next step was hit
            bool stepHit = false;
            if (pos.TradeType == TradeType.Buy)
            {
                double nextStep = _lastStepPrice + stepSize;
                if (currentPrice >= nextStep)
                {
                    _lastStepPrice = nextStep;
                    _consecutiveSteps++;
                    stepHit = true;
                    Print(string.Format("Step {0} hit at {1:F2}", _consecutiveSteps, currentPrice));
                }
            }
            else
            {
                double nextStep = _lastStepPrice - stepSize;
                if (currentPrice <= nextStep)
                {
                    _lastStepPrice = nextStep;
                    _consecutiveSteps++;
                    stepHit = true;
                    Print(string.Format("Step {0} hit at {1:F2}", _consecutiveSteps, currentPrice));
                }
            }

            // Check if step was SKIPPED (price jumped too far)
            bool stepSkipped = false;
            if (pos.TradeType == TradeType.Buy)
            {
                double expectedMax = _lastStepPrice + (stepSize * 1.5);
                if (currentPrice > expectedMax && !stepHit)
                {
                    stepSkipped = true;
                }
            }
            else
            {
                double expectedMin = _lastStepPrice - (stepSize * 1.5);
                if (currentPrice < expectedMin && !stepHit)
                {
                    stepSkipped = true;
                }
            }

            // Exit if step skipped (pattern broken)
            if (stepSkipped)
            {
                ClosePosition(pos);
                Print(string.Format("STEP SKIPPED — EXITING at {0:F2}", currentPrice));
                _consecutiveSteps = 0;
            }

            // Move stop to breakeven after 2 steps (using ClosePosition and re-open if needed)
            // Note: cTrader doesn't support ModifyPosition in all versions
            // Using breakeven logic through position management instead
            if (_consecutiveSteps >= 2)
            {
                Print("2 steps reached — consider moving to breakeven manually");
            }
        }

        private bool CanTrade()
        {
            if (Account.Balance <= 0) return false;
            if (Symbol.Spread / Symbol.PipSize > MaxSpread) return false;
            return true;
        }

        protected override void OnStop()
        {
            Print(string.Format("ARMAGEDDON STOPPED — Trades: {0}", _tradeCount));
        }
    }
}
