# CloudLens keynote film v3 - build report

- Output: `cloudlens-keynote-v3-1080p.mp4` - 1920x1080, 60 fps, H.264, AAC 256k, 112.9 s (1:53, under the 2:00 cap).
- Loudness: -14.0 LUFS integrated (target), true peak -1.2 dBTP.
- Metadata: title "CloudLens keynote film"; no author or other tags. Subtitles are NOT embedded; the `.srt` is a sidecar.
- Voice: Azure Speech `en-US-Andrew:DragonHDLatestNeural`, 23 lines at the brief's anchors (shifted at most 0.9 s to avoid overlaps).
- Score: original, procedurally synthesised (numpy), 110 BPM, cue-mapped to the shot list. No third-party audio.

## Deviations from the brief
1. 1080p60 instead of 4K (render time; all sizes were scaled by half).
2. Built from the existing product screenshots; the brief's concept screens (ranking, policy, insight card, work item, GitHub issue) are rendered as designed UI.
3. Placeholder values: PERIOD "this quarter", SCORE 92. Real values from the dashboard: 499 tracked, 63 retirements, 15 urgent. Deadline 2026-09-17 and 7 clusters / 4 regions / 1 subscription come from the real hero item.
4. Wiki page title shows "Azure Service Health" (real screenshot) rather than "Azure Health & Retirements"; Teams channel in the screenshot is "Incidents" rather than "#app-team-alerts".
5. Teams / wiki screenshot crops redact GUIDs and an author line.

## Capability status
- Shown as shipped: sources, understanding, grounding (estate map), impact/calendar, Teams, Azure DevOps Wiki.
- Roadmap, with a "Coming soon" chip: subscription ranking (Top N), Azure Policy auto-registration.
- Concept visuals: insight card, Azure DevOps work item, GitHub issue.
- End card carries "Some capabilities shown are in development and subject to change."
- ServiceNow is spoken in the VO and in the `.srt` only; never on screen.

## Rule 0 gate (no real names)
Source, captions, VO text, srt, file names and metadata scanned for customer, org and personal names: no hits. On-screen names are limited to the allow-list (sub-app-01..08, App team A, demo-org/app-a, Azure regions).
Not used: `impact-resources.png` and `health.png` (contain real resource and subscription names).
