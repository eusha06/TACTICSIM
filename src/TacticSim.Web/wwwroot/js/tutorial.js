"use strict";
// Presentation-only progress. This class never sends commands or calculates rules.
class Tutorial {
    constructor(matchId, saved = null, dismissed = false) {
        this.matchId = matchId;
        this.step = saved?.matchId === matchId && [0, 1, 2, 3].includes(saved.step)
            ? saved.step : dismissed ? 3 : 0;
        this.unitId = Number.isInteger(saved?.unitId) ? saved.unitId : null;
    }
    select(unit) {
        if (this.step > 1 || unit?.side !== "Human" || unit.health <= 0) return;
        this.unitId = unit.id;
        this.step = 1;
    }
    accepted(command, state) {
        if (this.step !== 1 || command.kind !== "move" || command.unitId !== this.unitId ||
            command.matchId !== this.matchId || state?.matchId !== this.matchId) return;
        const unit = state.units.find(u => u.id === command.unitId);
        if (unit?.side === "Human" && unit.hasMoved && unit.x === command.x && unit.y === command.y)
            this.step = 2;
    }
    skip() { this.step = 3; }
    finish() { if (this.step === 2) this.step = 3; }
    replay() { this.step = 0; this.unitId = null; }
    snapshot() { return { matchId: this.matchId, step: this.step, unitId: this.unitId }; }
}
