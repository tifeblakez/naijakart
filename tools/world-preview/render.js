// Renders replay frames deterministically to PNGs then encodes an MP4 with ffmpeg.
// Usage:
//   dotnet run --project Server/NaijaKart.Server -- export-world --track third_mainland_rush --out tools/world-preview/public/world.json
//   dotnet run --project Server/NaijaKart.Server -- simulate --players 8 --replay tools/world-preview/public/replay.json
//   cd tools/world-preview && npm install && npm run setup && node render.js --fps=30 --start=-3 --duration=60 --overview=5 --out=race.mp4
// Set NK_CHROME to a Chromium/headless-shell binary when the bundled Playwright browser is unavailable.
const {chromium} = require('playwright');
const http = require('http'); const fs = require('fs'); const path = require('path'); const {execSync} = require('child_process');
const root = path.join(__dirname, 'public');
const args = Object.fromEntries(process.argv.slice(2).map(a => a.replace(/^--/, '').split('=')));
const fps = +(args.fps || 30), start = +(args.start || 0), duration = +(args.duration || 40), out = args.out || 'race.mp4';
const overview = +(args.overview || 0);
const server = http.createServer((req, res) => {
  const f = path.join(root, decodeURIComponent(req.url.split('?')[0] === '/' ? '/index.html' : req.url.split('?')[0]));
  if (!fs.existsSync(f)) { res.writeHead(404); return res.end(); }
  const ext = path.extname(f); res.writeHead(200, {'Content-Type': {'.js': 'text/javascript', '.json': 'application/json', '.html': 'text/html', '.woff2': 'font/woff2', '.otf': 'font/otf'}[ext] || 'application/octet-stream'});
  fs.createReadStream(f).pipe(res);
});
server.listen(0, async () => {
  const port = server.address().port;
  const browser = await chromium.launch({executablePath: process.env.NK_CHROME || undefined,
    args: ['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist']});
  const page = await browser.newPage({viewport: {width: 1280, height: 720}});
  page.on('pageerror', e => console.error('page error:', e.message));
  page.on('console', m => { if (m.type() === 'error') console.error('console:', m.text()); });
  await page.goto(`http://127.0.0.1:${port}/index.html?fps=${fps}${args.follow ? '&follow=' + args.follow : ''}${args.world ? '&world=' + args.world : ''}`);
  await page.waitForFunction(() => window.__ready === true, null, {timeout: 180000});
  const dir = path.join(__dirname, 'frames'); fs.rmSync(dir, {recursive: true, force: true}); fs.mkdirSync(dir);
  const total = Math.round(duration * fps);
  const t0 = Date.now();
  for (let i = 0; i < total; i++) {
    const t = start + i / fps;
    const mode = i < overview * fps ? 'overview' : 'chase';
    await page.evaluate(([t, mode]) => window.renderAt(t, mode), [t, mode]);
    await page.screenshot({path: path.join(dir, `f${String(i).padStart(5, '0')}.png`), type: 'png'});
    if (i % 60 === 0) console.log(`frame ${i}/${total} (${((Date.now() - t0) / 1000).toFixed(0)}s)`);
  }
  await browser.close(); server.close();
  execSync(`ffmpeg -y -loglevel error -framerate ${fps} -i ${dir}/f%05d.png -c:v libx264 -pix_fmt yuv420p -crf 20 -movflags +faststart ${out}`);
  console.log('wrote', out);
});
