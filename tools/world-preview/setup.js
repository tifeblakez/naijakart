// Copies the previewer's runtime files (three.js, fonts, the page) into public/ after `npm install`.
// Plain Node so it behaves the same on Windows, macOS and Linux.
const fs = require('fs'); const path = require('path');
const here = __dirname, pub = path.join(here, 'public');
const nm = (...p) => path.join(here, 'node_modules', ...p);
function copy(from, to) { fs.mkdirSync(path.dirname(to), {recursive: true}); fs.cpSync(from, to, {recursive: true}); console.log('  ' + path.relative(here, to)); }
console.log('world-preview setup');
copy(nm('three', 'build', 'three.module.js'), path.join(pub, 'vendor', 'three.module.js'));
copy(nm('three', 'examples', 'jsm'), path.join(pub, 'vendor', 'jsm'));
const fonts = [
  ['@fontsource/baloo-2', 'baloo-2-latin-800-normal.woff2', 'baloo-800.woff2'],
  ['@fontsource/baloo-2', 'baloo-2-latin-700-normal.woff2', 'baloo-700.woff2'],
  ['@fontsource/lilita-one', 'lilita-one-latin-400-normal.woff2'],
  ['@fontsource/nunito', 'nunito-latin-700-normal.woff2'],
  ['@fontsource/nunito', 'nunito-latin-800-normal.woff2'],
  ['@fontsource/nunito', 'nunito-latin-900-normal.woff2'],
  ['@fontsource/nunito', 'nunito-latin-700-italic.woff2'],
  ['@fontsource/nunito', 'nunito-latin-800-italic.woff2'],
  ['@fontsource/nunito', 'nunito-latin-900-italic.woff2'],
];
for (const [pkg, file, as] of fonts) {
  const src = nm(pkg, 'files', file);
  if (!fs.existsSync(src)) { console.warn('  missing ' + src + ' (text falls back to the next font in the stack)'); continue; }
  copy(src, path.join(pub, 'fonts', as || file));
}
copy(path.join(here, 'index.html'), path.join(pub, 'index.html'));
console.log('done: now export world.json and replay.json with the server CLI (see README.md)');
