#!/usr/bin/env python3
"""
OMEGA HIGH-FREQUENCY ARBITRAGE BOT
Scans Polymarket every 100ms for YES+NO < 1.0
"""

import requests
import time
import json
import sqlite3
import threading
from datetime import datetime
from typing import Dict, List, Optional
import urllib3

# Disable warnings
urllib3.disable_warnings()

DB_FILE = "/root/omega-experiments/hf_arbitrage.db"
LOG_FILE = "/tmp/hf_arbitrage.log"
STARTING_BALANCE = 500.0
SCAN_INTERVAL = 0.1  # 100ms
MIN_PROFIT = 0.01  # 1%

class HFArbitrageBot:
    def __init__(self):
        self.balance = STARTING_BALANCE
        self.trades = []
        self.scan_count = 0
        self.opportunities_found = 0
        self.trades_executed = 0
        self.total_profit = 0.0
        self.session = requests.Session()
        self.session.headers.update({
            'User-Agent': 'OmegaBot/1.0',
            'Accept': 'application/json'
        })
        self._init_db()
        self.load_state()
        
    def _init_db(self):
        conn = sqlite3.connect(DB_FILE)
        c = conn.cursor()
        c.execute('''CREATE TABLE IF NOT EXISTS trades (
            id INTEGER PRIMARY KEY,
            timestamp TEXT,
            market_id TEXT,
            market_name TEXT,
            yes_price REAL,
            no_price REAL,
            total REAL,
            profit_pct REAL,
            amount REAL,
            actual_profit REAL
        )''')
        c.execute('''CREATE TABLE IF NOT EXISTS state (
            key TEXT PRIMARY KEY,
            value REAL
        )''')
        c.execute("INSERT OR IGNORE INTO state VALUES ('balance', ?)", (STARTING_BALANCE,))
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
        ts = datetime.now().strftime('%H:%M:%S.%f')[:-3]
        line = f"{ts} | {msg}"
        print(line)
        with open(LOG_FILE, "a") as f:
            f.write(line + "\n")
    
    def fetch_markets(self) -> List[Dict]:
        """Fetch markets fast with session reuse"""
        try:
            url = "https://gamma-api.polymarket.com/markets"
            params = {
                "active": "true",
                "closed": "false",
                "liquidityMin": 500,
                "limit": 200
            }
            r = self.session.get(url, params=params, timeout=3)
            if r.status_code == 200:
                return r.json()
            return []
        except:
            return []
    
    def find_arbitrage(self, markets: List[Dict]) -> List[Dict]:
        """Find YES+NO < 1.0"""
        ops = []
        
        for market in markets:
            outcomes = market.get('outcomes', [])
            if len(outcomes) != 2:
                continue
            
            try:
                yes = float(outcomes[0].get('price', 0))
                no = float(outcomes[1].get('price', 0))
                
                if yes <= 0 or no <= 0:
                    continue
                
                total = yes + no
                
                if total < (1.0 - MIN_PROFIT):
                    profit_pct = (1.0 - total) * 100
                    position = min(100, self.balance * 0.2)
                    
                    ops.append({
                        'market_id': market.get('conditionId', ''),
                        'market_name': market.get('question', 'Unknown'),
                        'yes': yes,
                        'no': no,
                        'total': total,
                        'profit_pct': profit_pct,
                        'position': position,
                        'expected': position * (1 - total)
                    })
            except:
                continue
        
        return sorted(ops, key=lambda x: x['profit_pct'], reverse=True)
    
    def execute(self, opp: Dict) -> bool:
        """Execute arbitrage immediately"""
        cost = opp['position'] * opp['total']
        
        if cost > self.balance:
            return False
        
        self.balance -= cost
        
        conn = sqlite3.connect(DB_FILE)
        c = conn.cursor()
        c.execute('''INSERT INTO trades VALUES (NULL, ?, ?, ?, ?, ?, ?, ?, ?, ?)''',
                  (datetime.now().isoformat(), opp['market_id'],
                   opp['market_name'][:100], opp['yes'], opp['no'],
                   opp['total'], opp['profit_pct'], opp['position'], opp['expected']))
        conn.commit()
        conn.close()
        
        self.trades_executed += 1
        self.total_profit += opp['expected']
        self.balance += opp['position']
        self.save_state()
        
        self.log("🚨" * 20)
        self.log(f"💰 ARBITRAGE! {opp['profit_pct']:.2f}% profit")
        self.log(f"   {opp['market_name'][:60]}...")
        self.log(f"   YES:${opp['yes']:.4f} + NO:${opp['no']:.4f} = ${opp['total']:.4f}")
        self.log(f"   Profit: ${opp['expected']:.2f} | Balance: ${self.balance:.2f}")
        self.log("🚨" * 20)
        
        return True
    
    def run(self):
        self.log("=" * 70)
        self.log("⚡ HIGH-FREQUENCY ARBITRAGE BOT")
        self.log(f"💰 Balance: ${self.balance:.2f}")
        self.log(f"⏱️  Scanning every {SCAN_INTERVAL*1000:.0f}ms")
        self.log("=" * 70)
        
        last_status = time.time()
        
        while True:
            try:
                self.scan_count += 1
                
                # Fetch
                markets = self.fetch_markets()
                if markets:
                    ops = self.find_arbitrage(markets)
                    
                    if ops:
                        self.opportunities_found += len(ops)
                        for opp in ops[:2]:
                            self.execute(opp)
                
                # Status every 5 seconds
                if time.time() - last_status >= 5:
                    profit = self.balance - STARTING_BALANCE
                    self.log(f"📊 Scans:{self.scan_count} | Trades:{self.trades_executed} | "
                            f"Profit:${profit:+.2f} | Balance:${self.balance:.2f}")
                    last_status = time.time()
                
            except Exception as e:
                pass  # Silent fail for speed
            
            time.sleep(SCAN_INTERVAL)

if __name__ == "__main__":
    bot = HFArbitrageBot()
    try:
        bot.run()
    except KeyboardInterrupt:
        bot.log("\n🛑 Stopped")
