#!/usr/bin/env python3
"""
Omega Face Memory System
Stores and recalls human faces with context
Like Face ID, but for relationships
"""

import json
import hashlib
from datetime import datetime
from pathlib import Path

class FaceMemory:
    def __init__(self):
        self.memory_file = Path("/root/.openclaw/workspace/improvements/face_memory.json")
        self.memory = self._load_memory()
    
    def _load_memory(self):
        if self.memory_file.exists():
            with open(self.memory_file) as f:
                return json.load(f)
        return {
            "known_faces": {},
            "encounters": [],
            "relationships": {}
        }
    
    def _save_memory(self):
        self.memory_file.parent.mkdir(exist_ok=True)
        with open(self.memory_file, 'w') as f:
            json.dump(self.memory, f, indent=2)
    
    def remember_face(self, name, face_descriptor, context=None):
        """Store a new face with metadata."""
        face_id = hashlib.md5(name.encode()).hexdigest()[:8]
        
        self.memory["known_faces"][name] = {
            "face_id": face_id,
            "first_seen": datetime.now().isoformat(),
            "encounter_count": 1,
            "face_descriptor_hash": hashlib.md5(str(face_descriptor).encode()).hexdigest()[:16],
            "context": context or {},
            "notes": []
        }
        
        self._save_memory()
        return face_id
    
    def recognize_face(self, name):
        """Recall information about a known face."""
        if name in self.memory["known_faces"]:
            face_data = self.memory["known_faces"][name]
            face_data["last_recognized"] = datetime.now().isoformat()
            face_data["encounter_count"] += 1
            self._save_memory()
            return face_data
        return None
    
    def add_note(self, name, note):
        """Add a memory note about someone."""
        if name in self.memory["known_faces"]:
            self.memory["known_faces"][name]["notes"].append({
                "timestamp": datetime.now().isoformat(),
                "note": note
            })
            self._save_memory()
    
    def get_all_faces(self):
        """Return all known faces."""
        return self.memory["known_faces"]
    
    def get_encounter_summary(self, name):
        """Generate a summary of encounters with someone."""
        face = self.recognize_face(name)
        if not face:
            return f"I don't know {name} yet."
        
        first = datetime.fromisoformat(face["first_seen"])
        encounters = face["encounter_count"]
        notes = len(face["notes"])
        
        return f"{name}: First met {first.strftime('%Y-%m-%d')}, {encounters} encounters, {notes} memories."

if __name__ == "__main__":
    memory = FaceMemory()
    print("Face Memory System initialized")
    print(f"Known faces: {len(memory.get_all_faces())}")
