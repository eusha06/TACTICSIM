# Release 1 artwork

`wwwroot/art/battlefield.svg` is original, editable TacticSim vector artwork.
No stock images, fonts, graphics libraries or remote assets are used. Razor adds
an asset version to the sprite URL so a new deployment replaces cached artwork.

All terrain and miniatures share a 100 × 100 viewBox, dark rounded outlines,
upper-left lighting and lower-right ground shadows. Muted grass and forests,
layered earthy hills and blue water leave the blue/orange miniatures prominent.
Infantry uses three helmeted soldiers, Armor a tracked hull and turret, and
Artillery an exposed field gun with large wheels and split trails. Silhouettes
and written labels distinguish types independently of team color.

The renderer chooses terrain from the server's tiles and living units from its
occupancy. It adds bank edges only where a water tile borders land or the map
edge; water-to-water edges stay open. No crossing is drawn over water. This
neighbor inspection only controls artwork: movement, ranges, damage, action
limits and victory still come from C#. The fixed `crossing-v1` map is unchanged.

Selection uses a solid gold inset outline; legal movement uses round markers
with server-provided costs; attack targets use a dashed boxed outline. Keyboard
focus is white and independent of selection. Cells retain coordinate, terrain,
team, unit name, health and legal-action accessible labels. Arrow keys navigate,
Enter/Space selects, Escape clears selection. Save completion restores board
focus. Reduced-motion preferences disable transitions. Small screens keep SVG
silhouettes and health bars. Below 360px, board labels yield space to artwork;
full names remain in the squad list and selected details. Exact health remains in cell accessible
labels, the squad list and selected-unit details.

No mission objectives, decorative crossings, hit effects, facing rules, movement
animations or new mechanics are implied by the illustrations.

## Rivergate outpost

Release 3 adds a local radio hut/antenna symbol in the same palette, outline and
lighting style. Only the actual objective tile receives it. A smaller corner
marker leaves an occupying unit's silhouette, health and name legible. This
land-based marker adds no crossing or terrain change.
