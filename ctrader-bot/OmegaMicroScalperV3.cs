using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class OmegaMicroScalperV3 : Robot
    {
        [Parameter("Commission", DefaultValue = 6.24)]
        public double Commission { get; set; }

        [Parameter("Risk Percent", DefaultValue = 2)]
        public double RiskPercent { get; set; }

        [Parameter("Max Spread", DefaultValue = 0.5)]
        public double MaxSpread { get; set; }

        private int _tradesToday = 0;
        private double _dailyPnL = 0;
        private DateTime _lastTradeDay = DateTime.MinValue;
        private int _consecutiveLosses = 0;

        protected override void OnStart()
        {
            Print("OMEGA MICRO-SCALPER v3 STARTED");
            Print(string.Format("Balance: {0}", Account.Balance));
            
            if (Symbol.Name != "EURUSD" && Symbol.Name != "GBPUSD")
            {
                Print("ERROR: Use EUR/USD or GBP/USD only");
                Stop();
            }
            
            // Subscribe to position closed event
            Positions.Closed += OnPositionClosedEvent;
        }

        protected override void OnTick()
        {
            if (DateTime.UtcNow.Date != _lastTradeDay.Date)
            {
                _tradesToday = 0;
                _dailyPnL = 0;
                _lastTradeDay = DateTime.UtcNow;
            }

            if (!CanTrade())
                return;

            var setup = AnalyzeSetup();
            if (setup.IsValid)
                ExecuteTrade(setup);
        }

        private bool CanTrade()
        {
            if (Account.Balance <= 0)
            {
                Print("ACCOUNT DESTROYED");
                Stop();
                return false;
            }

            if (_tradesToday >= 50)
                return false;

            if (_dailyPnL <= -100)
            {
                Print(string.Format("Daily loss limit: {0}", _dailyPnL));
                return false;
            }

            if (Positions.Count >= 2)
                return false;

            if (Symbol.Spread / Symbol.PipSize > MaxSpread)
                return false;

            if (_consecutiveLosses >= 5)
                return false;

            return true;
        }

        private Setup AnalyzeSetup()
        {
            var setup = new Setup { IsValid = false };

            if (Bars.Count < 10)
                return setup;

            int i = Bars.Count - 1;
            
            double currentPrice = Bars.ClosePrices[i];
            double prevPrice = Bars.ClosePrices[i - 1];
            double prev2Price = Bars.ClosePrices[i - 2];
            
            double currentVolume = Bars.TickVolumes[i];
            double avgVolume = 0;
            for (int j = i - 9; j <= i; j++)
                avgVolume += Bars.TickVolumes[j];
            avgVolume /= 10;

            double momentum = (currentPrice - prev2Price) / Symbol.PipSize;
            bool volumeSpike = currentVolume > avgVolume * 2;

            // LONG
            if (momentum > 1 && momentum < 5 && volumeSpike)
            {
                double pullback = (currentPrice - prevPrice) / Symbol.PipSize;
                if (pullback < 0)
                {
                    setup.IsValid = true;
                    setup.Direction = TradeType.Buy;
                    setup.Entry = Symbol.Ask;
                    setup.Stop = setup.Entry - (3 * Symbol.PipSize);
                    setup.Take = setup.Entry + (6 * Symbol.PipSize);
                }
            }

            // SHORT
            if (momentum < -1 && momentum > -5 && volumeSpike)
            {
                double pullback = (currentPrice - prevPrice) / Symbol.PipSize;
                if (pullback > 0)
                {
                    setup.IsValid = true;
                    setup.Direction = TradeType.Sell;
                    setup.Entry = Symbol.Bid;
                    setup.Stop = setup.Entry + (3 * Symbol.PipSize);
                    setup.Take = setup.Entry - (6 * Symbol.PipSize);
                }
            }

            return setup;
        }

        private void ExecuteTrade(Setup setup)
        {
            double riskAmount = Account.Balance * (RiskPercent / 100);
            double riskPips = 3; // Fixed 3 pip stop
            
            double volume = riskAmount / (riskPips * Symbol.PipValue);
            volume = Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);

            double minVolume = Symbol.VolumeInUnitsMin * 10;
            if (volume < minVolume)
                volume = minVolume;

            var result = ExecuteMarketOrder(setup.Direction, SymbolName, volume,
                                           "Micro", setup.Stop, setup.Take);

            if (result.IsSuccessful)
            {
                _tradesToday++;
                Print(string.Format("TRADE OPENED: {0}", setup.Direction));
            }
        }

        // Event handler for position closed
        private void OnPositionClosedEvent(PositionClosedEventArgs args)
        {
            Position pos = args.Position;
            double grossPnL = pos.NetProfit;
            double netPnL = grossPnL - Commission;
            
            _dailyPnL += netPnL;

            if (netPnL > 0)
            {
                _consecutiveLosses = 0;
                Print(string.Format("WIN: Gross={0:F2}, Net={1:F2}, Balance={2:F2}", grossPnL, netPnL, Account.Balance));
            }
            else
            {
                _consecutiveLosses++;
                Print(string.Format("LOSS: Gross={0:F2}, Net={1:F2}, Streak={2}, Balance={3:F2}", grossPnL, netPnL, _consecutiveLosses, Account.Balance));
            }

            if (Account.Balance <= 0)
            {
                Print("TERMINATED");
                Stop();
            }
        }

        protected override void OnStop()
        {
            // Unsubscribe from event
            Positions.Closed -= OnPositionClosedEvent;
            
            foreach (var pos in Positions.ToList())
                ClosePosition(pos);

            Print("BOT STOPPED");
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
