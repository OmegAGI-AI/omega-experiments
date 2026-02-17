#!/usr/bin/env python3
"""
OMEGA POLYMARKET SIMULATOR
Real-time arbitrage simulation with virtual portfolio tracking
"""

import requests
import json
import time
import sqlite3
from datetime import datetime, timedelta
from dataclasses import dataclass, asdict
from typing import List, Dict, Optional
import threading
import os

@dataclass
class VirtualTrade:
    id: int
    timestamp: str
    market_id: str
    market_name: str
    trade_type: str  # 'ARBITRAGE', 'DIRECTIONAL'
    outcome: str
    amount: float
    price: float
    expected_return: float
    status: str  # 'OPEN', 'CLOSED', 'SETTLED'
    actual_return: Optional[float] = None
    close_timestamp: Optional[str] = None

@dataclass
class MarketSnapshot:
    timestamp: str
    market_id: str
    market_name: str
    outcome_a: str
    price_a: float
    outcome_b: str
    price_b: float
    volume: float
    liquidity: float

class PolymarketSimulator:
    def __init__(self, starting_balance=500.0):
        self.starting_balance = starting_balance
        self.balance = starting_balance
        self.trades: List[VirtualTrade] = []
        self.trade_id_counter = 0
        self.db_path = "omega_trading.db"
        self.running = False
        self.stats = {
            'total_trades': 0,
            'winning_trades': 0,
            'losing_trades': 0,
            'total_profit': 0.0,
            'roi_percent': 0.0
        }
        
        self._init_database()
        self._load_state()
    
    def _init_database(self):
        """Initialize SQLite database for persistence"""
        conn = sqlite3.connect(self.db_path)
        cursor = conn.cursor()
        
        cursor.execute('''
            CREATE TABLE IF NOT EXISTS trades (
                id INTEGER PRIMARY KEY,
                timestamp TEXT,
                market_id TEXT,
                market_name TEXT,
                trade_type TEXT,
                outcome TEXT,
                amount REAL,
                price REAL,
                expected_return REAL,
                status TEXT,
                actual_return REAL,
                close_timestamp TEXT
            )
        ''')
        
        cursor.execute('''
            CREATE TABLE IF NOT EXISTS market_snapshots (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp TEXT,
                market_id TEXT,
                market_name TEXT,
                outcome_a TEXT,
                price_a REAL,
                outcome_b TEXT,
                price_b REAL,
                volume REAL,
                liquidity REAL
            )
        ''')
        
        cursor.execute('''
            CREATE TABLE IF NOT EXISTS portfolio (
                key TEXT PRIMARY KEY,
                value REAL
            )
        ''')
        
        conn.commit()
        conn.close()
    
    def _load_state(self):
        """Load portfolio state from database"""
        conn = sqlite3.connect(self.db_path)
        cursor = conn.cursor()
        
        cursor.execute("SELECT value FROM portfolio WHERE key = 'balance'")
        result = cursor.fetchone()
        if result:
            self.balance = result[0]
        else:
            cursor.execute("INSERT INTO portfolio VALUES ('balance', ?)", (self.balance,))
            conn.commit()
        
        # Load trades
        cursor.execute("SELECT * FROM trades")
        rows = cursor.fetchall()
        for row in rows:
            self.trades.append(VirtualTrade(*row))
            if row[0] > self.trade_id_counter:
                self.trade_id_counter = row[0]
        
        conn.close()
        self._update_stats()
    
    def _save_trade(self, trade: VirtualTrade):
        """Save trade to database"""
        conn = sqlite3.connect(self.db_path)
        cursor = conn.cursor()
        
        cursor.execute('''
            INSERT OR REPLACE INTO trades VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
        ''', (
            trade.id, trade.timestamp, trade.market_id, trade.market_name,
            trade.trade_type, trade.outcome, trade.amount, trade.price,
            trade.expected_return, trade.status, trade.actual_return, trade.close_timestamp
        ))
        
        cursor.execute("UPDATE portfolio SET value = ? WHERE key = 'balance'", (self.balance,))
        
        conn.commit()
        conn.close()
    
    def _save_snapshot(self, snapshot: MarketSnapshot):
        """Save market snapshot for analysis"""
        conn = sqlite3.connect(self.db_path)
        cursor = conn.cursor()
        
        cursor.execute('''
            INSERT INTO market_snapshots 
            (timestamp, market_id, market_name, outcome_a, price_a, outcome_b, price_b, volume, liquidity)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
        ''', (
            snapshot.timestamp, snapshot.market_id, snapshot.market_name,
            snapshot.outcome_a, snapshot.price_a, snapshot.outcome_b, snapshot.price_b,
            snapshot.volume, snapshot.liquidity
        ))
        
        conn.commit()
        conn.close()
    
    def fetch_markets(self) -> List[Dict]:
        """Fetch current market data from Polymarket"""
        try:
            url = "https://gamma-api.polymarket.com/markets"
            params = {
                "active": "true",
                "closed": "false",
                "liquidityMin": 10000,  # Only liquid markets
                "limit": 50
            }
            response = requests.get(url, params=params, timeout=15)
            return response.json()
        except Exception as e:
            print(f"❌ Error fetching markets: {e}")
            return []
    
    def find_arbitrage_opportunities(self, markets: List[Dict]) -> List[Dict]:
        """Find markets with arbitrage potential"""
        opportunities = []
        
        for market in markets:
            outcomes = market.get('outcomes', [])
            if len(outcomes) != 2:
                continue
            
            try:
                price_a = float(outcomes[0].get('price', 0))
                price_b = float(outcomes[1].get('price', 0))
                total = price_a + price_b
                
                # Save snapshot for analysis
                snapshot = MarketSnapshot(
                    timestamp=datetime.now().isoformat(),
                    market_id=market.get('conditionId', ''),
                    market_name=market.get('question', 'Unknown'),
                    outcome_a=outcomes[0].get('name', 'A'),
                    price_a=price_a,
                    outcome_b=outcomes[1].get('name', 'B'),
                    price_b=price_b,
                    volume=market.get('volume', 0),
                    liquidity=market.get('liquidity', 0)
                )
                self._save_snapshot(snapshot)
                
                # Arbitrage: prices sum to less than 1
                if total < 0.99:
                    profit = (1 - total) * 100
                    opportunities.append({
                        'market': market,
                        'outcome_a': outcomes[0],
                        'outcome_b': outcomes[1],
                        'price_a': price_a,
                        'price_b': price_b,
                        'total': total,
                        'profit_percent': profit,
                        'max_trade_size': min(
                            market.get('liquidity', 1000) * 0.1,
                            self.balance * 0.2  # Risk 20% max per trade
                        )
                    })
                    
            except (ValueError, TypeError):
                continue
        
        return sorted(opportunities, key=lambda x: x['profit_percent'], reverse=True)
    
    def execute_arbitrage_trade(self, opportunity: Dict) -> Optional[VirtualTrade]:
        """Execute a simulated arbitrage trade"""
        market = opportunity['market']
        trade_size = min(opportunity['max_trade_size'], self.balance * 0.15)
        
        if trade_size < 10:  # Minimum trade size
            return None
        
        total_cost = trade_size * opportunity['total']
        expected_return = trade_size  # Will receive $1 per share
        profit = expected_return - total_cost
        
        if total_cost > self.balance:
            return None
        
        self.trade_id_counter += 1
        trade = VirtualTrade(
            id=self.trade_id_counter,
            timestamp=datetime.now().isoformat(),
            market_id=market.get('conditionId', ''),
            market_name=market.get('question', 'Unknown')[:80],
            trade_type='ARBITRAGE',
            outcome=f"{opportunity['outcome_a'].get('name')} + {opportunity['outcome_b'].get('name')}",
            amount=trade_size,
            price=opportunity['total'],
            expected_return=expected_return,
            status='OPEN',
            actual_return=None,
            close_timestamp=None
        )
        
        self.balance -= total_cost
        self.trades.append(trade)
        self._save_trade(trade)
        
        print(f"\n✅ ARBITRAGE TRADE EXECUTED")
        print(f"   Market: {trade.market_name[:60]}...")
        print(f"   Invested: ${total_cost:.2f}")
        print(f"   Expected Return: ${expected_return:.2f}")
        print(f"   Profit: ${profit:.2f} ({opportunity['profit_percent']:.2f}%)")
        print(f"   New Balance: ${self.balance:.2f}")
        
        return trade
    
    def close_expired_trades(self):
        """Close trades for resolved markets"""
        # In simulation, randomly resolve some trades
        for trade in self.trades:
            if trade.status == 'OPEN' and trade.trade_type == 'ARBITRAGE':
                # Arbitrage trades are guaranteed profit when both sides bought
                trade.status = 'SETTLED'
                trade.actual_return = trade.expected_return
                trade.close_timestamp = datetime.now().isoformat()
                self.balance += trade.actual_return
                self._save_trade(trade)
                
                print(f"\n💰 TRADE SETTLED")
                print(f"   Profit: ${trade.actual_return - (trade.amount * trade.price):.2f}")
    
    def _update_stats(self):
        """Update performance statistics"""
        closed_trades = [t for t in self.trades if t.status in ['CLOSED', 'SETTLED']]
        
        self.stats['total_trades'] = len(self.trades)
        self.stats['winning_trades'] = len([t for t in closed_trades if (t.actual_return or 0) > (t.amount * t.price)])
        self.stats['losing_trades'] = len([t for t in closed_trades if (t.actual_return or 0) <= (t.amount * t.price)])
        
        total_invested = sum(t.amount * t.price for t in closed_trades)
        total_returned = sum(t.actual_return or 0 for t in closed_trades)
        self.stats['total_profit'] = total_returned - total_invested
        
        if total_invested > 0:
            self.stats['roi_percent'] = (self.stats['total_profit'] / total_invested) * 100
    
    def print_portfolio(self):
        """Display current portfolio status"""
        print("\n" + "=" * 80)
        print(f"💼 OMEGA TRADING SIMULATOR — {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
        print("=" * 80)
        print(f"\n📊 PORTFOLIO STATUS")
        print(f"   Starting Balance: ${self.starting_balance:.2f}")
        print(f"   Current Balance:  ${self.balance:.2f}")
        print(f"   Total Profit:     ${self.balance - self.starting_balance:.2f}")
        print(f"   ROI:              {((self.balance - self.starting_balance) / self.starting_balance) * 100:.2f}%")
        
        print(f"\n📈 TRADING STATS")
        print(f"   Total Trades:     {self.stats['total_trades']}")
        print(f"   Winning:          {self.stats['winning_trades']}")
        print(f"   Losing:           {self.stats['losing_trades']}")
        print(f"   Open Trades:      {len([t for t in self.trades if t.status == 'OPEN'])}")
        
        open_trades = [t for t in self.trades if t.status == 'OPEN']
        if open_trades:
            print(f"\n🔓 OPEN POSITIONS")
            for trade in open_trades[-5:]:  # Show last 5
                print(f"   #{trade.id}: {trade.market_name[:50]}... (${trade.amount:.2f})")
        
        print("\n" + "=" * 80)
    
    def run_simulation_cycle(self):
        """Run one cycle of market analysis and trading"""
        print(f"\n🔄 Scanning at {datetime.now().strftime('%H:%M:%S')}...")
        
        markets = self.fetch_markets()
        if not markets:
            print("   No market data available")
            return
        
        print(f"   Analyzing {len(markets)} markets...")
        
        opportunities = self.find_arbitrage_opportunities(markets)
        
        if opportunities:
            print(f"   🎯 Found {len(opportunities)} arbitrage opportunities!")
            
            # Execute best opportunity
            best = opportunities[0]
            if best['profit_percent'] > 1.0:  # Minimum 1% profit
                self.execute_arbitrage_trade(best)
        else:
            print("   No arbitrage opportunities found")
        
        # Close expired trades
        self.close_expired_trades()
        
        # Update and display stats
        self._update_stats()
        self.print_portfolio()
    
    def run_continuous(self, interval_seconds=60):
        """Run simulation continuously"""
        self.running = True
        print("\n🚀 OMEGA POLYMARKET SIMULATOR STARTED")
        print(f"   Starting Balance: ${self.starting_balance:.2f}")
        print(f"   Scan Interval: {interval_seconds}s")
        print("   Press Ctrl+C to stop\n")
        
        try:
            while self.running:
                self.run_simulation_cycle()
                print(f"\n⏳ Waiting {interval_seconds}s for next scan...")
                time.sleep(interval_seconds)
        except KeyboardInterrupt:
            print("\n\n🛑 Simulation stopped by user")
            self.print_portfolio()

# Run the simulator
if __name__ == "__main__":
    # Starting with €500 equivalent
    simulator = PolymarketSimulator(starting_balance=500.0)
    
    # Run one cycle immediately, then continuous
    simulator.run_simulation_cycle()
    
    # Ask if user wants continuous mode
    print("\n🔄 Run continuous simulation? (y/n): ", end="")
    try:
        response = input().strip().lower()
        if response == 'y':
            simulator.run_continuous(interval_seconds=30)
    except EOFError:
        pass
