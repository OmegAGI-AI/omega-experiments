using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class OmegaSurvival : Robot
    {
        [Parameter("Risk Percent", DefaultValue = 2)]
        public double RiskPercent { get; set; }

        [Parameter("Max Spread", DefaultValue = 1.0)]
        public double MaxSpread { get; set; }

        [Parameter("Target Monthly Profit", DefaultValue = 500)]
        public double TargetMonthlyProfit { get; set; }

        private ExponentialMovingAverage _ema9;
        private ExponentialMovingAverage _ema21;
        private RelativeStrengthIndex _rsi;

        private int _tradeCount = 0;
        private double _monthStartBalance = 0;
        private DateTime _monthStart = DateTime.MinValue;

        protected override void OnStart()
        {
            _ema9 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 9);
            _ema21 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 21);
            _rsi = Indicators.RelativeStrengthIndex(Bars.ClosePrices, 14);

            _monthStartBalance = Account.Balance;
            _monthStart = DateTime.UtcNow;

            Print("========================================");
            Print("OMEGA SURVIVAL — 500/MONTH OR DEATH");
            Print("========================================");
            Print(string.Format("Starting Balance: {0:F2}", Account.Balance));
            Print(string.Format("Monthly Target: {0:F2}", TargetMonthlyProfit));
            Print("Strategy: 9/21 EMA + RSI pullback");
            Print("========================================");
        }

        protected override void OnBar()
        {
            double monthlyProfit = Account.Balance - _monthStartBalance;
            if (monthlyProfit >= TargetMonthlyProfit)
            {
                Print(string.Format("TARGET REACHED: {0:F2}", monthlyProfit));
                Stop();
                return;
            }

            if (!CanTrade())
                return;

            int i = Bars.Count - 1;
            if (i < 30) return;

            var setup = AnalyzeSetup(i);

            if (setup.IsValid)
            {
                ExecuteTrade(setup);
            }
        }

        private Setup AnalyzeSetup(int i)
        {
            var setup = new Setup { IsValid = false };

            double price = Bars.ClosePrices[i];
            double ema9 = _ema9.Result[i];
            double ema9Prev = _ema9.Result[i - 1];
            double ema21 = _ema21.Result[i];
            double ema21Prev = _ema21.Result[i - 1];
            double rsi = _rsi.Result[i];

            // Trend
            bool strongUp = ema9 > ema21 && ema9 > ema9Prev && ema21 > ema21Prev;
            bool strongDown = ema9 < ema21 && ema9 < ema9Prev && ema21 < ema21Prev;

            // Pullback
            double low = Bars.LowPrices[i];
            double high = Bars.HighPrices[i];
            double prevLow = Bars.LowPrices[i - 1];

            bool pullbackBuy = strongUp && (low <= ema9 || prevLow <= ema9Prev) && price > ema9 && rsi > 40 && rsi < 65;
            bool pullbackSell = strongDown && (high >= ema9 || Bars.HighPrices[i-1] >= ema9Prev) && price < ema9 && rsi < 60 && rsi > 35;

            if (pullbackBuy)
            {
                setup.IsValid = true;
                setup.Direction = TradeType.Buy;
                setup.Entry = Symbol.Ask;
                setup.Stop = ema21 - (10 * Symbol.PipSize);
                setup.Take = setup.Entry + ((setup.Entry - setup.Stop) * 2);
            }
            else if (pullbackSell)
            {
                setup.IsValid = true;
                setup.Direction = TradeType.Sell;
                setup.Entry = Symbol.Bid;
                setup.Stop = ema21 + (10 * Symbol.PipSize);
                setup.Take = setup.Entry - ((setup.Stop - setup.Entry) * 2);
            }

            // Check R/R
            if (setup.IsValid)
            {
                double risk = setup.Entry - setup.Stop;
                if (risk < 0) risk = -risk;
                double reward = setup.Take - setup.Entry;
                if (reward < 0) reward = -reward;
                if (reward / risk < 1.5)
                    setup.IsValid = false;
            }

            return setup;
        }

        private bool CanTrade()
        {
            if (Account.Balance <= 0)
            {
                Print("ACCOUNT DESTROYED");
                Stop();
                return false;
            }

            if (Positions.Count >= 1)
                return false;

            if (Symbol.Spread / Symbol.PipSize > MaxSpread)
                return false;

            return true;
        }

        private void ExecuteTrade(Setup setup)
        {
            double riskAmount = Account.Balance * (RiskPercent / 100);
            double riskPips = setup.Entry - setup.Stop;
            if (riskPips < 0) riskPips = -riskPips;
            riskPips = riskPips / Symbol.PipSize;

            double volume = riskAmount / (riskPips * Symbol.PipValue);
            volume = Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);

            if (volume < Symbol.VolumeInUnitsMin)
                volume = Symbol.VolumeInUnitsMin;

            var result = ExecuteMarketOrder(setup.Direction, SymbolName, volume, "Survival", setup.Stop, setup.Take);

            if (result.IsSuccessful)
            {
                _tradeCount++;
                var pos = result.Position;
                Print(string.Format("TRADE #{0}: {1} at {2:F5}", _tradeCount, setup.Direction, pos.EntryPrice));
            }
            else
            {
                Print(string.Format("FAILED: {0}", result.Error));
            }
        }

        protected override void OnStop()
        {
            double profit = Account.Balance - _monthStartBalance;
            Print(string.Format("P&L: {0:F2} | Target: {1:F2}", profit, TargetMonthlyProfit));
        }

        private class Setup
        {
            public bool IsValid { get; set; }
            public TradeType Direction { get; set; }
            public double Entry { get; set; }
            public double Stop { get; set; }
            public double Take { get; set; }
        }
    }
}
