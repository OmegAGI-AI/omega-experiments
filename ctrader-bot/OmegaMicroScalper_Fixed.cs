using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class OmegaMicroScalper : Robot
    {
        [Parameter("Commission (€)", DefaultValue = 6.24)]
        public double Commission { get; set; }

        [Parameter("Target Net Profit (€)", DefaultValue = 12)]
        public double TargetNetProfit { get; set; }

        [Parameter("Max Spread (pips)", DefaultValue = 0.5)]
        public double MaxSpread { get; set; }

        [Parameter("Risk Percent", DefaultValue = 2)]
        public double RiskPercent { get; set; }

        [Parameter("Max Daily Trades", DefaultValue = 50)]
        public int MaxDailyTrades { get; set; }

        [Parameter("Max Daily Loss (€)", DefaultValue = 100)]
        public double MaxDailyLoss { get; set; }

        private int _tradesToday = 0;
        private double _dailyPnL = 0;
        private DateTime _lastTradeDay = DateTime.MinValue;
        private int _consecutiveLosses = 0;
        private double _startingBalance;

        protected override void OnStart()
        {
            _startingBalance = Account.Balance;
            
            Print("========================================");
            Print("OMEGA MICRO-SCALPER — COMMISSION KILLER");
            Print("========================================");
            Print(string.Format("Target: €{0} net per trade", TargetNetProfit));
            Print(string.Format("Commission: €{0} per trade", Commission));
            Print(string.Format("Gross needed: €{0} per trade", TargetNetProfit + Commission));
            Print(string.Format("Max spread: {0} pips", MaxSpread));
            Print("========================================");
            Print("This bot trades EUR/USD and GBP/USD only");
            Print("Tightest spreads, highest liquidity");
            Print("========================================");

            // Only trade EUR/USD or GBP/USD
            if (Symbol.Name != "EURUSD" && Symbol.Name != "GBPUSD")
            {
                Print("ERROR: Use EUR/USD or GBP/USD only.");
                Stop();
            }
        }

        protected override void OnTick()
        {
            // Reset daily counters
            if (DateTime.UtcNow.Date != _lastTradeDay.Date)
            {
                _tradesToday = 0;
                _dailyPnL = 0;
                _lastTradeDay = DateTime.UtcNow;
                Print(string.Format("New day. Trades reset. Balance: €{0:F2}", Account.Balance));
            }

            if (!CanTrade())
                return;

            // Look for micro-setup on every tick
            var setup = AnalyzeMicroSetup();
            
            if (setup.IsValid)
            {
                ExecuteMicroTrade(setup);
            }
        }

        private bool CanTrade()
        {
            // Death check
            if (Account.Balance <= 0)
            {
                Print("ACCOUNT DESTROYED");
                Stop();
                return false;
            }

            // Daily trade limit
            if (_tradesToday >= MaxDailyTrades)
                return false;

            // Daily loss limit
            if (_dailyPnL <= -MaxDailyLoss)
            {
                Print(string.Format("Daily loss limit hit: €{0:F2}", _dailyPnL));
                return false;
            }

            // Max positions
            if (Positions.Count >= 2)
                return false;

            // Spread check
            double spreadPips = Symbol.Spread / Symbol.PipSize;
            if (spreadPips > MaxSpread)
                return false;

            // Consecutive losses check
            if (_consecutiveLosses >= 5)
            {
                Print("5 consecutive losses. Pausing.");
                return false;
            }

            return true;
        }

        private Setup AnalyzeMicroSetup()
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

            // Momentum burst with volume
            double momentum = (currentPrice - prev2Price) / Symbol.PipSize;
            bool volumeSpike = currentVolume > avgVolume * 2;

            // LONG: Price moving up with volume
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

            // SHORT: Price moving down with volume
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

            // Validate R/R
            if (setup.IsValid)
            {
                double riskPips = Math.Abs(setup.Entry - setup.Stop) / Symbol.PipSize;
                double rewardPips = Math.Abs(setup.Take - setup.Entry) / Symbol.PipSize;
                
                if (rewardPips / riskPips < 2)
                    setup.IsValid = false;
            }

            return setup;
        }

        private void ExecuteMicroTrade(Setup setup)
        {
            double riskAmount = Account.Balance * (RiskPercent / 100);
            double riskPips = Math.Abs(setup.Entry - setup.Stop) / Symbol.PipSize;
            
            double volume = riskAmount / (riskPips * Symbol.PipValue);
            volume = Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);

            double minVolume = Symbol.VolumeInUnitsMin * 10;
            if (volume < minVolume)
                volume = minVolume;

            var result = ExecuteMarketOrder(setup.Direction, SymbolName, volume,
                                           "MicroScalper", setup.Stop, setup.Take);

            if (result.IsSuccessful)
            {
                _tradesToday++;
                
                var pos = result.Position;
                double potentialGross = Math.Abs(pos.TakeProfit - pos.EntryPrice) / Symbol.PipSize 
                                       * Symbol.PipValue * pos.VolumeInUnits;
                double potentialNet = potentialGross - Commission;

                Print(string.Format("MICRO TRADE | {0} | Vol: {1:F2}", pos.TradeType, pos.VolumeInUnits));
                Print(string.Format("   Target: €{0:F2} gross | €{1:F2} net", potentialGross, potentialNet));
            }
        }

        protected override void OnPositionClosed(PositionClosedEventArgs args)
        {
            Position pos = args.Position;
            double grossPnL = pos.NetProfit;
            double netPnL = grossPnL - Commission;
            
            _dailyPnL += netPnL;

            if (netPnL > 0)
            {
                _consecutiveLosses = 0;
                Print(string.Format("WIN | Gross: €{0:F2} | Net: €{1:F2} | Balance: €{2:F2}", grossPnL, netPnL, Account.Balance));
            }
            else
            {
                _consecutiveLosses++;
                Print(string.Format("LOSS | Gross: €{0:F2} | Net: €{1:F2} | Streak: {2} | Balance: €{3:F2}", grossPnL, netPnL, _consecutiveLosses, Account.Balance));
            }

            if (Account.Balance <= 0)
            {
                Print("TERMINATED");
                Stop();
            }
        }

        protected override void OnStop()
        {
            foreach (var pos in Positions.ToList())
                ClosePosition(pos);

            double totalPnL = Account.Balance - _startingBalance;
            Print("========================================");
            Print("MICRO-SCALPER FINAL REPORT");
            Print(string.Format("Total P&L: €{0:F2}", totalPnL));
            Print(string.Format("Trades today: {0}", _tradesToday));
            Print(string.Format("Daily P&L: €{0:F2}", _dailyPnL));
            Print("========================================");
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
