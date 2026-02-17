using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class OmegaPrimeActive : Robot
    {
        [Parameter("Risk Percent", DefaultValue = 2)]
        public double RiskPercent { get; set; }

        [Parameter("Max Spread", DefaultValue = 2)]
        public double MaxSpread { get; set; }

        private ExponentialMovingAverage _ema8;
        private ExponentialMovingAverage _ema21;
        private RelativeStrengthIndex _rsi;

        private int _tradeCount = 0;
        private DateTime _lastTradeTime = DateTime.MinValue;

        protected override void OnStart()
        {
            _ema8 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 8);
            _ema21 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 21);
            _rsi = Indicators.RelativeStrengthIndex(Bars.ClosePrices, 14);

            Print("========================================");
            Print("OMEGA PRIME ACTIVE — TRADING NOW");
            Print("========================================");
        }

        protected override void OnBar()
        {
            if ((DateTime.UtcNow - _lastTradeTime).TotalMinutes < 5)
                return;

            if (!CanTrade())
                return;

            int i = Bars.Count - 2;
            if (i < 30) return;

            double price = Bars.ClosePrices[i];
            double ema8 = _ema8.Result[i];
            double ema21 = _ema21.Result[i];
            double rsi = _rsi.Result[i];

            // RELAXED ENTRY: Price near EMA8 in trend direction
            bool uptrend = ema8 > ema21;
            bool downtrend = ema8 < ema21;
            
            double distFromEma8 = (price - ema8) / Symbol.PipSize;
            bool nearEma8 = distFromEma8 > -5 && distFromEma8 < 5;

            bool buySetup = uptrend && nearEma8 && rsi > 35 && rsi < 65;
            bool sellSetup = downtrend && nearEma8 && rsi > 35 && rsi < 65;

            Print(string.Format("Price: {0:F5}, EMA8: {1:F5}, EMA21: {2:F5}, RSI: {3:F1}", price, ema8, ema21, rsi));
            Print(string.Format("Uptrend: {0}, Downtrend: {1}, NearEMA8: {2}", uptrend, downtrend, nearEma8));

            if (buySetup)
            {
                EnterTrade(TradeType.Buy, ema21);
            }
            else if (sellSetup)
            {
                EnterTrade(TradeType.Sell, ema21);
            }
            else
            {
                Print("Waiting for setup...");
            }
        }

        private bool CanTrade()
        {
            if (Account.Balance <= 0) return false;
            if (Positions.Count > 0) return false;
            if (Symbol.Spread / Symbol.PipSize > MaxSpread) return false;
            return true;
        }

        private void EnterTrade(TradeType direction, double ema21)
        {
            double entry = direction == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
            double stop = direction == TradeType.Buy 
                ? ema21 - (10 * Symbol.PipSize) 
                : ema21 + (10 * Symbol.PipSize);
            double take = direction == TradeType.Buy
                ? entry + ((entry - stop) * 2)
                : entry - ((stop - entry) * 2);

            double riskAmount = Account.Balance * (RiskPercent / 100);
            double riskPips = entry - stop;
            if (riskPips < 0) riskPips = -riskPips;
            riskPips = riskPips / Symbol.PipSize;

            double volume = riskAmount / (riskPips * Symbol.PipValue);
            volume = Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);
            if (volume < Symbol.VolumeInUnitsMin) volume = Symbol.VolumeInUnitsMin;

            var result = ExecuteMarketOrder(direction, SymbolName, volume, "PrimeActive", stop, take);

            if (result.IsSuccessful)
            {
                _tradeCount++;
                _lastTradeTime = DateTime.UtcNow;
                Print(string.Format("*** TRADE #{0} EXECUTED: {1} ***", _tradeCount, direction));
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
