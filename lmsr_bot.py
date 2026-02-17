#!/usr/bin/env python3
"""
Omega LMSR Arbitrage Bot
Uses Logarithmic Market Scoring Rule for proper Polymarket arbitrage
"""

import requests
import math
import time
import json
import sqlite3
from datetime import datetime
from typing import Dict, List, Tuple, Optional

DB_FILE = "/root/omega-experiments/lmsr_trading.db"
LOG_FILE = "/tmp/lmsr_bot.log"

# LMSR Parameter (b = liquidity parameter)
B = 100  # Can be calibrated based on market liquidity

class LMSRBot:
    def __init__(self, starting_balance=500.0):
        self.balance = starting_balance
        self.db_path = DB_FILE
        self._init_db()
        self.btc_history = []
        
    def _init_db(self):
        conn = sqlite3.connect(self.db_path)
        c = conn.cursor()
        c.execute('''CREATE TABLE IF NOT EXISTS trades (
            id INTEGER PRIMARY KEY,
            timestamp TEXT,
            market_id TEXT,
            market_name TEXT,
            side TEXT,  -- 'YES' or 'NO'
            amount REAL,
            lmsr_price REAL,
            posterior_prob REAL,
            expected_profit REAL,
            actual_profit REAL
        )''')
        c.execute('''CREATE TABLE IF NOT EXISTS btc_prices (
            timestamp TEXT,
            price REAL,
            change_5m REAL
        )''')
        conn.commit()
        conn.close()
    
    def log(self, msg):
        ts = datetime.now().strftime('%H:%M:%S')
        full = f"{ts} | {msg}"
        print(full)
        with open(LOG_FILE, "a") as f:
            f.write(full + "\n")
        return full
    
    def fetch_btc_price(self) -> Optional[float]:
        """Fetch Bitcoin price from multiple sources"""
        sources = [
            "https://api.coingecko.com/api/v3/simple/price?ids=bitcoin&vs_currencies=usd",
            "https://api.coinbase.com/v2/exchange-rates?currency=BTC"
        ]
        
        for url in sources:
            try:
                if 'coingecko' in url:
                    r = requests.get(url, timeout=5)
                    return r.json()['bitcoin']['usd']
                else:
                    r = requests.get(url, timeout=5)
                    return float(r.json()['data']['rates']['USD'])
            except:
                continue
        return None
    
    def calculate_posterior(self, btc_price: float) -> float:
        """
        Calculate Bayesian posterior probability based on BTC price movement
        Returns probability (0-1) that BTC will be up in next 5 minutes
        """
        now = time.time()
        
        # Store price
        self.btc_history.append({'time': now, 'price': btc_price})
        
        # Keep only last 20 minutes
        self.btc_history = [h for h in self.btc_history if now - h['time'] < 1200]
        
        # Calculate 5-minute momentum
        price_5m_ago = None
        for h in self.btc_history:
            if 240 <= now - h['time'] <= 360:
                price_5m_ago = h['price']
                break
        
        if not price_5m_ago:
            return 0.5  # No data yet, neutral
        
        change_pct = (btc_price - price_5m_ago) / price_5m_ago
        
        # Convert momentum to probability using logistic function
        # Positive momentum = higher probability of YES
        prob = 1 / (1 + math.exp(-change_pct * 100))  # Scale factor
        
        return max(0.05, min(0.95, prob))  # Clamp to avoid extremes
    
    def lmsr_price(self, q_yes: float, q_no: float, side: str) -> float:
        """
        Calculate LMSR price for YES or NO
        C(q) = b * log(exp(q_yes/b) + exp(q_no/b))
        Price = exp(q_side/b) / (exp(q_yes/b) + exp(q_no/b))
        """
        exp_yes = math.exp(q_yes / B)
        exp_no = math.exp(q_no / B)
        
        if side == 'YES':
            return exp_yes / (exp_yes + exp_no)
        else:
            return exp_no / (exp_yes + exp_no)
    
    def find_mispriced_markets(self, markets: List[Dict], posterior_prob: float) -> List[Dict]:
        """
        Find markets where Polymarket price differs from posterior by >8%
        """
        opportunities = []
        
        for market in markets:
            # Only look at BTC-related binary markets
            question = market.get('question', '').lower()
            if not any(kw in question for kw in ['bitcoin', 'btc', 'crypto', 'price']):
                continue
            
            outcomes = market.get('outcomes', [])
            if len(outcomes) != 2:
                continue
            
            try:
                # Get current market prices
                yes_price = float(outcomes[0].get('price', 0))
                no_price = float(outcomes[1].get('price', 0))
                
                if yes_price <= 0 or no_price <= 0:
                    continue
                
                # Calculate mispricing
                yes_diff = abs(yes_price - posterior_prob)
                no_diff = abs(no_price - (1 - posterior_prob))
                
                # If market is off by >8%
                if yes_diff > 0.08:
                    # Market underpricing YES, buy YES
                    expected_profit = (posterior_prob - yes_price) * 100
                    opportunities.append({
                        'market': market,
                        'side': 'YES',
                        'market_price': yes_price,
                        'true_prob': posterior_prob,
                        'edge': yes_diff,
                        'expected_profit': expected_profit
                    })
                elif no_diff > 0.08:
                    # Market underpricing NO, buy NO
                    expected_profit = ((1 - posterior_prob) - no_price) * 100
                    opportunities.append({
                        'market': market,
                        'side': 'NO',
                        'market_price': no_price,
                        'true_prob': 1 - posterior_prob,
                        'edge': no_diff,
                        'expected_profit': expected_profit
                    })
                    
            except Exception as e:
                continue
        
        return sorted(opportunities, key=lambda x: x['edge'], reverse=True)
    
    def kelly_size(self, edge: float, prob: float) -> float:
        """
        Fractional Kelly criterion for position sizing
        f* = (bp - q) / b
        where b = odds, p = prob of win, q = prob of loss
        """
        if edge <= 0:
            return 0
        
        # Simplified Kelly: bet edge * bankroll * fraction
        kelly_fraction = 0.25  # Conservative quarter-Kelly
        bet_size = self.balance * edge * kelly_fraction
        
        # Limit bet size
        return min(bet_size, self.balance * 0.1)  # Max 10% per trade
    
    def execute_trade(self, opp: Dict) -> bool:
        """Execute virtual trade"""
        position_size = self.kelly_size(opp['edge'], opp['true_prob'])
        
        if position_size < 5:  # Minimum $5
            return False
        
        cost = position_size * opp['market_price']
        expected_return = position_size * opp['true_prob']
        expected_profit = expected_return - cost
        
        self.balance -= cost
        
        conn = sqlite3.connect(self.db_path)
        c = conn.cursor()
        c.execute('''INSERT INTO trades VALUES (NULL, ?, ?, ?, ?, ?, ?, ?, ?, NULL)''',
                  (datetime.now().isoformat(),
                   opp['market'].get('conditionId', ''),
                   opp['market'].get('question', 'Unknown')[:80],
                   opp['side'],
                   position_size,
                   opp['market_price'],
                   opp['true_prob'],
                   expected_profit))
        conn.commit()
        conn.close()
        
        self.log(f"💰 TRADE: {opp['side']} ${position_size:.2f} @ {opp['market_price']:.3f}")
        self.log(f"   Market: {opp['market'].get('question', 'Unknown')[:50]}...")
        self.log(f"   Edge: {opp['edge']*100:.1f}% | Expected: ${expected_profit:.2f}")
        self.log(f"   Balance: ${self.balance:.2f}")
        
        return True
    
    def fetch_polymarket(self) -> List[Dict]:
        """Fetch active Polymarket markets"""
        try:
            url = "https://gamma-api.polymarket.com/markets"
            params = {
                "active": "true",
                "closed": "false",
                "limit": 100
            }
            r = requests.get(url, params=params, timeout=10)
            return r.json()
        except Exception as e:
            self.log(f"❌ Polymarket fetch error: {e}")
            return []
    
    def run_cycle(self):
        """Run one trading cycle"""
        # Fetch BTC price
        btc_price = self.fetch_btc_price()
        if not btc_price:
            self.log("❌ Could not fetch BTC price")
            return
        
        # Calculate posterior probability
        posterior = self.calculate_posterior(btc_price)
        
        # Fetch Polymarket markets
        markets = self.fetch_polymarket()
        if not markets:
            return
        
        # Find mispriced markets
        opportunities = self.find_mispriced_markets(markets, posterior)
        
        self.log(f"📊 BTC: ${btc_price:,.2f} | Posterior: {posterior:.3f} | Markets: {len(markets)} | Ops: {len(opportunities)}")
        
        # Execute best opportunities
        for opp in opportunities[:2]:
            self.execute_trade(opp)
    
    def run(self):
        self.log("🚀 LMSR Arbitrage Bot Started")
        self.log(f"💰 Balance: ${self.balance:.2f}")
        self.log("📐 Using Logarithmic Market Scoring Rule")
        self.log("⏱️  Scanning every 60 seconds...")
        
        while True:
            try:
                self.run_cycle()
            except Exception as e:
                self.log(f"❌ Cycle error: {e}")
            
            time.sleep(60)

if __name__ == "__main__":
    bot = LMSRBot(starting_balance=500.0)
    bot.run()
