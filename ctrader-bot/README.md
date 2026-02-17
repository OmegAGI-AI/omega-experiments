# cTrader Scalping Bot — OMEGA

## Overview
Real cTrader bot for precise scalping with advanced chart analysis.

## Features
- Real-time price feed from cTrader
- Advanced technical indicators (EMA, RSI, Volume, Order Flow)
- Micro-scalping (1-5 minute holds)
- Risk management with tight stops
- Auto-execution on cTrader platform

## Requirements
- cTrader account
- cTrader Automate (cBots)
- API access enabled

## Installation

1. Open cTrader
2. Go to Automate → New cBot
3. Copy `OmegaScalper.cs` code
4. Build and run

## Strategy

### Entry Signals (ALL must align):
1. **EMA Cross**: 9 EMA crosses 21 EMA
2. **RSI**: Between 40-60 (neutral zone breakout)
3. **Volume**: Above 20-period average
4. **Spread**: Less than 2 pips
5. **Time**: Avoid high-impact news times

### Exit Signals:
- **Take Profit**: 5-10 pips
- **Stop Loss**: 3-5 pips
- **Time Stop**: Close after 5 minutes if not hit

### Risk Management:
- Max 1% risk per trade
- Max 3 open positions
- Daily loss limit: 3%

## Code

See `OmegaScalper.cs`
