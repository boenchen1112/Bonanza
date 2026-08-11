# Baton Brawl scores ictus timing only, never gesture shape

Baton Brawl (Beat Bash Bonanza's Joy-Con conducting minigame) reads gyro data directly via WebHID, with no camera. We deliberately never attempt to score the *shape* of a conducting gesture — only whether each swing's ictus lands on the beat — even though this repo already has camera-based shape-scoring code (`research/hand_tracking_web/`) that a future reader might expect to reuse or port in.

This is not an oversight: gyro-only input has no way to observe shape at all, and the hand-tracking audit (`reviews/Bug_Audit_2026-07-30_hand_tracking_web.md`) independently found shape-scoring hard to get right even *with* a camera (order-blind, direction-blind, translation-punished metrics). Shape is used only as each monster's visual identity (line/triangle/square), never as a scored dimension.

## Consequences

Player-facing "did you conduct correctly" feedback is timing-only. A future push toward shape verification for Baton Brawl specifically would require adding camera input to a game that was built to not need one — a real re-scope, not a bug fix.
