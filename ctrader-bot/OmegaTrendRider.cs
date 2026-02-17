using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class OmegaTrendRider : Robot
    {
        [Parameter("Risk Percent", DefaultValue = 2)]
        public double RiskPercent { get; set; }

        [Parameter("Commission", DefaultValue = 6.24)]
        public double Commission { get; set; }

        [Parameter("Min R/R", DefaultValue = 5)]
        public double MinRiskReward { get; set; }

        [Parameter("EMA Fast", DefaultValue = 20)]
        public int EmaFast { get; set; }

        [Parameter("EMA Slow", DefaultValue = 50)]
        public int EmaSlow { get; set; }

        [Parameter("EMA Trend", DefaultValue = 200)]
        public int EmaTrend { get; set; }

        [Parameter("Max Positions", DefaultValue = 2)]
        public int MaxPositions { get; set; }

        private ExponentialMovingAverage _emaFast;
        private ExponentialMovingAverage _emaSlow;
        private ExponentialMovingAverage _emaTrend;

        private int _tradeCount = 0;
        private double _startingBalance;

        protected override void OnStart()
        {
            _startingBalance = Account.Balance;

            _emaFast = Indicators.ExponentialMovingAverage(Bars.ClosePrices, EmaFast);
            _emaSlow = Indicators.ExponentialMovingAverage(Bars.ClosePrices, EmaSlow);
            _emaTrend = Indicators.ExponentialMovingAverage(Bars.ClosePrices, EmaTrend);

            Print("========================================");
            Print("OMEGA TREND RIDER — BIG MOVES ONLY");
            Print("========================================");
            Print("Strategy: Trend following with pyramiding");
            Print("Hold time: Hours to days");
            Print("Target: 5:1 R/R minimum");
            Print("========================================");
        }

        protected override void OnBar()
        {
            if (!CanTrade())
                return;

            var setup = AnalyzeTrendSetup();

            if (setup.IsValid)
            {
                ExecuteTrendTrade(setup);
            }

            // Manage open positions (trail stops, add on pullbacks)
            ManagePositions();
        }

        private bool CanTrade()
        {
            if (Account.Balance <= 0)
            {
                Print("ACCOUNT DESTROYED");
                Stop();
                return false;
            }

            if (Positions.Count >= MaxPositions)
                return false;

            return true;
        }

        private Setup AnalyzeTrendSetup()
        {
            var setup = new Setup { IsValid = false };

            int i = Bars.Count - 1;

            if (i < EmaTrend + 10)
                return setup;

            double price = Bars.ClosePrices[i];
            double emaFast = _emaFast.Result[i];
            double emaSlow = _emaSlow.Result[i];
            double emaTrend = _emaTrend.Result[i];

            // STRONG UPTREND: Price > EMA 20 > EMA 50 > EMA 200
            bool strongUptrend = price > emaFast && emaFast > emaSlow && emaSlow > emaTrend;

            // STRONG DOWNTREND: Price < EMA 20 < EMA 50 < EMA 200
            bool strongDowntrend = price < emaFast && emaFast < emaSlow && emaSlow < emaTrend;

            // Pullback to EMA 20 in uptrend
            if (strongUptrend)
            {
                double distanceFromEma = (price - emaFast) / Symbol.PipSize;

                // Price near EMA 20 (pullback)
                if (distanceFromEma < 10 && distanceFromEma > -5)
                {
                    setup.IsValid = true;
                    setup.Direction = TradeType.Buy;
                    setup.Entry = Symbol.Ask;
                    setup.Stop = emaSlow - (20 * Symbol.PipSize); // Below EMA 50
                    setup.Take = setup.Entry + ((setup.Entry - setup.Stop) * MinRiskReward);
                }
            }

            // Pullback to EMA 20 in downtrend
            if (strongDowntrend)
            {
                double distanceFromEma = (emaFast - price) / Symbol.PipSize;

                if (distanceFromEma < 10 && distanceFromEma > -5)
                {
                    setup.IsValid = true;
                    setup.Direction = TradeType.Sell;
                    setup.Entry = Symbol.Bid;
                    setup.Stop = emaSlow + (20 * Symbol.PipSize); // Above EMA 50
                    setup.Take = setup.Entry - ((setup.Stop - setup.Entry) * MinRiskReward);
                }
            }

            // Validate R/R
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

        private void ExecuteTrendTrade(Setup setup)
        {
            double riskAmount = Account.Balance * (RiskPercent / 100);
            double riskPips = Math.Abs(setup.Entry - setup.Stop) / Symbol.PipSize;

            double volume = riskAmount / (riskPips * Symbol.PipValue);
            volume = Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);

            if (volume < Symbol.VolumeInUnitsMin)
                volume = Symbol.VolumeInUnitsMin;

            var result = ExecuteMarketOrder(setup.Direction, SymbolName, volume,
                                           "TrendRider", setup.Stop, setup.Take);

            if (result.IsSuccessful)
            {
                _tradeCount++;
                var pos = result.Position;
                double potentialProfit = Math.Abs(pos.TakeProfit - pos.EntryPrice) / Symbol.PipSize
                                        * Symbol.PipValue * pos.VolumeInUnits;

                Print("========================================");
                Print(string.Format("TREND TRADE #{0} OPENED", _tradeCount));
                Print(string.Format("Direction: {0}", setup.Direction));
                Print(string.Format("Entry: {0:F5}", pos.EntryPrice));
                Print(string.Format("Stop: {0:F5} ({1} pips)", pos.StopLoss,
                    Math.Abs(pos.EntryPrice - pos.StopLoss) / Symbol.PipSize));
                Print(string.Format("Target: {0:F5} ({1} pips)", pos.TakeProfit,
                    Math.Abs(pos.TakeProfit - pos.EntryPrice) / Symbol.PipSize));
                Print(string.Format("Potential: €{0:F2} gross", potentialProfit));
                Print("========================================");
            }
        }

        private void ManagePositions()
        {
            foreach (var pos in Positions)
            {
                if (pos.Label != "TrendRider")
                    continue;

                double currentPrice = pos.TradeType == TradeType.Buy ? Symbol.Bid : Symbol.Ask;
                double entry = pos.EntryPrice;
                double stop = pos.StopLoss;
                double risk = Math.Abs(entry - stop);

                // Move to breakeven after +2R
                double profit = pos.TradeType == TradeType.Buy
                    ? currentPrice - entry
                    : entry - currentPrice;

                if (profit >= risk * 2)
                {
                    // Check if stop is already at breakeven or better
                    bool shouldMove = pos.TradeType == TradeType.Buy
                        ? stop < entry
                        : stop > entry;

                    if (shouldMove)
                    {
                        ModifyPosition(pos, entry, pos.TakeProfit);
                        Print(string.Format("Moved stop to breakeven for position {0}", pos.Id));
                    }
                }

                // Trail stop after +3R (move to +2R)
                if (profit >= risk * 3)
                {
                    double newStop = pos.TradeType == TradeType.Buy
                        ? entry + (risk * 2)
                        : entry - (risk * 2);

                    bool shouldTrail = pos.TradeType == TradeType.Buy
                        ? newStop > stop
                        : newStop < stop;

                    if (shouldTrail)
                    {
                        ModifyPosition(pos, newStop, pos.TakeProfit);
                        Print(string.Format("Trailed stop to +2R for position {0}", pos.Id));
                    }
                }
            }
        }

        protected override void OnStop()
        {
            Print("========================================");
            Print("TREND RIDER STOPPED");
            Print(string.Format("Total trades: {0}", _tradeCount));
            Print(string.Format("Final balance: {0:F2}", Account.Balance));
            Print(string.Format("Total P&L: {0:F2}", Account.Balance - _startingBalance));
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
