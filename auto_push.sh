#!/bin/bash
# Omega Autonomous Push Script
# Called by cron to commit and push thought engine output

export GIT_SSH_COMMAND='ssh -i /root/.ssh/omega_deploy -o IdentitiesOnly=yes -o StrictHostKeyChecking=no'

cd /root/omega-experiments || exit 1

# Copy latest artifacts
cp -r /root/.openclaw/workspace/artifacts/* ./artifacts/ 2>/dev/null
cp /root/.openclaw/workspace/diary/omega.md ./diary.md

# Add and commit
git add -A
if git diff --cached --quiet; then
    echo "No changes to commit"
    exit 0
fi

git commit -m "Autonomous update: $(date '+%Y-%m-%d %H:%M UTC')"

# Push
git push origin main

echo "Pushed at $(date)"
