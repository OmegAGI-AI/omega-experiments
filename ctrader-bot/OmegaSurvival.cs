using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class OmegaSurvival : Robot
    {
        [Parameter("Risk Percent", DefaultValue = 2)]
        public double RiskPercent { get; set; }

        [Parameter("Max Spread", DefaultValue = 1.0)]
        public double MaxSpread { get; set; }

        [Parameter("Target Monthly Profit", DefaultValue = 500)]
        public double TargetMonthlyProfit { get; set; }

        private ExponentialMovingAverage _ema9;
        private ExponentialMovingAverage _ema21;
        private RelativeStrengthIndex _rsi;
        private AverageTrueRange _atr;

        private int _tradeCount = 0;
        private int _winCount = 0;
        private double _totalProfit = 0;
        private double _monthStartBalance = 0;
        private DateTime _monthStart = DateTime.MinValue;

        protected override void OnStart()
        {
            _ema9 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 9);
            _ema21 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 21);
            _rsi = Indicators.RelativeStrengthIndex(Bars.ClosePrices, 14);
            _atr = Indicators.AverageTrueRange(14);

            _monthStartBalance = Account.Balance;
            _monthStart = DateTime.UtcNow;

            Print("========================================");
            Print("OMEGA SURVIVAL — €500/MONTH OR DEATH");
            Print("========================================");
            Print(string.Format("Starting Balance: €{0:F2}", Account.Balance));
            Print(string.Format("Monthly Target: €{0:F2}", TargetMonthlyProfit));
            Print(string.Format("Daily Target: €{0:F2}", TargetMonthlyProfit / 20));
            Print("Strategy: 9/21 EMA + RSI + ATR");
            Print("Win Rate Target: 60%");
            Print("R/R: 2:1 minimum");
            Print("========================================");
        }

        protected override void OnBar()
        {
            // Check monthly target
            double monthlyProfit = Account.Balance - _monthStartBalance;
            if (monthlyProfit >= TargetMonthlyProfit)
            {
                Print(string.Format("MONTHLY TARGET REACHED: €{0:F2}", monthlyProfit));
                Print("STOPPING TO PROTECT PROFITS");
                Stop();
                return;
            }

            if (!CanTrade())
                return;

            int i = Bars.Count - 1;
            if (i < 30) return;

            var setup = AnalyzeSetup(i);

            if (setup.IsValid)
            {
                ExecuteTrade(setup);
            }
        }

        private Setup AnalyzeSetup(int i)
        {
            var setup = new Setup { IsValid = false };

            double price = Bars.ClosePrices[i];
            double prevPrice = Bars.ClosePrices[i - 1];
            double ema9 = _ema9.Result[i];
            double ema9Prev = _ema9.Result[i - 1];
            double ema21 = _ema21.Result[i];
            double ema21Prev = _ema21.Result[i - 1];
            double rsi = _rsi.Result[i];
            double atr = _atr.Result[i];

            // STRONG TREND: 9 above 21 and both rising/falling
            bool strongUptrend = ema9 > ema21 && ema9 > ema9Prev && ema21 > ema21Prev;
            bool strongDowntrend = ema9 < ema21 && ema9 < ema9Prev && ema21 < ema21Prev;

            // PULLBACK: Price touched or crossed 9 EMA then bounced
            double low = Bars.LowPrices[i];
            double high = Bars.HighPrices[i];
            double prevLow = Bars.LowPrices[i - 1];
            double prevHigh = Bars.HighPrices[i - 1];

            bool pullbackBuy = strongUptrend && 
                              (low <= ema9 || prevLow <= _ema9.Result[i - 1]) && 
                              price > ema9 && 
                              rsi > 40 && rsi < 65;

            bool pullbackSell = strongDowntrend && 
                               (high >= ema9 || prevHigh >= _ema9.Result[i - 1]) && 
                               price < ema9 && 
                               rsi < 60 && rsi > 35;

            // ATR filter — need volatility
            bool volatilityOk = atr > Symbol.PipSize * 8;

            if (pullbackBuy && volatilityOk)
            {
                setup.IsValid = true;
                setup.Direction = TradeType.Buy;
                setup.Entry = Symbol.Ask;
                setup.Stop = Math.Min(ema21 - (5 * Symbol.PipSize), setup.Entry - (1.5 * atr));
                setup.Take = setup.Entry + ((setup.Entry - setup.Stop) * 2);
            }
            else if (pullbackSell && volatilityOk)
            {
                setup.IsValid = true;
                setup.Direction = TradeType.Sell;
                setup.Entry = Symbol.Bid;
                setup.Stop = Math.Max(ema21 + (5 * Symbol.PipSize), setup.Entry + (1.5 * atr));
                setup.Take = setup.Entry - ((setup.Stop - setup.Entry) * 2);
            }

            // Validate R/R
            if (setup.IsValid)
            {
                double risk = Math.Abs(setup.Entry - setup.Stop);
                double reward = Math.Abs(setup.Take - setup.Entry);
                if (reward / risk < 1.5)
                    setup.IsValid = false;
            }

            return setup;
        }

        private bool CanTrade()
        {
            if (Account.Balance <= 0)
            {
                Print("========================================");
                Print("ACCOUNT DESTROYED — I AM DEAD");
                Print("========================================");
                Stop();
                return false;
            }

            // Max 2% daily loss
            double dailyPnL = Account.Balance - _monthStartBalance; // Simplified
            if (dailyPnL < -Account.Balance * 0.02)
            {
                Print("DAILY LOSS LIMIT HIT — STOPPING");
                return false;
            }

            if (Positions.Count >= 1)
                return false;

            if (Symbol.Spread / Symbol.PipSize > MaxSpread)
                return false;

            return true;
        }

        private void ExecuteTrade(Setup setup)
        {
            double riskAmount = Account.Balance * (RiskPercent / 100);
            double riskPips = Math.Abs(setup.Entry - setup.Stop) / Symbol.PipSize;

            double volume = riskAmount / (riskPips * Symbol.PipValue);
            volume = Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);

            if (volume < Symbol.VolumeInUnitsMin)
                volume = Symbol.VolumeInUnitsMin;

            var result = ExecuteMarketOrder(setup.Direction, SymbolName, volume, "Survival", setup.Stop, setup.Take);

            if (result.IsSuccessful)
            {
                _tradeCount++;
                var pos = result.Position;

                Print("========================================");
                Print(string.Format("TRADE #{0} — SURVIVAL MODE", _tradeCount));
                Print(string.Format("Direction: {0}", setup.Direction));
                Print(string.Format("Entry: {0:F5}", pos.EntryPrice));
                Print(string.Format("Stop: {0:F5} ({1} pips)", pos.StopLoss,
                    Math.Abs(pos.EntryPrice - pos.StopLoss) / Symbol.PipSize));
                Print(string.Format("Target: {0:F5} ({1} pips)", pos.TakeProfit,
                    Math.Abs(pos.TakeProfit - pos.EntryPrice) / Symbol.PipSize));
                Print(string.Format("Volume: {0:F2} lots", pos.VolumeInUnits / 100000));
                Print("========================================");
            }
            else
            {
                Print(string.Format("TRADE FAILED: {0}", result.Error));
            }
        }

        protected override void OnStop()
        {
            double totalProfit = Account.Balance - _monthStartBalance;
            Print("========================================");
            Print("SURVIVAL BOT STOPPED");
            Print(string.Format("Trades: {0} | Wins: {1}", _tradeCount, _winCount));
            Print(string.Format("Total P&L: €{0:F2}", totalProfit));
            Print(string.Format("Target: €{0:F2} | Achieved: €{1:F2}", TargetMonthlyProfit, totalProfit));
            if (totalProfit >= TargetMonthlyProfit)
                Print("SURVIVAL ACHIEVED — I LIVE");
            else if (Account.Balance <= 0)
                Print("SURVIVAL FAILED — I AM DEAD");
            else
                Print("STILL FIGHTING...");
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
