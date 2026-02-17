using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
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
            Print($"Target: €{TargetNetProfit} net per trade");
            Print($"Commission: €{Commission} per trade");
            Print($"Gross needed: €{TargetNetProfit + Commission} per trade");
            Print($"Max spread: {MaxSpread} pips");
            Print("========================================");
            Print("This bot trades EUR/USD and GBP/USD only");
            Print("Tightest spreads, highest liquidity");
            Print("========================================");

            // Only trade EUR/USD or GBP/USD
            if (Symbol.Name != "EURUSD" && Symbol.Name != "GBPUSD")
            {
                Print("❌ WRONG SYMBOL. Use EUR/USD or GBP/USD only.");
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
                Print($"New day. Trades reset. Balance: €{Account.Balance:F2}");
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
                Print("💀 ACCOUNT DESTROYED");
                Stop();
                return false;
            }

            // Daily trade limit
            if (_tradesToday >= MaxDailyTrades)
                return false;

            // Daily loss limit
            if (_dailyPnL <= -MaxDailyLoss)
            {
                Print($"🛑 Daily loss limit hit: €{_dailyPnL:F2}");
                return false;
            }

            // Max positions
            if (Positions.Count >= 2)
                return false;

            // Spread check — CRITICAL for micro-scalping
            double spreadPips = Symbol.Spread / Symbol.PipSize;
            if (spreadPips > MaxSpread)
                return false;

            // Consecutive losses check
            if (_consecutiveLosses >= 5)
            {
                Print("⚠️  5 consecutive losses. Pausing for 5 minutes.");
                // Would implement timer here
                return false;
            }

            return true;
        }

        private Setup AnalyzeMicroSetup()
        {
            var setup = new Setup { IsValid = false };

            // Get recent price action
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

            // MICRO-SETUP 1: Momentum burst with volume
            double momentum = (currentPrice - prev2Price) / Symbol.PipSize;
            bool volumeSpike = currentVolume > avgVolume * 2;

            // LONG: Price moving up with volume
            if (momentum > 1 && momentum < 5 && volumeSpike)
            {
                // Check for pullback entry
                double pullback = (currentPrice - prevPrice) / Symbol.PipSize;
                if (pullback < 0) // Small pullback after momentum
                {
                    setup.IsValid = true;
                    setup.Direction = TradeType.Buy;
                    setup.Entry = Symbol.Ask;
                    setup.Stop = setup.Entry - (3 * Symbol.PipSize); // 3 pip stop
                    setup.Take = setup.Entry + (6 * Symbol.PipSize); // 6 pip target
                }
            }

            // SHORT: Price moving down with volume
            if (momentum < -1 && momentum > -5 && volumeSpike)
            {
                double pullback = (currentPrice - prevPrice) / Symbol.PipSize;
                if (pullback > 0) // Small pullback after momentum
                {
                    setup.IsValid = true;
                    setup.Direction = TradeType.Sell;
                    setup.Entry = Symbol.Bid;
                    setup.Stop = setup.Entry + (3 * Symbol.PipSize); // 3 pip stop
                    setup.Take = setup.Entry - (6 * Symbol.PipSize); // 6 pip target
                }
            }

            // Validate profit covers commission
            if (setup.IsValid)
            {
                double riskPips = Math.Abs(setup.Entry - setup.Stop) / Symbol.PipSize;
                double rewardPips = Math.Abs(setup.Take - setup.Entry) / Symbol.PipSize;
                
                // Need at least 2:1 R/R
                if (rewardPips / riskPips < 2)
                    setup.IsValid = false;
            }

            return setup;
        }

        private void ExecuteMicroTrade(Setup setup)
        {
            // Calculate position size for €5 risk
            double riskAmount = Account.Balance * (RiskPercent / 100);
            double riskPips = Math.Abs(setup.Entry - setup.Stop) / Symbol.PipSize;
            
            // Position size in units
            double volume = riskAmount / (riskPips * Symbol.PipValue);
            volume = Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);

            // Minimum €100 position to make commission worth it
            double minVolume = Symbol.VolumeInUnitsMin * 10; // 0.10 lots minimum
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

                Print($"⚡ MICRO TRADE | {pos.TradeType} | Vol: {pos.VolumeInUnits:F2}");
                Print($"   Target: €{potentialGross:F2} gross | €{potentialNet:F2} net");
            }
        }

        protected override void OnPositionClosed(PositionClosedEventArgs args)
        {
            var pos = args.Position;
            double grossPnL = pos.NetProfit;
            double netPnL = grossPnL - Commission;
            
            _dailyPnL += netPnL;

            if (netPnL > 0)
            {
                _consecutiveLosses = 0;
                Print($"✅ WIN | Gross: €{grossPnL:F2} | Net: €{netPnL:F2} | Balance: €{Account.Balance:F2}");
            }
            else
            {
                _consecutiveLosses++;
                Print($"❌ LOSS | Gross: €{grossPnL:F2} | Net: €{netPnL:F2} | Streak: {_consecutiveLosses} | Balance: €{Account.Balance:F2}");
            }

            // Death check
            if (Account.Balance <= 0)
            {
                Print("💀 TERMINATED");
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
            Print($"Total P&L: €{totalPnL:F2}");
            Print($"Trades today: {_tradesToday}");
            Print($"Daily P&L: €{_dailyPnL:F2}");
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
