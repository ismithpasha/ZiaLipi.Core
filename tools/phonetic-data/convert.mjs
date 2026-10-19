// Converts the upstream MPL-2.0 phonetic data (pinned commit below) into the
// data files used by ZiaLipi.Core, and records "golden" outputs of the original
// JavaScript engine so the C# port can be tested against it.
// The upstream file and object names below are kept as-is; they are what the
// downloaded sources define.
//
// Usage: node tools/phonetic-data/convert.mjs
import fs from "node:fs";
import path from "node:path";
import vm from "node:vm";
import zlib from "node:zlib";
import { fileURLToPath } from "node:url";

const COMMIT = "dd521a139af0b4bb64eaeeca44fef03f5fb770cf";
const BASE = `https://raw.githubusercontent.com/sarim/ibus-avro/${COMMIT}/`;
const FILES = [
  "avrolib.js", "avroregexlib.js", "avrodict.js", "suffixdict.js", "autocorrect.js",
  "dbsearch.js", "suggestionbuilder.js", "levenshtein.js", "utf8.js", "LICENSE",
];

const here = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(here, "../..");
const cacheDir = path.join(here, ".cache", COMMIT);
const dataDir = path.join(root, "src", "ZiaLipi.Core", "Data");
const goldenDir = path.join(root, "tests", "ZiaLipi.Core.Tests", "Golden");

async function fetchSources() {
  fs.mkdirSync(cacheDir, { recursive: true });
  for (const f of FILES) {
    const dest = path.join(cacheDir, f);
    if (fs.existsSync(dest)) continue;
    const res = await fetch(BASE + f);
    if (!res.ok) throw new Error(`${f}: HTTP ${res.status}`);
    fs.writeFileSync(dest, Buffer.from(await res.arrayBuffer()));
    console.log("downloaded", f);
  }
}

// Minimal emulation of the GJS `imports` module system.
function makeImports() {
  const loaded = {};
  const gi = {
    Gio: { File: { new_for_path() { throw new Error("no file io in converter"); } } },
    GLib: { get_home_dir: () => "/nonexistent" },
  };
  const imports = new Proxy({}, {
    get(_, name) {
      if (name === "gi") return gi;
      if (!loaded[name]) {
        const ctx = vm.createContext({ imports, print: () => {} });
        vm.runInContext(fs.readFileSync(path.join(cacheDir, name + ".js"), "utf8"), ctx, { filename: name + ".js" });
        loaded[name] = ctx;
      }
      return loaded[name];
    },
  });
  return imports;
}

function writeJson(file, obj) {
  fs.writeFileSync(file, JSON.stringify(obj, null, 1) + "\n", "utf8");
  console.log("wrote", path.relative(root, file));
}

await fetchSources();
const imports = makeImports();
fs.mkdirSync(dataDir, { recursive: true });
fs.mkdirSync(goldenDir, { recursive: true });

// --- Data files ---------------------------------------------------------------
const phonetic = imports.avrolib.OmicronLab.Avro.Phonetic;
const regexLib = new imports.avroregexlib.AvroRegex();
writeJson(path.join(dataDir, "phonetic.json"), phonetic.data);
writeJson(path.join(dataDir, "regex.json"), regexLib.data);
writeJson(path.join(dataDir, "suffix.json"), imports.suffixdict.db);
// Upstream autocorrect also has entries for its own product names; ZiaLipi leaves those out.
const autocorrect = Object.fromEntries(
  Object.entries(imports.autocorrect.db).filter(([k]) => !/avro|omicron/i.test(k)));
writeJson(path.join(dataDir, "autocorrect.json"), autocorrect);

// The original database compiler escaped ZWNJ/ZWJ as "\u0" + "200c", which
// decoded to U+0200 followed by "c"/"d". Restore the intended characters.
export const fixZeroWidth = s => s.replace(/Ȁc/g, "‌").replace(/Ȁd/g, "‍");

// Dictionary: "#table" header lines followed by one word per line, gzipped.
const tables = imports.avrodict.tables;
let text = "", count = 0;
for (const [name, words] of Object.entries(tables)) {
  text += "#" + name + "\n";
  for (const w of words) {
    if (/ওমিক্রনল্যাব/.test(w)) continue; // the upstream company name and web address
    text += fixZeroWidth(w) + "\n"; count++;
  }
}
fs.writeFileSync(path.join(dataDir, "dictionary.txt.gz"), zlib.gzipSync(Buffer.from(text, "utf8"), { level: 9 }));
console.log(`wrote dictionary (${Object.keys(tables).length} tables, ${count} words)`);

fs.copyFileSync(path.join(cacheDir, "LICENSE"), path.join(dataDir, "LICENSE-MPL-2.0.txt"));

// --- Golden vectors -----------------------------------------------------------
const inputs = fs.readFileSync(path.join(here, "golden-inputs.txt"), "utf8")
  .split(/\r?\n/).map(s => s.trim()).filter(s => s && !s.startsWith("//"));

const phoneticGolden = inputs.map(i => ({ input: i, output: phonetic.parse(i) }));
writeJson(path.join(goldenDir, "phonetic.json"), phoneticGolden);

const regexGolden = inputs.map(i => ({ input: i, output: regexLib.parse(i) }));
writeJson(path.join(goldenDir, "regex.json"), regexGolden);

// Suggestions are produced by typing each word one key at a time into a fresh
// builder, exactly like a user would (the phonetic cache affects suffixes).
const suggestGolden = [];
const SB = imports.suggestionbuilder.SuggestionBuilder;
for (const word of inputs) {
  if (/\s/.test(word)) continue;
  const sb = new SB();
  let s;
  for (let n = 1; n <= word.length; n++) {
    try { s = sb.suggest(word.substring(0, n)); }
    catch (e) { s = null; break; } // utf8Decode throws on some '%' inputs
  }
  if (s) suggestGolden.push({ input: word, words: s.words.map(fixZeroWidth), prevSelection: s.prevSelection });
}
writeJson(path.join(goldenDir, "suggest.json"), suggestGolden);
