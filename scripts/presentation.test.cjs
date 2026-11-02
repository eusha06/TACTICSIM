const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const TurnPresentation = vm.runInNewContext(fs.readFileSync('src/TacticSim.Web/wwwroot/js/presentation.js', 'utf8') + '\nTurnPresentation;', { AbortController, setTimeout, clearTimeout });
const before = { matchId: 'a', matchVersion: 2, history: [{sequence: 5}] };
const after = { matchId: 'a', matchVersion: 3, history: [{sequence: 7}] };
const batch = { commandId: 'c', matchId: 'a', fromMatchVersion: 2, toMatchVersion: 3, steps: [
    { sequence: 6, kind: 'Move', route: [{x:0,y:0},{x:0,y:1},{x:1,y:1}], frame:{ units:[{id:1,health:8,x:1,y:1}] } },
    { sequence: 7, kind: 'Attack', damage: 3, frame:{ units:[{id:1,health:5,x:1,y:1}] } }
] };
test('Only the matching next command can present; duplicates, old receipts, other matches and missing routes snap to final state', () => {
 const p = new TurnPresentation(); assert.ok(p.canPlay(before,after,batch,'c'));
 for(const [b,a,e,id] of [[after,after,batch,'c'],[before,after,null,'c'],[before,after,batch,'other'],[{...before,matchId:'b'},after,batch,'c'],[before,after,{...batch,steps:[]},'c'],[before,after,{...batch,steps:[{...batch.steps[0],route:null},batch.steps[1]]},'c']]) assert.equal(p.canPlay(b,a,e,id),false);
});
test('Out-of-order, skipped and duplicate event sequences cannot be animated', () => {
 const p=new TurnPresentation(); for(const steps of [[batch.steps[1],batch.steps[0]],[batch.steps[0],batch.steps[0]],[batch.steps[1]]]) assert.equal(p.canPlay(before,after,{...batch,steps},'c'),false);
});
test('Animation consumes exact server routes, health frames and damage in order without mutating them', async () => {
 const p=new TurnPresentation();const log=[];const original=JSON.stringify(batch);
 await p.play(batch,{announce:s=>log.push('say'+s.sequence),animate:async s=>log.push('animate'+s.sequence),show:s=>log.push('frame'+s.frame.units[0].health)});
 assert.deepEqual(log,['say6','animate6','frame8','say7','animate7','frame5']);assert.equal(JSON.stringify(batch),original);
});
test('Reduced motion has no animation or delayed intermediate frames', async () => {
 const p=new TurnPresentation();let calls=0;await p.play(batch,{animate:()=>calls++,show:()=>calls++,announce:()=>calls++},true);assert.equal(calls,0);assert.equal(p.controller,null);
});
test('Skip aborts in-flight motion and prevents later frames; controls can be released in finally', async () => {
 const p=new TurnPresentation();let shown=0;let started;
 const playing=p.play(batch,{announce:()=>{},animate:(_,__,signal)=>new Promise(resolve=>{started=()=>p.skip();signal.addEventListener('abort',resolve,{once:true});}),show:()=>shown++});
 started();await playing;assert.equal(shown,0);assert.equal(p.controller,null);
});
test('A failed effect clears the controller so the saved final state can be shown', async () => {
 const p=new TurnPresentation();await assert.rejects(p.play(batch,{announce:()=>{},animate:async()=>{throw Error('effect unavailable')},show:()=>assert.fail()}));assert.equal(p.controller,null);
});
test('Lengthy batches divide a fixed short budget rather than multiplying per-action delays', async () => {
 const p=new TurnPresentation();let total=0;await p.play({...batch,steps:Array(30).fill(batch.steps[0])},{announce:()=>{},animate:async(_,ms)=>{total+=ms},show:()=>{}});assert.ok(total<=1901);
});

test('Even an effect that never resolves cannot hold input after the hard deadline', async () => {
 const p=new TurnPresentation();let shown=0;const start=Date.now();
 const skipped=await p.play(batch,{announce:()=>{},animate:()=>new Promise(()=>{}),show:()=>shown++});
 assert.equal(skipped,true);assert.equal(shown,0);assert.equal(p.controller,null);assert.ok(Date.now()-start<3000);
});
