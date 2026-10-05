import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';
const source = fs.readFileSync(new URL('../../Aquarella/wwwroot/metrics-motion.js', import.meta.url), 'utf8');
function harness(reduced = false, intro = false) {
    let now = 0, id = 0, observer;
    const frames = new Map(), running = new Set(), mediaListeners = new Set(), calls = [];
    const media = {matches:reduced, addEventListener:(_,fn)=>mediaListeners.add(fn), removeEventListener:(_,fn)=>mediaListeners.delete(fn)};
    const rootDocument = {hasAttribute:()=>intro};
    const counter = (value, final, format) => ({dataset:{value,final,format},textContent:final});
    const counters = [counter('262240', '$262.240,00','money'),counter('-5.125','-5,13%','percent'),counter('40','40','count'),counter('', 'Sin datos','money'),counter('10000000000000000000','$10.000.000.000.000.000.000,00','money')];
    const shape = (attribute='82.5 17.5') => ({getAttribute:()=>attribute,animate:(keyframes,options)=>{
        let resolve; const finished = new Promise(r=>resolve=r);
        const animation = {finished,end:now+options.duration+options.delay,cancel:()=>{running.delete(animation);resolve();},resolve};
        running.add(animation); calls.push({keyframes,options}); return animation;
    }});
    const elements = {'[data-metric-counter]':counters,'[data-metric-surface]':Array.from({length:10},()=>shape()),'[data-metric-bar]':Array.from({length:14},()=>shape()),'[data-metric-ring]':[shape(),shape('17.5 82.5')]};
    const root = {isConnected:true,dataset:{},querySelectorAll:s=>elements[s]};
    const module = vm.runInNewContext(source.replaceAll('export function','function') + '\n({enter,dispose})', {
        matchMedia:()=>media,document:{documentElement:rootDocument},
        getComputedStyle:()=>({getPropertyValue:k=>({'--motion-normal':'200ms','--motion-slow':'280ms','--ease-soft':'cubic-bezier(.22, 1, .36, 1)'}[k])}),
        requestAnimationFrame:fn=>{frames.set(++id,fn);return id;},cancelAnimationFrame:i=>frames.delete(i),performance:{now:()=>now},
        MutationObserver:class {constructor(fn){this.fn=fn;observer=this;}observe(){this.connected=true;}disconnect(){this.connected=false;}}
    });
    async function advance(time) {
        now=time;
        for(const a of [...running]) if(time>=a.end){running.delete(a);a.resolve();}
        const callbacks=[...frames.values()];frames.clear();callbacks.forEach(fn=>fn(now));
        for(let i=0;i<8;i++)await Promise.resolve();
    }
    return {module,root,counters,calls,frames,running,mediaListeners,advance,media,
        reveal:()=>{intro=false;observer?.fn();},observer:()=>observer};
}
let h=harness();h.module.enter(h.root);await h.advance(400);
assert.notEqual(h.counters[0].textContent,h.counters[0].dataset.final);
assert.equal(h.counters[4].textContent,h.counters[4].dataset.final,'Unsafe numbers stay exact');
assert.equal(h.calls.length,26);assert.ok(h.calls.every(c=>c.options.duration+c.options.delay<=850));
await h.advance(850);
assert.ok(h.counters.every(c=>c.textContent===c.dataset.final));assert.equal(h.root.dataset.metricsMotion,'complete');
assert.equal(h.frames.size,0);assert.equal(h.running.size,0);assert.equal(h.mediaListeners.size,0);
h=harness(true);h.module.enter(h.root);assert.equal(h.calls.length,0);assert.equal(h.frames.size,0);assert.ok(h.counters.every(c=>c.textContent===c.dataset.final));
h=harness();h.module.enter(h.root);await h.advance(250);h.media.matches=true;for(const fn of [...h.mediaListeners])fn();await h.advance(850);
assert.ok(h.counters.every(c=>c.textContent===c.dataset.final));assert.equal(h.frames.size,0);assert.equal(h.running.size,0);
h=harness(false,true);h.module.enter(h.root);assert.equal(h.root.dataset.metricsMotion,'waiting-intro');assert.equal(h.calls.length,0);h.reveal();await h.advance(850);assert.equal(h.root.dataset.metricsMotion,'complete');assert.equal(h.observer().connected,false);
h=harness(false,true);h.module.enter(h.root);h.module.dispose(h.root);h.reveal();assert.equal(h.calls.length,0);assert.equal(h.mediaListeners.size,0);
h=harness();h.module.enter(h.root);await h.advance(250);h.module.enter(h.root);await h.advance(1100);assert.equal(h.root.dataset.metricsMotion,'complete');assert.equal(h.frames.size,0);assert.equal(h.running.size,0);
h=harness();h.module.enter(h.root);h.root.isConnected=false;await h.advance(200);assert.equal(h.root.dataset.metricsMotion,'complete');assert.equal(h.frames.size,0);assert.equal(h.running.size,0);
console.log('Motion verificado: timing <850ms, finales exactos, reduced motion, cancelación, intro, reemplazo de datos y desmontaje.');
