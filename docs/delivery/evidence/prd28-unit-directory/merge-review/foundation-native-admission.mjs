import fs from 'node:fs';
import assert from 'node:assert/strict';
import {chromium,expect} from '../StrataAI2/node_modules/@playwright/test/index.mjs';
import {pressAdmittedAction,pressObservedRequest} from '../StrataAI2/tests/browser/keyboardAdmission.ts';
const browser=await chromium.launch();
const html=`<!doctype html><title>Disposable dispatch protocol</title><button id="retry">Retry original</button><p id="result"></p><script>
let ignored=false;const button=document.getElementById('retry');
button.addEventListener('keydown',event=>{if(event.key==='Enter'&&!ignored){ignored=true;event.preventDefault();button.disabled=true;setTimeout(()=>button.disabled=false,30);}});
button.addEventListener('click',async()=>{button.disabled=true;await fetch('/original',{method:'POST',headers:{'Content-Type':'application/json','Idempotency-Key':'original-fixture-key'},body:'{"original":"unchanged"}'});document.getElementById('result').textContent='Acknowledged';});
</script>`;
const run=async observed=>{
 const context=await browser.newContext();const page=await context.newPage();const attempts=[];
 await page.route('https://dispatch.example.test/**',async route=>{
  if(new URL(route.request().url()).pathname==='/original'){
   attempts.push({method:route.request().method(),key:route.request().headers()['idempotency-key'],body:route.request().postData()});
   await route.fulfill({status:200,contentType:'application/json',body:'{"acknowledged":true}'});
  }else await route.fulfill({status:200,contentType:'text/html',body:html});
 });
 await page.goto('https://dispatch.example.test/');
 const button=page.getByRole('button',{name:'Retry original',exact:true});
 if(observed){
  await pressObservedRequest(button,()=>attempts.length,1);
  await expect(page.getByText('Acknowledged',{exact:true})).toBeVisible();
  assert.deepEqual(attempts,[{method:'POST',key:'original-fixture-key',body:'{"original":"unchanged"}'}]);
 }else{
  await pressAdmittedAction(button);
  assert.equal(attempts.length,0);
  await expect(button).toBeEnabled();
 }
 await context.close();return {result:observed?'Passed':'Failed',attempts:attempts.length,assessment:observed?'Native request-observed admission starts exactly one original request with unchanged key/body and reaches acknowledgment.':'Counterfactual: the focus-and-keypress helper returns after an ignored native key without starting any request; the required acknowledgment cannot be inferred.'};
};
try{
 const counterfactual=await run(false),repair=await run(true);
 fs.writeFileSync('../prd131-review/foundation-native-admission.json',JSON.stringify({at:new Date().toISOString(),scope:'Controlled native-browser dispatch protocol, not application or release-pipeline qualification. Real hosted trace independently records the ignored original-recovery key.',counterfactual,repair},null,2)+'\n');
 console.log('Native counterfactual: zero dispatched requests. Request-observed admission: exactly one original request and acknowledgment.');
}finally{await browser.close();}
