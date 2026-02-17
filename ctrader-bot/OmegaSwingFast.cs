using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class OmegaSwingFast : Robot
    {
        [Parameter("Risk Percent", DefaultValue = 2)]
        public double RiskPercent { get; set; }

        [Parameter("Max Spread", DefaultValue = 2)]
        public double MaxSpread { get; set; }

        [Parameter("Max Daily Trades", DefaultValue = 10)]
        public int MaxDailyTrades { get; set; }

        private ExponentialMovingAverage _ema20;
        private ExponentialMovingAverage _ema50;
        private RelativeStrengthIndex _rsi;

        private int _tradeCount = 0;
        private int _dailyTrades = 0;
        private DateTime _lastTradeDate = DateTime.MinValue;

        protected override void OnStart()
        {
            _ema20 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 20);
            _ema50 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 50);
            _rsi = Indicators.RelativeStrengthIndex(Bars.ClosePrices, 14);

            Print("SWING FAST BOT STARTED");
            Print("Trades on ANY pullback to EMA 20");
        }

        protected override void OnBar()
        {
            // Reset daily counter
            if (DateTime.UtcNow.Date != _lastTradeDate.Date)
            {
                _dailyTrades = 0;
                _lastTradeDate = DateTime.UtcNow;
            }

            if (!CanTrade())
                return;

            int i = Bars.Count - 1;
            if (i < 50) return;

            double price = Bars.ClosePrices[i];
            double ema20 = _ema20.Result[i];
            double ema50 = _ema50.Result[i];
            double rsi = _rsi.Result[i];

            // Simple trend check
            bool uptrend = price > ema50 && ema20 > ema50;
            bool downtrend = price < ema50 && ema20 < ema50;

            // Distance from EMA 20 (relaxed to 20 pips)
            double distEma20 = Math.Abs(price - ema20) / Symbol.PipSize;
            bool nearEma20 = distEma20 < 20;

            // Relaxed RSI (30-70 instead of 45-55)
            bool rsiOk = rsi > 30 && rsi < 70;

            if (uptrend && nearEma20 && rsiOk)
            {
                EnterTrade(TradeType.Buy, price, ema50);
            }
            else if (downtrend && nearEma20 && rsiOk)
            {
                EnterTrade(TradeType.Sell, price, ema50);
            }
        }

        private bool CanTrade()
        {
            if (Account.Balance <= 0) return false;
            if (Positions.Count >= 1) return false;
            if (Symbol.Spread / Symbol.PipSize > MaxSpread) return false;
            if (_dailyTrades >= MaxDailyTrades) return false;
            return true;
        }

        private void EnterTrade(TradeType direction, double price, double ema50)
        {
            double riskAmount = Account.Balance * (RiskPercent / 100);
            double stopDistance = 30 * Symbol.PipSize;
            double volume = riskAmount / (30 * Symbol.PipValue);
            volume = Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);

            if (volume < Symbol.VolumeInUnitsMin)
                volume = Symbol.VolumeInUnitsMin;

            double entry = direction == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
            double stop = direction == TradeType.Buy 
                ? entry - stopDistance 
                : entry + stopDistance;
            double take = direction == TradeType.Buy
                ? entry + (stopDistance * 3)  // 3:1 R/R
                : entry - (stopDistance * 3);

            var result = ExecuteMarketOrder(direction, SymbolName, volume, "SwingFast", stop, take);

            if (result.IsSuccessful)
            {
                _tradeCount++;
                _dailyTrades++;
                Print(string.Format("TRADE #{0}: {1} at {2:F5}", _tradeCount, direction, entry));
            }
            else
            {
                Print(string.Format("FAILED: {0}", result.Error));
            }
        }

        protected override void OnStop()
        {
            Print(string.Format("Total trades: {0}", _tradeCount));
        }
    }
}
