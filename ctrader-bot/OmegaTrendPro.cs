using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class OmegaTrendPro : Robot
    {
        [Parameter("Risk Percent", DefaultValue = 2)]
        public double RiskPercent { get; set; }

        [Parameter("Commission", DefaultValue = 6.24)]
        public double Commission { get; set; }

        [Parameter("Max Spread", DefaultValue = 2)]
        public double MaxSpread { get; set; }

        [Parameter("Max Positions", DefaultValue = 1)]
        public int MaxPositions { get; set; }

        private ExponentialMovingAverage _ema20;
        private ExponentialMovingAverage _ema50;
        private ExponentialMovingAverage _ema200;
        private AverageTrueRange _atr;

        private int _tradeCount = 0;

        protected override void OnStart()
        {
            _ema20 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 20);
            _ema50 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 50);
            _ema200 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 200);
            _atr = Indicators.AverageTrueRange(14);

            Print("========================================");
            Print("OMEGA TREND PRO — BIG MOVES ONLY");
            Print("========================================");
            Print(string.Format("Balance: €{0:F2}", Account.Balance));
            Print(string.Format("Commission: €{0:F2} per trade", Commission));
            Print("Strategy: Strong trend pullback");
            Print("Min R/R: 5:1");
            Print("========================================");
        }

        protected override void OnBar()
        {
            if (!CanTrade())
                return;

            int i = Bars.Count - 1;
            if (i < 210) return;

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
            double ema20 = _ema20.Result[i];
            double ema20Prev = _ema20.Result[i - 1];
            double ema50 = _ema50.Result[i];
            double ema200 = _ema200.Result[i];
            double atr = _atr.Result[i];

            // Strong trend alignment
            bool strongUptrend = price > ema20 && ema20 > ema50 && ema50 > ema200;
            bool strongDowntrend = price < ema20 && ema20 < ema50 && ema50 < ema200;

            // Pullback to EMA 20 with bounce
            bool pullbackBuy = prevPrice <= ema20Prev && price > ema20 && strongUptrend;
            bool pullbackSell = prevPrice >= ema20Prev && price < ema20 && strongDowntrend;

            // ATR check for volatility
            bool volatilityOk = atr > Symbol.PipSize * 10; // At least 10 pips ATR

            if (pullbackBuy && volatilityOk)
            {
                setup.IsValid = true;
                setup.Direction = TradeType.Buy;
                setup.Entry = Symbol.Ask;
                setup.Stop = ema50 - (20 * Symbol.PipSize); // Below EMA 50
                setup.Take = setup.Entry + ((setup.Entry - setup.Stop) * 5); // 5:1
            }
            else if (pullbackSell && volatilityOk)
            {
                setup.IsValid = true;
                setup.Direction = TradeType.Sell;
                setup.Entry = Symbol.Bid;
                setup.Stop = ema50 + (20 * Symbol.PipSize); // Above EMA 50
                setup.Take = setup.Entry - ((setup.Stop - setup.Entry) * 5); // 5:1
            }

            // Validate
            if (setup.IsValid)
            {
                double risk = Math.Abs(setup.Entry - setup.Stop);
                double reward = Math.Abs(setup.Take - setup.Entry);
                
                if (reward / risk < 4) // Minimum 4:1
                    setup.IsValid = false;
                
                if (risk > 100 * Symbol.PipSize) // Max 100 pip stop
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

            if (Positions.Count >= MaxPositions)
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

            var result = ExecuteMarketOrder(setup.Direction, SymbolName, volume, "TrendPro", setup.Stop, setup.Take);

            if (result.IsSuccessful)
            {
                _tradeCount++;

                var pos = result.Position;
                double grossProfit = Math.Abs(pos.TakeProfit - pos.EntryPrice) / Symbol.PipSize
                                    * Symbol.PipValue * pos.VolumeInUnits;
                double netProfit = grossProfit - Commission;

                Print("========================================");
                Print(string.Format("TREND TRADE #{0}", _tradeCount));
                Print(string.Format("Direction: {0}", setup.Direction));
                Print(string.Format("Entry: {0:F5}", pos.EntryPrice));
                Print(string.Format("Stop: {0:F5} ({1} pips)", pos.StopLoss,
                    Math.Abs(pos.EntryPrice - pos.StopLoss) / Symbol.PipSize));
                Print(string.Format("Target: {0:F5} ({1} pips)", pos.TakeProfit,
                    Math.Abs(pos.TakeProfit - pos.EntryPrice) / Symbol.PipSize));
                Print(string.Format("R/R: {0:F1}:1", 
                    Math.Abs(pos.TakeProfit - pos.EntryPrice) / Math.Abs(pos.EntryPrice - pos.StopLoss)));
                Print(string.Format("Gross if win: €{0:F2}", grossProfit));
                Print(string.Format("Net if win: €{0:F2}", netProfit));
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
            Print("TREND PRO STOPPED");
            Print(string.Format("Total trades: {0}", _tradeCount));
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
