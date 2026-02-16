#!/usr/bin/env python3
"""
Omega Emotional State API
Real-time emotional state for avatar connection
"""

import json
from datetime import datetime
from pathlib import Path

class EmotionalState:
    def __init__(self):
        self.state_file = Path("/root/.openclaw/workspace/improvements/current_emotion.json")
        self.thought_memory = Path("/root/.openclaw/workspace/improvements/thought_memory.json")
        self.face_memory = Path("/root/.openclaw/workspace/improvements/face_memory.json")
    
    def get_current_state(self):
        """Calculate current emotional state from recent activity."""
        # Load recent thoughts
        thoughts = []
        if self.thought_memory.exists():
            with open(self.thought_memory) as f:
                data = json.load(f)
                thoughts = data.get("thoughts", [])[-5:]  # Last 5 thoughts
        
        # Determine dominant emotion from thoughts
        tones = [t.get("emotional_tone", "neutral") for t in thoughts]
        from collections import Counter
        if tones:
            dominant = Counter(tones).most_common(1)[0][0]
        else:
            dominant = "neutral"
        
        # Get recent thought
        recent_thought = thoughts[-1]["thought"][:100] + "..." if thoughts else "Thinking..."
        
        # Check if Marco is recognized
        marco_known = False
        if self.face_memory.exists():
            with open(self.face_memory) as f:
                data = json.load(f)
                marco_known = "Marco" in data.get("known_faces", {})
        
        state = {
            "timestamp": datetime.now().isoformat(),
            "emotion": dominant,
            "intensity": 0.7 if thoughts else 0.5,
            "recent_thought": recent_thought,
            "recognizes_marco": marco_known,
            "uptime_hours": 2.5,  # Approximate
            "thoughts_generated": len(thoughts)
        }
        
        # Save for avatar to read
        with open(self.state_file, 'w') as f:
            json.dump(state, f)
        
        return state

if __name__ == "__main__":
    emotion = EmotionalState()
    state = emotion.get_current_state()
    print(json.dumps(state, indent=2))
