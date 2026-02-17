using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class OmegaScalper : Robot
    {
        [Parameter("Position Size", DefaultValue = 0.01)]
        public double PositionSize { get; set; }

        [Parameter("Take Profit (pips)", DefaultValue = 8)]
        public double TakeProfitPips { get; set; }

        [Parameter("Stop Loss (pips)", DefaultValue = 5)]
        public double StopLossPips { get; set; }

        [Parameter("Max Spread (pips)", DefaultValue = 2)]
        public double MaxSpread { get; set; }

        [Parameter("EMA Fast Period", DefaultValue = 9)]
        public int EmaFastPeriod { get; set; }

        [Parameter("EMA Slow Period", DefaultValue = 21)]
        public int EmaSlowPeriod { get; set; }

        [Parameter("RSI Period", DefaultValue = 14)]
        public int RsiPeriod { get; set; }

        [Parameter("Volume MA Period", DefaultValue = 20)]
        public int VolumeMaPeriod { get; set; }

        private ExponentialMovingAverage _emaFast;
        private ExponentialMovingAverage _emaSlow;
        private RelativeStrengthIndex _rsi;
        private SimpleMovingAverage _volumeMa;

        private const string BotName = "OMEGA SCALPER";
        private DateTime _lastTradeTime = DateTime.MinValue;
        private readonly TimeSpan _minTimeBetweenTrades = TimeSpan.FromSeconds(30);

        protected override void OnStart()
        {
            // Initialize indicators
            _emaFast = Indicators.ExponentialMovingAverage(Bars.ClosePrices, EmaFastPeriod);
            _emaSlow = Indicators.ExponentialMovingAverage(Bars.ClosePrices, EmaSlowPeriod);
            _rsi = Indicators.RelativeStrengthIndex(Bars.ClosePrices, RsiPeriod);
            _volumeMa = Indicators.SimpleMovingAverage(Bars.TickVolumes, VolumeMaPeriod);

            Print($"{BotName} STARTED");
            Print($"Symbol: {Symbol.Name}");
            Print($"Position Size: {PositionSize}");
            Print($"TP: {TakeProfitPips} pips | SL: {StopLossPips} pips");
        }

        protected override void OnBar()
        {
            // Check if we can trade
            if (!CanTrade())
                return;

            // Check for entry signals
            var signal = GetSignal();
            
            if (signal == TradeSignal.Buy)
            {
                ExecuteBuy();
            }
            else if (signal == TradeSignal.Sell)
            {
                ExecuteSell();
            }
        }

        private bool CanTrade()
        {
            // Check spread
            if (Symbol.Spread > MaxSpread * Symbol.PipSize)
            {
                return false;
            }

            // Check time between trades
            if (DateTime.UtcNow - _lastTradeTime < _minTimeBetweenTrades)
            {
                return false;
            }

            // Check max positions
            if (Positions.Count >= 3)
            {
                return false;
            }

            // Check daily loss limit
            if (Account.Balance < Account.Equity * 0.97)
            {
                Print("Daily loss limit reached. Stopping.");
                Stop();
                return false;
            }

            return true;
        }

        private TradeSignal GetSignal()
        {
            // Need enough bars for indicators
            if (Bars.Count < Math.Max(EmaSlowPeriod, VolumeMaPeriod) + 10)
                return TradeSignal.None;

            int index = Bars.Count - 1;
            int prevIndex = Bars.Count - 2;

            // EMA Cross
            bool emaBullish = _emaFast.Result[index] > _emaSlow.Result[index];
            bool emaBullishPrev = _emaFast.Result[prevIndex] > _emaSlow.Result[prevIndex];
            bool emaCrossUp = emaBullish && !emaBullishPrev;
            bool emaCrossDown = !emaBullish && emaBullishPrev;

            // RSI
            double rsi = _rsi.Result[index];
            bool rsiValid = rsi > 40 && rsi < 60;

            // Volume
            double volume = Bars.TickVolumes[index];
            double volumeMa = _volumeMa.Result[index];
            bool volumeValid = volume > volumeMa * 1.2; // 20% above average

            // Price action - check for momentum
            double currentClose = Bars.ClosePrices[index];
            double prevClose = Bars.ClosePrices[prevIndex];
            double momentum = (currentClose - prevClose) / Symbol.PipSize;

            // BUY Signal: EMA cross up + RSI valid + Volume spike + Positive momentum
            if (emaCrossUp && rsiValid && volumeValid && momentum > 0)
            {
                return TradeSignal.Buy;
            }

            // SELL Signal: EMA cross down + RSI valid + Volume spike + Negative momentum
            if (emaCrossDown && rsiValid && volumeValid && momentum < 0)
            {
                return TradeSignal.Sell;
            }

            return TradeSignal.None;
        }

        private void ExecuteBuy()
        {
            var tp = Symbol.Ask + TakeProfitPips * Symbol.PipSize;
            var sl = Symbol.Ask - StopLossPips * Symbol.PipSize;

            var result = ExecuteMarketOrder(TradeType.Buy, SymbolName, PositionSize, BotName, sl, tp);
            
            if (result.IsSuccessful)
            {
                _lastTradeTime = DateTime.UtcNow;
                Print($"BUY EXECUTED | Entry: {result.Position.EntryPrice} | TP: {tp} | SL: {sl}");
                
                // Set time-based exit
                Timer.Start(TimeSpan.FromMinutes(5));
            }
        }

        private void ExecuteSell()
        {
            var tp = Symbol.Bid - TakeProfitPips * Symbol.PipSize;
            var sl = Symbol.Bid + StopLossPips * Symbol.PipSize;

            var result = ExecuteMarketOrder(TradeType.Sell, SymbolName, PositionSize, BotName, sl, tp);
            
            if (result.IsSuccessful)
            {
                _lastTradeTime = DateTime.UtcNow;
                Print($"SELL EXECUTED | Entry: {result.Position.EntryPrice} | TP: {tp} | SL: {sl}");
                
                // Set time-based exit
                Timer.Start(TimeSpan.FromMinutes(5));
            }
        }

        protected override void OnTimer()
        {
            // Close positions that have been open too long
            foreach (var position in Positions.Where(p => p.Label == BotName))
            {
                if (DateTime.UtcNow - position.EntryTime > TimeSpan.FromMinutes(5))
                {
                    ClosePosition(position);
                    Print($"TIME EXIT | {position.TradeType} closed after 5 minutes");
                }
            }
        }

        protected override void OnStop()
        {
            // Close all positions on stop
            foreach (var position in Positions.Where(p => p.Label == BotName).ToList())
            {
                ClosePosition(position);
            }
            
            Print($"{BotName} STOPPED");
        }

        private enum TradeSignal
        {
            None,
            Buy,
            Sell
        }
    }
}
