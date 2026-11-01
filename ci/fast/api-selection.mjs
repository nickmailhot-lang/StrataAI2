// API tests share a partial ApiHostTests class across many files. Selecting that
// class accidentally runs the entire long regression suite. Select complete
// methods from reviewed files, retaining every original theory row/assertion.
export function methodsInTestFile(source) {
  if (typeof source !== 'string') throw new Error('Missing API test source');
  const namespace = source.match(/^namespace\s+([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*;/m)?.[1];
  const classes = [...source.matchAll(/^public\s+(?:(?:sealed|partial)\s+)*class\s+([A-Za-z_]\w*)\b/gm)];
  if (!namespace || classes.length !== 1) throw new Error('API test file requires explicit namespace and one reviewed public class');
  const methods = [];
  let pending = false;
  for (const line of source.split('\n')) {
    if (/^\s*\[(?:Fact|Theory)(?:\s*\(|\s*\])/.test(line)) pending = true;
    if (!pending) continue;
    const method = line.match(/^\s*public\s+(?:async\s+)?(?:Task|ValueTask|void)\s+([A-Za-z_]\w*)\s*\(/);
    if (method) { methods.push(`${namespace}.${classes[0][1]}.${method[1]}`); pending = false; }
    else if (/^\s*public\b/.test(line)) throw new Error('Unsupported API test declaration requires explicit review');
  }
  if (pending || methods.length === 0 || new Set(methods).size !== methods.length)
    throw new Error('Missing, duplicate or unsupported API test methods');
  return methods;
}

export function selectApiMethods(files, inventory, readSource) {
  if (!Array.isArray(files) || !files.length || typeof readSource !== 'function') throw new Error('Explicit API test files required');
  const methods = new Set();
  for (const file of [...new Set(files)].sort()) {
    if (!/^tests\/StrataAI\.Api\.Tests\/[A-Za-z0-9_.-]+\.cs$/.test(file) || !inventory.includes(file))
      throw new Error(`Unreviewed API test path: ${file}`);
    for (const method of methodsInTestFile(readSource(file))) methods.add(method);
  }
  return { files: [...new Set(files)].sort(), methods: [...methods].sort(), theoryRows: 'all',
    scope: 'Complete existing methods from reviewed files; complete host regression remains nightly/candidate.' };
}

export function apiTestArguments(selection) {
  if (!selection || selection.theoryRows !== 'all' || !Array.isArray(selection.methods) || !selection.methods.length
    || selection.methods.some(method => !/^[A-Za-z_]\w*(?:\.[A-Za-z_]\w*){2,}$/.test(method)))
    throw new Error('Explicit complete API methods required');
  return ['test','--project','tests/StrataAI.Api.Tests/StrataAI.Api.Tests.csproj',
    '--configuration','Release','--no-build','--filter-method',...selection.methods,
    '--report-trx','--report-trx-filename','feature-api.trx'];
}
