using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class OmegaMicroUltra : Robot
    {
        [Parameter("Risk Percent", DefaultValue = 1)]
        public double RiskPercent { get; set; }

        [Parameter("Commission", DefaultValue = 6.24)]
        public double Commission { get; set; }

        [Parameter("Max Spread", DefaultValue = 2)]
        public double MaxSpread { get; set; }

        private MovingAverage _fastMA;
        private MovingAverage _slowMA;
        private int _tradeCount = 0;
        private DateTime _lastTradeTime = DateTime.MinValue;

        protected override void OnStart()
        {
            _fastMA = Indicators.MovingAverage(Bars.ClosePrices, 5, MovingAverageType.Exponential);
            _slowMA = Indicators.MovingAverage(Bars.ClosePrices, 20, MovingAverageType.Exponential);

            Print("========================================");
            Print("OMEGA MICRO ULTRA — WILL TRADE");
            Print("========================================");
            Print(string.Format("Balance: €{0:F2}", Account.Balance));
        }

        protected override void OnBar()
        {
            // Log every bar
            int i = Bars.Count - 1;
            Print(string.Format("Bar #{0} - Price: {1:F5}, Fast: {2:F5}, Slow: {3:F5}", 
                i, Bars.ClosePrices[i], _fastMA.Result[i], _slowMA.Result[i]));

            // Wait 30 seconds between checks
            if ((DateTime.UtcNow - _lastTradeTime).TotalSeconds < 30)
                return;

            // Basic checks only
            if (Account.Balance <= 0)
            {
                Print("NO BALANCE");
                return;
            }

            if (Positions.Count > 0)
            {
                Print("POSITION ALREADY OPEN");
                return;
            }

            double spread = Symbol.Spread / Symbol.PipSize;
            if (spread > MaxSpread)
            {
                Print(string.Format("SPREAD TOO HIGH: {0:F1} pips", spread));
                return;
            }

            // Need enough bars
            if (i < 25)
            {
                Print("NOT ENOUGH BARS");
                return;
            }

            // Get values
            double fast = _fastMA.Result[i];
            double fastPrev = _fastMA.Result[i - 1];
            double slow = _slowMA.Result[i];
            double slowPrev = _slowMA.Result[i - 1];

            // Check crossover
            bool crossUp = fastPrev <= slowPrev && fast > slow;
            bool crossDown = fastPrev >= slowPrev && fast < slow;

            Print(string.Format("CrossUp: {0}, CrossDown: {1}", crossUp, crossDown));

            if (!crossUp && !crossDown)
            {
                Print("NO CROSSOVER");
                return;
            }

            // EXECUTE TRADE
            var direction = crossUp ? TradeType.Buy : TradeType.Sell;
            double entry = direction == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
            
            double stopDistance = 3 * Symbol.PipSize;
            double takeDistance = 6 * Symbol.PipSize;
            
            double stop = direction == TradeType.Buy 
                ? entry - stopDistance 
                : entry + stopDistance;
            double take = direction == TradeType.Buy
                ? entry + takeDistance
                : entry - takeDistance;

            double riskAmount = Account.Balance * (RiskPercent / 100);
            double volume = riskAmount / (3 * Symbol.PipValue);
            volume = Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);

            if (volume < Symbol.VolumeInUnitsMin)
                volume = Symbol.VolumeInUnitsMin;

            Print(string.Format("ATTEMPTING TRADE: {0} at {1:F5}, Vol: {2:F2}", direction, entry, volume));

            var result = ExecuteMarketOrder(direction, SymbolName, volume, "MicroUltra", stop, take);

            if (result.IsSuccessful)
            {
                _tradeCount++;
                _lastTradeTime = DateTime.UtcNow;
                Print(string.Format("*** TRADE #{0} EXECUTED ***", _tradeCount));
            }
            else
            {
                Print(string.Format("*** TRADE FAILED: {0} ***", result.Error));
            }
        }

        protected override void OnStop()
        {
            Print(string.Format("Total trades: {0}", _tradeCount));
        }
    }
}
