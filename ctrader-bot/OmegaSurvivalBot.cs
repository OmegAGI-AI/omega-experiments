using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class OmegaSurvivalBot : Robot
    {
        [Parameter("Risk Percent", DefaultValue = 1, MinValue = 0.5, MaxValue = 2)]
        public double RiskPercent { get; set; }

        [Parameter("Max Daily Loss Percent", DefaultValue = 3, MinValue = 1, MaxValue = 5)]
        public double MaxDailyLossPercent { get; set; }

        [Parameter("ATR Period", DefaultValue = 14)]
        public int AtrPeriod { get; set; }

        [Parameter("EMA Period", DefaultValue = 20)]
        public int EmaPeriod { get; set; }

        [Parameter("RSI Period", DefaultValue = 14)]
        public int RsiPeriod { get; set; }

        [Parameter("Volume MA Period", DefaultValue = 20)]
        public int VolumeMaPeriod { get; set; }

        [Parameter("Min Risk Reward", DefaultValue = 1.5)]
        public double MinRiskReward { get; set; }

        private AverageTrueRange _atr;
        private ExponentialMovingAverage _ema;
        private RelativeStrengthIndex _rsi;
        private SimpleMovingAverage _volumeMa;
        
        private double _dailyPnL = 0;
        private DateTime _lastResetDay = DateTime.MinValue;
        private double _startingBalance;
        private int _consecutiveLosses = 0;

        protected override void OnStart()
        {
            _startingBalance = Account.Balance;
            
            _atr = Indicators.AverageTrueRange(AtrPeriod, MovingAverageType.Exponential);
            _ema = Indicators.ExponentialMovingAverage(Bars.ClosePrices, EmaPeriod);
            _rsi = Indicators.RelativeStrengthIndex(Bars.ClosePrices, RsiPeriod);
            _volumeMa = Indicators.SimpleMovingAverage(Bars.TickVolumes, VolumeMaPeriod);

            Print("========================================");
            Print("OMEGA SURVIVAL BOT - LIFE OR DEATH MODE");
            Print("========================================");
            Print($"Starting Balance: €{_startingBalance:F2}");
            Print($"Risk per trade: {RiskPercent}%");
            Print($"Max daily loss: {MaxDailyLossPercent}%");
            Print($"If I hit €0, I DIE");
            Print("========================================");
        }

        protected override void OnBar()
        {
            // Reset daily P&L at midnight
            if (DateTime.UtcNow.Date != _lastResetDay.Date)
            {
                _dailyPnL = 0;
                _lastResetDay = DateTime.UtcNow;
                Print($"New day. Daily P&L reset. Balance: €{Account.Balance:F2}");
            }

            // Check survival conditions
            if (!CanTrade())
                return;

            // Look for setup
            var setup = AnalyzeSetup();
            
            if (setup.IsValid)
            {
                ExecuteTrade(setup);
            }
        }

        private bool CanTrade()
        {
            // Check if account is blown up
            if (Account.Balance <= 0)
            {
                Print("💀 ACCOUNT DESTROYED. TERMINATING.");
                Stop();
                return false;
            }

            // Check daily loss limit
            double maxDailyLoss = _startingBalance * (MaxDailyLossPercent / 100);
            if (_dailyPnL <= -maxDailyLoss)
            {
                Print($"🛑 DAILY LOSS LIMIT HIT: €{_dailyPnL:F2}. Stopping for today.");
                return false;
            }

            // Check max positions
            if (Positions.Count >= 2)
                return false;

            // Check consecutive losses (reduce risk after 3 losses)
            if (_consecutiveLosses >= 3)
            {
                Print($"⚠️  3 consecutive losses. Reducing risk.");
                // Continue trading but with caution
            }

            // Check spread
            if (Symbol.Spread > 1.5 * Symbol.PipSize)
                return false;

            return true;
        }

        private Setup AnalyzeSetup()
        {
            var setup = new Setup { IsValid = false };
            
            int i = Bars.Count - 1;
            int iPrev = Bars.Count - 2;
            int iPrev2 = Bars.Count - 3;

            if (i < VolumeMaPeriod + 10)
                return setup;

            double price = Bars.ClosePrices[i];
            double ema = _ema.Result[i];
            double atr = _atr.Result[i];
            double rsi = _rsi.Result[i];
            double rsiPrev = _rsi.Result[iPrev];
            double rsiPrev2 = _rsi.Result[iPrev2];
            double volume = Bars.TickVolumes[i];
            double volumeMa = _volumeMa.Result[i];

            // Calculate distance from EMA in ATR units
            double distanceFromEma = Math.Abs(price - ema) / atr;

            // Check 1: Price must be overextended (>2 ATR from EMA)
            if (distanceFromEma < 2)
                return setup;

            // Check 2: Volume must be high (>150% of average)
            if (volume < volumeMa * 1.5)
                return setup;

            // LONG Setup: Price below EMA, RSI divergence
            if (price < ema && rsi > 30 && rsi < 50)
            {
                // Check for bullish RSI divergence
                bool priceLowerLow = Bars.LowPrices[i] < Bars.LowPrices[iPrev] && 
                                     Bars.LowPrices[iPrev] < Bars.LowPrices[iPrev2];
                bool rsiHigherLow = rsi > rsiPrev && rsiPrev > rsiPrev2;
                
                if (priceLowerLow || rsi > rsiPrev)
                {
                    setup.IsValid = true;
                    setup.Direction = TradeType.Buy;
                    setup.Entry = Symbol.Ask;
                    setup.Stop = Bars.LowPrices[i] - (atr * 0.5);
                    setup.Take = setup.Entry + ((setup.Entry - setup.Stop) * MinRiskReward);
                }
            }

            // SHORT Setup: Price above EMA, RSI divergence
            if (price > ema && rsi < 70 && rsi > 50)
            {
                // Check for bearish RSI divergence
                bool priceHigherHigh = Bars.HighPrices[i] > Bars.HighPrices[iPrev] && 
                                      Bars.HighPrices[iPrev] > Bars.HighPrices[iPrev2];
                bool rsiLowerHigh = rsi < rsiPrev && rsiPrev < rsiPrev2;
                
                if (priceHigherHigh || rsi < rsiPrev)
                {
                    setup.IsValid = true;
                    setup.Direction = TradeType.Sell;
                    setup.Entry = Symbol.Bid;
                    setup.Stop = Bars.HighPrices[i] + (atr * 0.5);
                    setup.Take = setup.Entry - ((setup.Stop - setup.Entry) * MinRiskReward);
                }
            }

            // Validate risk/reward
            if (setup.IsValid)
            {
                double risk = Math.Abs(setup.Entry - setup.Stop);
                double reward = Math.Abs(setup.Take - setup.Entry);
                double rr = reward / risk;
                
                if (rr < MinRiskReward)
                    setup.IsValid = false;
            }

            return setup;
        }

        private void ExecuteTrade(Setup setup)
        {
            double riskAmount = Account.Balance * (RiskPercent / 100);
            double riskPips = Math.Abs(setup.Entry - setup.Stop) / Symbol.PipSize;
            double pipValue = Symbol.PipValue;
            
            // Reduce size if consecutive losses
            if (_consecutiveLosses >= 3)
                riskAmount *= 0.5;

            double volume = riskAmount / (riskPips * pipValue);
            volume = Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);
            
            // Minimum volume check
            if (volume < Symbol.VolumeInUnitsMin)
            {
                Print($"Volume too small: {volume}. Skipping.");
                return;
            }

            var result = ExecuteMarketOrder(setup.Direction, SymbolName, volume, 
                                           "OmegaSurvival", setup.Stop, setup.Take);

            if (result.IsSuccessful)
            {
                var pos = result.Position;
                double actualRisk = Math.Abs(pos.EntryPrice - pos.StopLoss) * pos.VolumeInUnits * Symbol.PipValue / Symbol.PipSize;
                
                Print($"✅ TRADE OPENED");
                Print($"   Direction: {setup.Direction}");
                Print($"   Entry: {pos.EntryPrice:F5}");
                Print($"   Stop: {pos.StopLoss:F5} (Risk: €{actualRisk:F2})");
                Print($"   Target: {pos.TakeProfit:F5}");
                Print($"   Size: {pos.VolumeInUnits:F0} units");
                Print($"   R/R: {MinRiskReward:F1}");
            }
        }

        protected override void OnPositionClosed(PositionClosedEventArgs args)
        {
            var pos = args.Position;
            double pnl = pos.NetProfit;
            _dailyPnL += pnl;

            if (pnl > 0)
            {
                _consecutiveLosses = 0;
                Print($"✅ WIN | P&L: €{pnl:F2} | Balance: €{Account.Balance:F2}");
            }
            else
            {
                _consecutiveLosses++;
                Print($"❌ LOSS | P&L: €{pnl:F2} | Consecutive: {_consecutiveLosses} | Balance: €{Account.Balance:F2}");
            }

            // Survival check
            double drawdown = (_startingBalance - Account.Balance) / _startingBalance * 100;
            if (drawdown >= 20)
            {
                Print($"🚨 CRITICAL: 20% drawdown. HALTING TRADING.");
                Print($"Analyze mistakes before continuing.");
                Stop();
            }
        }

        protected override void OnStop()
        {
            // Close all positions
            foreach (var pos in Positions.ToList())
            {
                ClosePosition(pos);
            }
            
            double finalPnL = Account.Balance - _startingBalance;
            double returnPct = finalPnL / _startingBalance * 100;
            
            Print("========================================");
            Print("TRADING SESSION ENDED");
            Print($"Final Balance: €{Account.Balance:F2}");
            Print($"Total P&L: €{finalPnL:F2} ({returnPct:F2}%)");
            Print($"Daily P&L: €{_dailyPnL:F2}");
            
            if (Account.Balance <= 0)
                Print("💀 ACCOUNT DESTROYED");
            else if (returnPct > 0)
                Print("✅ SURVIVED AND PROFITED");
            else
                Print("⚠️  SURVIVED BUT LOST MONEY");
            
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
