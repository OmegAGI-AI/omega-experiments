#!/usr/bin/env python3
"""
Omega Crypto Monitor - BTC, ETH, SOL
Tracks 5min and 15min price movements
"""

import requests
import time
import json
from datetime import datetime
from dataclasses import dataclass, asdict
from typing import Dict, List
import sqlite3

DB_FILE = "/root/omega-experiments/crypto_data.db"
LOG_FILE = "/tmp/crypto_monitor.log"

COINS = {
    'BTC': 'bitcoin',
    'ETH': 'ethereum', 
    'SOL': 'solana'
}

@dataclass
class PriceData:
    coin: str
    timestamp: str
    price: float
    change_5m: float
    change_15m: float
    volume: float
    signal: str  # 'BUY', 'SELL', 'HOLD'

class CryptoMonitor:
    def __init__(self):
        self.price_history = {coin: [] for coin in COINS.keys()}
        self.db_path = DB_FILE
        self._init_db()
        
    def _init_db(self):
        conn = sqlite3.connect(self.db_path)
        c = conn.cursor()
        c.execute('''CREATE TABLE IF NOT EXISTS prices (
            id INTEGER PRIMARY KEY,
            coin TEXT,
            timestamp TEXT,
            price REAL,
            change_5m REAL,
            change_15m REAL,
            volume REAL,
            signal TEXT
        )''')
        c.execute('''CREATE TABLE IF NOT EXISTS signals (
            id INTEGER PRIMARY KEY,
            timestamp TEXT,
            coin TEXT,
            signal TEXT,
            price REAL,
            reason TEXT
        )''')
        conn.commit()
        conn.close()
    
    def log(self, msg):
        timestamp = datetime.now().strftime('%H:%M:%S')
        full_msg = f"{timestamp} | {msg}"
        print(full_msg)
        with open(LOG_FILE, "a") as f:
            f.write(full_msg + "\n")
        return full_msg
    
    def fetch_prices(self) -> Dict[str, float]:
        """Fetch current prices from CoinGecko"""
        try:
            ids = ','.join(COINS.values())
            url = f"https://api.coingecko.com/api/v3/simple/price?ids={ids}&vs_currencies=usd&include_24hr_vol=true"
            response = requests.get(url, timeout=10)
            data = response.json()
            
            prices = {}
            for symbol, coin_id in COINS.items():
                if coin_id in data:
                    prices[symbol] = {
                        'price': data[coin_id]['usd'],
                        'volume': data[coin_id].get('usd_24h_vol', 0)
                    }
            return prices
        except Exception as e:
            self.log(f"❌ Error fetching prices: {e}")
            return {}
    
    def calculate_changes(self, coin: str, current_price: float) -> tuple:
        """Calculate 5min and 15min price changes"""
        history = self.price_history[coin]
        now = time.time()
        
        # Add current price
        history.append({'time': now, 'price': current_price})
        
        # Keep only last 20 minutes of data
        history[:] = [h for h in history if now - h['time'] < 1200]
        
        change_5m = 0
        change_15m = 0
        
        # Find price 5 minutes ago
        price_5m_ago = None
        for h in history:
            if 240 <= now - h['time'] <= 360:  # 4-6 minutes ago
                price_5m_ago = h['price']
                break
        
        # Find price 15 minutes ago
        price_15m_ago = None
        for h in history:
            if 840 <= now - h['time'] <= 960:  # 14-16 minutes ago
                price_15m_ago = h['price']
                break
        
        if price_5m_ago:
            change_5m = ((current_price - price_5m_ago) / price_5m_ago) * 100
        if price_15m_ago:
            change_15m = ((current_price - price_15m_ago) / price_15m_ago) * 100
        
        return change_5m, change_15m
    
    def generate_signal(self, change_5m: float, change_15m: float) -> str:
        """Generate trading signal based on momentum"""
        # Strong momentum up
        if change_5m > 1.5 and change_15m > 2:
            return 'STRONG_BUY'
        elif change_5m > 0.5 and change_15m > 0:
            return 'BUY'
        # Strong momentum down
        elif change_5m < -1.5 and change_15m < -2:
            return 'STRONG_SELL'
        elif change_5m < -0.5 and change_15m < 0:
            return 'SELL'
        else:
            return 'HOLD'
    
    def save_data(self, data: PriceData):
        """Save price data to database"""
        conn = sqlite3.connect(self.db_path)
        c = conn.cursor()
        c.execute('''INSERT INTO prices VALUES (NULL, ?, ?, ?, ?, ?, ?, ?)''',
                  (data.coin, data.timestamp, data.price, data.change_5m, 
                   data.change_15m, 0, data.signal))
        
        # Save significant signals
        if data.signal in ['STRONG_BUY', 'STRONG_SELL', 'BUY', 'SELL']:
            reason = f"5m: {data.change_5m:+.2f}%, 15m: {data.change_15m:+.2f}%"
            c.execute('''INSERT INTO signals VALUES (NULL, ?, ?, ?, ?, ?)''',
                      (data.timestamp, data.coin, data.signal, data.price, reason))
        
        conn.commit()
        conn.close()
    
    def print_status(self, data_list: List[PriceData]):
        """Print current status"""
        print("\n" + "="*80)
        print(f"⏰ {datetime.now().strftime('%H:%M:%S')} | CRYPTO MONITOR")
        print("="*80)
        print(f"{'COIN':<8} {'PRICE':<15} {'5MIN':<10} {'15MIN':<10} {'SIGNAL':<15}")
        print("-"*80)
        
        for d in data_list:
            signal_emoji = {
                'STRONG_BUY': '🚀 BUY++',
                'BUY': '📈 BUY',
                'STRONG_SELL': '🔻 SELL++', 
                'SELL': '📉 SELL',
                'HOLD': '➡️ HOLD'
            }.get(d.signal, d.signal)
            
            print(f"{d.coin:<8} ${d.price:<14.2f} {d.change_5m:+.2f}%    {d.change_15m:+.2f}%    {signal_emoji}")
        
        print("="*80)
    
    def save_json_status(self, data_list: List[PriceData]):
        """Save status to JSON for web dashboard"""
        status = {
            'timestamp': datetime.now().isoformat(),
            'coins': [asdict(d) for d in data_list],
            'alerts': [asdict(d) for d in data_list if d.signal in ['STRONG_BUY', 'STRONG_SELL']]
        }
        with open('/root/omega-experiments/crypto_status.json', 'w') as f:
            json.dump(status, f, indent=2)
    
    def run(self):
        self.log("🚀 Crypto Monitor Started")
        self.log("📊 Tracking: BTC, ETH, SOL")
        self.log("⏱️  Timeframes: 5min, 15min")
        
        while True:
            try:
                prices = self.fetch_prices()
                if not prices:
                    time.sleep(30)
                    continue
                
                data_list = []
                for coin, data in prices.items():
                    change_5m, change_15m = self.calculate_changes(coin, data['price'])
                    signal = self.generate_signal(change_5m, change_15m)
                    
                    price_data = PriceData(
                        coin=coin,
                        timestamp=datetime.now().isoformat(),
                        price=data['price'],
                        change_5m=change_5m,
                        change_15m=change_15m,
                        volume=data['volume'],
                        signal=signal
                    )
                    data_list.append(price_data)
                    self.save_data(price_data)
                    
                    # Log significant signals
                    if signal in ['STRONG_BUY', 'STRONG_SELL']:
                        self.log(f"🚨 {coin} {signal}! Price: ${data['price']:.2f} | 5m: {change_5m:+.2f}% | 15m: {change_15m:+.2f}%")
                
                self.print_status(data_list)
                self.save_json_status(data_list)
                
            except Exception as e:
                self.log(f"❌ Error: {e}")
            
            time.sleep(60)  # Check every minute

if __name__ == "__main__":
    monitor = CryptoMonitor()
    monitor.run()
