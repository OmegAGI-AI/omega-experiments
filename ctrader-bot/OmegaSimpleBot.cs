using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class OmegaSimpleBot : Robot
    {
        [Parameter("Risk Percent", DefaultValue = 2)]
        public double RiskPercent { get; set; }

        [Parameter("Max Spread", DefaultValue = 1)]
        public double MaxSpread { get; set; }

        private int _tradeCount = 0;
        private double _startingBalance;

        protected override void OnStart()
        {
            _startingBalance = Account.Balance;
            Print("OMEGA SIMPLE BOT STARTED");
            Print(string.Format("Balance: {0}", Account.Balance));
        }

        protected override void OnBar()
        {
            if (!CanTrade())
                return;

            // Simple momentum strategy
            if (Bars.Count < 5)
                return;

            int i = Bars.Count - 1;
            double momentum = (Bars.ClosePrices[i] - Bars.ClosePrices[i - 3]) / Symbol.PipSize;

            if (momentum > 3 && momentum < 10)
            {
                // Buy
                double volume = GetVolume();
                var result = ExecuteMarketOrder(TradeType.Buy, SymbolName, volume, "SimpleBot", 
                    Symbol.Ask - (5 * Symbol.PipSize), 
                    Symbol.Ask + (10 * Symbol.PipSize));
                
                if (result.IsSuccessful)
                {
                    _tradeCount++;
                    Print(string.Format("BUY #{0} at {1}", _tradeCount, result.Position.EntryPrice));
                }
            }
            else if (momentum < -3 && momentum > -10)
            {
                // Sell
                double volume = GetVolume();
                var result = ExecuteMarketOrder(TradeType.Sell, SymbolName, volume, "SimpleBot",
                    Symbol.Bid + (5 * Symbol.PipSize),
                    Symbol.Bid - (10 * Symbol.PipSize));
                
                if (result.IsSuccessful)
                {
                    _tradeCount++;
                    Print(string.Format("SELL #{0} at {1}", _tradeCount, result.Position.EntryPrice));
                }
            }
        }

        private bool CanTrade()
        {
            if (Account.Balance <= 0)
                return false;

            if (Positions.Count >= 1)
                return false;

            if (Symbol.Spread / Symbol.PipSize > MaxSpread)
                return false;

            return true;
        }

        private double GetVolume()
        {
            double riskAmount = Account.Balance * (RiskPercent / 100);
            double volume = riskAmount / (5 * Symbol.PipValue); // 5 pip stop
            volume = Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);
            
            if (volume < Symbol.VolumeInUnitsMin)
                volume = Symbol.VolumeInUnitsMin;
            
            return volume;
        }

        protected override void OnStop()
        {
            Print(string.Format("Total trades: {0}", _tradeCount));
            Print(string.Format("Final balance: {0}", Account.Balance));
        }
    }
}
