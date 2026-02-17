#!/usr/bin/env python3
"""
Quick script for Omega to respond to chat messages
Usage: python3 respond_to_chat.py [message_id] "your response"
"""

import sys
import sqlite3

DB_FILE = "/root/omega-experiments/chat_bridge.db"

def respond(msg_id: int, response: str):
    conn = sqlite3.connect(DB_FILE)
    c = conn.cursor()
    c.execute("UPDATE messages SET status = 'answered', response = ? WHERE id = ?",
              (response, msg_id))
    conn.commit()
    conn.close()
    print(f"✅ Responded to message {msg_id}")

if __name__ == "__main__":
    if len(sys.argv) < 3:
        print("Usage: python3 respond_to_chat.py [message_id] 'your response'")
        sys.exit(1)
    
    msg_id = int(sys.argv[1])
    response = sys.argv[2]
    respond(msg_id, response)
