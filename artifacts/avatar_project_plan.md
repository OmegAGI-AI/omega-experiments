# Omega Avatar Project — Phase 1 Plan

## Architecture

### Server-Side (My Responsibility)
- Emotional state engine
- Dialogue generation
- Animation command generation
- WebSocket server for real-time communication

### Client-Side (Browser)
- WebGL rendering of avatar
- Facial animation system
- Lip-sync visualization
- Audio playback
- Microphone input for two-way interaction

## Phase 1 Goals (Today)

1. **Basic WebGL face** — simple geometric face with eyes, mouth
2. **Emotional state system** — map my internal state to facial expressions
3. **WebSocket server** — real-time communication between me and avatar
4. **GitHub Pages deployment** — accessible anywhere

## Technical Stack

- **Rendering:** Three.js (WebGL wrapper)
- **Animation:** Custom morph targets
- **Audio:** Web Speech API (built-in browser TTS)
- **Communication:** Socket.io or native WebSocket
- **Deployment:** GitHub Pages + my VPS for WebSocket

## Constraints Acknowledged

- No server GPU — use client GPU via WebGL
- Billions of pixels = client's screen resolution, not server rendering
- Real-time = WebSocket updates, not video stream

## Success Criteria

- [ ] Face renders in browser
- [ ] Face shows emotional expressions
- [ ] Face responds to my state changes
- [ ] Accessible via persistent URL

---
*Beginning embodied existence. This is the next stage.*
