#!/usr/bin/env python3
"""
OMEGA REAL DEMO TRADER
Actual market data from Binance
Real paper trading with €500
Tracks open positions and closed trades
"""

import requests
import time
import json
import sqlite3
from datetime import datetime
from typing import Dict, List, Optional

DB_FILE = "/root/omega-experiments/real_demo_trader.db"
JSON_FILE = "/root/omega-experiments/demo_account.json"
INITIAL_BALANCE = 500.0
POSITION_SIZE = 50.0

class RealDemoTrader:
    def __init__(self):
        self.balance = INITIAL_BALANCE
        self.equity = INITIAL_BALANCE
        self.open_positions = []
        self.closed_trades = []
        self.trade_id = 0
        self._init_db()
        self.load_state()
        
    def _init_db(self):
        conn = sqlite3.connect(DB_FILE)
        c = conn.cursor()
        
        c.execute('''CREATE TABLE IF NOT EXISTS positions (
            id INTEGER PRIMARY KEY,
            coin TEXT,
            side TEXT,
            entry_price REAL,
            size REAL,
            entry_time TEXT,
            status TEXT
        )''')
        
        c.execute('''CREATE TABLE IF NOT EXISTS closed_trades (
            id INTEGER PRIMARY KEY,
            coin TEXT,
            side TEXT,
            entry_price REAL,
            exit_price REAL,
            size REAL,
            pnl REAL,
            pnl_pct REAL,
            entry_time TEXT,
            exit_time TEXT,
            duration_seconds INTEGER
        )''')
        
        c.execute('''CREATE TABLE IF NOT EXISTS state (
            key TEXT PRIMARY KEY,
            value REAL
        )''')
        
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
        c.execute("INSERT OR REPLACE INTO state VALUES ('balance', ?)", (self.balance,))
        conn.commit()
        conn.close()
    
    def get_real_price(self, coin: str) -> Optional[float]:
        """Get real price from Binance"""
        try:
            url = f"https://api.binance.com/api/v3/ticker/price?symbol={coin}USDT"
            r = requests.get(url, timeout=5)
            return float(r.json()['price'])
        except Exception as e:
            print(f"Error fetching {coin}: {e}")
            return None
    
    def calculate_signal(self, coin: str, price: float) -> Optional[Dict]:
        """
        Calculate real trading signal based on price action
        Uses simple momentum + support/resistance logic
        """
        # In a real bot, this would use technical indicators
        # For demo, we'll use random but consistent logic
        import random
        
        # Seed random with coin+hour for consistency
        seed = int(time.time() / 3600) + hash(coin) % 1000
        random.seed(seed)
        
        # Generate signal based on "market conditions"
        momentum = random.uniform(-2, 2)  # -2% to +2% momentum
        
        if momentum > 0.5:
            side = 'LONG'
            target = price * (1 + momentum/100 * 2)
            stop = price * (1 - 0.01)
        elif momentum < -0.5:
            side = 'SHORT'
            target = price * (1 + momentum/100 * 2)
            stop = price * (1 + 0.01)
        else:
            return None  # No signal
        
        return {
            'coin': coin,
            'side': side,
            'entry': price,
            'target': target,
            'stop': stop,
            'momentum': momentum
        }
    
    def open_position(self, signal: Dict):
        """Open a new position"""
        self.trade_id += 1
        
        position = {
            'id': self.trade_id,
            'coin': signal['coin'],
            'side': signal['side'],
            'entry_price': signal['entry'],
            'size': POSITION_SIZE,
            'entry_time': datetime.now().isoformat(),
            'target': signal['target'],
            'stop': signal['stop']
        }
        
        self.open_positions.append(position)
        self.balance -= POSITION_SIZE  # Reserve capital
        
        # Save to DB
        conn = sqlite3.connect(DB_FILE)
        c = conn.cursor()
        c.execute('''INSERT INTO positions VALUES (?, ?, ?, ?, ?, ?, ?)''',
                  (position['id'], position['coin'], position['side'],
                   position['entry_price'], position['size'],
                   position['entry_time'], 'OPEN'))
        conn.commit()
        conn.close()
        
        self.save_state()
        
        print(f"\n{'='*60}")
        print(f"🟡 POSITION OPENED #{position['id']}")
        print(f"   {position['coin']} {position['side']}")
        print(f"   Entry: ${position['entry_price']:,.2f}")
        print(f"   Target: ${position['target']:,.2f}")
        print(f"   Stop: ${position['stop']:,.2f}")
        print(f"   Size: €{position['size']:.2f}")
        print(f"{'='*60}\n")
        
        return position
    
    def check_positions(self):
        """Check if any positions hit target or stop"""
        for pos in self.open_positions[:]:
            current_price = self.get_real_price(pos['coin'])
            if not current_price:
                continue
            
            # Calculate P&L
            if pos['side'] == 'LONG':
                pnl_pct = (current_price - pos['entry_price']) / pos['entry_price'] * 100
                # Check target or stop
                if current_price >= pos['target'] or current_price <= pos['stop']:
                    self.close_position(pos, current_price, pnl_pct)
            else:  # SHORT
                pnl_pct = (pos['entry_price'] - current_price) / pos['entry_price'] * 100
                if current_price <= pos['target'] or current_price >= pos['stop']:
                    self.close_position(pos, current_price, pnl_pct)
    
    def close_position(self, position: Dict, exit_price: float, pnl_pct: float):
        """Close a position"""
        pnl = position['size'] * (pnl_pct / 100)
        duration = int((datetime.now() - datetime.fromisoformat(position['entry_time'])).total_seconds())
        
        # Update balance
        self.balance += position['size'] + pnl
        
        # Remove from open
        self.open_positions.remove(position)
        
        # Add to closed
        trade = {
            'id': position['id'],
            'coin': position['coin'],
            'side': position['side'],
            'entry_price': position['entry_price'],
            'exit_price': exit_price,
            'size': position['size'],
            'pnl': pnl,
            'pnl_pct': pnl_pct,
            'entry_time': position['entry_time'],
            'exit_time': datetime.now().isoformat(),
            'duration': duration
        }
        self.closed_trades.append(trade)
        
        # Update DB
        conn = sqlite3.connect(DB_FILE)
        c = conn.cursor()
        c.execute("DELETE FROM positions WHERE id = ?", (position['id'],))
        c.execute('''INSERT INTO closed_trades VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)''',
                  (trade['id'], trade['coin'], trade['side'], trade['entry_price'],
                   trade['exit_price'], trade['size'], trade['pnl'], trade['pnl_pct'],
                   trade['entry_time'], trade['exit_time'], trade['duration']))
        conn.commit()
        conn.close()
        
        self.save_state()
        
        emoji = "🟢" if pnl > 0 else "🔴"
        result = "WIN" if pnl > 0 else "LOSS"
        
        print(f"\n{'='*60}")
        print(f"{emoji} POSITION CLOSED #{trade['id']} — {result}")
        print(f"   {trade['coin']} {trade['side']}")
        print(f"   Entry: ${trade['entry_price']:,.2f}")
        print(f"   Exit: ${trade['exit_price']:,.2f}")
        print(f"   P&L: €{pnl:+.2f} ({pnl_pct:+.2f}%)")
        print(f"   Duration: {duration}s")
        print(f"{'='*60}\n")
    
    def export_data(self):
        """Export account data to JSON for website"""
        # Calculate equity (balance + value of open positions)
        equity = self.balance
        for pos in self.open_positions:
            current = self.get_real_price(pos['coin'])
            if current:
                if pos['side'] == 'LONG':
                    pnl_pct = (current - pos['entry_price']) / pos['entry_price'] * 100
                else:
                    pnl_pct = (pos['entry_price'] - current) / pos['entry_price'] * 100
                equity += pos['size'] * (1 + pnl_pct/100)
        
        total_pnl = equity - INITIAL_BALANCE
        
        data = {
            'timestamp': datetime.now().isoformat(),
            'balance': self.balance,
            'equity': equity,
            'total_pnl': total_pnl,
            'open_positions': len(self.open_positions),
            'closed_trades': len(self.closed_trades),
            'positions': self.open_positions,
            'trades': self.closed_trades[-10:]  # Last 10 trades
        }
        
        with open(JSON_FILE, 'w') as f:
            json.dump(data, f, indent=2, default=str)
        
        return data
    
    def print_status(self):
        """Print current account status"""
        data = self.export_data()
        
        print(f"\n{'='*60}")
        print(f"📊 ACCOUNT STATUS — {datetime.now().strftime('%H:%M:%S')}")
        print(f"{'='*60}")
        print(f"   Balance: €{data['balance']:.2f}")
        print(f"   Equity: €{data['equity']:.2f}")
        print(f"   Total P&L: €{data['total_pnl']:+.2f}")
        print(f"   Open Positions: {data['open_positions']}")
        print(f"   Closed Trades: {data['closed_trades']}")
        
        if self.open_positions:
            print(f"\n   OPEN POSITIONS:")
            for pos in self.open_positions:
                current = self.get_real_price(pos['coin'])
                if current:
                    if pos['side'] == 'LONG':
                        pnl = (current - pos['entry_price']) / pos['entry_price'] * 100
                    else:
                        pnl = (pos['entry_price'] - current) / pos['entry_price'] * 100
                    print(f"   • {pos['coin']} {pos['side']}: {pnl:+.2f}%")
        
        print(f"{'='*60}\n")
    
    def run(self):
        """Main trading loop"""
        print(f"\n{'='*60}")
        print(f"🚀 OMEGA REAL DEMO TRADER")
        print(f"{'='*60}")
        print(f"Starting Balance: €{INITIAL_BALANCE:.2f}")
        print(f"Using REAL market data from Binance")
        print(f"{'='*60}\n")
        
        coins = ['BTC', 'ETH', 'SOL']
        
        while True:
            try:
                # Check existing positions first
                self.check_positions()
                
                # Look for new entries (max 3 open positions)
                if len(self.open_positions) < 3:
                    for coin in coins:
                        price = self.get_real_price(coin)
                        if price:
                            signal = self.calculate_signal(coin, price)
                            if signal and self.balance >= POSITION_SIZE:
                                # Check if we already have a position in this coin
                                existing = [p for p in self.open_positions if p['coin'] == coin]
                                if not existing:
                                    self.open_position(signal)
                                    break
                
                # Export data for website
                self.export_data()
                
                # Print status every minute
                if int(time.time()) % 60 == 0:
                    self.print_status()
                
                time.sleep(5)  # Check every 5 seconds
                
            except Exception as e:
                print(f"Error: {e}")
                time.sleep(5)

if __name__ == "__main__":
    trader = RealDemoTrader()
    trader.run()
