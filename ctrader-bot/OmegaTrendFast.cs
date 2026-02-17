using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class OmegaTrendFast : Robot
    {
        [Parameter("Risk Percent", DefaultValue = 2)]
        public double RiskPercent { get; set; }

        [Parameter("Max Spread", DefaultValue = 3)]
        public double MaxSpread { get; set; }

        [Parameter("Max Positions", DefaultValue = 2)]
        public int MaxPositions { get; set; }

        private ExponentialMovingAverage _ema20;
        private ExponentialMovingAverage _ema50;
        private ExponentialMovingAverage _ema200;

        private int _tradeCount = 0;

        protected override void OnStart()
        {
            _ema20 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 20);
            _ema50 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 50);
            _ema200 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 200);

            Print("TREND FAST BOT STARTED");
            Print("Trades on ANY trend alignment");
        }

        protected override void OnBar()
        {
            if (!CanTrade())
                return;

            int i = Bars.Count - 1;
            if (i < 200) return;

            double price = Bars.ClosePrices[i];
            double ema20 = _ema20.Result[i];
            double ema50 = _ema50.Result[i];
            double ema200 = _ema200.Result[i];

            // Relaxed trend check
            bool uptrend = price > ema20 && ema20 > ema50;
            bool downtrend = price < ema20 && ema20 < ema50;

            // Pullback check (relaxed to 30 pips)
            double distEma20 = Math.Abs(price - ema20) / Symbol.PipSize;
            bool nearEma20 = distEma20 < 30;

            if (uptrend && nearEma20)
            {
                EnterTrade(TradeType.Buy, price, ema50);
            }
            else if (downtrend && nearEma20)
            {
                EnterTrade(TradeType.Sell, price, ema50);
            }
        }

        private bool CanTrade()
        {
            if (Account.Balance <= 0) return false;
            if (Positions.Count >= MaxPositions) return false;
            if (Symbol.Spread / Symbol.PipSize > MaxSpread) return false;
            return true;
        }

        private void EnterTrade(TradeType direction, double price, double ema50)
        {
            double riskAmount = Account.Balance * (RiskPercent / 100);
            double stopDistance = 50 * Symbol.PipSize;
            double volume = riskAmount / (50 * Symbol.PipValue);
            volume = Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);

            if (volume < Symbol.VolumeInUnitsMin)
                volume = Symbol.VolumeInUnitsMin;

            double entry = direction == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
            double stop = direction == TradeType.Buy 
                ? ema50 - (20 * Symbol.PipSize)  // Below EMA 50
                : ema50 + (20 * Symbol.PipSize); // Above EMA 50
            double take = direction == TradeType.Buy
                ? entry + ((entry - stop) * 5)  // 5:1 R/R
                : entry - ((stop - entry) * 5);

            var result = ExecuteMarketOrder(direction, SymbolName, volume, "TrendFast", stop, take);

            if (result.IsSuccessful)
            {
                _tradeCount++;
                Print(string.Format("TREND #{0}: {1} at {2:F5}", _tradeCount, direction, entry));
            }
        }

        protected override void OnStop()
        {
            Print(string.Format("Total trend trades: {0}", _tradeCount));
        }
    }
}
