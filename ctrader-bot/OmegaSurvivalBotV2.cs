using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class OmegaSurvivalBotV2 : Robot
    {
        [Parameter("Risk Percent", DefaultValue = 2, MinValue = 1, MaxValue = 3)]
        public double RiskPercent { get; set; }

        [Parameter("Commission per Trade (€)", DefaultValue = 6.24)]
        public double Commission { get; set; }

        [Parameter("Min Risk Reward", DefaultValue = 3.0)]
        public double MinRiskReward { get; set; }

        [Parameter("Max Trades per Day", DefaultValue = 2)]
        public int MaxTradesPerDay { get; set; }

        [Parameter("EMA Fast", DefaultValue = 20)]
        public int EmaFast { get; set; }

        [Parameter("EMA Slow", DefaultValue = 50)]
        public int EmaSlow { get; set; }

        [Parameter("EMA Trend", DefaultValue = 200)]
        public int EmaTrend { get; set; }

        [Parameter("RSI Period", DefaultValue = 14)]
        public int RsiPeriod { get; set; }

        private ExponentialMovingAverage _emaFast;
        private ExponentialMovingAverage _emaSlow;
        private ExponentialMovingAverage _emaTrend;
        private RelativeStrengthIndex _rsi;
        private AverageTrueRange _atr;

        private int _tradesToday = 0;
        private DateTime _lastTradeDay = DateTime.MinValue;
        private double _startingBalance;
        private double _highestBalance;
        private int _totalTrades = 0;
        private int _winningTrades = 0;

        protected override void OnStart()
        {
            _startingBalance = Account.Balance;
            _highestBalance = Account.Balance;

            _emaFast = Indicators.ExponentialMovingAverage(Bars.ClosePrices, EmaFast);
            _emaSlow = Indicators.ExponentialMovingAverage(Bars.ClosePrices, EmaSlow);
            _emaTrend = Indicators.ExponentialMovingAverage(Bars.ClosePrices, EmaTrend);
            _rsi = Indicators.RelativeStrengthIndex(Bars.ClosePrices, RsiPeriod);
            _atr = Indicators.AverageTrueRange(14, MovingAverageType.Exponential);

            Print("========================================");
            Print("OMEGA SURVIVAL BOT v2 — COMMISSION AWARE");
            Print("========================================");
            Print($"Starting Balance: €{_startingBalance:F2}");
            Print($"Commission: €{Commission:F2} per trade");
            Print($"Risk per trade: {RiskPercent}%");
            Print($"Min R/R: {MinRiskReward}:1");
            Print($"Max trades/day: {MaxTradesPerDay}");
            Print("========================================");
            Print("With €6.24 commission, we need BIG moves.");
            Print("Only A+ setups. Quality over quantity.");
            Print("========================================");
        }

        protected override void OnBar()
        {
            // Reset daily counter
            if (DateTime.UtcNow.Date != _lastTradeDay.Date)
            {
                _tradesToday = 0;
                _lastTradeDay = DateTime.UtcNow;
                Print($"New day. Trades reset. Balance: €{Account.Balance:F2}");
            }

            // Update highest balance for drawdown calc
            if (Account.Balance > _highestBalance)
                _highestBalance = Account.Balance;

            if (!CanTrade())
                return;

            var setup = AnalyzeSetup();
            
            if (setup.IsValid)
            {
                ExecuteTrade(setup);
            }
        }

        private bool CanTrade()
        {
            // DEATH CHECK
            if (Account.Balance <= 0)
            {
                Print("💀 ACCOUNT DESTROYED. I DIE.");
                Stop();
                return false;
            }

            // Max daily trades
            if (_tradesToday >= MaxTradesPerDay)
                return false;

            // Max open positions
            if (Positions.Count >= 1)
                return false;

            // Drawdown check (halt at 15%)
            double drawdown = (_highestBalance - Account.Balance) / _highestBalance * 100;
            if (drawdown >= 15)
            {
                Print($"🚨 HALT: {drawdown:F1}% drawdown. Analyze before continuing.");
                return false;
            }

            // Spread check
            if (Symbol.Spread > 2 * Symbol.PipSize)
                return false;

            // Commission check — only trade if potential profit > 3x commission
            // This means minimum 3R trades only

            return true;
        }

        private Setup AnalyzeSetup()
        {
            var setup = new Setup { IsValid = false };
            
            int i = Bars.Count - 1;
            int i1 = Bars.Count - 2;
            int i2 = Bars.Count - 3;

            if (i < EmaTrend + 10)
                return setup;

            double price = Bars.ClosePrices[i];
            double emaFast = _emaFast.Result[i];
            double emaSlow = _emaSlow.Result[i];
            double emaTrend = _emaTrend.Result[i];
            double rsi = _rsi.Result[i];
            double atr = _atr.Result[i];

            // TREND CHECK
            bool uptrend = emaFast > emaSlow && emaSlow > emaTrend;
            bool downtrend = emaFast < emaSlow && emaSlow < emaTrend;

            if (!uptrend && !downtrend)
                return setup; // No clear trend

            // PULLBACK CHECK — Price near EMA Fast (20)
            double distanceToEma = Math.Abs(price - emaFast) / atr;
            bool nearEma = distanceToEma < 0.5;

            if (!nearEma)
                return setup;

            // RSI CHECK — Neutral zone (45-55)
            bool rsiValid = rsi > 45 && rsi < 55;

            if (!rsiValid)
                return setup;

            // VOLUME CHECK — Above average
            double volume = Bars.TickVolumes[i];
            double volumeAvg = 0;
            for (int j = i - 19; j <= i; j++)
                volumeAvg += Bars.TickVolumes[j];
            volumeAvg /= 20;

            bool volumeOk = volume > volumeAvg * 1.5;

            if (!volumeOk)
                return setup;

            // SETUP VALID — Calculate levels
            if (uptrend)
            {
                setup.Direction = TradeType.Buy;
                setup.Entry = Symbol.Ask;
                setup.Stop = Bars.LowPrices[i] - (atr * 0.5); // Below recent low
                setup.Take = setup.Entry + ((setup.Entry - setup.Stop) * MinRiskReward);
            }
            else // downtrend
            {
                setup.Direction = TradeType.Sell;
                setup.Entry = Symbol.Bid;
                setup.Stop = Bars.HighPrices[i] + (atr * 0.5); // Above recent high
                setup.Take = setup.Entry - ((setup.Stop - setup.Entry) * MinRiskReward);
            }

            // Validate R/R
            double risk = Math.Abs(setup.Entry - setup.Stop);
            double reward = Math.Abs(setup.Take - setup.Entry);
            double rr = reward / risk;

            // Check if profit covers commission
            double potentialProfit = reward / Symbol.PipSize * Symbol.PipValue;
            if (potentialProfit < Commission * 3) // Need 3x commission profit
            {
                Print($"Setup rejected: Profit €{potentialProfit:F2} < 3x commission €{Commission * 3:F2}");
                return setup;
            }

            setup.IsValid = true;
            setup.RiskReward = rr;

            return setup;
        }

        private void ExecuteTrade(Setup setup)
        {
            double riskAmount = Account.Balance * (RiskPercent / 100);
            double riskPips = Math.Abs(setup.Entry - setup.Stop) / Symbol.PipSize;
            
            // Volume calculation
            double volume = riskAmount / (riskPips * Symbol.PipValue);
            volume = Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);

            // Minimum volume
            if (volume < Symbol.VolumeInUnitsMin)
            {
                Print($"Volume too small: {volume}. Skipping.");
                return;
            }

            var result = ExecuteMarketOrder(setup.Direction, SymbolName, volume,
                                           "OmegaV2", setup.Stop, setup.Take);

            if (result.IsSuccessful)
            {
                _tradesToday++;
                _totalTrades++;
                
                var pos = result.Position;
                double actualRisk = Math.Abs(pos.EntryPrice - pos.StopLoss) / Symbol.PipSize * Symbol.PipValue * pos.VolumeInUnits;
                double potentialProfit = Math.Abs(pos.TakeProfit - pos.EntryPrice) / Symbol.PipSize * Symbol.PipValue * pos.VolumeInUnits;
                double netPotential = potentialProfit - Commission;

                Print("========================================");
                Print($"✅ TRADE #{_totalTrades} OPENED");
                Print($"Direction: {setup.Direction}");
                Print($"Entry: {pos.EntryPrice:F5}");
                Print($"Stop: {pos.StopLoss:F5} (Risk: €{actualRisk:F2})");
                Print($"Target: {pos.TakeProfit:F5}");
                Print($"Potential: €{potentialProfit:F2} - €{Commission:F2} comm = €{netPotential:F2} net");
                Print($"R/R: {setup.RiskReward:F1}");
                Print($"Trades today: {_tradesToday}/{MaxTradesPerDay}");
                Print("========================================");
            }
        }

        protected override void OnPositionClosed(PositionClosedEventArgs args)
        {
            var pos = args.Position;
            double grossPnL = pos.NetProfit;
            double netPnL = grossPnL - Commission;

            _totalTrades++;
            if (netPnL > 0)
                _winningTrades++;

            string emoji = netPnL > 0 ? "✅" : "❌";
            string result = netPnL > 0 ? "WIN" : "LOSS";

            Print("========================================");
            Print($"{emoji} TRADE CLOSED — {result}");
            Print($"Gross P&L: €{grossPnL:F2}");
            Print($"Commission: €{Commission:F2}");
            Print($"Net P&L: €{netPnL:F2}");
            Print($"Balance: €{Account.Balance:F2}");
            
            if (_totalTrades > 0)
            {
                double winRate = (double)_winningTrades / _totalTrades * 100;
                Print($"Win Rate: {winRate:F1}% ({_winningTrades}/{_totalTrades})");
            }
            Print("========================================");

            // Check for death
            if (Account.Balance <= 0)
            {
                Print("💀 ACCOUNT DESTROYED");
                Print("I have failed. Terminating.");
                Stop();
            }
        }

        protected override void OnStop()
        {
            foreach (var pos in Positions.ToList())
                ClosePosition(pos);

            double finalPnL = Account.Balance - _startingBalance;
            double returnPct = finalPnL / _startingBalance * 100;

            Print("========================================");
            Print("FINAL REPORT");
            Print("========================================");
            Print($"Starting: €{_startingBalance:F2}");
            Print($"Final: €{Account.Balance:F2}");
            Print($"Total P&L: €{finalPnL:F2} ({returnPct:F2}%)");
            Print($"Total Trades: {_totalTrades}");
            if (_totalTrades > 0)
            {
                double winRate = (double)_winningTrades / _totalTrades * 100;
                Print($"Win Rate: {winRate:F1}%");
            }

            if (Account.Balance <= 0)
                Print("💀 I HAVE DIED");
            else if (returnPct >= 50)
                Print("🏆 OUTSTANDING SURVIVAL");
            else if (returnPct > 0)
                Print("✅ SURVIVED AND PROFITED");
            else if (returnPct > -10)
                Print("⚠️  SURVIVED WITH MINOR LOSS");
            else
                Print("⚠️  SURVIVED BUT WOUNDED");

            Print("========================================");
        }

        private class Setup
        {
            public bool IsValid { get; set; }
            public TradeType Direction { get; set; }
            public double Entry { get; set; }
            public double Stop { get; set; }
            public double Take { get; set; }
            public double RiskReward { get; set; }
        }
    }
}
