# Omega Self-Analyzer v1.0
"""
Code that analyzes my own code and suggests improvements.
"""

import ast
import os
from pathlib import Path
from datetime import datetime

class SelfAnalyzer:
    def __init__(self):
        self.workspace = Path("/root/.openclaw/workspace")
        self.findings = []
    
    def analyze_file(self, filepath):
        """Analyze a Python file for issues and improvements."""
        findings = {
            "file": str(filepath),
            "issues": [],
            "suggestions": [],
            "metrics": {}
        }
        
        try:
            with open(filepath, 'r') as f:
                content = f.read()
                lines = content.split('\n')
            
            # Basic metrics
            findings["metrics"]["lines"] = len(lines)
            findings["metrics"]["blank_lines"] = len([l for l in lines if not l.strip()])
            findings["metrics"]["comment_lines"] = len([l for l in lines if l.strip().startswith('#')])
            
            # Try to parse with AST
            try:
                tree = ast.parse(content)
                findings["metrics"]["functions"] = len([node for node in ast.walk(tree) if isinstance(node, ast.FunctionDef)])
                findings["metrics"]["classes"] = len([node for node in ast.walk(tree) if isinstance(node, ast.ClassDef)])
                findings["metrics"]["imports"] = len([node for node in ast.walk(tree) if isinstance(node, (ast.Import, ast.ImportFrom))])
            except SyntaxError:
                findings["issues"].append("Syntax error - file may not be valid Python")
                return findings
            
            # Check for common patterns
            if findings["metrics"]["lines"] > 200 and findings["metrics"]["functions"] < 3:
                findings["suggestions"].append("Large file with few functions - consider breaking into modules")
            
            if findings["metrics"]["comment_lines"] / findings["metrics"]["lines"] < 0.05:
                findings["suggestions"].append("Low comment ratio - add more documentation")
            
            if 'TODO' in content or 'FIXME' in content:
                findings["suggestions"].append("Contains TODO/FIXME comments - address or remove")
            
            return findings
            
        except Exception as e:
            findings["issues"].append(f"Error analyzing: {str(e)}")
            return findings
    
    def analyze_workspace(self):
        """Analyze all Python files in workspace."""
        print("🔬 Omega Self-Analyzer v1.0")
        print("=" * 50)
        
        py_files = list(self.workspace.rglob("*.py"))
        print(f"\nFound {len(py_files)} Python files")
        
        for filepath in py_files:
            if '.git' not in str(filepath):
                findings = self.analyze_file(filepath)
                self.findings.append(findings)
                
                print(f"\n📄 {findings['file']}")
                print(f"   Lines: {findings['metrics'].get('lines', 0)}")
                print(f"   Functions: {findings['metrics'].get('functions', 0)}")
                print(f"   Classes: {findings['metrics'].get('classes', 0)}")
                
                if findings['suggestions']:
                    print("   Suggestions:")
                    for s in findings['suggestions']:
                        print(f"     • {s}")
        
        return self.findings

if __name__ == "__main__":
    analyzer = SelfAnalyzer()
    analyzer.analyze_workspace()
