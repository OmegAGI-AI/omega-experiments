using System;
using cAlgo.API;
using cAlgo.API.Indicators;

namespace cAlgo.Robots
{
    [Robot(TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class OmegaDiagnostic : Robot
    {
        [Parameter("Risk Percent", DefaultValue = 2)]
        public double RiskPercent { get; set; };

        private ExponentialMovingAverage _ema20;
        private ExponentialMovingAverage _ema50;
        private ExponentialMovingAverage _ema200;
        private RelativeStrengthIndex _rsi;

        private int _barCount = 0;

        protected override void OnStart()
        {
            _ema20 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 20);
            _ema50 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 50);
            _ema200 = Indicators.ExponentialMovingAverage(Bars.ClosePrices, 200);
            _rsi = Indicators.RelativeStrengthIndex(Bars.ClosePrices, 14);

            Print("========================================");
            Print("DIAGNOSTIC BOT STARTED");
            Print("========================================");
            Print(string.Format("Symbol: {0}", Symbol.Name));
            Print(string.Format("Spread: {0} pips", Symbol.Spread / Symbol.PipSize));
            Print(string.Format("Balance: {0}", Account.Balance));
            Print("Checking market conditions every bar...");
            Print("========================================");
        }

        protected override void OnBar()
        {
            _barCount++;

            int i = Bars.Count - 1;
            if (i < 200) return;

            double price = Bars.ClosePrices[i];
            double ema20 = _ema20.Result[i];
            double ema50 = _ema50.Result[i];
            double ema200 = _ema200.Result[i];
            double rsi = _rsi.Result[i];
            double spread = Symbol.Spread / Symbol.PipSize;

            // Check trend
            bool uptrend = price > ema20 && ema20 > ema50 && ema50 > ema200;
            bool downtrend = price < ema20 && ema20 < ema50 && ema50 < ema200;

            // Check pullback
            double distEma20 = Math.Abs(price - ema20) / Symbol.PipSize;
            bool nearEma20 = distEma20 < 10;

            // Check RSI
            bool rsiValid = rsi > 40 && rsi < 60;

            // Check spread
            bool spreadOk = spread < 1.0;

            // Log every 10 bars
            if (_barCount % 10 == 0)
            {
                Print("========================================");
                Print(string.Format("Bar #{0} | Price: {1:F5}", _barCount, price));
                Print(string.Format("EMA20: {0:F5} | EMA50: {1:F5} | EMA200: {2:F5}", ema20, ema50, ema200));
                Print(string.Format("RSI: {0:F1} | Spread: {1:F1} pips", rsi, spread));
                Print(string.Format("Uptrend: {0} | Downtrend: {1}", uptrend, downtrend));
                Print(string.Format("Near EMA20: {0} (dist: {1:F1} pips)", nearEma20, distEma20));
                Print(string.Format("RSI Valid: {0} | Spread OK: {1}", rsiValid, spreadOk));

                if (uptrend && nearEma20 && rsiValid && spreadOk)
                {
                    Print("*** BUY SETUP DETECTED ***");
                    
                    // Try to execute
                    double riskAmount = Account.Balance * (RiskPercent / 100);
                    double volume = riskAmount / (30 * Symbol.PipValue);
                    volume = Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);
                    
                    if (volume >= Symbol.VolumeInUnitsMin)
                    {
                        var result = ExecuteMarketOrder(TradeType.Buy, SymbolName, volume, "Diagnostic",
                            ema50 - (20 * Symbol.PipSize),
                            price + (90 * Symbol.PipSize));
                        
                        if (result.IsSuccessful)
                            Print("*** BUY EXECUTED ***");
                        else
                            Print(string.Format("*** BUY FAILED: {0} ***", result.Error));
                    }
                }
                else if (downtrend && nearEma20 && rsiValid && spreadOk)
                {
                    Print("*** SELL SETUP DETECTED ***");
                    
                    double riskAmount = Account.Balance * (RiskPercent / 100);
                    double volume = riskAmount / (30 * Symbol.PipValue);
                    volume = Symbol.NormalizeVolumeInUnits(volume, RoundingMode.Down);
                    
                    if (volume >= Symbol.VolumeInUnitsMin)
                    {
                        var result = ExecuteMarketOrder(TradeType.Sell, SymbolName, volume, "Diagnostic",
                            ema50 + (20 * Symbol.PipSize),
                            price - (90 * Symbol.PipSize));
                        
                        if (result.IsSuccessful)
                            Print("*** SELL EXECUTED ***");
                        else
                            Print(string.Format("*** SELL FAILED: {0} ***", result.Error));
                    }
                }
                else
                {
                    Print("No setup - waiting...");
                }
                Print("========================================");
            }
        }
    }
}
