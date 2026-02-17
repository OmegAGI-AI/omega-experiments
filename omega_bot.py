#!/usr/bin/env python3
"""Omega Trading Bot - Background Daemon"""

import os
import sys
import time
import json
import sqlite3
import requests
from datetime import datetime

PID_FILE = "/tmp/omega_bot.pid"
DB_FILE = "/root/omega-experiments/omega_trading.db"
LOG_FILE = "/tmp/omega_bot.log"

def log(msg):
    with open(LOG_FILE, "a") as f:
        f.write(f"{datetime.now().isoformat()} | {msg}\n")
    print(msg)

def init_db():
    conn = sqlite3.connect(DB_FILE)
    c = conn.cursor()
    c.execute('''CREATE TABLE IF NOT EXISTS trades 
                 (id INTEGER PRIMARY KEY, time TEXT, market TEXT, profit REAL)''')
    c.execute('''CREATE TABLE IF NOT EXISTS stats 
                 (key TEXT PRIMARY KEY, value REAL)''')
    c.execute("INSERT OR IGNORE INTO stats VALUES ('balance', 500.0)")
    c.execute("INSERT OR IGNORE INTO stats VALUES ('trades', 0)")
    c.execute("INSERT OR IGNORE INTO stats VALUES ('profit', 0)")
    conn.commit()
    conn.close()

def get_balance():
    conn = sqlite3.connect(DB_FILE)
    c = conn.cursor()
    c.execute("SELECT value FROM stats WHERE key='balance'")
    result = c.fetchone()
    conn.close()
    return result[0] if result else 500.0

def update_balance(profit):
    conn = sqlite3.connect(DB_FILE)
    c = conn.cursor()
    c.execute("UPDATE stats SET value = value + ? WHERE key='balance'", (profit,))
    c.execute("UPDATE stats SET value = value + 1 WHERE key='trades'")
    c.execute("UPDATE stats SET value = value + ? WHERE key='profit'", (profit,))
    conn.commit()
    conn.close()

def fetch_markets():
    try:
        r = requests.get("https://gamma-api.polymarket.com/markets?active=true&limit=100", timeout=10)
        return r.json()
    except:
        return []

def find_arbitrage(markets):
    ops = []
    for m in markets:
        outcomes = m.get('outcomes', [])
        if len(outcomes) != 2:
            continue
        try:
            p1, p2 = float(outcomes[0].get('price',0)), float(outcomes[1].get('price',0))
            if p1 + p2 < 0.98:
                ops.append({
                    'market': m.get('question','Unknown'),
                    'profit_pct': (1 - p1 - p2) * 100,
                    'market_id': m.get('conditionId','')
                })
        except:
            continue
    return sorted(ops, key=lambda x: x['profit_pct'], reverse=True)

def execute_trade(opp):
    profit = opp['profit_pct'] / 100 * 10  # $10 position
    conn = sqlite3.connect(DB_FILE)
    c = conn.cursor()
    c.execute("INSERT INTO trades VALUES (NULL, ?, ?, ?)", 
              (datetime.now().isoformat(), opp['market'][:80], profit))
    conn.commit()
    conn.close()
    update_balance(profit)
    return profit

def main():
    # Write PID
    with open(PID_FILE, "w") as f:
        f.write(str(os.getpid()))
    
    init_db()
    log("🚀 Omega Bot Started")
    
    scan_count = 0
    while True:
        try:
            markets = fetch_markets()
            if markets:
                ops = find_arbitrage(markets)
                scan_count += 1
                
                for opp in ops[:3]:
                    if opp['profit_pct'] > 1.5:
                        profit = execute_trade(opp)
                        log(f"💰 TRADE: {opp['profit_pct']:.2f}% profit | {opp['market'][:50]}")
                
                balance = get_balance()
                log(f"📊 Scan #{scan_count} | {len(markets)} markets | {len(ops)} ops | Balance: ${balance:.2f}")
            
            time.sleep(5)  # 5 second intervals
        except Exception as e:
            log(f"❌ Error: {e}")
            time.sleep(5)

if __name__ == "__main__":
    main()
