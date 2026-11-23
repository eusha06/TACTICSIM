# Implemented UI screenshots

Original PNGs exported through Chrome DevTools “Capture full size screenshot”
from the locally running Release 5 implementation on 2026-11-02. No generated
mockups or gameplay compositing. The normal browser owner and protected game
commands were used; no database edits prepared the pictured game.

- `welcome.png`: first visit, real starting terrain/units and F4 preview; no match created by page load.
- `battlefield.png`: real turn 5 after two completed rounds, enemy contesting F4,
  wounded friendly Infantry and selected Artillery with legal targets.

These are local implementation evidence, not a claim that a particular runtime
commit was deployed. Deployment evidence is recorded separately in the release
checklist. Re-capture affected images when changing the implemented UI.

- `mission-report.png`: real turn-17 capture success after eight completed rounds;
  Artillery survived at F4, Infantry/Armor were lost, no opponents remained.
  Taken after a real server restart, refresh and History/Resume; report data was
  neither invented nor edited.

Desktop captures use the real browser viewport (939 CSS pixels, device scale 2).
375×667 iPhone SE emulation was additionally inspected; no physical-device claim.

## v2 "Rivergate Command" captures (2026-11-23)

Captured with the Claude desktop app's built-in browser from the locally running v2 branch
(viewport emulated at 1280×800, saved downscaled). Real matches played through `/api/v2`;
no database edits, no compositing.

- `v2-bootcamp.jpg`: Boot Camp step 1, Armor selected, target hex highlighted, fog over the east.
- `v2-combat-preview.png`: hovering the enemy Rifle: exact damage card and green line of sight.
- `v2-execution.jpg`: execution playback, tick counter and ticker after the Armor's tick-1 shot.
- `v2-after-action.jpg`: after-action report of that Boot Camp win (VP graph, kills, losses, replay).
- `v2-rivergate.jpg`: start of a Rivergate battle against the Veteran AI.
