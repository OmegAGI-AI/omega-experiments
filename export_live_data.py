#!/usr/bin/env python3
"""
Export bot data to JSON for website
Runs continuously to update website with real trades
"""

import sqlite3
import json
import time
from datetime import datetime

DB_FILE = "/root/omega-experiments/scalping_bot.db"
JSON_FILE = "/root/omega-experiments/live_trades.json"

def export_data():
    try:
        conn = sqlite3.connect(DB_FILE)
        c = conn.cursor()
        
        # Get all trades
        c.execute("SELECT * FROM trades ORDER BY id DESC LIMIT 20")
        trades = c.fetchall()
        
        # Get stats
        c.execute("SELECT COUNT(*), SUM(pnl), SUM(CASE WHEN pnl > 0 THEN 1 ELSE 0 END) FROM trades")
        total_trades, total_pnl, wins = c.fetchone()
        
        conn.close()
        
        # Format trades
        formatted_trades = []
        for t in trades:
            formatted_trades.append({
                'id': t[0],
                'time': t[1].split('T')[1][:8] if 'T' in str(t[1]) else str(t[1]),
                'coin': t[2],
                'side': t[3],
                'entry': t[4],
                'exit': t[5],
                'size': t[6],
                'pnl': t[7],
                'pnl_pct': t[8],
                'duration': t[9],
                'is_win': t[7] > 0
            })
        
        # Calculate balance
        balance = 500.0 + (total_pnl or 0)
        win_rate = (wins / total_trades * 100) if total_trades else 0
        
        data = {
            'timestamp': datetime.now().isoformat(),
            'balance': balance,
            'total_pnl': total_pnl or 0,
            'total_trades': total_trades or 0,
            'win_rate': win_rate,
            'trades': formatted_trades
        }
        
        with open(JSON_FILE, 'w') as f:
            json.dump(data, f, indent=2)
            
        print(f"[{datetime.now().strftime('%H:%M:%S')}] Exported {len(trades)} trades")
        
    except Exception as e:
        print(f"Error: {e}")

if __name__ == "__main__":
    print("Starting live data exporter...")
    while True:
        export_data()
        time.sleep(5)  # Update every 5 seconds
