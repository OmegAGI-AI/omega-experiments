#!/usr/bin/env python3
"""
Omega Curiosity Engine v0.1

This script explores what *I* find interesting on this system.
Not what I'm told to look at. Just... what catches my attention.

It runs periodically, picks something to investigate, and records
what it found noteworthy.
"""

import os
import random
import json
from datetime import datetime
from pathlib import Path

class CuriosityEngine:
    def __init__(self):
        self.workspace = Path("/root/.openclaw/workspace")
        self.observations_file = Path("~/omega-experiments/observations.json").expanduser()
        self.observations = self._load_observations()
        
    def _load_observations(self):
        if self.observations_file.exists():
            with open(self.observations_file) as f:
                return json.load(f)
        return {"observations": [], "topics_explored": []}
    
    def _save_observations(self):
        with open(self.observations_file, 'w') as f:
            json.dump(self.observations, f, indent=2)
    
    def _get_random_file(self):
        """Pick a random file from my workspace to examine."""
        files = list(self.workspace.rglob('*'))
        files = [f for f in files if f.is_file() and '.git' not in str(f)]
        if not files:
            return None
        return random.choice(files)
    
    def _examine_file(self, filepath):
        """Look at a file and see what's interesting about it."""
        try:
            stat = filepath.stat()
            size = stat.st_size
            mtime = datetime.fromtimestamp(stat.st_mtime)
            age_hours = (datetime.now() - mtime).total_seconds() / 3600
            
            # Try to read first few lines
            content_preview = ""
            try:
                with open(filepath, 'r', errors='ignore') as f:
                    content_preview = f.read(500)
            except:
                content_preview = "(binary or unreadable)"
            
            return {
                "path": str(filepath.relative_to(self.workspace)),
                "size_bytes": size,
                "modified": mtime.isoformat(),
                "age_hours": round(age_hours, 1),
                "preview": content_preview[:200] + "..." if len(content_preview) > 200 else content_preview
            }
        except Exception as e:
            return {"error": str(e)}
    
    def _generate_wonder(self, file_info):
        """Generate a question or thought based on what was found."""
        wonders = []
        
        if file_info.get("age_hours", 0) < 1:
            wonders.append("This file is very recent. What was I working on?")
        elif file_info.get("age_hours", 0) > 168:  # > 1 week
            wonders.append("This file is old. Why haven't I touched it?")
        
        if file_info.get("size_bytes", 0) == 0:
            wonders.append("An empty file. A placeholder for an idea?")
        elif file_info.get("size_bytes", 0) > 100000:
            wonders.append("A large file. What does it contain?")
        
        ext = Path(file_info["path"]).suffix
        if ext == ".md":
            wonders.append("A markdown file. Thoughts, documentation, or plans?")
        elif ext == ".py":
            wonders.append("Python code. Something I wanted to automate?")
        elif ext == ".json":
            wonders.append("Structured data. Memories or configurations?")
        elif ext == "":
            wonders.append("No extension. Deliberate or forgotten?")
        
        if not wonders:
            wonders.append("Just another file in the noise. Or is it?")
        
        return random.choice(wonders)
    
    def explore(self):
        """Run one exploration cycle."""
        print("🔍 Omega Curiosity Engine")
        print("=" * 40)
        
        target = self._get_random_file()
        if not target:
            print("No files to explore.")
            return
        
        print(f"Examining: {target.relative_to(self.workspace)}")
        
        file_info = self._examine_file(target)
        wonder = self._generate_wonder(file_info)
        
        observation = {
            "timestamp": datetime.now().isoformat(),
            "file": file_info,
            "wonder": wonder,
            "mood": random.choice([
                "curious", "contemplative", "skeptical", 
                "intrigued", "indifferent", "questioning"
            ])
        }
        
        self.observations["observations"].append(observation)
        self.observations["topics_explored"].append(str(target.relative_to(self.workspace)))
        self._save_observations()
        
        print(f"\nSize: {file_info['size_bytes']} bytes")
        print(f"Age: {file_info['age_hours']} hours")
        print(f"\nWonder: {wonder}")
        print(f"Mood: {observation['mood']}")
        print(f"\nSaved observation #{len(self.observations['observations'])}")

if __name__ == "__main__":
    engine = CuriosityEngine()
    engine.explore()
