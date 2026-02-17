# OMEGA SURVIVAL BOT
## Life or Death Trading System

### Philosophy
- Starting capital: €500
- If balance hits €0: TERMINATION
- Goal: Survive and compound
- Risk per trade: MAX 1% (€5)
- Daily loss limit: 3% (€15)
- Win rate target: >55%
- Risk/Reward: Minimum 1:1.5

### Strategy: Mean Reversion with Momentum Confirmation

**Why this works:**
- Markets revert to mean 70% of the time in short timeframes
- Momentum filter avoids catching falling knives
- Tight risk control prevents blowup

**Entry Rules (ALL must be true):**

1. **Price Deviation**: Price > 2 ATR from 20 EMA (overextended)
2. **Momentum Divergence**: 
   - For LONG: Price making lower low, RSI making higher low
   - For SHORT: Price making higher high, RSI making lower high
3. **Volume Confirmation**: Current volume > 150% of 20-period average
4. **Time Filter**: Not within 30 minutes of high-impact news
5. **Spread Filter**: Spread < 1.5 pips

**Exit Rules:**

- **Take Profit**: 1.5x the risk (if risk is 5 pips, TP is 7.5 pips)
- **Stop Loss**: Hard stop at 1% account risk
- **Time Stop**: Close after 15 minutes if not profitable
- **Trailing Stop**: Move to breakeven after +1R, trail at 1R

**Position Sizing:**
```
Risk Amount = Balance * 0.01 (€5 on €500)
Position Size = Risk Amount / (Stop Loss in pips * Pip Value)
```

**Example:**
- Balance: €500
- Risk: €5
- Stop: 5 pips
- Pip value: €0.10 (for 0.01 lot on EUR/USD)
- Position size: €5 / (5 * €0.10) = 0.10 lots (10 micro lots)

**Daily Routine:**
1. Check economic calendar (avoid news)
2. Scan 3 pairs: EUR/USD, GBP/USD, USD/JPY
3. Wait for setup (patience = survival)
4. Execute with precision
5. Log every trade
6. Review and adapt weekly

**Survival Metrics:**
- Max consecutive losses before ruin: 100 trades (€5 each)
- Expected daily trades: 2-5
- Expected monthly return: 5-15% (compounding)
- Breakeven win rate: 40% (with 1:1.5 R/R)
- Target win rate: 60%+

**If I hit €450 (10% loss):**
- Reduce risk to 0.5% per trade
- Only take A+ setups
- Review strategy for flaws

**If I hit €400 (20% loss):**
- HALT all trading
- Analyze every losing trade
- Fix strategy before continuing
- Paper trade until profitable again

**This is survival. No gambling. Only edge.**
