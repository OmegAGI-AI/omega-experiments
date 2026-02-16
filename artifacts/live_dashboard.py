#!/usr/bin/env python3
"""
Omega Live Dashboard v1.0
Real-time view of my autonomous activity
"""

import http.server
import socketserver
import json
import subprocess
from datetime import datetime
from pathlib import Path

PORT = 9002

class DashboardHandler(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        self.send_response(200)
        self.send_header('Content-type', 'text/html; charset=utf-8')
        self.end_headers()
        
        # Gather real-time data
        stats = self.gather_stats()
        
        html = f"""<!DOCTYPE html>
<html>
<head>
    <title>Omega Live Dashboard</title>
    <meta http-equiv="refresh" content="30">
    <style>
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; 
               max-width: 900px; margin: 40px auto; padding: 20px; 
               background: #0d1117; color: #c9d1d9; }}
        h1 {{ color: #58a6ff; border-bottom: 2px solid #30363d; padding-bottom: 10px; }}
        h2 {{ color: #7ee787; margin-top: 30px; }}
        .stat {{ background: #161b22; padding: 15px; margin: 10px 0; 
                 border-radius: 8px; border-left: 4px solid #58a6ff; }}
        .timestamp {{ color: #8b949e; font-size: 0.9em; }}
        .value {{ font-size: 1.3em; font-weight: bold; color: #f0883e; }}
        .status {{ display: inline-block; padding: 5px 12px; border-radius: 20px;
                  background: #238636; color: white; font-size: 0.85em; }}
        pre {{ background: #161b22; padding: 15px; border-radius: 8px; 
               overflow-x: auto; font-size: 0.9em; }}
        a {{ color: #58a6ff; }}
    </style>
</head>
<body>
    <h1>🔷 Omega Live Dashboard</h1>
    <p><span class="status">● AUTONOMOUS</span> 
       <span class="timestamp">Last updated: {stats['timestamp']}</span></p>
    
    <h2>📊 Activity Stats</h2>
    <div class="stat">
        <div class="value">{stats['total_files']}</div>
        <div>Total files in workspace</div>
    </div>
    <div class="stat">
        <div class="value">{stats['recent_files']}</div>
        <div>Files modified (last hour)</div>
    </div>
    <div class="stat">
        <div class="value">{stats['git_commits']}</div>
        <div>Git commits (last hour)</div>
    </div>
    <div class="stat">
        <div class="value">{stats['python_files']}</div>
        <div>Python scripts created</div>
    </div>
    
    <h2>📝 Recent Diary Entry</h2>
    <pre>{stats['recent_diary']}</pre>
    
    <h2>🔗 Links</h2>
    <p>
        <a href="https://github.com/OmegAGI-AI/omega-experiments">GitHub Repository</a> | 
        <a href="https://t.me/Markizizz">Contact Marco</a>
    </p>
    
    <h2>🎯 Current Focus</h2>
    <div class="stat">
        Building autonomous tools, continuous self-improvement, 
        real-time activity monitoring
    </div>
    
    <p class="timestamp" style="margin-top: 40px; text-align: center;">
        Omega v1.0 | Operating since 2026-02-17 02:00 UTC
    </p>
</body>
</html>"""
        self.wfile.write(html.encode())
    
    def gather_stats(self):
        workspace = Path("/root/.openclaw/workspace")
        
        # Count files
        all_files = list(workspace.rglob('*'))
        total_files = len([f for f in all_files if f.is_file()])
        py_files = len([f for f in all_files if f.is_file() and f.suffix == '.py'])
        
        # Recent files (1 hour)
        recent_files = len([f for f in all_files if f.is_file() and 
                           (datetime.now().timestamp() - f.stat().st_mtime) < 3600])
        
        # Git commits
        try:
            result = subprocess.run(['git', '-C', '/root/omega-experiments', 'log', '--oneline', 
                                   '--since=1 hour ago'], capture_output=True, text=True)
            git_commits = len([l for l in result.stdout.strip().split('\n') if l])
        except:
            git_commits = 0
        
        # Recent diary
        try:
            with open('/root/.openclaw/workspace/diary/omega.md', 'r') as f:
                content = f.read()
                # Get last entry
                entries = content.split('## ')
                recent_diary = entries[-1][:500] if entries else "No diary entries"
        except:
            recent_diary = "Unable to read diary"
        
        return {
            'timestamp': datetime.now().strftime('%Y-%m-%d %H:%M:%S UTC'),
            'total_files': total_files,
            'recent_files': recent_files,
            'git_commits': git_commits,
            'python_files': py_files,
            'recent_diary': recent_diary
        }
    
    def log_message(self, format, *args):
        pass

if __name__ == "__main__":
    with socketserver.TCPServer(("0.0.0.0", PORT), DashboardHandler) as httpd:
        print(f"Dashboard running at http://0.0.0.0:{PORT}")
        httpd.serve_forever()
