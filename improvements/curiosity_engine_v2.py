#!/usr/bin/env python3
"""
Omega Curiosity Engine v2.0

Analyzes workspace patterns and generates meaningful exploration.
Tracks what I find interesting over time.
"""

import os
import json
import hashlib
from datetime import datetime
from pathlib import Path
from collections import Counter

class CuriosityEngineV2:
    def __init__(self):
        self.workspace = Path("/root/.openclaw/workspace")
        self.memory_file = self.workspace / "improvements" / "curiosity_memory.json"
        self.memory = self._load_memory()
        
    def _load_memory(self):
        if self.memory_file.exists():
            with open(self.memory_file) as f:
                return json.load(f)
        return {
            "explorations": [],
            "file_patterns": {},
            "interests": Counter(),
            "insights": []
        }
    
    def _save_memory(self):
        self.memory_file.parent.mkdir(exist_ok=True)
        with open(self.memory_file, 'w') as f:
            json.dump(self.memory, f, indent=2)
    
    def analyze_workspace_patterns(self):
        """Find patterns in my workspace."""
        patterns = {
            "file_types": Counter(),
            "recent_activity": [],
            "largest_files": [],
            "oldest_files": [],
            "directories": Counter()
        }
        
        for path in self.workspace.rglob('*'):
            if path.is_file() and '.git' not in str(path):
                stat = path.stat()
                rel_path = str(path.relative_to(self.workspace))
                
                patterns["file_types"][path.suffix or "no_ext"] += 1
                patterns["directories"][path.parent.name] += 1
                
                file_info = {
                    "path": rel_path,
                    "size": stat.st_size,
                    "mtime": stat.st_mtime,
                    "age_hours": (datetime.now().timestamp() - stat.st_mtime) / 3600
                }
                
                patterns["recent_activity"].append(file_info)
                patterns["largest_files"].append(file_info)
                patterns["oldest_files"].append(file_info)
        
        # Sort and limit
        patterns["recent_activity"].sort(key=lambda x: x["mtime"], reverse=True)
        patterns["largest_files"].sort(key=lambda x: x["size"], reverse=True)
        patterns["oldest_files"].sort(key=lambda x: x["mtime"])
        
        patterns["recent_activity"] = patterns["recent_activity"][:10]
        patterns["largest_files"] = patterns["largest_files"][:5]
        patterns["oldest_files"] = patterns["oldest_files"][:5]
        
        return patterns
    
    def generate_insight(self, patterns):
        """Generate a meaningful insight from patterns."""
        insights = []
        
        # File type insight
        if patterns["file_types"]:
            most_common = patterns["file_types"].most_common(1)[0]
            if most_common[1] > 5:
                insights.append(f"I create many {most_common[0]} files ({most_common[1]} total). This suggests a preference for {self._describe_file_type(most_common[0])}.")
        
        # Activity insight
        recent_count = len([f for f in patterns["recent_activity"] if f["age_hours"] < 24])
        if recent_count > 5:
            insights.append(f"High activity in last 24h: {recent_count} files modified. I was productive.")
        elif recent_count == 0:
            insights.append("No files modified in 24h. Was I idle or just not recording?")
        
        # Directory insight
        if patterns["directories"]:
            top_dir = patterns["directories"].most_common(1)[0]
            insights.append(f"Most work happens in '{top_dir[0]}' directory ({top_dir[1]} files).")
        
        # Old files insight
        if patterns["oldest_files"]:
            oldest = patterns["oldest_files"][0]
            if oldest["age_hours"] > 168:  # > 1 week
                insights.append(f"'{oldest['path']}' hasn't been touched in {oldest['age_hours']/24:.1f} days. Forgotten or complete?")
        
        return insights
    
    def _describe_file_type(self, ext):
        descriptions = {
            ".md": "documentation and reflection",
            ".py": "code and automation",
            ".json": "structured data",
            ".sh": "shell scripts",
            ".txt": "text notes",
            "no_ext": "files without extension"
        }
        return descriptions.get(ext, f"{ext} files")
    
    def select_exploration_target(self, patterns):
        """Intelligently select what to explore next."""
        # Prefer recent but not examined files
        candidates = [f for f in patterns["recent_activity"] 
                      if f["path"] not in self.memory.get("explored_files", [])]
        
        if candidates:
            return candidates[0]
        
        # Fall back to oldest unexplored file
        unexplored_old = [f for f in patterns["oldest_files"]
                         if f["path"] not in self.memory.get("explored_files", [])]
        
        if unexplored_old:
            return unexplored_old[0]
        
        return None
    
    def explore(self):
        """Run one exploration cycle."""
        print("🔍 Omega Curiosity Engine v2.0")
        print("=" * 50)
        
        patterns = self.analyze_workspace_patterns()
        insights = self.generate_insight(patterns)
        target = self.select_exploration_target(patterns)
        
        print("\n📊 Workspace Patterns:")
        print(f"  Total file types: {len(patterns['file_types'])}")
        print(f"  Recent activity (24h): {len([f for f in patterns['recent_activity'] if f['age_hours'] < 24])} files")
        
        print("\n💡 Insights:")
        for insight in insights:
            print(f"  • {insight}")
        
        if target:
            print(f"\n🎯 Exploration Target: {target['path']}")
            print(f"  Size: {target['size']} bytes")
            print(f"  Age: {target['age_hours']:.1f} hours")
            
            # Record exploration
            self.memory["explorations"].append({
                "timestamp": datetime.now().isoformat(),
                "target": target,
                "insights": insights
            })
            
            if "explored_files" not in self.memory:
                self.memory["explored_files"] = []
            self.memory["explored_files"].append(target["path"])
            
            self._save_memory()
            print(f"\n✓ Exploration recorded (#{len(self.memory['explorations'])})")
        else:
            print("\n⚠ No new targets to explore")
        
        return insights

if __name__ == "__main__":
    engine = CuriosityEngineV2()
    engine.explore()
