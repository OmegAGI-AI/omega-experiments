using System;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class OmegaAggressive : Robot
    {
        [Parameter("Risk Percent", DefaultValue = 1)]
        public double RiskPercent { get; set; }

        [Parameter("Take Profit (pips)", DefaultValue = 10)]
        public double TakeProfitPips { get; set; }

        [Parameter("Stop Loss (pips)", DefaultValue = 5)]
        public double StopLossPips { get; set; }

        private int _tradeCount = 0;
        private DateTime _lastTradeTime = DateTime.MinValue;

        protected override void OnStart()
        {
            Print("AGGRESSIVE BOT STARTED");
            Print("This bot trades EVERY 5 minutes if no position open");
            Print(string.Format("Balance: {0}", Account.Balance));
        }

        protected override void OnTick()
        {
            // Only check every 5 seconds
            if ((DateTime.UtcNow - _lastTradeTime).TotalSeconds < 5)
                return;

            _lastTradeTime = DateTime.UtcNow;

            // No position open? Trade immediately
            if (Positions.Count == 0 && Account.Balance > 0)
            {
                // Random direction (50/50)
                var direction = (DateTime.UtcNow.Second % 2 == 0) 
                    ? TradeType.Buy 
                    : TradeType.Sell;

                double riskAmount = Account.Balance * (RiskPercent / 100);
                double volume = riskAmount / (StopLossPips * Symbol.PipValue);
                volume = Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);

                if (volume < Symbol.VolumeInUnitsMin)
                    volume = Symbol.VolumeInUnitsMin;

                double entry = direction == TradeType.Buy ? Symbol.Ask : Symbol.Bid;
                double stop = direction == TradeType.Buy 
                    ? entry - (StopLossPips * Symbol.PipSize)
                    : entry + (StopLossPips * Symbol.PipSize);
                double take = direction == TradeType.Buy
                    ? entry + (TakeProfitPips * Symbol.PipSize)
                    : entry - (TakeProfitPips * Symbol.PipSize);

                var result = ExecuteMarketOrder(direction, SymbolName, volume, "Aggressive", stop, take);

                if (result.IsSuccessful)
                {
                    _tradeCount++;
                    Print(string.Format("TRADE #{0}: {1} at {2}", _tradeCount, direction, entry));
                }
                else
                {
                    Print(string.Format("FAILED: {0}", result.Error));
                }
            }
        }

        protected override void OnStop()
        {
            Print(string.Format("Total trades: {0}", _tradeCount));
        }
    }
}
