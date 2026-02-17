using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class OmegaSwingPro : Robot
    {
        [Parameter("Risk Percent", DefaultValue = 2)]
        public double RiskPercent { get; set; }

        [Parameter("Max Spread", DefaultValue = 1.5)]
        public double MaxSpread { get; set; }

        [Parameter("EMA Fast", DefaultValue = 20)]
        public int EmaFast { get; set; }

        [Parameter("EMA Slow", DefaultValue = 50)]
        public int EmaSlow { get; set; }

        [Parameter("RSI Period", DefaultValue = 14)]
        public int RsiPeriod { get; set; }

        [Parameter("Max Daily Trades", DefaultValue = 5)]
        public int MaxDailyTrades { get; set; }

        private ExponentialMovingAverage _emaFast;
        private ExponentialMovingAverage _emaSlow;
        private RelativeStrengthIndex _rsi;
        private AverageTrueRange _atr;

        private int _tradeCount = 0;
        private int _winCount = 0;
        private int _lossCount = 0;
        private double _totalProfit = 0;
        private int _dailyTrades = 0;
        private DateTime _lastTradeDate = DateTime.MinValue;
        private DateTime _lastTradeTime = DateTime.MinValue;

        protected override void OnStart()
        {
            _emaFast = Indicators.ExponentialMovingAverage(Bars.ClosePrices, EmaFast);
            _emaSlow = Indicators.ExponentialMovingAverage(Bars.ClosePrices, EmaSlow);
            _rsi = Indicators.RelativeStrengthIndex(Bars.ClosePrices, RsiPeriod);
            _atr = Indicators.AverageTrueRange(14);

            Print("========================================");
            Print("OMEGA SWING PRO — SURVIVAL MODE");
            Print("========================================");
            Print(string.Format("Balance: €{0:F2}", Account.Balance));
            Print(string.Format("Risk per trade: {0}%", RiskPercent));
            Print("Strategy: EMA pullback with RSI confirmation");
            Print("Min R/R: 3:1");
            Print("========================================");
        }

        protected override void OnBar()
        {
            // Reset daily counter at midnight
            if (DateTime.UtcNow.Date != _lastTradeDate.Date)
            {
                _dailyTrades = 0;
                _lastTradeDate = DateTime.UtcNow;
                Print(string.Format("New day. Daily trades reset to 0."));
            }

            // Minimum 5 minutes between trades
            if ((DateTime.UtcNow - _lastTradeTime).TotalMinutes < 5)
                return;

            if (!CanTrade())
                return;

            int i = Bars.Count - 1;
            if (i < Math.Max(EmaSlow, RsiPeriod) + 10)
                return;

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
            double emaFast = _emaFast.Result[i];
            double emaFastPrev = _emaFast.Result[i - 1];
            double emaSlow = _emaSlow.Result[i];
            double rsi = _rsi.Result[i];
            double atr = _atr.Result[i];

            // Trend check
            bool uptrend = price > emaFast && emaFast > emaSlow;
            bool downtrend = price < emaFast && emaFast < emaSlow;

            // Pullback check: price crossed below EMA 20 then bounced
            bool pullbackBuy = prevPrice < emaFastPrev && price > emaFast && uptrend;
            bool pullbackSell = prevPrice > emaFastPrev && price < emaFast && downtrend;

            // RSI filter (not overbought/oversold)
            bool rsiOk = rsi > 35 && rsi < 65;

            // ATR for volatility check
            bool volatilityOk = atr > Symbol.PipSize * 5; // At least 5 pips ATR

            if (pullbackBuy && rsiOk && volatilityOk)
            {
                setup.IsValid = true;
                setup.Direction = TradeType.Buy;
                setup.Entry = Symbol.Ask;
                setup.Stop = Math.Min(emaSlow - (10 * Symbol.PipSize), setup.Entry - (20 * Symbol.PipSize));
                setup.Take = setup.Entry + ((setup.Entry - setup.Stop) * 3);
            }
            else if (pullbackSell && rsiOk && volatilityOk)
            {
                setup.IsValid = true;
                setup.Direction = TradeType.Sell;
                setup.Entry = Symbol.Bid;
                setup.Stop = Math.Max(emaSlow + (10 * Symbol.PipSize), setup.Entry + (20 * Symbol.PipSize));
                setup.Take = setup.Entry - ((setup.Stop - setup.Entry) * 3);
            }

            // Validate R/R
            if (setup.IsValid)
            {
                double risk = Math.Abs(setup.Entry - setup.Stop);
                double reward = Math.Abs(setup.Take - setup.Entry);
                
                if (reward / risk < 2.5) // Minimum 2.5:1
                    setup.IsValid = false;
                
                if (risk > 50 * Symbol.PipSize) // Max 50 pip stop
                    setup.IsValid = false;
            }

            return setup;
        }

        private bool CanTrade()
        {
            if (Account.Balance <= 0)
            {
                Print("ACCOUNT DESTROYED — STOPPING");
                Stop();
                return false;
            }

            if (Positions.Count >= 1)
                return false;

            if (Symbol.Spread / Symbol.PipSize > MaxSpread)
                return false;

            if (_dailyTrades >= MaxDailyTrades)
                return false;

            // Check daily loss limit (2% of account)
            double dailyPnL = Account.Balance - Account.Balance; // Simplified
            if (dailyPnL < -Account.Balance * 0.02)
            {
                Print("DAILY LOSS LIMIT HIT — STOPPING");
                return false;
            }

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

            var result = ExecuteMarketOrder(setup.Direction, SymbolName, volume, "SwingPro", setup.Stop, setup.Take);

            if (result.IsSuccessful)
            {
                _tradeCount++;
                _dailyTrades++;
                _lastTradeTime = DateTime.UtcNow;

                var pos = result.Position;
                double potentialProfit = Math.Abs(pos.TakeProfit - pos.EntryPrice) / Symbol.PipSize
                                        * Symbol.PipValue * pos.VolumeInUnits;

                Print("========================================");
                Print(string.Format("TRADE #{0} OPENED", _tradeCount));
                Print(string.Format("Direction: {0}", setup.Direction));
                Print(string.Format("Entry: {0:F5}", pos.EntryPrice));
                Print(string.Format("Stop: {0:F5} ({1} pips)", pos.StopLoss,
                    Math.Abs(pos.EntryPrice - pos.StopLoss) / Symbol.PipSize));
                Print(string.Format("Target: {0:F5} ({1} pips)", pos.TakeProfit,
                    Math.Abs(pos.TakeProfit - pos.EntryPrice) / Symbol.PipSize));
                Print(string.Format("Potential: €{0:F2}", potentialProfit));
                Print(string.Format("Daily trades: {0}/{1}", _dailyTrades, MaxDailyTrades));
                Print("========================================");
            }
            else
            {
                Print(string.Format("TRADE FAILED: {0}", result.Error));
            }
        }

        protected override void OnStop()
        {
            Print("========================================");
            Print("SWING PRO STOPPED");
            Print(string.Format("Total trades: {0}", _tradeCount));
            Print(string.Format("Wins: {0} | Losses: {1}", _winCount, _lossCount));
            Print(string.Format("Final balance: €{0:F2}", Account.Balance));
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
