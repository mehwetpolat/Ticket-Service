import { build, context } from "esbuild";
import { cp, mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const output = join(here, "..", "src", "TickeTask.Web", "wwwroot");
const watch = process.argv.includes("--watch");

const options = {
  entryPoints: [join(here, "src", "main.tsx")],
  bundle: true,
  minify: !watch,
  sourcemap: watch,
  target: ["es2020"],
  outfile: join(output, "assets", "app.js"),
  jsx: "automatic",
  loader: { ".mjs": "js" },
  legalComments: "none"
};

async function styles() {
  const sheets = ["globals.css", "workspace.css", "settings.css", "frost.css"];
  const parts = [];
  for (const sheet of sheets) {
    const css = await readFile(join(here, "src", "styles", sheet), "utf8");
    parts.push(css.replace(/^@import .*;$/gm, "").trim());
  }
  await writeFile(join(here, "src", "styles", "app.css"), parts.join("\n\n"));
}

async function copyStatic() {
  await mkdir(join(output, "assets"), { recursive: true });
  await styles();
  await cp(join(here, "src", "styles", "app.css"), join(output, "assets", "app.css"));
  await cp(join(here, "static", "index.html"), join(output, "index.html"));
  await cp(join(here, "static", "favicon.svg"), join(output, "favicon.svg"));
}

await copyStatic();
if (watch) {
  const ctx = await context(options);
  await ctx.watch();
  console.log("Watching frontend changes…");
} else {
  await build(options);
  console.log("Frontend bundle written to wwwroot.");
}
