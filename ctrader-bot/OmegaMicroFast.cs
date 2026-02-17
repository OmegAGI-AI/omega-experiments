using System;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class OmegaMicroFast : Robot
    {
        [Parameter("Risk Percent", DefaultValue = 1)]
        public double RiskPercent { get; set; }

        [Parameter("Max Spread", DefaultValue = 1)]
        public double MaxSpread { get; set; }

        [Parameter("Max Daily Trades", DefaultValue = 20)]
        public int MaxDailyTrades { get; set; }

        private int _tradeCount = 0;
        private int _dailyTrades = 0;
        private DateTime _lastTradeDate = DateTime.MinValue;
        private double _prevPrice = 0;

        protected override void OnStart()
        {
            Print("MICRO FAST BOT STARTED");
            Print("Trades on ANY 3-pip move with momentum");
        }

        protected override void OnTick()
        {
            // Reset daily counter
            if (DateTime.UtcNow.Date != _lastTradeDate.Date)
            {
                _dailyTrades = 0;
                _lastTradeDate = DateTime.UtcNow;
            }

            if (!CanTrade())
                return;

            double price = Symbol.Bid;
            if (_prevPrice == 0)
            {
                _prevPrice = price;
                return;
            }

            // 3-pip move in last tick
            double move = (price - _prevPrice) / Symbol.PipSize;
            _prevPrice = price;

            if (move > 3 && move < 15)  // Up move, not too big
            {
                EnterTrade(TradeType.Buy);
            }
            else if (move < -3 && move > -15)  // Down move, not too big
            {
                EnterTrade(TradeType.Sell);
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

        private void EnterTrade(TradeType direction)
        {
            double riskAmount = Account.Balance * (RiskPercent / 100);
            double stopDistance = 3 * Symbol.PipSize;
            double volume = riskAmount / (3 * Symbol.PipValue);
            volume = Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);

            if (volume < Symbol.VolumeInUnitsMin)
                volume = Symbol.VolumeInUnitsMin;

            double entry = direction == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
            double stop = direction == TradeType.Buy 
                ? entry - stopDistance 
                : entry + stopDistance;
            double take = direction == TradeType.Buy
                ? entry + (stopDistance * 2)  // 2:1 R/R
                : entry - (stopDistance * 2);

            var result = ExecuteMarketOrder(direction, SymbolName, volume, "MicroFast", stop, take);

            if (result.IsSuccessful)
            {
                _tradeCount++;
                _dailyTrades++;
                Print(string.Format("MICRO #{0}: {1} at {2:F5}", _tradeCount, direction, entry));
            }
        }

        protected override void OnStop()
        {
            Print(string.Format("Total micro trades: {0}", _tradeCount));
        }
    }
}
