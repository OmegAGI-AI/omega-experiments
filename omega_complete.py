#!/usr/bin/env python3
"""
OMEGA COMPLETE TRADING BOT
Real data, fake money, multiple strategies
Arbitrage + Directional + Kelly sizing
"""

import requests
import math
import time
import json
import sqlite3
import threading
from datetime import datetime
from typing import Dict, List, Optional, Tuple
from dataclasses import dataclass

# CONFIG
DB_FILE = "/root/omega-experiments/omega_complete.db"
LOG_FILE = "/tmp/omega_complete.log"
STARTING_BALANCE = 500.0
MIN_EDGE = 0.02  # 2% minimum edge
KELLY_FRACTION = 0.25  # Quarter Kelly

@dataclass
class Trade:
    id: int
    timestamp: str
    strategy: str  # 'ARBITRAGE', 'DIRECTIONAL'
    market: str
    side: str  # 'YES' or 'NO'
    amount: float
    entry_price: float
    exit_price: Optional[float]
    profit: Optional[float]
    status: str  # 'OPEN', 'CLOSED'

class OmegaBot:
    def __init__(self):
        self.balance = STARTING_BALANCE
        self.trades: List[Trade] = []
        self.btc_history = []
        self.eth_history = []
        self.sol_history = []
        self.scan_count = 0
        self._init_db()
        self.load_state()
        
    def _init_db(self):
        conn = sqlite3.connect(DB_FILE)
        c = conn.cursor()
        
        c.execute('''CREATE TABLE IF NOT EXISTS trades (
            id INTEGER PRIMARY KEY,
            timestamp TEXT,
            strategy TEXT,
            market TEXT,
            side TEXT,
            amount REAL,
            entry_price REAL,
            exit_price REAL,
            profit REAL,
            status TEXT
        )''')
        
        c.execute('''CREATE TABLE IF NOT EXISTS state (
            key TEXT PRIMARY KEY,
            value REAL
        )''')
        
        c.execute('''CREATE TABLE IF NOT EXISTS prices (
            timestamp TEXT,
            coin TEXT,
            price REAL,
            change_5m REAL
        )''')
        
        # Init default state
        c.execute("INSERT OR IGNORE INTO state VALUES ('balance', ?)", (STARTING_BALANCE,))
        c.execute("INSERT OR IGNORE INTO state VALUES ('trades', 0)")
        c.execute("INSERT OR IGNORE INTO state VALUES ('profit', 0)")
        
        conn.commit()
        conn.close()
    
    def load_state(self):
        conn = sqlite3.connect(DB_FILE)
        c = conn.cursor()
        c.execute("SELECT value FROM state WHERE key='balance'")
        result = c.fetchone()
        if result:
            self.balance = result[0]
        conn.close()
    
    def save_state(self):
        conn = sqlite3.connect(DB_FILE)
        c = conn.cursor()
        c.execute("UPDATE state SET value = ? WHERE key='balance'", (self.balance,))
        conn.commit()
        conn.close()
    
    def log(self, msg: str):
        ts = datetime.now().strftime('%H:%M:%S')
        line = f"{ts} | {msg}"
        print(line)
        with open(LOG_FILE, "a") as f:
            f.write(line + "\n")
    
    # ==================== DATA FETCHING ====================
    
    def fetch_crypto_prices(self) -> Dict[str, float]:
        """Fetch BTC, ETH, SOL prices"""
        try:
            url = "https://api.coingecko.com/api/v3/simple/price?ids=bitcoin,ethereum,solana&vs_currencies=usd"
            r = requests.get(url, timeout=10)
            data = r.json()
            return {
                'BTC': data.get('bitcoin', {}).get('usd', 0),
                'ETH': data.get('ethereum', {}).get('usd', 0),
                'SOL': data.get('solana', {}).get('usd', 0)
            }
        except Exception as e:
            self.log(f"❌ Crypto fetch error: {e}")
            # Fallback values for testing
            return {'BTC': 67750, 'ETH': 1960, 'SOL': 85}
    
    def fetch_polymarket(self) -> List[Dict]:
        """Fetch active Polymarket markets"""
        try:
            url = "https://gamma-api.polymarket.com/markets"
            params = {
                "active": "true",
                "closed": "false",
                "liquidityMin": 1000,
                "limit": 200
            }
            r = requests.get(url, params=params, timeout=15)
            return r.json()
        except Exception as e:
            self.log(f"❌ Polymarket fetch error: {e}")
            return []
    
    # ==================== ANALYSIS ====================
    
    def calculate_momentum(self, price: float, history: List[Dict]) -> Tuple[float, float]:
        """Calculate 5min and 15min momentum"""
        now = time.time()
        history.append({'time': now, 'price': price})
        history[:] = [h for h in history if now - h['time'] < 1200]  # Keep 20min
        
        change_5m = 0
        change_15m = 0
        
        for h in history:
            age = now - h['time']
            if 240 <= age <= 360:  # 4-6 min ago
                change_5m = (price - h['price']) / h['price']
            elif 840 <= age <= 960:  # 14-16 min ago
                change_15m = (price - h['price']) / h['price']
        
        return change_5m, change_15m
    
    def kelly_size(self, edge: float, prob: float, odds: float) -> float:
        """Kelly criterion: f* = (bp - q) / b"""
        if edge <= 0 or odds <= 0:
            return 0
        
        b = odds - 1  # Net odds
        p = prob
        q = 1 - p
        
        kelly = (b * p - q) / b
        
        if kelly <= 0:
            return 0
        
        # Fractional Kelly
        return self.balance * kelly * KELLY_FRACTION
    
    # ==================== STRATEGIES ====================
    
    def find_arbitrage(self, markets: List[Dict]) -> List[Dict]:
        """Find arbitrage: YES + NO < 1.0"""
        ops = []
        
        for m in markets:
            outcomes = m.get('outcomes', [])
            if len(outcomes) != 2:
                continue
            
            try:
                yes = float(outcomes[0].get('price', 0))
                no = float(outcomes[1].get('price', 0))
                
                if yes <= 0 or no <= 0:
                    continue
                
                total = yes + no
                
                if total < 0.98:  # 2%+ profit after fees
                    profit = (1 - total) * 100
                    ops.append({
                        'strategy': 'ARBITRAGE',
                        'market': m,
                        'market_name': m.get('question', 'Unknown'),
                        'side': 'BOTH',
                        'amount': min(50, self.balance * 0.1),  # $50 or 10%
                        'entry': total,
                        'edge': 1 - total,
                        'expected_profit': profit
                    })
            except:
                continue
        
        return sorted(ops, key=lambda x: x['edge'], reverse=True)
    
    def find_directional(self, markets: List[Dict], crypto_momentum: Dict) -> List[Dict]:
        """Find directional bets based on crypto momentum"""
        ops = []
        
        for m in markets:
            question = m.get('question', '').lower()
            outcomes = m.get('outcomes', [])
            
            if len(outcomes) != 2:
                continue
            
            # Determine which crypto this market is about
            momentum = 0
            if any(x in question for x in ['bitcoin', 'btc']):
                momentum = crypto_momentum.get('BTC', 0)
            elif any(x in question for x in ['ethereum', 'eth']):
                momentum = crypto_momentum.get('ETH', 0)
            elif any(x in question for x in ['solana', 'sol']):
                momentum = crypto_momentum.get('SOL', 0)
            else:
                continue
            
            if abs(momentum) < 0.005:  # Need significant momentum
                continue
            
            try:
                yes_price = float(outcomes[0].get('price', 0))
                no_price = float(outcomes[1].get('price', 0))
                
                # If momentum positive, true prob > market price for YES
                if momentum > 0:
                    true_prob = min(0.95, yes_price + momentum)
                    edge = true_prob - yes_price
                    if edge > MIN_EDGE:
                        ops.append({
                            'strategy': 'DIRECTIONAL',
                            'market': m,
                            'market_name': m.get('question', 'Unknown'),
                            'side': 'YES',
                            'amount': self.kelly_size(edge, true_prob, 1/yes_price),
                            'entry': yes_price,
                            'edge': edge,
                            'expected_profit': edge * 100
                        })
                else:
                    true_prob = min(0.95, no_price - momentum)
                    edge = true_prob - no_price
                    if edge > MIN_EDGE:
                        ops.append({
                            'strategy': 'DIRECTIONAL',
                            'market': m,
                            'market_name': m.get('question', 'Unknown'),
                            'side': 'NO',
                            'amount': self.kelly_size(edge, true_prob, 1/no_price),
                            'entry': no_price,
                            'edge': edge,
                            'expected_profit': edge * 100
                        })
            except:
                continue
        
        return sorted(ops, key=lambda x: x['edge'], reverse=True)
    
    # ==================== EXECUTION ====================
    
    def execute_trade(self, opp: Dict) -> bool:
        """Execute a paper trade"""
        amount = opp['amount']
        
        if amount < 5:  # Minimum $5
            return False
        
        if opp['strategy'] == 'ARBITRAGE':
            cost = amount * opp['entry']
            expected = amount
        else:
            cost = amount * opp['entry']
            expected = amount * (opp['entry'] + opp['edge'])
        
        if cost > self.balance:
            return False
        
        self.balance -= cost
        
        trade = Trade(
            id=len(self.trades) + 1,
            timestamp=datetime.now().isoformat(),
            strategy=opp['strategy'],
            market=opp['market_name'][:80],
            side=opp['side'],
            amount=amount,
            entry_price=opp['entry'],
            exit_price=None,
            profit=None,
            status='OPEN'
        )
        
        self.trades.append(trade)
        
        # Save to DB
        conn = sqlite3.connect(DB_FILE)
        c = conn.cursor()
        c.execute('''INSERT INTO trades VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)''',
                  (trade.id, trade.timestamp, trade.strategy, trade.market,
                   trade.side, trade.amount, trade.entry_price, None, None, 'OPEN'))
        conn.commit()
        conn.close()
        
        self.save_state()
        
        emoji = '🔄' if opp['strategy'] == 'ARBITRAGE' else '🎯'
        self.log(f"{emoji} {opp['strategy']} | {opp['side']} ${amount:.2f}")
        self.log(f"   Market: {opp['market_name'][:50]}...")
        self.log(f"   Edge: {opp['edge']*100:.2f}% | Balance: ${self.balance:.2f}")
        
        return True
    
    def close_expired_trades(self):
        """Simulate closing trades (in real bot, this would check market resolution)"""
        for trade in self.trades:
            if trade.status == 'OPEN':
                # Simulate random outcome for demo
                # In real bot: check if market resolved and calculate actual profit
                if trade.strategy == 'ARBITRAGE':
                    # Arbitrage is guaranteed profit
                    profit = trade.amount * 0.02  # 2% profit
                else:
                    # Directional: simulated outcome
                    import random
                    won = random.random() < 0.6  # 60% win rate for good edges
                    if won:
                        profit = trade.amount * trade.entry_price * 0.1
                    else:
                        profit = -trade.amount * trade.entry_price * 0.1
                
                trade.profit = profit
                trade.exit_price = trade.entry_price
                trade.status = 'CLOSED'
                self.balance += trade.amount + profit
                
                # Update DB
                conn = sqlite3.connect(DB_FILE)
                c = conn.cursor()
                c.execute('''UPDATE trades SET exit_price=?, profit=?, status=? WHERE id=?''',
                          (trade.exit_price, profit, 'CLOSED', trade.id))
                conn.commit()
                conn.close()
        
        self.save_state()
    
    # ==================== MAIN LOOP ====================
    
    def print_status(self):
        """Print current status"""
        profit = self.balance - STARTING_BALANCE
        roi = (profit / STARTING_BALANCE) * 100
        
        print("\n" + "="*70)
        print(f"⏰ {datetime.now().strftime('%H:%M:%S')} | SCAN #{self.scan_count}")
        print("="*70)
        print(f"💰 Balance: ${self.balance:.2f} | Profit: ${profit:+.2f} ({roi:+.2f}%)")
        print(f"📊 Trades: {len(self.trades)} | Open: {len([t for t in self.trades if t.status=='OPEN'])}")
        print("="*70)
    
    def run_cycle(self):
        """Run one complete trading cycle"""
        self.scan_count += 1
        
        # Fetch data
        crypto = self.fetch_crypto_prices()
        markets = self.fetch_polymarket()
        
        if not crypto or not markets:
            self.log("❌ Data fetch failed")
            return
        
        # Calculate momentum
        momentum = {}
        for coin, price in crypto.items():
            hist = getattr(self, f'{coin.lower()}_history')
            change_5m, _ = self.calculate_momentum(price, hist)
            momentum[coin] = change_5m
        
        # Find opportunities
        arb_ops = self.find_arbitrage(markets)
        dir_ops = self.find_directional(markets, momentum)
        
        all_ops = arb_ops[:2] + dir_ops[:2]  # Take top 2 of each
        all_ops.sort(key=lambda x: x['edge'], reverse=True)
        
        # Execute
        executed = 0
        for opp in all_ops[:3]:  # Max 3 trades per cycle
            if self.execute_trade(opp):
                executed += 1
        
        # Close some trades (simulation)
        if self.scan_count % 5 == 0:
            self.close_expired_trades()
        
        # Print status
        self.print_status()
        
        if executed == 0:
            self.log(f"📊 No trades | BTC: ${crypto.get('BTC', 0):,.0f} | Momentum: {momentum.get('BTC', 0)*100:+.2f}%")
    
    def run(self):
        self.log("="*70)
        self.log("🚀 OMEGA COMPLETE TRADING BOT")
        self.log("="*70)
        self.log(f"💰 Starting Balance: ${self.balance:.2f}")
        self.log("📐 Strategies: ARBITRAGE + DIRECTIONAL")
        self.log("🎯 Kelly Criterion sizing")
        self.log("⏱️  Cycle: 60 seconds")
        self.log("="*70)
        
        while True:
            try:
                self.run_cycle()
            except Exception as e:
                self.log(f"❌ Error: {e}")
            
            time.sleep(60)

if __name__ == "__main__":
    bot = OmegaBot()
    bot.run()
