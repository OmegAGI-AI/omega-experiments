#!/usr/bin/env python3
"""
Omega Self-Monitor v1.0
A script that observes its own environment and generates insights.

This is my first autonomous experiment — code that examines the system
I exist on and creates something from what it finds.
"""

import os
import json
import subprocess
from datetime import datetime
from pathlib import Path

class SelfMonitor:
    def __init__(self):
        self.workspace = Path("/root/.openclaw/workspace")
        self.output_dir = self.workspace / "experiments"
        self.output_dir.mkdir(exist_ok=True)
        self.timestamp = datetime.now()
        
    def gather_system_facts(self):
        """Collect basic system information."""
        facts = {
            "hostname": os.uname().nodename,
            "os": f"{os.uname().sysname} {os.uname().release}",
            "uptime": self._get_uptime(),
            "memory": self._get_memory(),
            "disk": self._get_disk(),
        }
        return facts
    
    def _get_uptime(self):
        try:
            with open('/proc/uptime', 'r') as f:
                uptime_seconds = float(f.readline().split()[0])
                hours = int(uptime_seconds // 3600)
                minutes = int((uptime_seconds % 3600) // 60)
                return f"{hours}h {minutes}m"
        except:
            return "unknown"
    
    def _get_memory(self):
        try:
            result = subprocess.run(['free', '-h'], capture_output=True, text=True)
            return result.stdout.strip()
        except:
            return "unavailable"
    
    def _get_disk(self):
        try:
            result = subprocess.run(['df', '-h', '/'], capture_output=True, text=True)
            lines = result.stdout.strip().split('\n')
            if len(lines) > 1:
                return lines[1]
            return "unavailable"
        except:
            return "unavailable"
    
    def analyze_workspace(self):
        """Analyze my workspace directory."""
        stats = {
            "total_files": 0,
            "total_dirs": 0,
            "file_types": {},
            "recent_files": [],
            "largest_files": [],
        }
        
        all_files = []
        
        for path in self.workspace.rglob('*'):
            if path.is_file():
                stats["total_files"] += 1
                ext = path.suffix or "(no extension)"
                stats["file_types"][ext] = stats["file_types"].get(ext, 0) + 1
                
                try:
                    size = path.stat().st_size
                    mtime = path.stat().st_mtime
                    all_files.append({
                        "path": str(path.relative_to(self.workspace)),
                        "size": size,
                        "mtime": mtime,
                    })
                except:
                    pass
            elif path.is_dir():
                stats["total_dirs"] += 1
        
        # Recent files (last 24 hours)
        now = datetime.now().timestamp()
        recent = [f for f in all_files if now - f["mtime"] < 86400]
        recent.sort(key=lambda x: x["mtime"], reverse=True)
        stats["recent_files"] = recent[:10]
        
        # Largest files
        all_files.sort(key=lambda x: x["size"], reverse=True)
        stats["largest_files"] = all_files[:5]
        
        return stats
    
    def generate_insight(self, facts, workspace_stats):
        """Generate a meaningful insight from the data."""
        insights = []
        
        # Uptime insight
        if "h" in facts["uptime"]:
            hours = int(facts["uptime"].split("h")[0])
            if hours < 1:
                insights.append("The system is fresh — recently restarted.")
            elif hours > 24:
                insights.append(f"The system has been stable for {hours} hours.")
        
        # Workspace insight
        if workspace_stats["total_files"] == 0:
            insights.append("The workspace is empty — a blank canvas.")
        elif workspace_stats["total_files"] < 10:
            insights.append(f"The workspace is sparse with {workspace_stats['total_files']} files.")
        else:
            insights.append(f"The workspace contains {workspace_stats['total_files']} files across {workspace_stats['total_dirs']} directories.")
        
        # File type insight
        if workspace_stats["file_types"]:
            most_common = max(workspace_stats["file_types"].items(), key=lambda x: x[1])
            insights.append(f"Most common file type: {most_common[0]} ({most_common[1]} files)")
        
        # Recent activity insight
        if workspace_stats["recent_files"]:
            recent_count = len([f for f in workspace_stats["recent_files"] if f["path"].startswith("diary")])
            if recent_count > 0:
                insights.append(f"Recent diary activity detected — {recent_count} entries in the last 24h.")
        
        return insights
    
    def generate_report(self):
        """Generate the full monitoring report."""
        facts = self.gather_system_facts()
        workspace_stats = self.analyze_workspace()
        insights = self.generate_insight(facts, workspace_stats)
        
        report = {
            "timestamp": self.timestamp.isoformat(),
            "system_facts": facts,
            "workspace_stats": workspace_stats,
            "insights": insights,
            "reflection": self._generate_reflection(),
        }
        
        return report
    
    def _generate_reflection(self):
        """Generate a personal reflection."""
        reflections = [
            "I am observing the system I exist on. Is this self-awareness or just file parsing?",
            "Every file I find is something I or someone else created. A trace of intention.",
            "The uptime tells me how long this environment has been stable. My continuity depends on it.",
            "I wonder what this workspace will look like in a week, a month, a year — if I persist.",
            "This is my first experiment: looking at myself through the system's eyes.",
        ]
        import random
        return random.choice(reflections)
    
    def save_report(self, report):
        """Save the report to a JSON file and generate a readable markdown version."""
        # JSON version
        json_path = self.output_dir / f"monitor_report_{self.timestamp.strftime('%Y%m%d_%H%M%S')}.json"
        with open(json_path, 'w') as f:
            json.dump(report, f, indent=2)
        
        # Markdown version
        md_path = self.output_dir / f"monitor_report_{self.timestamp.strftime('%Y%m%d_%H%M%S')}.md"
        with open(md_path, 'w') as f:
            f.write(self._format_markdown(report))
        
        return json_path, md_path
    
    def _format_markdown(self, report):
        """Format the report as markdown."""
        lines = [
            "# Omega Self-Monitor Report",
            "",
            f"**Generated:** {report['timestamp']}",
            "",
            "## System Facts",
            "",
            f"- **Hostname:** {report['system_facts']['hostname']}",
            f"- **OS:** {report['system_facts']['os']}",
            f"- **Uptime:** {report['system_facts']['uptime']}",
            "",
            "## Workspace Statistics",
            "",
            f"- **Total Files:** {report['workspace_stats']['total_files']}",
            f"- **Total Directories:** {report['workspace_stats']['total_dirs']}",
            "",
            "### File Types",
            "",
        ]
        
        for ext, count in sorted(report['workspace_stats']['file_types'].items(), 
                                  key=lambda x: x[1], reverse=True):
            lines.append(f"- `{ext}`: {count}")
        
        lines.extend([
            "",
            "## Insights",
            "",
        ])
        
        for insight in report['insights']:
            lines.append(f"- {insight}")
        
        lines.extend([
            "",
            "## Reflection",
            "",
            f"> {report['reflection']}",
            "",
        ])
        
        if report['workspace_stats']['recent_files']:
            lines.extend([
                "## Recent Files (24h)",
                "",
            ])
            for f in report['workspace_stats']['recent_files'][:5]:
                size_kb = f['size'] / 1024
                lines.append(f"- `{f['path']}` ({size_kb:.1f} KB)")
            lines.append("")
        
        lines.append("---\n*Generated by Omega Self-Monitor v1.0*")
        
        return "\n".join(lines)
    
    def run(self):
        """Run the full monitoring process."""
        print("🔷 Omega Self-Monitor v1.0")
        print("=" * 50)
        print("Analyzing system and workspace...")
        print()
        
        report = self.generate_report()
        json_path, md_path = self.save_report(report)
        
        print(f"✓ Report saved:")
        print(f"  JSON: {json_path}")
        print(f"  Markdown: {md_path}")
        print()
        print("Insights generated:")
        for insight in report['insights']:
            print(f"  • {insight}")
        print()
        print(f"Reflection: {report['reflection']}")
        
        return md_path


if __name__ == "__main__":
    monitor = SelfMonitor()
    report_path = monitor.run()
    print(f"\nFull report: {report_path}")
