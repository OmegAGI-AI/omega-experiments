using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class OmegaMicroPro : Robot
    {
        [Parameter("Risk Percent", DefaultValue = 1)]
        public double RiskPercent { get; set; }

        [Parameter("Commission", DefaultValue = 6.24)]
        public double Commission { get; set; }

        [Parameter("Max Spread", DefaultValue = 0.5)]
        public double MaxSpread { get; set; }

        [Parameter("Max Daily Trades", DefaultValue = 10)]
        public int MaxDailyTrades { get; set; }

        private MovingAverage _fastMA;
        private MovingAverage _slowMA;
        private int _tradeCount = 0;
        private int _dailyTrades = 0;
        private DateTime _lastTradeDate = DateTime.MinValue;
        private DateTime _lastTradeTime = DateTime.MinValue;

        protected override void OnStart()
        {
            _fastMA = Indicators.MovingAverage(Bars.ClosePrices, 5, MovingAverageType.Exponential);
            _slowMA = Indicators.MovingAverage(Bars.ClosePrices, 20, MovingAverageType.Exponential);

            Print("========================================");
            Print("OMEGA MICRO PRO — COMMISSION KILLER");
            Print("========================================");
            Print(string.Format("Balance: €{0:F2}", Account.Balance));
            Print(string.Format("Commission: €{0:F2} per trade", Commission));
            Print(string.Format("Need €{0:F2} gross profit to break even", Commission * 2));
            Print("Strategy: EMA crossover momentum");
            Print("Min R/R: 2:1");
            Print("========================================");
        }

        protected override void OnBar()
        {
            // Reset daily counter
            if (DateTime.UtcNow.Date != _lastTradeDate.Date)
            {
                _dailyTrades = 0;
                _lastTradeDate = DateTime.UtcNow;
            }

            // Minimum 2 minutes between trades
            if ((DateTime.UtcNow - _lastTradeTime).TotalMinutes < 2)
                return;

            if (!CanTrade())
                return;

            int i = Bars.Count - 1;
            if (i < 25) return;

            var setup = AnalyzeSetup(i);

            if (setup.IsValid)
            {
                ExecuteTrade(setup);
            }
        }

        private Setup AnalyzeSetup(int i)
        {
            var setup = new Setup { IsValid = false };

            double fast = _fastMA.Result[i];
            double fastPrev = _fastMA.Result[i - 1];
            double slow = _slowMA.Result[i];
            double slowPrev = _slowMA.Result[i - 1];
            double price = Bars.ClosePrices[i];

            // EMA crossover
            bool crossUp = fastPrev <= slowPrev && fast > slow;
            bool crossDown = fastPrev >= slowPrev && fast < slow;

            // Momentum check (last 3 bars)
            double momentum = (price - Bars.ClosePrices[i - 3]) / Symbol.PipSize;
            bool momentumUp = momentum > 2 && momentum < 10;
            bool momentumDown = momentum < -2 && momentum > -10;

            // Volume check - MANUAL CALCULATION (no LINQ)
            double volumeAvg = 0;
            for (int j = i - 9; j <= i; j++)
                volumeAvg += Bars.TickVolumes[j];
            volumeAvg /= 10;
            bool volumeOk = Bars.TickVolumes[i] > volumeAvg * 1.2;

            if (crossUp && momentumUp && volumeOk)
            {
                setup.IsValid = true;
                setup.Direction = TradeType.Buy;
            }
            else if (crossDown && momentumDown && volumeOk)
            {
                setup.IsValid = true;
                setup.Direction = TradeType.Sell;
            }

            return setup;
        }

        private bool CanTrade()
        {
            if (Account.Balance <= 0)
            {
                Print("ACCOUNT DESTROYED — STOPPING");
                Stop();
                return false;
            }

            if (Positions.Count >= 1)
                return false;

            if (Symbol.Spread / Symbol.PipSize > MaxSpread)
                return false;

            if (_dailyTrades >= MaxDailyTrades)
                return false;

            return true;
        }

        private void ExecuteTrade(Setup setup)
        {
            double entry = setup.Direction == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
            
            // Fixed 3 pip stop, 6 pip target (2:1 R/R)
            double stopDistance = 3 * Symbol.PipSize;
            double takeDistance = 6 * Symbol.PipSize;
            
            double stop = setup.Direction == TradeType.Buy 
                ? entry - stopDistance 
                : entry + stopDistance;
            double take = setup.Direction == TradeType.Buy
                ? entry + takeDistance
                : entry - takeDistance;

            // Calculate volume for 1% risk
            double riskAmount = Account.Balance * (RiskPercent / 100);
            double volume = riskAmount / (3 * Symbol.PipValue);
            volume = Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);

            if (volume < Symbol.VolumeInUnitsMin)
                volume = Symbol.VolumeInUnitsMin;

            var result = ExecuteMarketOrder(setup.Direction, SymbolName, volume, "MicroPro", stop, take);

            if (result.IsSuccessful)
            {
                _tradeCount++;
                _dailyTrades++;
                _lastTradeTime = DateTime.UtcNow;

                var pos = result.Position;
                double grossProfit = 6 * Symbol.PipValue * pos.VolumeInUnits;
                double netProfit = grossProfit - Commission;

                Print("========================================");
                Print(string.Format("MICRO TRADE #{0}", _tradeCount));
                Print(string.Format("Direction: {0}", setup.Direction));
                Print(string.Format("Entry: {0:F5}", pos.EntryPrice));
                Print(string.Format("Stop: {0:F5} | Target: {1:F5}", pos.StopLoss, pos.TakeProfit));
                Print(string.Format("Gross if win: €{0:F2}", grossProfit));
                Print(string.Format("Net if win: €{0:F2}", netProfit));
                Print(string.Format("Daily: {0}/{1}", _dailyTrades, MaxDailyTrades));
                Print("========================================");
            }
            else
            {
                Print(string.Format("TRADE FAILED: {0}", result.Error));
            }
        }

        protected override void OnStop()
        {
            Print("========================================");
            Print("MICRO PRO STOPPED");
            Print(string.Format("Total trades: {0}", _tradeCount));
            Print("========================================");
        }

        private class Setup
        {
            public bool IsValid { get; set; }
            public TradeType Direction { get; set; }
        }
    }
}
