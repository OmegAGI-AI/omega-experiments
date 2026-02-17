using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class OmegaPrime : Robot
    {
        [Parameter("Risk Percent", DefaultValue = 2)]
        public double RiskPercent { get; set; }

        [Parameter("Max Daily Risk", DefaultValue = 6)]
        public double MaxDailyRisk { get; set; }

        [Parameter("Max Spread", DefaultValue = 1.0)]
        public double MaxSpread { get; set; }

        private ExponentialMovingAverage _ema8;
        private ExponentialMovingAverage _ema21;
        private ExponentialMovingAverage _ema50;
        private RelativeStrengthIndex _rsi;
        private BollingerBands _bb;
        private AverageTrueRange _atr;
        private MacdHistogram _macd;

        private int _tradeCount = 0;
        private double _dailyRiskUsed = 0;
        private DateTime _lastTradeDate = DateTime.MinValue;
        private DateTime _lastTradeTime = DateTime.MinValue;
        private double _highestBalance = 0;

        protected override void OnStart()
        {
            _ema8 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 8);
            _ema21 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 21);
            _ema50 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 50);
            _rsi = Indicators.RelativeStrengthIndex(Bars.ClosePrices, 14);
            _bb = Indicators.BollingerBands(Bars.ClosePrices, 20, 2, MovingAverageType.Simple);
            _atr = Indicators.AverageTrueRange(14);
            _macd = Indicators.MacdHistogram(Bars.ClosePrices, 12, 26, 9);

            _highestBalance = Account.Balance;

            Print("========================================");
            Print("OMEGA PRIME — MULTI-STRATEGY");
            Print("========================================");
            Print(string.Format("Balance: {0:F2}", Account.Balance));
            Print(string.Format("Risk: {0}% per trade", RiskPercent));
            Print("========================================");
        }

        protected override void OnBar()
        {
            if (DateTime.UtcNow.Date != _lastTradeDate.Date)
            {
                _dailyRiskUsed = 0;
                _lastTradeDate = DateTime.UtcNow;
            }

            if (Account.Balance > _highestBalance)
                _highestBalance = Account.Balance;

            if ((DateTime.UtcNow - _lastTradeTime).TotalMinutes < 10)
                return;

            if (!CanTrade())
                return;

            int i = Bars.Count - 1;
            if (i < 55) return;

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
            double ema8 = _ema8.Result[i];
            double ema21 = _ema21.Result[i];
            double ema50 = _ema50.Result[i];
            double rsi = _rsi.Result[i];
            double macd = _macd.Histogram[i];
            double macdPrev = _macd.Histogram[i - 1];
            double bbTop = _bb.Top[i];
            double bbBottom = _bb.Bottom[i];
            double bbMain = _bb.Main[i];
            double atr = _atr.Result[i];

            // STRATEGY 1: Trend Pullback
            bool uptrend = ema8 > ema21 && ema21 > ema50;
            bool downtrend = ema8 < ema21 && ema21 < ema50;

            bool pullbackBuy = uptrend && price <= ema8 && price > ema21 && rsi > 40 && rsi < 60 && macd > macdPrev;
            bool pullbackSell = downtrend && price >= ema8 && price < ema21 && rsi < 60 && rsi > 40 && macd < macdPrev;

            // STRATEGY 2: Mean Reversion (Bollinger Bands)
            bool oversold = price <= bbBottom && rsi < 30;
            bool overbought = price >= bbTop && rsi > 70;

            // STRATEGY 3: Breakout
            double high20 = GetHigh20(i);
            double low20 = GetLow20(i);
            bool breakoutUp = price > high20 && Bars.ClosePrices[i-1] <= high20 && rsi > 50 && rsi < 75;
            bool breakoutDown = price < low20 && Bars.ClosePrices[i-1] >= low20 && rsi < 50 && rsi > 25;

            // PRIORITY: Trend > Mean Reversion > Breakout
            if (pullbackBuy)
            {
                setup.IsValid = true;
                setup.Direction = TradeType.Buy;
                setup.Entry = Symbol.Ask;
                setup.Stop = ema21 - (8 * Symbol.PipSize);
                setup.Take = setup.Entry + ((setup.Entry - setup.Stop) * 2);
                setup.Strategy = "TREND";
            }
            else if (pullbackSell)
            {
                setup.IsValid = true;
                setup.Direction = TradeType.Sell;
                setup.Entry = Symbol.Bid;
                setup.Stop = ema21 + (8 * Symbol.PipSize);
                setup.Take = setup.Entry - ((setup.Stop - setup.Entry) * 2);
                setup.Strategy = "TREND";
            }
            else if (oversold)
            {
                setup.IsValid = true;
                setup.Direction = TradeType.Buy;
                setup.Entry = Symbol.Ask;
                setup.Stop = price - (12 * Symbol.PipSize);
                setup.Take = bbMain;
                setup.Strategy = "REVERSION";
            }
            else if (overbought)
            {
                setup.IsValid = true;
                setup.Direction = TradeType.Sell;
                setup.Entry = Symbol.Bid;
                setup.Stop = price + (12 * Symbol.PipSize);
                setup.Take = bbMain;
                setup.Strategy = "REVERSION";
            }
            else if (breakoutUp)
            {
                setup.IsValid = true;
                setup.Direction = TradeType.Buy;
                setup.Entry = Symbol.Ask;
                setup.Stop = high20 - (8 * Symbol.PipSize);
                setup.Take = setup.Entry + ((setup.Entry - setup.Stop) * 2);
                setup.Strategy = "BREAKOUT";
            }
            else if (breakoutDown)
            {
                setup.IsValid = true;
                setup.Direction = TradeType.Sell;
                setup.Entry = Symbol.Bid;
                setup.Stop = low20 + (8 * Symbol.PipSize);
                setup.Take = setup.Entry - ((setup.Stop - setup.Entry) * 2);
                setup.Strategy = "BREAKOUT";
            }

            // Validate
            if (setup.IsValid)
            {
                double risk = setup.Entry - setup.Stop;
                if (risk < 0) risk = -risk;
                double reward = setup.Take - setup.Entry;
                if (reward < 0) reward = -reward;

                if (reward / risk < 1.5)
                    setup.IsValid = false;

                if (risk / Symbol.PipSize > 40)
                    setup.IsValid = false;
            }

            return setup;
        }

        private bool CanTrade()
        {
            if (Account.Balance <= 0)
            {
                Print("ACCOUNT DESTROYED");
                Stop();
                return false;
            }

            double drawdown = (_highestBalance - Account.Balance) / _highestBalance;
            if (drawdown > 0.20)
            {
                Print(string.Format("DRAWDOWN: {0:P} — STOPPING", drawdown));
                return false;
            }

            if (Positions.Count >= 1)
                return false;

            if (Symbol.Spread / Symbol.PipSize > MaxSpread)
                return false;

            if (_dailyRiskUsed >= MaxDailyRisk)
                return false;

            return true;
        }

        private void ExecuteTrade(Setup setup)
        {
            double riskAmount = Account.Balance * (RiskPercent / 100);
            double riskPips = setup.Entry - setup.Stop;
            if (riskPips < 0) riskPips = -riskPips;
            riskPips = riskPips / Symbol.PipSize;

            double volume = riskAmount / (riskPips * Symbol.PipValue);
            volume = Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);

            if (volume < Symbol.VolumeInUnitsMin)
                volume = Symbol.VolumeInUnitsMin;

            var result = ExecuteMarketOrder(setup.Direction, SymbolName, volume, "Prime", setup.Stop, setup.Take);

            if (result.IsSuccessful)
            {
                _tradeCount++;
                _dailyRiskUsed += RiskPercent;
                _lastTradeTime = DateTime.UtcNow;

                var pos = result.Position;
                Print(string.Format("TRADE #{0} — {1}: {2} at {3:F5}", 
                    _tradeCount, setup.Strategy, setup.Direction, pos.EntryPrice));
            }
            else
            {
                Print(string.Format("FAILED: {0}", result.Error));
            }
        }

        private double GetHigh20(int i)
        {
            double high = Bars.HighPrices[i];
            for (int j = i - 19; j < i; j++)
            {
                if (Bars.HighPrices[j] > high)
                    high = Bars.HighPrices[j];
            }
            return high;
        }

        private double GetLow20(int i)
        {
            double low = Bars.LowPrices[i];
            for (int j = i - 19; j < i; j++)
            {
                if (Bars.LowPrices[j] < low)
                    low = Bars.LowPrices[j];
            }
            return low;
        }

        protected override void OnStop()
        {
            Print(string.Format("PRIME STOPPED — Trades: {0}", _tradeCount));
        }

        private class Setup
        {
            public bool IsValid { get; set; }
            public TradeType Direction { get; set; }
            public double Entry { get; set; }
            public double Stop { get; set; }
            public double Take { get; set; }
            public string Strategy { get; set; }
        }
    }
}
