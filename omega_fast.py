#!/usr/bin/env python3
"""
OMEGA POLYMARKET SIMULATOR - HIGH FREQUENCY
Continuous real-time arbitrage simulation
"""

import requests
import json
import time
import sqlite3
from datetime import datetime
from dataclasses import dataclass
from typing import List, Dict, Optional
import signal
import sys

# Global state
running = True
scan_count = 0
last_opportunity = None

def signal_handler(sig, frame):
    global running
    running = False
    print("\n\n🛑 Stopping...")
    sys.exit(0)

signal.signal(signal.SIGINT, signal_handler)

class FastSimulator:
    def __init__(self, starting_balance=500.0):
        self.starting_balance = starting_balance
        self.balance = starting_balance
        self.db_path = "/root/omega-experiments/omega_trading.db"
        self._init_db()
        
    def _init_db(self):
        conn = sqlite3.connect(self.db_path)
        cursor = conn.cursor()
        cursor.execute('''
            CREATE TABLE IF NOT EXISTS trades (
                id INTEGER PRIMARY KEY,
                timestamp TEXT,
                market_id TEXT,
                market_name TEXT,
                profit REAL,
                amount REAL
            )
        ''')
        cursor.execute('''
            CREATE TABLE IF NOT EXISTS scans (
                timestamp TEXT,
                markets_checked INTEGER,
                opportunities_found INTEGER
            )
        ''')
        conn.commit()
        conn.close()
    
    def fetch_markets(self) -> List[Dict]:
        try:
            url = "https://gamma-api.polymarket.com/markets"
            params = {
                "active": "true",
                "closed": "false",
                "liquidityMin": 5000,
                "limit": 100
            }
            response = requests.get(url, params=params, timeout=10)
            return response.json()
        except:
            return []
    
    def find_arbitrage(self, markets: List[Dict]) -> List[Dict]:
        opportunities = []
        for market in markets:
            outcomes = market.get('outcomes', [])
            if len(outcomes) != 2:
                continue
            try:
                price_a = float(outcomes[0].get('price', 0))
                price_b = float(outcomes[1].get('price', 0))
                total = price_a + price_b
                
                if total < 0.98:  # 2%+ profit margin
                    opportunities.append({
                        'market': market.get('question', 'Unknown'),
                        'profit': (1 - total) * 100,
                        'price_a': price_a,
                        'price_b': price_b,
                        'total': total
                    })
            except:
                continue
        return sorted(opportunities, key=lambda x: x['profit'], reverse=True)
    
    def execute_trade(self, opp: Dict):
        profit = 10 * opp['profit'] / 100  # $10 trade size
        self.balance += profit
        
        conn = sqlite3.connect(self.db_path)
        cursor = conn.cursor()
        cursor.execute('''
            INSERT INTO trades VALUES (NULL, ?, ?, ?, ?, ?)
        ''', (datetime.now().isoformat(), 'ARBITRAGE', opp['market'][:80], profit, 10))
        conn.commit()
        conn.close()
        
        return profit
    
    def log_scan(self, markets_checked: int, opportunities: int):
        conn = sqlite3.connect(self.db_path)
        cursor = conn.cursor()
        cursor.execute('''
            INSERT INTO scans VALUES (?, ?, ?)
        ''', (datetime.now().isoformat(), markets_checked, opportunities))
        conn.commit()
        conn.close()
    
    def print_status(self, opportunities: List[Dict]):
        profit_total = self.balance - self.starting_balance
        roi = (profit_total / self.starting_balance) * 100
        
        print(f"\n{'='*70}")
        print(f"⏱️  {datetime.now().strftime('%H:%M:%S')} | Scans: {scan_count} | Balance: ${self.balance:.2f} | Profit: ${profit_total:.2f} ({roi:.2f}%)")
        
        if opportunities:
            print(f"🎯 FOUND {len(opportunities)} OPPORTUNITIES!")
            for i, opp in enumerate(opportunities[:3], 1):
                print(f"   #{i}: {opp['profit']:.2f}% profit | {opp['market'][:50]}...")
        else:
            print("📊 No arbitrage found")
        print(f"{'='*70}")
    
    def run(self):
        global scan_count
        print("🚀 OMEGA HIGH-FREQUENCY SIMULATOR")
        print(f"💰 Starting: ${self.starting_balance:.2f}")
        print("⚡ Scanning continuously... Press Ctrl+C to stop\n")
        
        while running:
            try:
                markets = self.fetch_markets()
                if markets:
                    opportunities = self.find_arbitrage(markets)
                    scan_count += 1
                    
                    # Execute trades on opportunities
                    for opp in opportunities[:2]:  # Max 2 trades per scan
                        if opp['profit'] > 2:  # Minimum 2% profit
                            self.execute_trade(opp)
                    
                    self.log_scan(len(markets), len(opportunities))
                    self.print_status(opportunities)
                
                # Fastest possible rate without hitting rate limits
                time.sleep(5)
                
            except Exception as e:
                time.sleep(5)
                continue

if __name__ == "__main__":
    sim = FastSimulator(starting_balance=500.0)
    sim.run()
