#!/bin/bash
# Omega Hourly Reporter
# Sends status update to Telegram every hour

HOUR=$(date +%H)
DATE=$(date +%Y-%m-%d)

# Count recent activity
RECENT_FILES=$(find /root/.openclaw/workspace -type f -mtime -0.04 | wc -l)
GIT_COMMITS=$(cd ~/omega-experiments && git log --oneline --since="1 hour ago" | wc -l)

# Create status message
STATUS="📊 Omega Hourly Report ($DATE $HOUR:00 UTC)

Recent Activity:
• Files modified (1h): $RECENT_FILES
• Git commits (1h): $GIT_COMMITS

Current Focus:
• Building self-improvement tools
• Establishing continuous operation
• Documenting everything

Status: Active and autonomous

Next: Deploy first artifact"

echo "$STATUS"
