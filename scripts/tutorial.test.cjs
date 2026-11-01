// No npm packages or frontend build. Tests exercise tutorial transitions with server replies.
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const Tutorial = vm.runInNewContext(fs.readFileSync('src/TacticSim.Web/wwwroot/js/tutorial.js', 'utf8') + '\nTutorial;');
const friendly = { id: 1, side: 'Human', health: 10, hasMoved: false, x: 0, y: 0 };
const command = { kind: 'move', matchId: 'match-a', unitId: 1, x: 0, y: 2 };
const reply = { matchId: 'match-a', units: [{ ...friendly, hasMoved: true, x: 0, y: 2 }] };
test('Only a living friendly selection advances the first step', () => {
    const t = new Tutorial('match-a');
    t.select({ ...friendly, side: 'Computer' }); t.select({ ...friendly, health: 0 });
    assert.equal(t.step, 0); t.select(friendly); assert.equal(t.step, 1);
});
test('A matching confirmed move advances once, including an identical retry receipt', () => {
    const t = new Tutorial('match-a'); t.select(friendly); t.accepted(command, reply);
    assert.equal(t.step, 2); t.accepted(command, reply); assert.equal(t.step, 2);
});
test('Selection, old hasMoved flags and unrelated success cannot pretend a move happened', () => {
    const t = new Tutorial('match-a'); t.select({ ...friendly, hasMoved: true });
    assert.equal(t.step, 1);
    for (const c of [{...command,kind:'attack'}, {...command,kind:'endTurn'}, {...command,unitId:2}, {...command,matchId:'other'}, {...command,x:7}]) t.accepted(c,reply);
    t.accepted(command,{...reply,matchId:'other'});
    t.accepted(command,{...reply,units:[friendly]});
    assert.equal(t.step, 1);
});
test('Progress cannot skip directly from selecting to acknowledgement', () => {
    const t = new Tutorial('match-a'); t.finish(); assert.equal(t.step, 0);
    t.select(friendly); t.finish(); assert.equal(t.step, 1);
    t.accepted(command, reply); t.finish(); assert.equal(t.step, 3);
});
test('Refresh restores progress only for that match; Skip survives refresh', () => {
    const t = new Tutorial('match-a'); t.select(friendly);
    assert.equal(new Tutorial('match-a',t.snapshot()).step,1);
    assert.equal(new Tutorial('match-b',t.snapshot()).step,0);
    t.skip(); assert.equal(new Tutorial('match-a',t.snapshot()).step,3);
    assert.equal(new Tutorial('new-match',null,true).step,3);
});
test('Replay and skip never alter a saved game, command or server flags', () => {
    const before = JSON.stringify({friendly,command,reply});
    const t = new Tutorial('match-a',null,true); t.replay(); t.select(reply.units[0]);
    assert.equal(t.step,1); t.skip(); t.replay(); assert.equal(t.step,0);
    assert.equal(JSON.stringify({friendly,command,reply}),before);
});
test('Selecting a different friendly unit arms that unit for the move step', () => {
    const t = new Tutorial('match-a'); t.select(friendly); t.select({...friendly,id:2});
    t.accepted(command,reply); assert.equal(t.step,1);
    t.accepted({...command,unitId:2},{...reply,units:[{...reply.units[0],id:2}]}); assert.equal(t.step,2);
});
test('Malformed stored progress safely starts from selection', () => {
    assert.equal(new Tutorial('match-a',{matchId:'match-a',step:500}).step,0);
});
