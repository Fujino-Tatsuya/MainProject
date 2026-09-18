"""Named token counts for captured responses and visible task traces; never billing."""
import json, os, pathlib, sys
root = pathlib.Path(__file__).resolve().parents[1]
deps = root / 'output/unity-mcp-audit-2026-09-08'
sys.path.insert(0, str(deps / 'python-deps'))
os.environ['TIKTOKEN_CACHE_DIR'] = str(deps / 'tokenizer-cache')
import tiktoken
out = root / 'output/unity-mcp-vnext'
enc = tiktoken.get_encoding('o200k_base')
compact = lambda value: json.dumps(value, ensure_ascii=False, separators=(',', ':'))
rows = []
for file in sorted((out / 'extended').glob('*.json')):
    if not (file.name.startswith(('local-', 'extended-')) or file.name == 'stdio-tools.json'):
        continue
    data = json.loads(file.read_text(encoding='utf-8-sig'))
    wire = compact(data)
    row = {'file': file.name, 'wireBytes': len(wire.encode()), 'wireTokens': len(enc.encode(wire))}
    content = data.get('result', {}).get('content', [])
    if content and content[0].get('type') == 'text':
        payload = json.loads(content[0]['text'])
        row['payloadTokens'] = len(enc.encode(compact(payload)))
        without = dict(payload)
        without.pop('evidence', None)
        row['evidenceIncrementTokens'] = row['payloadTokens'] - len(enc.encode(compact(without)))
    rows.append(row)
trials = []
for name in ['model-holdout', 'reference-dev']:
    records = [json.loads(line) for line in (out / name / 'token-inputs.jsonl').read_text().splitlines() if line]
    trials.append({'run': name, 'records': len(records), 'observedTextTokens': sum(len(enc.encode(r['text'])) for r in records)})
result = {'tokenizer': 'o200k_base', 'tiktokenVersion': tiktoken.__version__,
          'scope': 'Captured JSON/trace text only; no hidden reasoning, system wrappers or billable usage. Evidence increment is additive cost, not savings.',
          'responses': rows, 'trials': trials}
(out / 'token-results.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
print(json.dumps(result, indent=2))
