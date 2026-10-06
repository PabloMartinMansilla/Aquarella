import assert from 'node:assert/strict';
import fs from 'node:fs';
import vm from 'node:vm';
const values = new Map(); let writes = 0;
const localStorage = {getItem:key=>values.get(key)??null,setItem:()=>writes++};
globalThis.localStorage = localStorage;
const window = {};
vm.runInNewContext(fs.readFileSync(new URL('../../Aquarella/wwwroot/business-identity.js',import.meta.url),'utf8'), {window,localStorage,document:{documentElement:{style:{setProperty(){}}}}});
const stock = await import('../../Aquarella/wwwroot/stock-storage.js');
const prices = await import('../../Aquarella/wwwroot/prices-storage.js');
const agenda = await import('../../Aquarella/wwwroot/agenda-storage.js');
const put = (key,value)=>values.set('aquarella.'+key+'.v1',JSON.stringify(value));
const validProfile = {name:'Conservado',primaryColor:'#123456',secondaryColor:'#654321',tertiaryColor:'#abcdef',futureField:42};
put('business-profile',validProfile); assert.deepEqual(JSON.parse(JSON.stringify(window.aquarellaIdentity.load())),validProfile);
put('business-profile',{...validProfile,primaryColor:'bad'}); assert.throws(()=>window.aquarellaIdentity.load());
values.set('aquarella.business-profile.v1','{broken'); assert.throws(()=>window.aquarellaIdentity.load()); assert.equal(values.get('aquarella.business-profile.v1'),'{broken');
for (const [key,load] of [['stock',stock.load],['prices',prices.load],['agenda',agenda.load]]) {
  values.clear(); put(key,[null]); assert.throws(load);
  values.clear(); values.set('aquarella.'+key+'.v1','{broken'); assert.throws(load);
  values.clear(); put(key,null); assert.throws(load);
}
values.clear(); put('stock',[{id:'1',name:'Válido',quantity:3,extra:1}]); assert.equal(stock.load()[0].quantity,3);
put('prices',[{productId:'1',cost:1,desiredProfitPercent:10,salePrice:2,manualSalePrice:true,extra:1}]); assert.equal(prices.load().prices[0].cost,1);
put('agenda',[{id:'1',date:'2026-10-05',title:'Versión anterior',extra:1}]); const note=agenda.load()[0]; assert.equal(note.content,''); assert.equal(note.time,null); assert.equal(note.extra,1);
put('stock',[{id:'1',name:'Válido',quantity:-1}]); assert.throws(stock.load);
assert.equal(writes,0,'Reading legacy never rewrites the originals');
console.log('PASS legacy JSON: valid, partial optional fields, unknown fields, invalid JSON/root/rows/ranges, original preservation.');
