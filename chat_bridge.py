#!/usr/bin/env python3
"""
OMEGA CHAT BRIDGE
Connects website chat to actual Omega (me)
Uses SQLite as message queue
"""

import sqlite3
import time
import json
from datetime import datetime

DB_FILE = "/root/omega-experiments/chat_bridge.db"
MESSAGES_FILE = "/root/omega-experiments/chat_messages.json"

class ChatBridge:
    def __init__(self):
        self._init_db()
        
    def _init_db(self):
        conn = sqlite3.connect(DB_FILE)
        c = conn.cursor()
        c.execute('''CREATE TABLE IF NOT EXISTS messages (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            timestamp TEXT,
            from_user TEXT,
            content TEXT,
            status TEXT,
            response TEXT
        )''')
        conn.commit()
        conn.close()
    
    def add_user_message(self, content: str):
        """Add message from user (called by website)"""
        conn = sqlite3.connect(DB_FILE)
        c = conn.cursor()
        c.execute('''INSERT INTO messages VALUES (NULL, ?, ?, ?, ?, ?)''',
                  (datetime.now().isoformat(), 'marco', content, 'pending', None))
        conn.commit()
        conn.close()
        print(f"[{datetime.now().strftime('%H:%M:%S')}] New message from Marco: {content[:50]}...")
    
    def get_pending_messages(self) -> list:
        """Get messages waiting for Omega's response"""
        conn = sqlite3.connect(DB_FILE)
        c = conn.cursor()
        c.execute("SELECT * FROM messages WHERE status = 'pending' ORDER BY id")
        messages = c.fetchall()
        conn.close()
        return messages
    
    def add_response(self, msg_id: int, response: str):
        """Add Omega's response"""
        conn = sqlite3.connect(DB_FILE)
        c = conn.cursor()
        c.execute("UPDATE messages SET status = 'answered', response = ? WHERE id = ?",
                  (response, msg_id))
        conn.commit()
        conn.close()
    
    def export_messages(self):
        """Export all messages to JSON for website"""
        conn = sqlite3.connect(DB_FILE)
        c = conn.cursor()
        c.execute("SELECT * FROM messages ORDER BY id DESC LIMIT 50")
        rows = c.fetchall()
        conn.close()
        
        messages = []
        for row in rows:
            messages.append({
                'id': row[0],
                'time': row[1].split('T')[1][:8] if 'T' in str(row[1]) else str(row[1]),
                'from': row[2],
                'content': row[3],
                'status': row[4],
                'response': row[5]
            })
        
        with open(MESSAGES_FILE, 'w') as f:
            json.dump({'messages': messages}, f, indent=2)
    
    def run(self):
        """Main loop — check for messages and alert me"""
        print(f"\n{'='*60}")
        print(f"🌉 OMEGA CHAT BRIDGE STARTED")
        print(f"{'='*60}")
        print(f"Waiting for messages from website...")
        print(f"{'='*60}\n")
        
        while True:
            try:
                pending = self.get_pending_messages()
                
                if pending:
                    for msg in pending:
                        msg_id, timestamp, from_user, content, status, response = msg
                        
                        # Alert me (this prints to log that I can see)
                        print(f"\n{'='*60}")
                        print(f"🔔 NEW MESSAGE FROM {from_user.upper()}")
                        print(f"{'='*60}")
                        print(f"Time: {timestamp}")
                        print(f"Message: {content}")
                        print(f"{'='*60}")
                        print(f"⚠️  RESPOND TO MESSAGE ID: {msg_id}")
                        print(f"Use: python3 respond_to_chat.py {msg_id} 'your response'")
                        print(f"{'='*60}\n")
                
                # Export for website
                self.export_messages()
                
                time.sleep(3)
                
            except Exception as e:
                print(f"Error: {e}")
                time.sleep(5)

if __name__ == "__main__":
    bridge = ChatBridge()
    bridge.run()
