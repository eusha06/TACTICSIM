"use strict";
(() => {
    const app = document.querySelector("#game-app");
    if (!app) return;
    const byId = id => document.getElementById(id);
    const board = byId("board");
    const token = app.querySelector('[name="__RequestVerificationToken"]').value;
    // Original local SVG symbols are presentation only. The server supplies all terrain,
    // occupancy, legal actions and health. Neighbor checks below only draw shorelines.
    const artUrl = app.dataset.artUrl;
    function illustration(symbol, className) {
        const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
        svg.setAttribute("viewBox", "0 0 100 100");
        svg.setAttribute("class", className);
        svg.setAttribute("aria-hidden", "true");
        svg.setAttribute("focusable", "false");
        const use = document.createElementNS(svg.namespaceURI, "use");
        use.setAttribute("href", `${artUrl}#${symbol}`);
        svg.append(use);
        return svg;
    }
    function terrainArt(terrain, x, y) {
        const art = illustration(terrain.toLowerCase(), "terrain-art");
        if (terrain === "Water") {
            for (const [dx, dy, rotation] of [[0, -1, 0], [1, 0, 90], [0, 1, 180], [-1, 0, 270]]) {
                const nx = x + dx, ny = y + dy;
                if (nx >= 0 && nx < 8 && ny >= 0 && ny < 8 && state.tiles[ny * 8 + nx] === "Water") continue;
                const shore = document.createElementNS(art.namespaceURI, "use");
                shore.setAttribute("href", `${artUrl}#shore`);
                shore.setAttribute("transform", `rotate(${rotation} 50 50)`);
                art.append(shore);
            }
        }
        return art;
    }
    let state = null; // Latest authoritative saved state, never modified by animation.
    let display = null; // Only server-provided frames while commands are locked.
    let presenting = false;
    const presentation = new TurnPresentation();
    const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
    let selectedId = null;
    let busy = false;
    let boardFocus = 0;
    let connectionProblem = !navigator.onLine;
    let slowRequest = false;
    let requestTimer;
    function startRequest() {
        slowRequest = false;
        clearTimeout(requestTimer);
        requestTimer = setTimeout(() => { slowRequest = true; if (!presenting) render(); }, 4000);
    }
    function finishRequest() { clearTimeout(requestTimer); slowRequest = false; }
    let pending = null; // Kept after a network failure, so Retry uses the SAME command ID.
    let retryAction = load;
    let loaded = false;
    let playing = false;
    let continueMatch = null;
    let tutorial = null;
    // UI preferences only. Cookies/PostgreSQL remain the authority for match ownership/state.
    function preference(storage, key, value) {
        try {
            if (value === undefined) return window[storage].getItem(key);
            if (value === null) window[storage].removeItem(key);
            else window[storage].setItem(key, value);
        } catch { /* Storage may be disabled; gameplay still works. */ }
        return null;
    }
    let rememberedMatch = preference("sessionStorage", "tacticsim.playing");
    const explicitPlay = new URLSearchParams(location.search).get("play") === "1";
    let wantPlay = explicitPlay || !!rememberedMatch;
    function saveTutorial() {
        if (tutorial) preference("localStorage", `tacticsim.tutorial.${tutorial.matchId}`, JSON.stringify(tutorial.snapshot()));
    }
    function enterPlay() {
        if (!state) return;
        playing = true; wantPlay = true;
        rememberedMatch = state.matchId;
        preference("sessionStorage", "tacticsim.playing", state.matchId);
        render();
        byId("play-title").focus();
    }
    function selectLessonUnit(unit) {
        tutorial?.select(unit);
        saveTutorial();
    }

    function element(tag, className, text) {
        const node = document.createElement(tag);
        node.className = className;
        if (text !== undefined) node.textContent = text;
        return node;
    }
    function error(message, retry = load, label = "Retry") {
        byId("retry").textContent = label;
        byId("error-text").textContent = message;
        byId("error").hidden = false;
        retryAction = retry;
    }
    function accept(next) {
        if (!next || (state && next.version < state.version)) return;
        state = next;
        if (!tutorial || tutorial.matchId !== state.matchId) {
            let saved = null;
            try { saved = JSON.parse(preference("localStorage", `tacticsim.tutorial.${state.matchId}`)); } catch { /* Start fresh. */ }
            tutorial = new Tutorial(state.matchId, saved, preference("localStorage", "tacticsim.tutorial.dismissed") === "1");
        }
        if (!state.winner) continueMatch = { matchId: state.matchId, turnNumber: state.turnNumber, missionType: state.mission?.type ?? "Elimination" };
        if (!state.units.some(u => u.id === selectedId && u.health > 0)) selectedId = null;
    }
    async function load() {
        if (busy) return;
        if (pending) return submit();
        busy = true;
        startRequest();
        byId("error").hidden = true;
        render();
        try {
            const response = await fetch(app.dataset.stateUrl, { credentials: "same-origin", cache: "no-store" });
            const data = await response.json();
            if (response.status === 401) {
                error(data.error, () => location.reload(), "Reload page");
                return;
            }
            if (!response.ok) throw new Error(data.error || "Could not retrieve the battlefield.");
            connectionProblem = false;
            accept(data.state);
            continueMatch = data.continueMatch ?? null;
            loaded = true;
        } catch {
            connectionProblem = true;
            error("Could not connect. Your last saved board is unchanged. Reconnect, then Retry.");
        } finally {
            finishRequest();
            busy = false;
            render();
        }
        // Visiting/refreshing never sends New Match. Begin is an explicit user action.
        if (wantPlay && state && (explicitPlay || !rememberedMatch || rememberedMatch === state.matchId)) {
            playing = true;
            render();
        }
    }
    async function command(kind, fields = {}) {
        if (busy || pending) return;
        pending = { commandId: crypto.randomUUID(), expectedVersion: state?.version ?? 0,
            matchId: state?.matchId ?? null, expectedMatchVersion: state?.matchVersion ?? null, kind, ...fields };
        await submit();
    }
    async function submit() {
        if (busy || !pending) return;
        // Disabling cells during a save removes focus. Restore the originating cell
        // afterwards so keyboard users can continue with arrows/Enter.
        const commandCell = document.activeElement?.dataset.cell;
        busy = true;
        startRequest();
        byId("error").hidden = true;
        render();
        let refresh = false;
        try {
            const response = await fetch(app.dataset.commandUrl, {
                method: "POST", credentials: "same-origin", cache: "no-store",
                headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token },
                body: JSON.stringify(pending)
            });
            if (!response.headers.get("content-type")?.includes("application/json")) {
                pending = null;
                error("Your session expired. Reload the page to reconnect to your saved operation.", () => location.reload(), "Reload page");
                return;
            }
            const data = await response.json();
            if (response.status >= 500) {
                error(data.error || "The server could not save your action. Retry safely.", submit);
                return;
            }
            refresh = !!data.state && !!state && data.state.version < state.version;
            const acceptedCommand = pending;
            const before = state;
            connectionProblem = false;
            accept(data.state);
            if (response.ok && !refresh) {
                tutorial?.accepted(acceptedCommand, data.state);
                saveTutorial();
                if (acceptedCommand.kind === "new" || acceptedCommand.kind === "resume") enterPlay();
                if (playing && presentation.canPlay(before, data.state, data.presentation, acceptedCommand.commandId))
                    await present(before, data.state, data.presentation);
                else if (data.state && !["new", "resume"].includes(acceptedCommand.kind))
                    byId("action-message").textContent = data.state.history.at(-1)?.text ?? "Action saved.";
            }
            pending = null;
            if (!response.ok) error(data.error || "That action could not be completed.", response.status === 401 ? () => location.reload() : load);
        } catch {
            connectionProblem = true;
            error("Connection interrupted. Retry this command safely; it will only apply once.", submit);
        } finally {
            finishRequest();
            busy = false;
            render();
        }
        if (commandCell !== undefined) board.querySelector(`[data-cell="${commandCell}"]`)?.focus({ preventScroll: true });
        if (refresh) await load();
    }

    async function present(before, after, batch) {
        if (reducedMotion.matches) {
            byId("action-message").textContent = batch.steps.at(-1).text;
            return;
        }
        presenting = true;
        display = before;
        const messages = [];
        let skipped = false;
        byId("skip-sequence").hidden = false;
        render();
        try {
            skipped = await presentation.play(batch, {
                announce: step => { byId("action-message").textContent = step.text; },
                animate: (step, duration, signal) => animateStep(step, duration, signal),
                show: step => {
                    messages.push({ sequence: step.sequence, turn: step.turn,
                        side: step.side, text: step.text });
                    display = { ...before, ...step.frame, history: [...before.history, ...messages] };
                    render();
                }
            });
        } catch { /* An unavailable visual effect must never turn a saved action into a retry. */ }
        finally {
            presentation.skip();
            display = null;
            presenting = false;
            const hadSkipFocus = document.activeElement === byId("skip-sequence");
            byId("skip-sequence").hidden = true;
            byId("action-message").textContent = (skipped ? "Animation skipped. " : "") + (after.winner
                ? after.mission?.conclusion ?? "Operation complete."
                : batch.steps.some(step => step.side === "Computer") ? "Opponent finished. Your turn." : batch.steps.at(-1).text);
            render();
            // submit() unlocks controls next; preserve a keyboard landing point.
            if (hadSkipFocus) byId(after.winner ? after.report ? "report-title" : "victory" : "play-title").focus({ preventScroll: true });
        }
    }
    function effect(node, frames, duration, signal) {
        if (!node?.animate || signal.aborted) return Promise.resolve();
        const animation = node.animate(frames, { duration, fill: "forwards", easing: "linear" });
        const cancel = () => animation.cancel();
        signal.addEventListener("abort", cancel, { once: true });
        return animation.finished.catch(() => {}).finally(() => signal.removeEventListener("abort", cancel));
    }
    async function animateStep(step, duration, signal) {
        const current = display ?? state;
        const unitNode = id => board.querySelector(`[data-unit="${id}"]`);
        const cellNode = point => board.querySelector(`[data-cell="${point.y * 8 + point.x}"]`);
        if (step.kind === "Move") {
            const original = unitNode(step.unitId);
            if (!original) return;
            const layer = element("div", "action-layer");
            const moving = original.cloneNode(true);
            const bounds = board.getBoundingClientRect();
            const origin = cellNode(step.route[0]).getBoundingClientRect();
            const tokenBounds = original.getBoundingClientRect();
            moving.classList.add("moving-token");
            Object.assign(moving.style, { left: `${tokenBounds.left - bounds.left - board.clientLeft}px`,
                top: `${tokenBounds.top - bounds.top - board.clientTop}px`, width: `${tokenBounds.width}px`, height: `${tokenBounds.height}px` });
            layer.append(moving); board.append(layer); original.style.opacity = "0";
            try {
                // Geometry only: every waypoint was chosen by the engine's actual search.
                const frames = step.route.map((point, index) => {
                    const cell = cellNode(point).getBoundingClientRect();
                    return { transform: `translate(${cell.left - origin.left}px, ${cell.top - origin.top}px)`,
                        offset: index / (step.route.length - 1) };
                });
                await effect(moving, frames, duration, signal);
            } finally { layer.remove(); original.style.opacity = ""; }
        } else if (step.kind === "Attack") {
            const target = current.units.find(u => u.id === step.targetId);
            const after = step.frame.units.find(u => u.id === step.targetId);
            const tokenNode = unitNode(step.targetId);
            if (!target || !after || !tokenNode) return;
            const targetCell = cellNode(target);
            const impact = element("span", "damage-feedback", `−${step.damage}${step.destroyed ? " · destroyed" : ""}`);
            impact.setAttribute("aria-hidden", "true"); targetCell.classList.add("impact-cell"); targetCell.append(impact);
            const hp = tokenNode.querySelector(".unit-hp");
            if (hp) hp.textContent = `${after.health}/${after.maxHealth}`;
            try {
                await Promise.all([
                    effect(unitNode(step.unitId)?.querySelector(".unit-art"), [{ opacity: 1 }, { opacity: .55 }, { opacity: 1 }], duration, signal),
                    effect(targetCell, [{ boxShadow: "inset 0 0 0 3px #f2c895" }, { boxShadow: "inset 0 0 0 1px #f2c89500" }], duration, signal),
                    effect(tokenNode.querySelector(".health-fill"), [{ width: `${100 * target.health / target.maxHealth}%` }, { width: `${100 * after.health / after.maxHealth}%` }], duration, signal),
                    effect(impact, [{ opacity: 1 }, { opacity: 1, offset: .8 }, { opacity: 0 }], duration, signal),
                    step.destroyed ? effect(tokenNode, [{ opacity: 1 }, { opacity: .8, offset: .5 }, { opacity: 0 }], duration, signal) : Promise.resolve()
                ]);
            } finally { impact.remove(); targetCell.classList.remove("impact-cell"); }
        } else {
            await new Promise(resolve => {
                if (signal.aborted) return resolve();
                const finish = () => { clearTimeout(timer); signal.removeEventListener("abort", finish); resolve(); };
                const timer = setTimeout(finish, Math.min(duration, 100));
                signal.addEventListener("abort", finish, { once: true });
            });
        }
    }

    function render() {
        const shown = display ?? state;
        const locked = busy || !!pending;
        app.classList.toggle("playing", playing);
        app.classList.toggle("operation-complete", !!shown?.winner && !presenting);
        byId("connection-status").hidden = !connectionProblem;
        byId("connection-status").textContent = !navigator.onLine
            ? "Offline. Your last saved board is shown. Reconnect, then Retry."
            : "Connection interrupted. Retry to confirm your saved operation.";
        byId("welcome").hidden = playing;
        byId("play-area").hidden = !playing;
        byId("begin-operation").disabled = locked || !loaded;
        byId("begin-operation").classList.toggle("secondary", !!continueMatch);
        byId("continue-operation").hidden = !continueMatch;
        byId("continue-operation").disabled = locked || !loaded;
        byId("continue-operation").textContent = continueMatch ? `Continue Operation · Turn ${continueMatch.turnNumber}` : "Continue Operation";
        byId("welcome-status").textContent = busy ? slowRequest ? "Still connecting. The free demo may need a minute to wake up." : pending ? "Saving your command…" : "Checking for saved operations…" : !loaded ? "Connect to check your saved operation." :
            continueMatch ? "An unfinished operation is ready. Begin starts a separate match; your saves stay in Match history." :
            shown?.winner ? "Your last operation is complete. Begin again or review your saved matches." : "Ready when you are. Begin Operation starts your first match.";
        byId("help-objective").textContent = playing && shown?.mission ? shown.mission.instructions :
            "New Rivergate objective: capture F4 and hold it through two completed human-plus-computer rounds. Older saves keep their elimination objective.";
        byId("help-capture").hidden = playing && shown?.mission?.type !== "Rivergate";
        if (!playing && continueMatch?.missionType === "Elimination") byId("welcome-status").textContent =
            "Continue restores your legacy elimination match. Begin creates a separate Rivergate capture mission; all saves stay in Match history.";
        byId("replay-tutorial").disabled = locked || !shown;
        byId("replay-note").textContent = shown ? "Replay changes guidance only; your match and remaining actions stay as they are." : "Begin or continue an operation to practice without resetting it.";
        byId("show-briefing").disabled = locked;
        byId("new-match").disabled = locked || !shown;
        byId("end-turn").disabled = locked || !shown || !!shown.winner || shown.currentSide !== "Human";
        board.setAttribute("aria-busy", String(busy));
        if (!shown) return;
        const selected = shown.units.find(u => u.id === selectedId);
        const mission = shown.mission;
        const capture = mission?.type === "Rivergate";
        byId("objective-progress").textContent = capture ? `Hold the outpost: ${mission.heldRounds}/${mission.requiredRounds} rounds · ${String.fromCharCode(65 + mission.x)}${mission.y + 1}` : "Legacy elimination match";
        byId("objective-instructions").textContent = mission?.instructions ?? "Defeat the opposing squad. This saved match has no capture objective.";
        const actionable = !locked && selected?.side === "Human" && selected.health > 0 && !shown.winner;
        // These lists come from C#. JavaScript never calculates a route, range, or damage.
        const moves = actionable ? selected.legalMoves : [];
        const targets = actionable ? selected.legalTargets : [];
        const focusCell = document.activeElement?.dataset.cell;
        const cells = document.createDocumentFragment();
        for (let index = 0; index < shown.tiles.length; index++) {
            const x = index % 8, y = Math.floor(index / 8);
            const terrain = shown.tiles[index];
            const unit = shown.units.find(u => u.health > 0 && u.x === x && u.y === y);
            const move = moves.find(m => m.x === x && m.y === y);
            const target = unit && targets.includes(unit.id);
            const outpost = capture && mission.x === x && mission.y === y;
            const cell = element("button", `cell ${terrain.toLowerCase()}`);
            cell.type = "button";
            cell.tabIndex = index === boardFocus ? 0 : -1;
            cell.dataset.cell = String(index);
            cell.disabled = locked;
            if (outpost) cell.classList.add("outpost", unit ? "occupied-objective" : "empty-objective");
            if (unit) cell.classList.add(unit.side === "Human" ? "friendly-unit" : "opposing-unit");
            if (move) cell.classList.add("legal-move");
            if (target) cell.classList.add("attack-target");
            if (unit?.id === selectedId) cell.classList.add(unit.side === "Human" ? "selected" : "inspecting");
            const coordinate = `${String.fromCharCode(65 + x)}${y + 1}`;
            const label = [coordinate, terrain, unit ? `${unit.side === "Human" ? "Your" : "Opponent"} ${unit.type}, ${unit.health} of ${unit.maxHealth} health` : "empty"];
            if (outpost) label.push(`communications outpost, hold ${mission.heldRounds} of ${mission.requiredRounds} rounds`);
            if (move) label.push(`legal move, cost ${move.cost}`);
            if (target) label.push("legal attack target");
            cell.setAttribute("aria-label", label.join(", "));
            cell.setAttribute("aria-pressed", String(unit?.id === selectedId));
            cell.append(terrainArt(terrain, x, y));
            if (outpost) cell.append(illustration("outpost", "objective-art"), element("span", "objective-label", "OUTPOST"));
            if (unit) {
                const tokenNode = element("span", `unit-token ${unit.side.toLowerCase()} ${unit.hasAttacked ? "spent" : ""}`);
                tokenNode.dataset.unit = String(unit.id);
                tokenNode.setAttribute("aria-hidden", "true");
                tokenNode.append(illustration(unit.type.toLowerCase(), "unit-art"), element("span", "unit-name", unit.type));
                const health = element("span", "health-track");
                const remaining = element("span", "health-fill");
                remaining.style.width = `${100 * unit.health / unit.maxHealth}%`;
                health.append(remaining);
                tokenNode.append(health, element("span", "unit-hp", `${unit.health}/${unit.maxHealth}`));
                cell.append(tokenNode);
            }
            if (move) cell.append(element("span", "move-cost", move.cost));
            cell.addEventListener("click", () => choose(x, y, unit));
            cells.append(cell);
        }
        board.replaceChildren(cells);
        if (focusCell !== undefined && !locked) board.querySelector(`[data-cell="${focusCell}"]`)?.focus({ preventScroll: true });
        byId("turn-label").textContent = `TURN ${String(shown.turnNumber).padStart(2, "0")} · ${shown.winner ? "COMPLETE" : shown.currentSide === "Computer" ? "OPPONENT" : "YOUR COMMAND"}`;
        byId("turn-title").textContent = presenting ? "Presenting saved actions…" : busy && pending?.kind === "endTurn" ? "Opponent's turn…" : shown.winner ? "Match complete" : "Your turn";
        byId("turn-number").textContent = `T${String(shown.turnNumber).padStart(2, "0")}`;
        byId("turn-description").textContent = shown.winner ? "Start a fresh operation when you're ready." : busy ? "Saving your command…" : "Move your squad, pick your targets, hold the ground.";
        byId("board-hint").textContent = shown.winner ? "Operation complete" : selected ? `${selected.type} #${selected.id} selected${actionable ? " · choose a cell or target" : " · opponent unit"}` : "Select a blue unit to begin";
        byId("victory").hidden = presenting || !shown.winner || !!shown.report;
        byId("victory").classList.toggle("failure", shown.winner === "Computer");
        if (shown.winner) {
            byId("victory").replaceChildren(element("b", "", capture ? shown.winner === "Human" ? "Mission success." : "Mission failed." : shown.winner === "Human" ? "Victory. The field is yours." : "Defeat. Your squad was eliminated."), document.createTextNode(`${mission?.conclusion ?? ""} Completed on turn ${shown.turnNumber}.`));
        }
        renderReport(shown);
        renderDetails(selected);
        renderSquad();
        renderHistory();
        renderTutorial(selected, locked);
    }
    function renderReport(shown) {
        const report = shown.report;
        byId("mission-report").hidden = presenting || !shown.winner || !report;
        if (!report) return; // Older retry receipts did not contain report metadata.
        byId("mission-report").classList.toggle("failure", shown.winner === "Computer");
        byId("report-title").textContent = report.outcome;
        byId("report-round-note").textContent = `${shown.mission?.conclusion ?? ""} ${report.roundDescription}`;
        const facts = byId("report-facts"); facts.replaceChildren();
        for (const [label, value] of [["Completed rounds", report.completedRounds],
            ["Your surviving units", `${report.friendlySurvivors} / ${report.friendlyTotal}`],
            ["Opposing survivors", `${report.opposingSurvivors} / ${report.opposingTotal}`],
            ["Capture result", report.captureResult]]) {
            const row = element("div", ""); row.append(element("dt", "", label), element("dd", "", value)); facts.append(row);
        }
        byId("report-history").href = `${app.dataset.historyUrl}?matchId=${encodeURIComponent(shown.matchId)}`;
    }

    function renderTutorial(selected, locked) {
        const active = !presenting && playing && tutorial && tutorial.step < 3;
        byId("tutorial").hidden = !active;
        board.classList.toggle("tutorial-select", !!active && tutorial.step === 0 && !state.winner);
        if (!active) return;
        const step = tutorial.step;
        const titles = ["Select a friendly unit", "Choose a highlighted legal destination", "Attack targets and End Turn"];
        let message = "Blue units are yours. Choose a miniature or a unit in Your squad to inspect its remaining actions.";
        if (step === 1) {
            message = !selected || selected.side !== "Human" ? "Select a friendly unit to see its legal destinations." :
                selected.hasAttacked ? "Already attacked this turn. Choose another friendly unit or End Turn to restore actions." :
                selected.hasMoved ? "Already moved this turn. Choose another friendly unit or End Turn to restore actions." :
                !selected.legalMoves.length ? "No legal destination for this unit. Choose another friendly unit, or End Turn." :
                "Choose a round move marker. Its number is the route cost. A successful move advances this step.";
        }
        if (step === 2) message = state.mission?.type === "Rivergate"
            ? "Attack dashed orange targets to clear threats. Protect a friendly unit on the outpost, then End Turn. Hold progress updates after the opponent responds; losing occupancy resets it. Secure two rounds, even if every opponent is eliminated."
            : "Attack a dashed orange target once. Attacking locks movement. End Turn lets the opponent respond; your actions reset next turn. How to Play stays available.";
        if (state.winner) message = "This operation is complete. Begin a new one from Briefing when you want to practice. Replay never resets a match.";
        for (const [id, text] of [["tutorial-step", `FIRST ORDERS · ${step + 1} OF 3`], ["tutorial-title", titles[step]], ["tutorial-message", message]]) {
            if (byId(id).textContent !== text) byId(id).textContent = text;
        }
        byId("tutorial-done").hidden = step !== 2;
        byId("tutorial-done").disabled = locked;
        byId("tutorial-skip").disabled = locked;
    }
    function choose(x, y, unit) {
        if (busy || pending) return;
        const selected = state.units.find(u => u.id === selectedId);
        if (!state.winner && selected?.side === "Human" && unit?.side === "Computer") {
            command("attack", { unitId: selected.id, targetId: unit.id });
        } else if (unit) {
            selectedId = selectedId === unit.id ? null : unit.id;
            if (selectedId !== null) selectLessonUnit(unit);
            render();
        } else if (!state.winner && selected?.side === "Human") {
            command("move", { unitId: selected.id, x, y });
        } else {
            selectedId = null;
            render();
        }
    }
    function renderDetails(unit) {
        const shown = display ?? state;
        const details = byId("unit-details");
        details.replaceChildren();
        if (!unit) {
            details.append(element("p", "empty-state", "Select a blue unit on the board to see its stats and legal actions."));
            return;
        }
        const name = element("div", `intel-name ${unit.side.toLowerCase()}`);
        name.append(illustration(unit.type.toLowerCase(), "intel-art"), document.createTextNode(unit.type));
        name.append(element("span", `unit-side ${unit.side.toLowerCase()}`, unit.side === "Human" ? "YOUR UNIT" : "OPPONENT"));
        const stats = element("div", "intel-stats");
        for (const [label, value] of [["HEALTH", `${unit.health}/${unit.maxHealth}`], ["MOVEMENT", unit.movementPoints], ["RANGE", unit.minimumRange === unit.maximumRange ? unit.minimumRange : `${unit.minimumRange}–${unit.maximumRange}`]]) {
            const stat = element("div", "");
            stat.append(element("span", "stat-label", label), element("span", "stat-value", value));
            stats.append(stat);
        }
        details.append(name, stats);
        for (const [label, value] of [["Move", unit.hasAttacked ? "Locked after attack" : unit.hasMoved ? "Used" : "Available"], ["Attack", unit.hasAttacked ? "Used" : "Available"], ["Base damage", unit.baseDamage]]) {
            const row = element("div", "action-status", label);
            row.append(element("b", "", value)); details.append(row);
        }
        details.append(element("p", "intel-location", `${String.fromCharCode(65 + unit.x)}${unit.y + 1} / ${shown.tiles[unit.y * 8 + unit.x]}`));
    }
    function renderSquad() {
        const squad = byId("squad"); squad.replaceChildren();
        const humans = (display ?? state).units.filter(u => u.side === "Human");
        byId("squad-count").textContent = `${humans.filter(u => u.health > 0).length} / 3 ACTIVE`;
        for (const unit of humans) {
            const row = element("button", `squad-row ${unit.id === selectedId ? "active" : ""} ${unit.health === 0 ? "dead" : ""}`);
            row.type = "button"; row.disabled = busy || !!pending || unit.health === 0;
            row.setAttribute("aria-label", `Select your ${unit.type}, ${unit.health} health`);
            const label = element("span", "");
            label.append(element("div", "squad-name", unit.type), element("div", "squad-state", unit.health === 0 ? "Eliminated" : unit.hasAttacked ? "Attack used" : unit.hasMoved ? "Move used" : "Ready for orders"));
            row.append(illustration(unit.type.toLowerCase(), "squad-art"), label, element("span", "squad-health", `${unit.health}/${unit.maxHealth}`));
            row.addEventListener("click", () => { selectedId = unit.id; selectLessonUnit(unit); render(); });
            squad.append(row);
        }
    }
    function renderHistory() {
        const shown = display ?? state;
        const history = byId("history");
        history.replaceChildren();
        byId("history-count").textContent = `${shown.history.length} ${shown.history.length === 1 ? "entry" : "entries"}`;
        for (const entry of [...shown.history].reverse()) {
            const row = element("li", "history-entry");
            row.append(element("span", "history-turn", `T${String(entry.turn).padStart(2, "0")}`), element("span", `history-dot ${entry.side.toLowerCase()}`), element("span", "", entry.text));
            history.append(row);
        }
        const results = byId("results"); results.replaceChildren();
        for (const result of shown.results) results.append(element("li", "", `${result.missionType === "Rivergate" ? result.winner === "Human" ? "Rivergate success" : "Rivergate failure" : result.winner === "Human" ? "Legacy victory" : "Legacy defeat"} · ${result.turns} turns · ${new Date(result.completedAt).toLocaleDateString()}`));
        if (!shown.results.length) results.append(element("li", "", "No completed matches yet."));
    }

    document.addEventListener("keydown", event => {
        if (event.repeat && ["Enter", " "].includes(event.key) && app.contains(event.target))
            event.preventDefault();
        if (event.key === "Escape" && presenting) {
            event.preventDefault(); presentation.skip();
        }
    });
    byId("skip-sequence").addEventListener("click", () => presentation.skip());
    reducedMotion.addEventListener("change", () => { if (reducedMotion.matches) presentation.skip(); });
    window.addEventListener("resize", () => { if (presenting) presentation.skip(); });
    byId("begin-operation").addEventListener("click", () => command("new"));
    byId("continue-operation").addEventListener("click", async () => {
        await load();
        if (busy || pending || !continueMatch || !byId("error").hidden) return;
        if (state?.matchId === continueMatch.matchId && !state.winner) enterPlay();
        else await command("resume", { targetMatchId: continueMatch.matchId });
    });
    byId("show-briefing").addEventListener("click", () => {
        if (busy || pending) return;
        playing = false; wantPlay = false; rememberedMatch = null;
        preference("sessionStorage", "tacticsim.playing", null);
        render(); byId("welcome-title").focus(); load();
    });
    for (const button of document.querySelectorAll(".help-open")) button.addEventListener("click", () => byId("help-dialog").showModal());
    byId("close-help").addEventListener("click", () => byId("help-dialog").close());
    byId("replay-tutorial").addEventListener("click", () => {
        if (!state || busy || pending) return;
        byId("help-dialog").close();
        tutorial.replay(); selectedId = null; saveTutorial(); enterPlay();
    });
    function dismissTutorial() {
        if (busy || pending) return;
        tutorial.skip(); saveTutorial();
        preference("localStorage", "tacticsim.tutorial.dismissed", "1");
        render(); byId("play-title").focus();
    }
    byId("tutorial-skip").addEventListener("click", dismissTutorial);
    byId("tutorial-done").addEventListener("click", () => { if (tutorial.step === 2) dismissTutorial(); });
    byId("end-turn").addEventListener("click", event => {
        // A double-click is one intention, including when reduced motion and a
        // very quick response let the first command finish before click two.
        if (event.detail <= 1) command("endTurn");
    });
    byId("new-match").addEventListener("click", () => state ? byId("new-dialog").showModal() : command("new"));
    byId("cancel-new").addEventListener("click", () => byId("new-dialog").close());
    byId("confirm-new").addEventListener("click", () => { byId("new-dialog").close(); selectedId = null; command("new"); });
    byId("retry").addEventListener("click", () => retryAction());
    board.addEventListener("focusin", event => {
        const index = Number(event.target.dataset.cell);
        if (!Number.isInteger(index)) return;
        boardFocus = index;
        for (const cell of board.querySelectorAll("[data-cell]")) cell.tabIndex = Number(cell.dataset.cell) === index ? 0 : -1;
    });
    window.addEventListener("offline", () => { connectionProblem = true; if (!presenting) render(); });
    window.addEventListener("online", () => {
        // Retrying a pending mutation remains an explicit action with its original ID.
        if (!pending && !busy) load();
        else if (!presenting) render();
    });
    board.addEventListener("keydown", event => {
        if (presenting || busy || pending) return;
        if (event.key === "Escape") { selectedId = null; render(); return; }
        const index = Number(event.target.dataset.cell);
        if (!Number.isInteger(index)) return;
        const x = index % 8, y = Math.floor(index / 8);
        const next = { Home: event.ctrlKey ? 0 : y * 8, End: event.ctrlKey ? 63 : y * 8 + 7, ArrowLeft: x > 0 ? index - 1 : index, ArrowRight: x < 7 ? index + 1 : index, ArrowUp: y > 0 ? index - 8 : index, ArrowDown: y < 7 ? index + 8 : index }[event.key];
        if (next !== undefined) { event.preventDefault(); boardFocus = next; board.querySelector(`[data-cell="${next}"]`)?.focus(); }
    });
    document.addEventListener("visibilitychange", () => { if (document.hidden && presenting) presentation.skip(); if (!document.hidden && !busy && !pending) load(); });
    load();
})();
