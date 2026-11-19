const { chromium } = require('playwright');
(async () => {
  const [,, file, out, w, h] = process.argv;
  const b = await chromium.launch();
  const p = await b.newPage({ viewport: { width: +w, height: +h } });
  await p.goto('file://' + file);
  await p.waitForTimeout(150);
  await p.screenshot({ path: out });
  await b.close();
})();
