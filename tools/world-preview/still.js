// Renders single frames for quick look-development: node still.js --t=12 --mode=chase --out=frame.png [--warm=40]
const {chromium} = require('playwright');
const http = require('http'); const fs = require('fs'); const path = require('path');
const root = path.join(__dirname, 'public');
const args = Object.fromEntries(process.argv.slice(2).map(a => a.replace(/^--/, '').split('=')));
const times = String(args.t || '10').split(',').map(Number), mode = args.mode || 'chase', warm = +(args.warm || 45), fps = 30;
const server = http.createServer((req, res) => {
  const f = path.join(root, decodeURIComponent(req.url.split('?')[0] === '/' ? '/index.html' : req.url.split('?')[0]));
  if (!fs.existsSync(f)) { res.writeHead(404); return res.end(); }
  const ext = path.extname(f); res.writeHead(200, {'Content-Type': {'.js': 'text/javascript', '.json': 'application/json', '.html': 'text/html', '.woff2': 'font/woff2', '.otf': 'font/otf'}[ext] || 'application/octet-stream'});
  fs.createReadStream(f).pipe(res);
});
server.listen(0, async () => {
  const port = server.address().port;
  const browser = await chromium.launch({executablePath: process.env.NK_CHROME || undefined, args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist']});
  const page = await browser.newPage({viewport: {width: +(args.w || 1280), height: +(args.h || 720)}});
  page.on('pageerror', e => console.error('page error:', e.message));
  page.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') console.error('console:', m.text().slice(0, 300)); });
  const t0 = Date.now();
  await page.goto(`http://127.0.0.1:${port}/index.html?fps=${fps}${args.follow ? '&follow=' + args.follow : ''}`);
  await page.waitForFunction(() => window.__ready === true, null, {timeout: 180000});
  console.log(`ready in ${((Date.now() - t0) / 1000).toFixed(1)}s`);
  for (const t of times) {
    const t1 = Date.now();
    for (let i = warm; i >= 0; i--) await page.evaluate(([t, mode]) => window.renderAt(t, mode), [t - i / fps, mode]);
    const out = (args.out || 'still.png').replace('.png', times.length > 1 ? `_${t}.png` : '.png');
    await page.screenshot({path: out, type: 'png'});
    console.log(`wrote ${out} (${((Date.now() - t1) / 1000 / (warm + 1)).toFixed(2)} s/frame)`);
  }
  await browser.close(); server.close();
});
