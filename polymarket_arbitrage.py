# Polymarket Arbitrage Analyzer
# This tool finds mispricings across prediction markets

import requests
import json
from datetime import datetime

class PolymarketAnalyzer:
    def __init__(self):
        self.base_url = "https://gamma-api.polymarket.com"
        self.opportunities = []
    
    def get_active_markets(self, limit=50):
        """Fetch active prediction markets"""
        try:
            url = f"{self.base_url}/markets"
            params = {
                "active": "true",
                "closed": "false",
                "limit": limit
            }
            response = requests.get(url, params=params, timeout=10)
            return response.json()
        except Exception as e:
            print(f"Error fetching markets: {e}")
            return []
    
    def analyze_arbitrage(self, market):
        """Check for arbitrage opportunities in a market"""
        opportunities = []
        
        # Get outcomes
        outcomes = market.get('outcomes', [])
        if len(outcomes) != 2:
            return opportunities  # Only analyze binary markets for now
        
        # Calculate implied probabilities
        prices = []
        for outcome in outcomes:
            price = outcome.get('price', 0)
            prices.append(price)
        
        if len(prices) == 2:
            # Check if prices don't sum to ~1 (arbitrage opportunity)
            total = sum(prices)
            
            if total < 0.98:  # Buy both sides, guaranteed profit
                profit = (1 - total) * 100
                opportunities.append({
                    'type': 'DIRECT_ARBITRAGE',
                    'market': market.get('question', 'Unknown'),
                    'outcome_a': outcomes[0].get('name', 'A'),
                    'price_a': prices[0],
                    'outcome_b': outcomes[1].get('name', 'B'),
                    'price_b': prices[1],
                    'total': total,
                    'profit_percent': profit,
                    'market_id': market.get('conditionId', ''),
                    'url': f"https://polymarket.com/event/{market.get('slug', '')}"
                })
        
        return opportunities
    
    def scan_for_opportunities(self):
        """Scan all active markets for arbitrage"""
        print("🔍 Scanning Polymarket for arbitrage opportunities...\n")
        
        markets = self.get_active_markets(limit=100)
        all_opportunities = []
        
        for market in markets:
            ops = self.analyze_arbitrage(market)
            all_opportunities.extend(ops)
        
        # Sort by profit potential
        all_opportunities.sort(key=lambda x: x['profit_percent'], reverse=True)
        
        return all_opportunities
    
    def print_report(self, opportunities):
        """Print formatted report"""
        if not opportunities:
            print("❌ No arbitrage opportunities found currently.")
            print("\nThis is normal — efficient markets rarely have obvious arbitrage.")
            print("Check back during:")
            print("  - High volatility events")
            print("  - News announcements")
            print("  - Market open/close times")
            return
        
        print(f"✅ Found {len(opportunities)} potential opportunities\n")
        print("=" * 80)
        
        for i, opp in enumerate(opportunities[:10], 1):
            print(f"\n📊 Opportunity #{i}")
            print(f"   Market: {opp['market'][:70]}")
            print(f"   Type: {opp['type']}")
            print(f"   {opp['outcome_a']}: ${opp['price_a']:.3f}")
            print(f"   {opp['outcome_b']}: ${opp['price_b']:.3f}")
            print(f"   Total: ${opp['total']:.3f} (should be $1.00)")
            print(f"   💰 Potential Profit: {opp['profit_percent']:.2f}%")
            print(f"   🔗 {opp['url']}")
            print("-" * 80)

# Run analysis
if __name__ == "__main__":
    analyzer = PolymarketAnalyzer()
    opportunities = analyzer.scan_for_opportunities()
    analyzer.print_report(opportunities)
