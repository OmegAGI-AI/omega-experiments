using System;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class OmegaPrime : Robot
    {
        // Risk Management
        [Parameter("Risk Percent", DefaultValue = 2)]
        public double RiskPercent { get; set; }

        [Parameter("Max Daily Risk", DefaultValue = 6)]
        public double MaxDailyRisk { get; set; }

        [Parameter("Max Spread", DefaultValue = 1.0)]
        public double MaxSpread { get; set; }

        // Strategy Selection
        [Parameter("Use Trend Strategy", DefaultValue = true)]
        public bool UseTrend { get; set; }

        [Parameter("Use Mean Reversion", DefaultValue = true)]
        public bool UseMeanReversion { get; set; }

        [Parameter("Use Breakout", DefaultValue = true)]
        public bool UseBreakout { get; set; }

        // Indicators - Multiple Timeframes
        private ExponentialMovingAverage _ema8;
        private ExponentialMovingAverage _ema21;
        private ExponentialMovingAverage _ema50;
        private RelativeStrengthIndex _rsi;
        private BollingerBands _bb;
        private AverageTrueRange _atr;
        private MacdHistogram _macd;

        // Tracking
        private int _tradeCount = 0;
        private int _winCount = 0;
        private int _lossCount = 0;
        private double _dailyRiskUsed = 0;
        private DateTime _lastTradeDate = DateTime.MinValue;
        private DateTime _lastTradeTime = DateTime.MinValue;
        private double _highestBalance = 0;

        protected override void OnStart()
        {
            // Initialize indicators
            _ema8 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 8);
            _ema21 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 21);
            _ema50 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 50);
            _rsi = Indicators.RelativeStrengthIndex(Bars.ClosePrices, 14);
            _bb = Indicators.BollingerBands(Bars.ClosePrices, 20, 2, MovingAverageType.Simple);
            _atr = Indicators.AverageTrueRange(14);
            _macd = Indicators.MacdHistogram(Bars.ClosePrices, 12, 26, 9);

            _highestBalance = Account.Balance;

            Print("========================================");
            Print("OMEGA PRIME — MULTI-STRATEGY SYSTEM");
            Print("========================================");
            Print(string.Format("Balance: {0:F2}", Account.Balance));
            Print(string.Format("Risk per trade: {0}%", RiskPercent));
            Print(string.Format("Max daily risk: {0}%", MaxDailyRisk));
            Print("Strategies: Trend + Mean Reversion + Breakout");
            Print("Target: 60% win rate, 2.5:1 R/R");
            Print("========================================");
        }

        protected override void OnBar()
        {
            // Reset daily tracking
            if (DateTime.UtcNow.Date != _lastTradeDate.Date)
            {
                _dailyRiskUsed = 0;
                _lastTradeDate = DateTime.UtcNow;
                Print(string.Format("New day. Daily risk reset."));
            }

            // Update highest balance for drawdown tracking
            if (Account.Balance > _highestBalance)
                _highestBalance = Account.Balance;

            // Minimum 10 minutes between trades
            if ((DateTime.UtcNow - _lastTradeTime).TotalMinutes < 10)
                return;

            if (!CanTrade())
                return;

            int i = Bars.Count - 1;
            if (i < 55) return;

            // Analyze market regime
            var regime = AnalyzeMarketRegime(i);

            // Select strategy based on regime
            Setup setup = null;

            if (regime.IsTrending && UseTrend)
            {
                setup = TrendStrategy(i);
            }
            else if (regime.IsRanging && UseMeanReversion)
            {
                setup = MeanReversionStrategy(i);
            }
            else if (regime.IsBreaking && UseBreakout)
            {
                setup = BreakoutStrategy(i);
            }

            if (setup != null && setup.IsValid)
            {
                ExecuteTrade(setup, regime.Name);
            }
        }

        private MarketRegime AnalyzeMarketRegime(int i)
        {
            var regime = new MarketRegime();

            double ema8 = _ema8.Result[i];
            double ema21 = _ema21.Result[i];
            double ema50 = _ema50.Result[i];
            double atr = _atr.Result[i];
            double bbWidth = (_bb.Top[i] - _bb.Bottom[i]) / _bb.Main[i];

            // Trend strength
            bool emaAligned = (ema8 > ema21 && ema21 > ema50) || (ema8 < ema21 && ema21 < ema50);
            double adx = CalculateADX(i);

            // Range detection
            bool inRange = bbWidth < 0.03 && adx < 25;

            // Breakout detection
            double high20 = GetHighestHigh(i, 20);
            double low20 = GetLowestLow(i, 20);
            bool nearHigh = Bars.ClosePrices[i] > high20 - (atr * 0.5);
            bool nearLow = Bars.ClosePrices[i] < low20 + (atr * 0.5);

            if (emaAligned && adx > 25)
            {
                regime.IsTrending = true;
                regime.Name = "TREND";
            }
            else if (inRange)
            {
                regime.IsRanging = true;
                regime.Name = "RANGE";
            }
            else if (nearHigh || nearLow)
            {
                regime.IsBreaking = true;
                regime.Name = "BREAKOUT";
            }
            else
            {
                regime.IsTrending = true; // Default to trend
                regime.Name = "TREND";
            }

            return regime;
        }

        private Setup TrendStrategy(int i)
        {
            var setup = new Setup { IsValid = false };

            double price = Bars.ClosePrices[i];
            double ema8 = _ema8.Result[i];
            double ema21 = _ema21.Result[i];
            double rsi = _rsi.Result[i];
            double macd = _macd.Histogram[i];
            double macdPrev = _macd.Histogram[i - 1];

            bool uptrend = ema8 > ema21;
            bool downtrend = ema8 < ema21;

            // Pullback to EMA 8 with MACD confirmation
            bool pullbackBuy = uptrend && price <= ema8 && price > ema21 && rsi > 40 && rsi < 65 && macd > macdPrev;
            bool pullbackSell = downtrend && price >= ema8 && price < ema21 && rsi < 60 && rsi > 35 && macd < macdPrev;

            if (pullbackBuy)
            {
                setup.IsValid = true;
                setup.Direction = TradeType.Buy;
                setup.Entry = Symbol.Ask;
                setup.Stop = ema21 - (5 * Symbol.PipSize);
                setup.Take = setup.Entry + ((setup.Entry - setup.Stop) * 2.5);
            }
            else if (pullbackSell)
            {
                setup.IsValid = true;
                setup.Direction = TradeType.Sell;
                setup.Entry = Symbol.Bid;
                setup.Stop = ema21 + (5 * Symbol.PipSize);
                setup.Take = setup.Entry - ((setup.Stop - setup.Entry) * 2.5);
            }

            return ValidateSetup(setup);
        }

        private Setup MeanReversionStrategy(int i)
        {
            var setup = new Setup { IsValid = false };

            double price = Bars.ClosePrices[i];
            double rsi = _rsi.Result[i];
            double bbTop = _bb.Top[i];
            double bbBottom = _bb.Bottom[i];
            double bbMain = _bb.Main[i];

            // Oversold bounce from lower band
            bool oversoldBounce = price <= bbBottom && rsi < 30 && rsi > _rsi.Result[i - 1];
            // Overbought pullback from upper band  
            bool overboughtPullback = price >= bbTop && rsi > 70 && rsi < _rsi.Result[i - 1];

            if (oversoldBounce)
            {
                setup.IsValid = true;
                setup.Direction = TradeType.Buy;
                setup.Entry = Symbol.Ask;
                setup.Stop = price - (15 * Symbol.PipSize);
                setup.Take = bbMain;
            }
            else if (overboughtPullback)
            {
                setup.IsValid = true;
                setup.Direction = TradeType.Sell;
                setup.Entry = Symbol.Bid;
                setup.Stop = price + (15 * Symbol.PipSize);
                setup.Take = bbMain;
            }

            return ValidateSetup(setup);
        }

        private Setup BreakoutStrategy(int i)
        {
            var setup = new Setup { IsValid = false };

            double price = Bars.ClosePrices[i];
            double prevPrice = Bars.ClosePrices[i - 1];
            double rsi = _rsi.Result[i];
            double volume = Bars.TickVolumes[i];
            double volumeAvg = 0;
            for (int j = i - 9; j <= i; j++)
                volumeAvg += Bars.TickVolumes[j];
            volumeAvg /= 10;

            double high20 = GetHighestHigh(i - 1, 20);
            double low20 = GetLowestLow(i - 1, 20);

            bool breakoutUp = price > high20 && prevPrice <= high20 && volume > volumeAvg * 1.5 && rsi > 50 && rsi < 75;
            bool breakoutDown = price < low20 && prevPrice >= low20 && volume > volumeAvg * 1.5 && rsi < 50 && rsi > 25;

            if (breakoutUp)
            {
                setup.IsValid = true;
                setup.Direction = TradeType.Buy;
                setup.Entry = Symbol.Ask;
                setup.Stop = high20 - (10 * Symbol.PipSize);
                setup.Take = setup.Entry + ((setup.Entry - setup.Stop) * 2);
            }
            else if (breakoutDown)
            {
                setup.IsValid = true;
                setup.Direction = TradeType.Sell;
                setup.Entry = Symbol.Bid;
                setup.Stop = low20 + (10 * Symbol.PipSize);
                setup.Take = setup.Entry - ((setup.Stop - setup.Entry) * 2);
            }

            return ValidateSetup(setup);
        }

        private Setup ValidateSetup(Setup setup)
        {
            if (!setup.IsValid) return setup;

            double risk = setup.Entry - setup.Stop;
            if (risk < 0) risk = -risk;
            double reward = setup.Take - setup.Entry;
            if (reward < 0) reward = -reward;

            // Minimum 1.5:1 R/R
            if (reward / risk < 1.5)
                setup.IsValid = false;

            // Maximum 50 pip stop
            if (risk / Symbol.PipSize > 50)
                setup.IsValid = false;

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

            // Drawdown protection — stop at 20% drawdown
            double drawdown = (_highestBalance - Account.Balance) / _highestBalance;
            if (drawdown > 0.20)
            {
                Print(string.Format("DRAWDOWN LIMIT HIT: {0:P}", drawdown));
                return false;
            }

            if (Positions.Count >= 1)
                return false;

            if (Symbol.Spread / Symbol.PipSize > MaxSpread)
                return false;

            if (_dailyRiskUsed >= MaxDailyRisk)
            {
                Print("DAILY RISK LIMIT REACHED");
                return false;
            }

            return true;
        }

        private void ExecuteTrade(Setup setup, string strategy)
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
                double risk = pos.EntryPrice - pos.StopLoss;
                if (risk < 0) risk = -risk;
                double reward = pos.TakeProfit - pos.EntryPrice;
                if (reward < 0) reward = -reward;
                double rr = reward / risk;

                Print("========================================");
                Print(string.Format("PRIME TRADE #{0} — {1}", _tradeCount, strategy));
                Print(string.Format("Direction: {0}", setup.Direction));
                Print(string.Format("Entry: {0:F5}", pos.EntryPrice));
                Print(string.Format("Stop: {0:F5} | Target: {1:F5}", pos.StopLoss, pos.TakeProfit));
                Print(string.Format("R/R: {0:F1}:1", rr));
                Print(string.Format("Daily risk used: {0:F1}%", _dailyRiskUsed));
                Print("========================================");
            }
            else
            {
                Print(string.Format("TRADE FAILED: {0}", result.Error));
            }
        }

        private double CalculateADX(int i)
        {
            // Simplified ADX calculation
            double plusDM = 0, minusDM = 0, tr = 0;
            for (int j = i - 13; j <= i; j++)
            {
                double up = Bars.HighPrices[j] - Bars.HighPrices[j - 1];
                double down = Bars.LowPrices[j - 1] - Bars.LowPrices[j];
                if (up > down && up > 0) plusDM += up;
                if (down > up && down > 0) minusDM += down;
                tr += (Bars.HighPrices[j] - Bars.LowPrices[j]);
            }
            if (tr == 0) return 0;
            return ((plusDM + minusDM) / tr) * 100;
        }

        private double GetHighestHigh(int start, int periods)
        {
            double highest = Bars.HighPrices[start];
            for (int j = start - periods + 1; j <= start; j++)
            {
                if (Bars.HighPrices[j] > highest)
                    highest = Bars.HighPrices[j];
            }
            return highest;
        }

        private double GetLowestLow(int start, int periods)
        {
            double lowest = Bars.LowPrices[start];
            for (int j = start - periods + 1; j <= start; j++)
            {
                if (Bars.LowPrices[j] < lowest)
                    lowest = Bars.LowPrices[j];
            }
            return lowest;
        }

        protected override void OnStop()
        {
            Print("========================================");
            Print("OMEGA PRIME STOPPED");
            Print(string.Format("Total trades: {0}", _tradeCount));
            Print(string.Format("Final balance: {0:F2}", Account.Balance));
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

        private class MarketRegime
        {
            public string Name { get; set; }
            public bool IsTrending { get; set; }
            public bool IsRanging { get; set; }
            public bool IsBreaking { get; set; }
        }
    }
}
