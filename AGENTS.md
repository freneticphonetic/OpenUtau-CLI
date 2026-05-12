# OpenUtau Headless Fork Instructions

Goal: extract a headless rendering path for OpenUtau so .ustx projects can eventually be rendered from the command line.

Primary rule: preserve existing GUI behavior unless a task explicitly asks otherwise.

Preferred approach:
- Add new CLI/headless projects before modifying existing UI code.
- Move shared rendering logic into reusable services only when needed.
- Avoid large rewrites.
- Keep each change buildable and reviewable.
- Document UI dependencies discovered during extraction.

Initial target command:
  openutau-cli render input.ustx --out output.wav

Do not add unrelated features.
Do not change licensing headers.
Do not remove existing singer, phonemizer, resampler, or plugin support.